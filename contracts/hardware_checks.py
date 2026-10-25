"""Independent accounting for aggregate hardware diagnostics; no native SDK calls."""
import math


def check_hardware_accounting(data):
    counts, report = data["counts"], data["quality_report"]
    quality = report["quality"]
    assert all(data[k] == report[k] for k in ("session_id", "data_source", "duration_ms"))
    assert report["replay_origin"] is None
    assert not report["capabilities"]["confidence_informative"]
    assert counts["frame_polls"] == counts["frames_received"] + counts["read_failures"]
    assert counts["accepted_samples"] <= counts["frames_received"]
    assert counts["accepted_samples"] <= data["capture_policy"]["maximum_samples"]
    assert counts["unaligned_head_frames"] <= quality["counts"]["invalid_gaze"]
    assert counts["dt_count"] == max(0, counts["accepted_samples"] - 1)
    assert sum(data["dt_histogram"].values()) == counts["dt_count"]
    assert (data["median_dt_ms"] is None) == (counts["dt_count"] == 0)
    assert quality["counts"]["input"] == quality["counts"]["accepted"] == counts["accepted_samples"]
    assert quality["counts"]["valid_gaze"] + quality["counts"]["invalid_gaze"] == counts["accepted_samples"]
    assert math.isclose(quality["valid_duration_ms"] + quality["gap_duration_ms"], data["duration_ms"], abs_tol=1e-8)
    assert data["duration_ms"] <= data["capture_policy"]["maximum_duration_us"] / 1000
    expected = counts["duplicate_timestamps"] / counts["frames_received"] if counts["frames_received"] else 0
    assert math.isclose(data["repeated_fraction"], expected, abs_tol=1e-12)
    assert data["recording_period_complete"] == (data["data_source"] == "live" and data["duration_ms"] >= 60000)
    for eye in ("left", "right"):
        confidence = data["confidence"][eye]
        assert confidence["count"] <= counts["accepted_samples"]
        if confidence["count"] == 0:
            assert all(confidence[k] is None for k in ("minimum", "maximum", "observed_variation"))
        else:
            assert confidence["minimum"] <= confidence["maximum"]
            assert math.isclose(confidence["observed_variation"], confidence["maximum"] - confidence["minimum"], abs_tol=1e-12)
