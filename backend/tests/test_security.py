"""Tests for the security helpers (password hashing + JWT issuing)."""

import pytest
from fastapi import HTTPException

from app.core import security


JWT_VERIFICATION = {
    "secret": "test-secret-key-0123456789012345678901",
    "issuer": "SmartSchoolBackend",
    "audience": "SmartSchoolClients",
}
INVALID_VERIFICATION = (
    pytest.param({"secret": "wrong-secret-key-012345678901234567890"}, id="wrong-secret"),
    pytest.param({"issuer": "WrongIssuer"}, id="wrong-issuer"),
    pytest.param({"audience": "WrongAudience"}, id="wrong-audience"),
)


def _issue_token(expires_minutes: int = 5) -> str:
    return security.create_access_token(
        "admin", "Admin", **JWT_VERIFICATION, expires_minutes=expires_minutes
    )


def _assert_rejected(token: str, verification: dict[str, str]) -> None:
    with pytest.raises(HTTPException) as exc_info:
        security.decode_access_token(token, **verification)
    assert exc_info.value.status_code == 401


def test_hash_and_verify_password_roundtrip():
    stored = security.hash_password("Correct123!")
    assert stored.startswith("PBKDF2$SHA256$")
    assert security.verify_password("Correct123!", stored)
    assert not security.verify_password("Wrong123!", stored)


def test_same_password_uses_different_salt():
    first = security.hash_password("SamePassword123!")
    second = security.hash_password("SamePassword123!")
    assert first != second
    assert security.verify_password("SamePassword123!", first)
    assert security.verify_password("SamePassword123!", second)


def test_verify_rejects_malformed_hash():
    assert not security.verify_password("x", "not-a-hash")
    assert not security.verify_password("x", "PLAIN$TEXT$1$aa$bb")
    assert not security.verify_password("x", "")


def test_jwt_roundtrip():
    payload = security.decode_access_token(_issue_token(), **JWT_VERIFICATION)
    assert payload["sub"] == "admin"
    assert payload["role"] == "Admin"


@pytest.mark.parametrize("override", INVALID_VERIFICATION)
def test_jwt_rejects_wrong_verification_parameters(override):
    # Merge first so the verifier is actually called, without duplicate kwargs.
    _assert_rejected(_issue_token(), {**JWT_VERIFICATION, **override})


@pytest.mark.parametrize("token", ["", "not-a-jwt", "a.b", "a.b.c", "a.b.c.d"])
def test_jwt_rejects_malformed_tokens(token):
    _assert_rejected(token, JWT_VERIFICATION)


def test_jwt_rejects_expired_token():
    _assert_rejected(_issue_token(expires_minutes=-1), JWT_VERIFICATION)


def test_jwt_rejects_tampered_signature():
    header, payload, signature = _issue_token().split(".")
    replacement = "A" if signature[0] != "A" else "B"
    _assert_rejected(f"{header}.{payload}.{replacement}{signature[1:]}", JWT_VERIFICATION)


@pytest.mark.parametrize("override", INVALID_VERIFICATION)
def test_invalid_jwt_check_fails_if_decoder_accepts_token(monkeypatch, override):
    """Exercise the rejection test itself against an intentionally broken verifier."""
    calls = []

    def accept_any_token(token, **verification):
        calls.append((token, verification))
        return {"sub": "incorrectly accepted"}

    monkeypatch.setattr(security, "decode_access_token", accept_any_token)
    with pytest.raises(pytest.fail.Exception, match="DID NOT RAISE"):
        test_jwt_rejects_wrong_verification_parameters(override)

    assert len(calls) == 1
    assert calls[0][1] == {**JWT_VERIFICATION, **override}
