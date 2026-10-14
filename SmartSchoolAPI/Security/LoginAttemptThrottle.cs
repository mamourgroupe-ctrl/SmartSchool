using System.Collections.Concurrent;

namespace SmartSchoolAPI.Security;

/// <summary>Configuration for <see cref="LoginAttemptThrottle"/>.</summary>
public sealed class LoginThrottleOptions
{
    /// <summary>Failed attempts allowed inside a single window before the account is temporarily throttled.</summary>
    public int FailureLimit { get; init; } = 5;

    /// <summary>Length of the fixed window. The window index changes over time, so the throttle always clears itself.</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Upper bound on tracked accounts before stale windows are pruned.</summary>
    public int MaxTrackedAccounts { get; init; } = 5000;
}

/// <summary>
/// Temporary, self-clearing throttle for failed login attempts.
///
/// design notes:
/// - Failures are counted per normalized username inside a fixed time window. There is
///   deliberately no permanent account lock, so an attacker cannot deny an account owner
///   access forever: once the window rolls over the account is usable again.
/// - Successful authentication clears the counter.
/// - The throttle is per process (in-memory). A multi-instance deployment will need a
///   shared store; this is documented in docs/engineering/P0_A_RATE_LIMITING.md.
/// </summary>
public sealed class LoginAttemptThrottle
{
    private const int MaxKeyLength = 256;

    private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly LoginThrottleOptions _options;

    public LoginAttemptThrottle(TimeProvider timeProvider, LoginThrottleOptions options)
    {
        _timeProvider = timeProvider;
        _options = options;
    }

    /// <summary>Returns true when the account reached the failure limit inside the current window.</summary>
    public bool IsBlocked(string username, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        if (!_counters.TryGetValue(Normalize(username), out var counter)) return false;

        var windowIndex = CurrentWindowIndex();
        lock (counter)
        {
            if (counter.WindowIndex != windowIndex || counter.Count < Limit) return false;
        }

        retryAfterSeconds = SecondsRemainingInWindow();
        return true;
    }

    /// <summary>Records one failed attempt. The counter is bounded, so flooding cannot grow it without limit.</summary>
    public void RecordFailure(string username)
    {
        PruneStaleWindows();
        var windowIndex = CurrentWindowIndex();
        var counter = _counters.GetOrAdd(Normalize(username), _ => new Counter(windowIndex));
        lock (counter)
        {
            if (counter.WindowIndex != windowIndex)
            {
                counter.WindowIndex = windowIndex;
                counter.Count = 0;
            }

            if (counter.Count < Limit) counter.Count++;
        }
    }

    /// <summary>Clears the tracked failures after a successful authentication.</summary>
    public void Reset(string username) => _counters.TryRemove(Normalize(username), out _);

    private int Limit => _options.FailureLimit <= 0 ? 1 : _options.FailureLimit;

    private long WindowSeconds
    {
        get
        {
            var seconds = (long)_options.Window.TotalSeconds;
            return seconds <= 0 ? 60 : seconds;
        }
    }

    private long CurrentWindowIndex() => _timeProvider.GetUtcNow().ToUnixTimeSeconds() / WindowSeconds;

    private int SecondsRemainingInWindow()
    {
        var seconds = WindowSeconds;
        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var remaining = seconds - (now % seconds);
        return (int)(remaining <= 0 ? 1 : remaining);
    }

    private void PruneStaleWindows()
    {
        if (_counters.Count < (_options.MaxTrackedAccounts <= 0 ? 5000 : _options.MaxTrackedAccounts)) return;

        var windowIndex = CurrentWindowIndex();
        foreach (var pair in _counters)
        {
            if (pair.Value.WindowIndex != windowIndex) _counters.TryRemove(pair.Key, out _);
        }
    }

    /// <summary>Login identifiers are compared case-insensitively after trimming, matching how usernames are treated.</summary>
    private static string Normalize(string username)
    {
        var normalized = username is null ? string.Empty : username.Trim().ToLowerInvariant();
        return normalized.Length > MaxKeyLength ? normalized[..MaxKeyLength] : normalized;
    }

    private sealed class Counter
    {
        public Counter(long windowIndex) => WindowIndex = windowIndex;

        public long WindowIndex { get; set; }

        public int Count { get; set; }
    }
}
