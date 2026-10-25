import secrets
from typing import Annotated

from fastapi import Depends, FastAPI, HTTPException, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer

from app.config import Settings
from app.models import AnalysisRequest, AnalysisResponse
from app.security import RequestBodyLimit
from app.service import Adapter, AnalysisService


def create_app(settings: Settings | None = None, adapter: Adapter | None = None) -> FastAPI:
    settings = settings or Settings()
    service = AnalysisService(settings, adapter)
    app = FastAPI(
        title="VR Fatigue Backend",
        version="0.1.0",
        description="Local Unity demo API. Experimental wellness summaries; offline by default.",
    )
    app.state.analysis_service = service
    app.add_middleware(RequestBodyLimit, max_bytes=settings.max_request_bytes)
    bearer = HTTPBearer(auto_error=False)

    async def require_access(
        credentials: Annotated[HTTPAuthorizationCredentials | None, Depends(bearer)],
    ):
        token = settings.backend_access_token.get_secret_value()
        if token and (
            credentials is None
            or not secrets.compare_digest(credentials.credentials.encode(), token.encode())
        ):
            raise HTTPException(
                status_code=401, detail="Unauthorized", headers={"WWW-Authenticate": "Bearer"}
            )

    @app.exception_handler(RequestValidationError)
    async def validation_error(_request: Request, exc: RequestValidationError):
        # FastAPI's default includes offending input values. Keep personal text out of errors.
        return JSONResponse(
            status_code=422,
            content={
                "detail": [
                    {
                        "loc": list(error["loc"][:-1]) + ["<unknown_field>"]
                        if error["type"] == "extra_forbidden"
                        else list(error["loc"]),
                        "type": error["type"],
                        "msg": error["msg"],
                    }
                    for error in exc.errors()
                ]
            },
        )

    @app.get("/health", tags=["health"])
    async def health():
        return {
            "status": "ok",
            "service": "vr-fatigue-backend",
            "gemini_enabled": settings.live_ready,
        }

    @app.post(
        "/api/v1/analyze",
        response_model=AnalysisResponse,
        tags=["analysis"],
        dependencies=[Depends(require_access)],
    )
    async def analyze(body: AnalysisRequest):
        return await service.analyze(body)

    return app


app = create_app()
