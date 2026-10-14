namespace SmartSchoolAPI.RateLimiting;

/// <summary>
/// Supplies the partition key used by the rate limiting policies so that limits are
/// applied per client instead of globally.
/// </summary>
public interface IClientIdentityProvider
{
    string GetClientKey(HttpContext context);
}

/// <summary>
/// Default implementation: the key is the transport-level remote address.
/// Forwarding headers (X-Forwarded-For and friends) are deliberately ignored because
/// they are supplied by the caller and cannot be trusted unless a trusted reverse
/// proxy is explicitly configured (see docs/engineering/P0_A_RATE_LIMITING.md).
/// </summary>
public sealed class RemoteIpClientIdentityProvider : IClientIdentityProvider
{
    public const string UnknownClientKey = "unknown";

    public string GetClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? UnknownClientKey;
}
