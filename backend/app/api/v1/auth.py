import hmac

from fastapi import APIRouter, HTTPException, status

from app.core.config import settings
from app.core.security import create_access_token, verify_password
from app.schemas.auth import LoginRequest, LoginResponse, LoginError


router = APIRouter(
    prefix="/auth",
    tags=["Authentication"],
)


def _authentication_configured() -> bool:
    return bool(
        settings.api_username
        and settings.api_password_hash
        and settings.jwt_secret_key
        and len(settings.jwt_secret_key.encode("utf-8")) >= 32
    )


@router.post("/login", response_model=LoginResponse, responses={401: {"model": LoginError}})
async def login(request: LoginRequest):
    # Credentials come from the environment only. Login fails closed (503)
    # when the service has not been configured, and never falls back to
    # hard-coded accounts.
    if not _authentication_configured():
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="Authentication is not configured.",
        )

    username_ok = hmac.compare_digest(request.username, settings.api_username)
    password_ok = verify_password(request.password, settings.api_password_hash)
    if not (username_ok and password_ok):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid username or password",
        )

    token = create_access_token(
        request.username,
        "Admin",
        secret=settings.jwt_secret_key,
        issuer=settings.jwt_issuer,
        audience=settings.jwt_audience,
        expires_minutes=settings.jwt_expire_minutes,
    )
    return LoginResponse(
        token=token,
        role="Admin",
        message="Login successful",
    )
