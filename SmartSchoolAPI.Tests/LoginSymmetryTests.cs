using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Models;
using SmartSchoolAPI.Security;

namespace SmartSchoolAPI.Tests;

/// <summary>
/// P0-B decision F5: the login answer must not reveal whether an account exists.
///
/// The status code and the body were already identical for "unknown account" and "wrong
/// password"; what differed was the work performed (PBKDF2 ran only for existing accounts) and
/// therefore the response time. <see cref="LoginCredentialCheck"/> now always performs the hash
/// verification, and these tests pin both halves of that promise: the observable response, and
/// the internal invariant that the expensive step is never skipped.
/// </summary>
public sealed class LoginSymmetryTests
{
    private const string Password = "Correct123!";

    private static HttpClient Client(IntegrationTestFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task UnknownUsernameAndWrongPassword_ProduceIdenticalResponses()
    {
        using var factory = new IntegrationTestFactory();
        factory.SeedUser("symmetry-user", Password, RoleNames.Student);
        using var client = Client(factory);

        using var unknownAccount = await client.PostAsJsonAsync("/api/auth/login", new { Username = "no-such-account", Password });
        using var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { Username = "symmetry-user", Password = "Wrong123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, unknownAccount.StatusCode);
        Assert.Equal(wrongPassword.StatusCode, unknownAccount.StatusCode);

        var unknownBody = await unknownAccount.Content.ReadAsStringAsync();
        var wrongPasswordBody = await wrongPassword.Content.ReadAsStringAsync();
        Assert.Equal(wrongPasswordBody, unknownBody);
    }

    [Fact]
    public async Task UnknownUsername_IsStillAuditedAsAFailure()
    {
        using var factory = new IntegrationTestFactory();
        var existingUserId = factory.SeedUser("audit-user", Password, RoleNames.Student);
        using var client = Client(factory);

        using var unknownUserFailure = await client.PostAsJsonAsync("/api/auth/login", new { Username = "audit-unknown", Password });
        using var wrongPasswordFailure = await client.PostAsJsonAsync("/api/auth/login", new { Username = "audit-user", Password = "Wrong123!" });

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, unknownUserFailure.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, wrongPasswordFailure.StatusCode);

        var failures = factory.AuditLogs().Where(log => log.Action == "LOGIN_FAILURE").ToList();

        // The only remaining asymmetry is server-side and deliberate: the audit can attribute the
        // failure to an account when one exists. The client sees the same 401 body either way.
        Assert.Contains(failures, log => log.UserId == existingUserId);
        Assert.Contains(failures, log => log.UserId is null);
    }

    [Fact]
    public void Verify_ForAnUnknownAccount_StillPerformsThePasswordHashCheck()
    {
        var result = LoginCredentialCheck.Verify(null, "Whatever123!");

        Assert.False(result.AccountExists);
        Assert.False(result.PasswordMatches);
        Assert.True(result.PasswordHashVerified, "the PBKDF2 verification must run even when the account does not exist");
    }

    [Fact]
    public void Verify_ReportsAccountExistenceSeparatelyFromPasswordMatch()
    {
        var user = new User { Username = "verify-user", PasswordHash = PasswordService.Hash(Password) };

        var match = LoginCredentialCheck.Verify(user, Password);
        Assert.True(match.AccountExists);
        Assert.True(match.PasswordMatches);
        Assert.True(match.PasswordHashVerified);

        var mismatch = LoginCredentialCheck.Verify(user, "Wrong123!");
        Assert.True(mismatch.AccountExists);
        Assert.False(mismatch.PasswordMatches);

        var accountWithoutHash = LoginCredentialCheck.Verify(new User { Username = "no-hash", PasswordHash = string.Empty }, Password);
        Assert.False(accountWithoutHash.AccountExists);
        Assert.False(accountWithoutHash.PasswordMatches);
        Assert.True(accountWithoutHash.PasswordHashVerified);
    }

    [Fact]
    public void DummyHash_HasTheSameCostParametersAsRealHashes()
    {
        static string[] Parts(string hash) => hash.Split('$');

        var real = Parts(PasswordService.Hash(Password));
        var dummy = Parts(PasswordService.DummyHash);

        Assert.Equal(5, real.Length);
        Assert.Equal(5, dummy.Length);
        Assert.Equal(real[0], dummy[0]);
        Assert.Equal(real[1], dummy[1]);
        Assert.Equal(real[2], dummy[2]); // iteration count drives the cost of the verification
        Assert.Equal(Convert.FromBase64String(real[3]).Length, Convert.FromBase64String(dummy[3]).Length);
        Assert.Equal(Convert.FromBase64String(real[4]).Length, Convert.FromBase64String(dummy[4]).Length);
    }
}
