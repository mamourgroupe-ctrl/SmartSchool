using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartSchoolAPI.Authorization;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Models;
using SmartSchoolAPI.Security;

namespace SmartSchoolAPI.Tests;

public sealed class RefreshTokenTests
{
    private static IntegrationTestFactory NewFactory() => new();

    private static HttpClient Client(IntegrationTestFactory factory) => factory.CreateClient();

    private static async Task<(string AccessToken, string RefreshToken)> Login(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = username, Password = password });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (json.RootElement.GetProperty("accessToken").GetString()!, json.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private static void Bearer(HttpClient client, string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task Login_ReturnsRefreshToken_And_RefreshRotatesTokenPair()
    {
        using var factory = NewFactory(); factory.SeedUser("refresh-user", "Correct123!", RoleNames.Student); using var client = Client(factory);
        var (accessToken, refreshToken) = await Login(client, "refresh-user", "Correct123!");
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = refreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        using var json = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());
        var newAccess = json.RootElement.GetProperty("accessToken").GetString()!;
        var newRefresh = json.RootElement.GetProperty("refreshToken").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(newAccess));
        Assert.False(string.IsNullOrWhiteSpace(newRefresh));
        Assert.NotEqual(refreshToken, newRefresh);

        // The rotated (new) access token works on protected endpoints.
        using var authed = Client(factory); Bearer(authed, newAccess);
        Assert.Equal(HttpStatusCode.OK, (await authed.GetAsync("/api/students")).StatusCode);

        // The old refresh token is revoked and cannot be reused.
        var reuse = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = refreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Contains(factory.AuditLogs(), x => x.Action == "TOKEN_REFRESHED");
    }

    [Fact]
    public async Task Refresh_WithUnknownToken_Returns401_AndAuditsFailure()
    {
        using var factory = NewFactory(); using var client = Client(factory);
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = "not-a-real-token-value" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(factory.AuditLogs(), x => x.Action == "TOKEN_REFRESH_FAILURE");
    }

    [Fact]
    public async Task Refresh_WithExpiredToken_Returns401()
    {
        using var factory = NewFactory();
        var userId = factory.SeedUser("expired-refresh", "Correct123!", RoleNames.Student);
        var rawToken = "expired-refresh-token-value-0123456789";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
            db.RefreshTokens.Add(new RefreshToken
            {
                UserId = userId,
                TokenHash = RefreshTokenService.Hash(rawToken),
                ExpiresAtUtc = DateTime.UtcNow.AddDays(-1)
            });
            db.SaveChanges();
        }

        using var client = Client(factory);
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = rawToken });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesAllRefreshTokens()
    {
        using var factory = NewFactory(); factory.SeedUser("logout-user", "Correct123!", RoleNames.Student); using var client = Client(factory);
        var (accessToken, refreshToken) = await Login(client, "logout-user", "Correct123!");

        Bearer(client, accessToken);
        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = refreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Contains(factory.AuditLogs(), x => x.Action == "LOGOUT");
    }

    [Fact]
    public async Task Logout_RequiresAuthentication()
    {
        using var factory = NewFactory(); using var client = Client(factory);
        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, logout.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_IsStoredAsHash_NotPlaintext()
    {
        using var factory = NewFactory(); factory.SeedUser("hash-user", "Correct123!", RoleNames.Student); using var client = Client(factory);
        var (_, refreshToken) = await Login(client, "hash-user", "Correct123!");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var stored = db.RefreshTokens.Single();
        Assert.Equal(RefreshTokenService.Hash(refreshToken), stored.TokenHash);
        Assert.NotEqual(refreshToken, stored.TokenHash);
        Assert.DoesNotContain(refreshToken, stored.TokenHash);
    }

    [Fact]
    public async Task RefreshRateLimit_Returns429AfterTwentyRequests()
    {
        using var factory = NewFactory(); using var client = Client(factory);
        HttpResponseMessage? last = null;
        for (var i = 0; i < 21; i++)
            last = await client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = "rate-limit-probe-token" });
        Assert.Equal((HttpStatusCode)429, last!.StatusCode);
    }
}
