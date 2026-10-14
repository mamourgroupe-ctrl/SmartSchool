using SmartSchoolAPI.Models;

namespace SmartSchoolAPI.Security;

/// <summary>
/// Outcome of the credential check performed by the login endpoint.
/// </summary>
/// <param name="AccountExists">False when no account matches the submitted username.</param>
/// <param name="PasswordMatches">True only when the account exists and the password matches its stored hash.</param>
/// <param name="PasswordHashVerified">
/// True whenever the PBKDF2 verification ran — which is always, including for unknown usernames.
/// The flag is not decoration: it is the invariant that keeps registered and unregistered
/// usernames equally expensive to answer, and a unit test pins it so a future refactor that
/// short-circuits the verification fails CI instead of silently reopening the enumeration oracle
/// (P0-B decision F5).
/// </param>
public readonly record struct CredentialCheckResult(bool AccountExists, bool PasswordMatches, bool PasswordHashVerified);

/// <summary>
/// Performs the username/password check for login without revealing whether the account exists.
///
/// The stored hash is verified when there is an account and a fixed dummy hash otherwise, so the
/// work done and the response time do not depend on the username being registered. The result is
/// identical for "unknown account" and "wrong password", and the caller answers both with the
/// same 401 body.
/// </summary>
public static class LoginCredentialCheck
{
    public static CredentialCheckResult Verify(User? user, string password)
    {
        var accountExists = user is not null && !string.IsNullOrWhiteSpace(user.PasswordHash);

        // Unconditional on purpose: an early return for an unknown username would skip ~210k
        // PBKDF2 iterations and make the account-existence oracle measurable.
        var storedHash = accountExists ? user!.PasswordHash : PasswordService.DummyHash;
        var passwordMatches = PasswordService.Verify(password, storedHash);

        return new CredentialCheckResult(
            AccountExists: accountExists,
            PasswordMatches: passwordMatches && accountExists,
            PasswordHashVerified: true);
    }
}
