using Microsoft.AspNetCore.Http;
using SmartSchoolAPI.RateLimiting;

namespace SmartSchoolAPI.Tests;

/// <summary>
/// Test-only client identity provider: an explicit header lets a test simulate several
/// independent clients through the in-process test server (where the remote address is
/// always null). Registered only by <see cref="IntegrationTestFactory"/>; production uses
/// <see cref="RemoteIpClientIdentityProvider"/>, which ignores request headers.
/// </summary>
public sealed class TestClientIdentityProvider : IClientIdentityProvider
{
    public const string HeaderName = "X-Test-Client";

    public string GetClientKey(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            var candidate = values.ToString();
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? RemoteIpClientIdentityProvider.UnknownClientKey;
    }
}
