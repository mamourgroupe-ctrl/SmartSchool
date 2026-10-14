using System.Net;
using Microsoft.AspNetCore.Http;
using SmartSchoolAPI.RateLimiting;

namespace SmartSchoolAPI.Tests;

/// <summary>
/// P0-A: the production partition key is derived from the transport remote address only.
/// Forwarded headers are caller-supplied and must never influence the key, otherwise a
/// client could reset its own quota on every request.
/// </summary>
public sealed class ClientIdentityTests
{
    private static HttpContext ContextWith(IPAddress? remoteAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteAddress;
        return context;
    }

    [Fact]
    public void Key_UsesTransportRemoteAddress()
    {
        var context = ContextWith(IPAddress.Parse("10.11.12.13"));

        Assert.Equal("10.11.12.13", new RemoteIpClientIdentityProvider().GetClientKey(context));
    }

    [Fact]
    public void Key_IgnoresForwardedAndCustomHeaders()
    {
        var context = ContextWith(IPAddress.Parse("10.11.12.13"));
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
        context.Request.Headers["X-Real-IP"] = "203.0.113.8";
        context.Request.Headers["X-Test-Client"] = "spoofed-client";
        context.Request.Headers["Forwarded"] = "for=203.0.113.9";

        Assert.Equal("10.11.12.13", new RemoteIpClientIdentityProvider().GetClientKey(context));
    }

    [Fact]
    public void Key_FallsBackToAConstant_WhenRemoteAddressIsUnavailable()
    {
        var context = ContextWith(null);

        var provider = new RemoteIpClientIdentityProvider();
        Assert.Equal(RemoteIpClientIdentityProvider.UnknownClientKey, provider.GetClientKey(context));
        Assert.Equal(RemoteIpClientIdentityProvider.UnknownClientKey, provider.GetClientKey(ContextWith(null)));
    }
}
