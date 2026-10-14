using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace SmartSchoolAPI.RateLimiting;

/// <summary>Names of the rate limiting policies applied to authentication endpoints.</summary>
public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Refresh = "refresh";
}

/// <summary>
/// Registers the authentication rate limiting policies.
///
/// Unlike <c>AddFixedWindowLimiter</c> (which creates a single partition keyed by the
/// policy name and therefore gives the whole system one shared quota), these policies
/// create one fixed window per client, so a single client cannot exhaust the login
/// quota of every other user.
/// </summary>
public static class SmartSchoolRateLimiting
{
    public static IServiceCollection AddSmartSchoolRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var loginPermitLimit = configuration.GetValue("Security:RateLimit:Login:PermitLimit", 5);
        var refreshPermitLimit = configuration.GetValue("Security:RateLimit:Refresh:PermitLimit", 20);
        var window = TimeSpan.FromSeconds(configuration.GetValue("Security:RateLimit:WindowSeconds", 60));

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RateLimitPolicies.Login, context => Partition(context, loginPermitLimit, window));
            options.AddPolicy(RateLimitPolicies.Refresh, context => Partition(context, refreshPermitLimit, window));

            options.OnRejected = async (context, token) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers["Retry-After"] =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                // Generic response: no policy names, client identifiers or limits are disclosed.
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { success = false, message = "Too many requests." }, token);
            };
        });

        return services;
    }

    private static RateLimitPartition<string> Partition(HttpContext context, int permitLimit, TimeSpan window)
    {
        var clientKey = context.RequestServices.GetRequiredService<IClientIdentityProvider>().GetClientKey(context);
        return RateLimitPartition.GetFixedWindowLimiter(clientKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit <= 0 ? 1 : permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = false
        });
    }
}
