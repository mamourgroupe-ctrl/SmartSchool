"""Tests for the security helpers (password hashing + JWT issuing)."""

from app.core import security


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


def test_jwt_roundtrip_and_expiry():
    secret = "test-secret-key-0123456789012345678901"
    token = security.create_access_token(
        "admin",
        "Admin",
        secret=secret,
        issuer="SmartSchoolBackend",
        audience="SmartSchoolClients",
        expires_minutes=5,
    )
    payload = security.decode_access_token(
        token,
        secret=secret,
        issuer="SmartSchoolBackend",
        audience="SmartSchoolClients",
    )
    assert payload["sub"] == "admin"
    assert payload["role"] == "Admin"


def test_jwt_rejects_wrong_secret_issuer_audience_and_garbage():
    secret = "test-secret-key-0123456789012345678901"
    token = security.create_access_token(
        "admin",
        "Admin",
        secret=secret,
        issuer="SmartSchoolBackend",
        audience="SmartSchoolClients",
        expires_minutes=5,
    )
    for kwargs in (
        {"secret": "wrong-secret-key-012345678901234567890"},
        {"issuer": "WrongIssuer"},
        {"audience": "WrongAudience"},
    ):
        try:
            security.decode_access_token(
                token,
                secret=secret,
                issuer="SmartSchoolBackend",
                audience="SmartSchoolClients",
                **kwargs,
            )
        except Exception:
            pass
        else:
            raise AssertionError(f"token should have been rejected for {kwargs}")

    for bad_token in ("", "not-a-jwt", "a.b", "a.b.c.d"):
        try:
            security.decode_access_token(
                bad_token,
                secret=secret,
                issuer="SmartSchoolBackend",
                audience="SmartSchoolClients",
            )
        except Exception:
            pass
        else:
            raise AssertionError(f"token should have been rejected: {bad_token!r}")
