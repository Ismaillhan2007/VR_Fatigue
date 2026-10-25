"""Independent Python checks of the experimental indicator's arithmetic and temporal state."""
import math


def close(actual, expected):
    if expected is None:
        assert actual is None
    else:
        assert actual is not None and math.isclose(actual, expected, rel_tol=1e-12, abs_tol=1e-12)


def advance(state, start, end, raw, policy, unavailable_reason=None):
    """Reference recurrence, expressed independently of the C# state object."""
    previous = state["level"]
    gap = state["end"] is not None and start != state["end"]
    reset = "initial" if state["end"] is None else "time_gap" if gap else None
    if gap:
        state.update(value=None, count=0, active=False)
    if raw is None:
        state.update(value=None, count=0, active=False, level="unavailable", end=end)
        return dict(previous_level=previous, level="unavailable", value=None, nudge_active=False,
                    nudge_event="none", consecutive_high_windows=0, state_reset=True,
                    reset_reason="unavailable_window", unavailable_reason=unavailable_reason,
                    level_changed=previous != "unavailable")
    value = raw if state["value"] is None else state["value"] + policy["alpha"] * (raw - state["value"])
    value = min(1, max(0, value))
    level = "low" if value < policy["low_threshold"] else "moderate" if value < policy["high_threshold"] else "high"
    count = min(policy["required_high_windows"], state["count"] + 1) if value > policy["high_threshold"] else 0
    active, event = state["active"], "none"
    if not active and count == policy["required_high_windows"]:
        active, event = True, "offer_break"
    elif active and value < policy["release_threshold"]:
        active, event = False, "released"
    state.update(value=value, count=count, active=active, level=level, end=end)
    return dict(previous_level=previous, level=level, value=value, nudge_active=active, nudge_event=event,
                consecutive_high_windows=count, state_reset=reset is not None, reset_reason=reset,
                unavailable_reason=None, level_changed=level != previous)


def empty_state():
    return dict(value=None, count=0, active=False, level="unavailable", end=None)


def check_sequence(data):
    raw = data["raw_values"]
    for key in ("expected_values", "expected_levels", "expected_events", "expected_active", "expected_high_counts"):
        assert len(data[key]) == len(raw)
    policy = dict(alpha=data["alpha"], low_threshold=0.35, high_threshold=0.6,
                  release_threshold=0.4, required_high_windows=3)
    state = empty_state()
    for i, value in enumerate(raw):
        start = data["start_timestamp_us"] + i * data["window_duration_us"]
        point = advance(state, start, start + data["window_duration_us"], value, policy,
                        "synthetic_unavailable" if value is None else None)
        close(point["value"], data["expected_values"][i])
        assert point["level"] == data["expected_levels"][i]
        assert point["nudge_event"] == data["expected_events"][i]
        assert point["nudge_active"] == data["expected_active"][i]
        assert point["consecutive_high_windows"] == data["expected_high_counts"][i]


def check_indicator_accounting(data, check_baseline):
    source, policy = data["baseline_comparison"], data["policy"]
    check_baseline(source)
    assert data["experimental"] is True and data["interpretation"] == "baseline_deviation"
    assert all(data[k] == source[k] for k in ("session_id", "data_source", "replay_origin"))
    baseline = source["baseline"]
    assert data["baseline_low_confidence"] == baseline["low_confidence"]
    assert data["baseline_low_confidence_reasons"] == baseline["low_confidence_reasons"]
    assert len(data["windows"]) == len(source["comparison"]["windows"])
    terms = policy["terms"]
    total = sum(t["weight"] for t in terms.values())
    close(policy["total_weight"], total)
    assert 0 < policy["low_threshold"] < policy["high_threshold"] < 1
    assert 0 <= policy["release_threshold"] < policy["high_threshold"]
    state = empty_state()
    for point, measured in zip(data["windows"], source["comparison"]["windows"]):
        assert all(point[k] == measured[k] for k in ("index", "start_timestamp_us", "end_timestamp_us"))
        missing, combined = [], 0.0
        for name, term in terms.items():
            z = measured["deviations"][name]["z_score"]
            actual = point["contributions"][name]
            assert actual["weight"] == term["weight"] and actual["direction"] == term["direction"]
            close(actual["normalized_weight"], term["weight"] / total)
            close(actual["z_score"], z)
            if term["weight"] == 0:
                assert actual["status"] == "disabled" and actual["contribution"] == 0
                assert actual["unavailable_reason"] is None
            elif z is None or abs(z) > policy["expected_z_clip"]:
                missing.append(name)
                assert actual["status"] == "unavailable" and actual["contribution"] is None
                assert actual["unavailable_reason"] == (measured["deviations"][name]["unavailable_reason"] or "invalid_deviation")
            else:
                expected = (term["weight"] / total) * term["direction"] * z
                combined += expected
                assert actual["status"] == "available" and actual["unavailable_reason"] is None
                close(actual["contribution"], expected)
        assert set(point["missing_metrics"]) == set(missing)
        conditions = source["comparison"]["conditions_id"]
        reason = None
        if baseline["status"] != "ready":
            reason = "baseline_unavailable"
        elif source["baseline_policy"]["window_duration_us"] != policy["expected_window_us"] or source["baseline_policy"]["z_clip"] != policy["expected_z_clip"]:
            reason = "incompatible_baseline_policy"
        elif baseline["conditions_id"] is None or conditions is None:
            reason = "conditions_unconfirmed"
        elif baseline["conditions_id"] != conditions:
            reason = "conditions_mismatch"
        elif not measured["complete"]:
            reason = "partial_window"
        elif not measured["eligible"] or measured["measurements"]["quality"]["status"] != "usable":
            reason = "insufficient_window_data"
        elif any(baseline["references"][k]["median"] is None for k in ("probe_latency_ms", "probe_rms_error_deg", "probe_miss_fraction")):
            reason = "baseline_probes_unavailable"
        elif not measured["measurements"]["probes"]:
            reason = "probes_unavailable"
        elif missing:
            reason = "required_deviation_unavailable"
        if reason is not None:
            combined, raw = None, None
        else:
            combined = max(-policy["expected_z_clip"], min(policy["expected_z_clip"], combined))
            raw = max(0, min(1, combined / policy["normalization_scale"]))
        close(point["signed_combination"], combined)
        close(point["raw_value"], raw)
        expected_state = advance(state, point["start_timestamp_us"], point["end_timestamp_us"], raw, policy, reason)
        close(point["value"], expected_state.pop("value"))
        for key, expected in expected_state.items():
            assert point[key] == expected, (key, point[key], expected)
