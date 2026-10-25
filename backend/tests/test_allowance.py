"""A demo allowance survives restarts and shared-process reservations."""

from concurrent.futures import ThreadPoolExecutor

from test_api import FakeAdapter, client_for, live_settings

from app.service import AnalysisService


def config(tmp_path, **overrides):
    return live_settings(
        allow_participant_cloud=True,
        backend_access_token="invented-allowance-token",
        gemini_ledger_path=tmp_path / "reservations.sqlite3",
        gemini_max_total_attempts=2,
        **overrides,
    )


def test_total_attempts_survive_service_restart_and_day_rollover(tmp_path):
    settings = config(tmp_path)
    assert AnalysisService(settings).reserve_attempt() is None
    assert AnalysisService(settings).reserve_attempt() is None
    service = AnalysisService(settings)
    with service.ledger.connect() as db:
        db.execute("UPDATE reservations SET day='2000-01-01'")
    assert service.reserve_attempt() == "attempt_limit"
    assert service.readiness_reason() == "attempt_limit"


def test_parallel_services_cannot_exceed_total_cap(tmp_path):
    settings = config(tmp_path)
    services = [AnalysisService(settings) for _ in range(8)]
    with ThreadPoolExecutor(max_workers=8) as pool:
        outcomes = list(pool.map(lambda s: s.reserve_attempt(), services))
    assert outcomes.count(None) == 2
    assert outcomes.count("attempt_limit") == 6


def test_cost_reservations_survive_restart(tmp_path):
    settings = config(
        tmp_path, input_price_per_million="0.25", output_price_per_million="1.50",
        gemini_budget_usd="0.002",
    )
    assert AnalysisService(settings).reserve_attempt() is None
    assert AnalysisService(settings).reserve_attempt() == "budget_limit"


def test_readiness_is_authenticated_and_never_calls_provider(tmp_path):
    settings = config(tmp_path)
    adapter = FakeAdapter()
    with client_for(settings, adapter) as client:
        assert client.get("/api/v1/readiness").status_code == 401
        response = client.get(
            "/api/v1/readiness", headers={"Authorization": "Bearer invented-allowance-token"}
        )
        assert response.json() == {
            "ready_for_consented_summary": True, "reason": None,
            "provider_verified": False, "persistent_allowance": True,
        }
        assert adapter.calls == 0
        assert client.app.state.analysis_service.reserve_attempt() is None
        assert client.app.state.analysis_service.reserve_attempt() is None
        response = client.get(
            "/api/v1/readiness", headers={"Authorization": "Bearer invented-allowance-token"}
        )
        assert response.json()["reason"] == "attempt_limit"
        assert response.json()["ready_for_consented_summary"] is False
