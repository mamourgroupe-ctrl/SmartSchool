from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    gemini_api_key: str = ""
    gemini_model: str = "gemini-3.6-flash"

    # Authentication (fail closed: login is disabled until these are set).
    api_username: str = ""
    # PBKDF2 hash, generate with: python -m app.core.security
    api_password_hash: str = ""
    jwt_secret_key: str = ""
    jwt_issuer: str = "SmartSchoolBackend"
    jwt_audience: str = "SmartSchoolClients"
    jwt_expire_minutes: int = 60

    # Comma-separated list of allowed origins. Never use "*" together with
    # credentials in production.
    cors_origins: str = (
        "http://localhost:8081,http://localhost:19006,http://localhost:5197"
    )

    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    def cors_origin_list(self) -> list[str]:
        return [origin.strip() for origin in self.cors_origins.split(",") if origin.strip()]


settings = Settings()
