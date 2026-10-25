from decimal import Decimal
from pathlib import Path
from typing import Literal

from pydantic import Field, SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="VR_",
        env_file=Path(__file__).resolve().parents[1] / ".env",
        env_ignore_empty=True,
        extra="ignore",
        hide_input_in_errors=True,
    )

    runtime_mode: Literal["local", "hosted"] = "local"
    gemini_enabled: bool = False
    allow_participant_cloud: bool = False
    gemini_api_key: SecretStr = Field(default=SecretStr(""), validation_alias="GEMINI_API_KEY")
    gemini_model: str = Field(default="", validation_alias="GEMINI_MODEL")
    input_price_per_million: Decimal | None = Field(default=None, ge=0)
    output_price_per_million: Decimal | None = Field(default=None, ge=0)
    gemini_budget_usd: Decimal = Field(default=Decimal("0"), ge=0)
    gemini_max_attempts_per_day: int = Field(default=20, ge=0, le=100)
    gemini_max_total_attempts: int | None = Field(default=None, ge=0, le=100)
    gemini_ledger_path: Path | None = None
    gemini_timeout_seconds: float = Field(default=12, gt=0, le=15)
    max_request_bytes: int = Field(default=16384, ge=4096, le=65536)
    backend_access_token: SecretStr = SecretStr("")

    @property
    def live_ready(self) -> bool:
        return bool(
            self.gemini_enabled
            and self.gemini_api_key.get_secret_value()
            and self.gemini_model
            and self.input_price_per_million is not None
            and self.output_price_per_million is not None
        )
