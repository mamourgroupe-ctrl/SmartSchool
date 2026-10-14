using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSchoolAPI.Authorization;

namespace SmartSchoolAPI.Tests;

/// <summary>
/// P0-A: authentication quotas must be partitioned per client. Before this change the
/// limiter used a single shared partition per policy, so one client could consume the
/// login quota of every user in the system.
/// </summary>
public sealed class LoginRateLimitTests
{
    private const int LoginPermitLimit = 5;
    private const int RefreshPermitLimit = 20;

    private static IntegrationTestFactory NewFactory() => new();

    private static HttpClient Client(IntegrationTestFactory factory, string? clientKey = null)
    {
        var client = factory.CreateClient();
        if (clientKey is not null)
        {
            client.DefaultRequestHeaders.Add(TestClientIdentityProvider.HeaderName, clientKey);
        }

        return client;
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string username, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { Username = username, Password = password });

    /// <summary>Body that fails model validation (no database access) so limiter behaviour is isolated.</summary>
    private static HttpRequestMessage InvalidLoginRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { Username = "ab", Password = "short" })
        };
        return request;
    }

    [Fact]
    public async Task LoginQuota_IsPerClient_OneClientCannotExhaustAnother()
    {
        using var factory = NewFactory();
        factory.SeedUser("quota-user", "Correct123!", RoleNames.Student);
        using var clientA = Client(factory, "client-a");
        using var clientB = Client(factory, "client-b");

        for (var i = 0; i < LoginPermitLimit; i++)
        {
            using var allowed = await Login(clientA, "quota-user", "Correct123!");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        // Client A is now over its own quota.
        using var exhausted = await Login(clientA, "quota-user", "Correct123!");
        Assert.Equal((HttpStatusCode)429, exhausted.StatusCode);

        // ...but an unrelated client still has its own untouched quota.
        using var unaffected = await Login(clientB, "quota-user", "Correct123!");
        Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
    }

    [Fact]
    public async Task LoginQuota_UnderConcurrency_AdmitsAtMostTheLimitAndNeverFailsWith500()
    {
        using var factory = NewFactory();
        using var client = Client(factory, "burst-client");

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(_ => client.SendAsync(InvalidLoginRequest())));
        var codes = responses.Select(response => (int)response.StatusCode).ToList();
        foreach (var response in responses) response.Dispose();

        Assert.Equal(12, codes.Count);
        Assert.True(codes.Count(code => code == 200 || code == 400) <= LoginPermitLimit,
            $"admitted={codes.Count(code => code == 200 || code == 400)}, expected <= {LoginPermitLimit}");
        Assert.Contains(429, codes);
        Assert.DoesNotContain(500, codes);
    }

    [Fact]
    public async Task LoginQuota_IgnoresUntrustedForwardedHeaders()
    {
        using var factory = NewFactory();
        factory.SeedUser("xff-user", "Correct123!", RoleNames.Student);
        using var client = Client(factory);

        // Every request claims to come from a different address. If the header were trusted
        // each request would open a fresh quota; because it is not, the quota stays shared.
        for (var i = 0; i < LoginPermitLimit; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { Username = "xff-user", Password = "Correct123!" })
            };
            request.Headers.Add("X-Forwarded-For", $"203.0.113.{i}");
            using var allowed = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var spoofed = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { Username = "xff-user", Password = "Correct123!" })
        };
        spoofed.Headers.Add("X-Forwarded-For", "203.0.113.250");
        using var rejected = await client.SendAsync(spoofed);
        Assert.Equal((HttpStatusCode)429, rejected.StatusCode);
    }

    [Fact]
    public async Task RejectedRequest_Returns429WithRetryAfterAndWithoutDetails()
    {
        using var factory = NewFactory();
        using var client = Client(factory, "header-client");

        for (var i = 0; i < LoginPermitLimit; i++)
        {
            using var admitted = await client.SendAsync(InvalidLoginRequest());
            Assert.Equal(HttpStatusCode.BadRequest, admitted.StatusCode);
        }

        using var rejected = await client.SendAsync(InvalidLoginRequest());
        Assert.Equal((HttpStatusCode)429, rejected.StatusCode);
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.NotEmpty(retryAfter!);

        var body = await rejected.Content.ReadAsStringAsync();
        Assert.Contains("Too many requests", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SmartSchoolAPI.Tests", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshQuota_IsPerClient()
    {
        using var factory = NewFactory();
        using var clientA = Client(factory, "refresh-client-a");
        using var clientB = Client(factory, "refresh-client-b");

        for (var i = 0; i < RefreshPermitLimit; i++)
        {
            using var attempt = await clientA.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = "invalid-refresh-token-0123456789" });
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        using var exhausted = await clientA.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = "invalid-refresh-token-0123456789" });
        Assert.Equal((HttpStatusCode)429, exhausted.StatusCode);

        using var unaffected = await clientB.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = "invalid-refresh-token-0123456789" });
        Assert.Equal(HttpStatusCode.Unauthorized, unaffected.StatusCode);
    }

    [Fact]
    public async Task RejectionResponse_DoesNotLeakPolicyOrClientIdentifiers()
    {
        using var factory = NewFactory();
        using var client = Client(factory, "leak-check-client");

        HttpResponseMessage? rejected = null;
        for (var i = 0; i <= LoginPermitLimit; i++)
        {
            rejected?.Dispose();
            rejected = await client.SendAsync(InvalidLoginRequest());
        }

        Assert.NotNull(rejected);
        using var response = rejected!;
        Assert.Equal((HttpStatusCode)429, response.StatusCode);

        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Too many requests.", payload.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("leak-check-client", payload.RootElement.ToString(), StringComparison.Ordinal);
    }
}
