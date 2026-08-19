using System.Text.Json;
using SmartSchoolAPI.Controllers;
using SmartSchoolAPI.Models;
using SmartSchoolAPI.Security;

namespace SmartSchoolAPI.Tests;

public sealed class SecurityBaselineTests
{
    [Fact]
    public void PasswordHash_VerifiesCorrectPassword()
    {
        var hash = PasswordService.Hash("CorrectPassword123!");
        Assert.True(PasswordService.Verify("CorrectPassword123!", hash));
        Assert.False(PasswordService.Verify("WrongPassword123!", hash));
    }

    [Fact]
    public void SamePassword_UsesDifferentSalt()
    {
        var first = PasswordService.Hash("SamePassword123!");
        var second = PasswordService.Hash("SamePassword123!");
        Assert.NotEqual(first, second);
        Assert.True(PasswordService.Verify("SamePassword123!", first));
        Assert.True(PasswordService.Verify("SamePassword123!", second));
    }

    [Fact]
    public void Plaintext_IsNotAcceptedAsPasswordHash()
    {
        Assert.False(PasswordService.Verify("Plaintext123!", "Plaintext123!"));
    }

    [Fact]
    public void UserSerialization_DoesNotExposePasswordHash()
    {
        var json = JsonSerializer.Serialize(new User { UserId = 1, Username = "user", PasswordHash = PasswordService.Hash("Secret123!"), Role = "STUDENT" });
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret123!", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PBKDF2", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoginResponse_ContainsOnlyPublicUserFields()
    {
        var json = JsonSerializer.Serialize(new LoginResponse { AccessToken = "test-token", User = new UserDto { UserId = 1, Username = "user", Role = "STUDENT" } });
        Assert.Contains("AccessToken", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", json, StringComparison.OrdinalIgnoreCase);
    }
}
