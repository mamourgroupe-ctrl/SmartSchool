"""Security helpers: PBKDF2 password hashing and HS256 JWT issuing/verification.

Only the Python standard library (plus FastAPI's auth primitives) is used so
this module stays testable without the AI/LLM dependencies.

Run ``python -m app.core.security`` to generate a password hash for the
``SMARTSCHOOL_API_PASSWORD_HASH`` environment variable.
"""

from __future__ import annotations

import base64
import hashlib
import hmac
import json
import secrets
import sys
import time
import uuid
from typing import Any

from fastapi import Depends, HTTPException, status
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer

from app.core.config import settings

PBKDF2_ITERATIONS = 210_000
SALT_SIZE = 16
KEY_SIZE = 32
MIN_SECRET_BYTES = 32

_bearer_scheme = HTTPBearer(auto_error=False)


def hash_password(password: str, iterations: int = PBKDF2_ITERATIONS) -> str:
    """Hash a password as ``PBKDF2$SHA256$iterations$salt_b64$key_b64``."""
    salt = secrets.token_bytes(SALT_SIZE)
    key = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, iterations, KEY_SIZE)
    return "$".join(
        [
            "PBKDF2",
            "SHA256",
            str(iterations),
            base64.b64encode(salt).decode("ascii"),
            base64.b64encode(key).decode("ascii"),
        ]
    )


def verify_password(password: str, stored: str) -> bool:
    """Verify a password against a PBKDF2 hash in constant time."""
    try:
        scheme, algorithm, iterations, salt_b64, key_b64 = stored.split("$")
        if scheme != "PBKDF2" or algorithm != "SHA256":
            return False
        salt = base64.b64decode(salt_b64)
        expected = base64.b64decode(key_b64)
        actual = hashlib.pbkdf2_hmac(
            "sha256", password.encode("utf-8"), salt, int(iterations), len(expected)
        )
        return hmac.compare_digest(actual, expected)
    except (ValueError, TypeError):
        return False


def _b64url_encode(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")


def _b64url_decode(data: str) -> bytes:
    padding = "=" * (-len(data) % 4)
    return base64.urlsafe_b64decode(data + padding)


def create_access_token(
    subject: str,
    role: str,
    *,
    secret: str,
    issuer: str,
    audience: str,
    expires_minutes: int,
) -> str:
    """Create a signed HS256 JWT."""
    now = int(time.time())
    header = {"alg": "HS256", "typ": "JWT"}
    payload = {
        "iss": issuer,
        "aud": audience,
        "sub": subject,
        "role": role,
        "iat": now,
        "exp": now + expires_minutes * 60,
        "jti": uuid.uuid4().hex,
    }
    signing_input = (
        f"{_b64url_encode(json.dumps(header, separators=(',', ':')).encode('utf-8'))}"
        f".{_b64url_encode(json.dumps(payload, separators=(',', ':')).encode('utf-8'))}"
    )
    signature = hmac.new(
        secret.encode("utf-8"), signing_input.encode("ascii"), hashlib.sha256
    ).digest()
    return f"{signing_input}.{_b64url_encode(signature)}"


def decode_access_token(
    token: str, *, secret: str, issuer: str, audience: str
) -> dict[str, Any]:
    """Verify signature, issuer, audience and expiry. Raises 401 on any failure."""
    unauthorized = HTTPException(
        status_code=status.HTTP_401_UNAUTHORIZED, detail="Invalid or expired token."
    )
    try:
        header_b64, payload_b64, signature_b64 = token.split(".")
    except ValueError:
        raise unauthorized from None

    signing_input = f"{header_b64}.{payload_b64}"
    expected = hmac.new(
        secret.encode("utf-8"), signing_input.encode("ascii"), hashlib.sha256
    ).digest()
    try:
        signature = _b64url_decode(signature_b64)
        payload = json.loads(_b64url_decode(payload_b64))
    except Exception:
        raise unauthorized from None
    if not hmac.compare_digest(expected, signature):
        raise unauthorized
    if payload.get("iss") != issuer or payload.get("aud") != audience:
        raise unauthorized
    try:
        if int(payload.get("exp", 0)) < int(time.time()):
            raise unauthorized
    except (TypeError, ValueError):
        raise unauthorized from None
    return payload


async def get_current_user(
    credentials: HTTPAuthorizationCredentials | None = Depends(_bearer_scheme),
) -> dict[str, Any]:
    """FastAPI dependency that requires a valid bearer token."""
    if credentials is None or credentials.scheme.lower() != "bearer":
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED, detail="Not authenticated."
        )
    if not settings.jwt_secret_key or len(settings.jwt_secret_key.encode("utf-8")) < MIN_SECRET_BYTES:
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="Authentication is not configured.",
        )
    return decode_access_token(
        credentials.credentials,
        secret=settings.jwt_secret_key,
        issuer=settings.jwt_issuer,
        audience=settings.jwt_audience,
    )


if __name__ == "__main__":
    password = sys.argv[1] if len(sys.argv) > 1 else input("Password to hash: ")
    print(hash_password(password))
