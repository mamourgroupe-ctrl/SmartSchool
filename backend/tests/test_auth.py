"""Tests for the login endpoint (env-configured credentials, real signed JWT)."""

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from app.api.v1.auth import router as auth_router
from app.core import security
from app.core.config import settings

SECRET = "test-secret-key-0123456789012345678901"


def _app() -> FastAPI:
    app = FastAPI()
    app.include_router(auth_router, prefix="/api/v1")
    return app


@pytest.fixture()
def client(monkeypatch):
    monkeypatch.setattr(settings, "api_username", "admin")
    monkeypatch.setattr(settings, "api_password_hash", security.hash_password("Correct123!"))
    monkeypatch.setattr(settings, "jwt_secret_key", SECRET)
    return TestClient(_app())


def test_login_success_returns_signed_jwt(client):
    response = client.post(
        "/api/v1/auth/login", json={"username": "admin", "password": "Correct123!"}
    )
    assert response.status_code == 200
    body = response.json()
    assert body["token"] != "jwt-sample-token-12345"
    payload = security.decode_access_token(
        body["token"],
        secret=SECRET,
        issuer=settings.jwt_issuer,
        audience=settings.jwt_audience,
    )
    assert payload["sub"] == "admin"
    assert payload["role"] == "Admin"


def test_login_wrong_password_returns_401(client):
    response = client.post(
        "/api/v1/auth/login", json={"username": "admin", "password": "Wrong123!"}
    )
    assert response.status_code == 401


def test_login_unknown_user_returns_401(client):
    response = client.post(
        "/api/v1/auth/login", json={"username": "nobody", "password": "Correct123!"}
    )
    assert response.status_code == 401


def test_login_fails_closed_when_not_configured(monkeypatch):
    monkeypatch.setattr(settings, "api_username", "")
    monkeypatch.setattr(settings, "api_password_hash", "")
    monkeypatch.setattr(settings, "jwt_secret_key", "")
    client = TestClient(_app())
    response = client.post(
        "/api/v1/auth/login", json={"username": "admin", "password": "password"}
    )
    assert response.status_code == 503


def test_login_rejects_short_jwt_secret(monkeypatch):
    monkeypatch.setattr(settings, "api_username", "admin")
    monkeypatch.setattr(
        settings, "api_password_hash", security.hash_password("Correct123!")
    )
    monkeypatch.setattr(settings, "jwt_secret_key", "short")
    client = TestClient(_app())
    response = client.post(
        "/api/v1/auth/login", json={"username": "admin", "password": "Correct123!"}
    )
    assert response.status_code == 503
