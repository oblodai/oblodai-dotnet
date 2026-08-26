namespace Oblodai;

/// <summary>
/// Retry policy. Two questions decide every retry:
/// <list type="number">
/// <item><description>
/// Can it succeed? — the gateway's <c>retryable</c> flag (authoritative when the gateway wrote the
/// envelope), or a transient status for answers that carry no envelope.
/// </description></item>
/// <item><description>
/// Is repeating safe? — only for read-only routes and for writes the gateway deduplicates by
/// Idempotency-Key. A write the gateway does not deduplicate is never re-sent once it MAY have
/// reached the gateway: a transport error or a proxy 503 after the request left the socket could
/// mean the payout already happened.
/// </description></item>
/// </list>
/// An envelope error on an unsafe write is still retried when <c>retryable</c> — the gateway
/// answered, so it did not perform the operation. <c>Retry-After</c> always wins over the computed
/// backoff; otherwise exponential backoff with jitter.
/// </summary>
public sealed record RetryOptions
{
    /// <summary>The defaults: 2 retries, 250 ms base, 4 s cap, 30 s cap on a server-provided Retry-After.</summary>
    public static readonly RetryOptions Default = new();

    /// <summary>Maximum number of retries after the first attempt. Default 2.</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Base delay for the first retry, ms. Default 250.</summary>
    public int BaseDelayMs { get; init; } = 250;

    /// <summary>Upper bound for a computed (non Retry-After) delay, ms. Default 4000.</summary>
    public int MaxDelayMs { get; init; } = 4000;

    /// <summary>Upper bound honoured for a server-provided Retry-After, ms. Default 30000.</summary>
    public int MaxRetryAfterMs { get; init; } = 30_000;
}

/// <summary>Decides whether and when to repeat an attempt.</summary>
public static class RetryPolicy
{
    /// <summary>Should the failed attempt be repeated?</summary>
    /// <param name="error">The failure.</param>
    /// <param name="attempt">0 for the first retry decision (after attempt #1 failed).</param>
    /// <param name="safeToRepeat">True when re-sending cannot duplicate a side effect.</param>
    /// <param name="options">Policy in force.</param>
    public static bool ShouldRetry(Exception? error, int attempt, bool safeToRepeat, RetryOptions options)
    {
        if (attempt >= options.MaxRetries || error is not OblodaiException err || !err.Retryable)
        {
            return false;
        }

        if (err is TransportException)
        {
            return safeToRepeat;
        }

        // No gateway envelope: something in front of the gateway answered; the gateway may have done the work.
        return !err.Synthetic || safeToRepeat;
    }

    /// <summary>Delay before the next attempt, in milliseconds.</summary>
    /// <param name="error">The failure that triggered the retry.</param>
    /// <param name="attempt">Zero-based retry index.</param>
    /// <param name="options">Policy in force.</param>
    /// <param name="random">Injectable randomness for deterministic tests.</param>
    public static int DelayMs(Exception? error, int attempt, RetryOptions options, Func<double>? random = null)
    {
        if (error is OblodaiException { RetryAfter: > 0 } err)
        {
            // In milliseconds a plausible Retry-After already exceeds int.MaxValue, and the wrapped value
            // is negative: the pause would vanish and three attempts would leave in the same millisecond.
            var requested = (long)err.RetryAfter!.Value * 1000L;
            return (int)Math.Clamp(requested, 0L, Math.Max(0L, options.MaxRetryAfterMs));
        }

        var exp = (int)Math.Clamp(
            (long)options.BaseDelayMs * (1L << Math.Min(Math.Max(attempt, 0), 20)),
            0L,
            Math.Max(0L, options.MaxDelayMs));
        var roll = (random ?? Random.Shared.NextDouble)();

        // Full jitter with a floor so a burst of retries never lands in the same instant.
        return Math.Max(exp / 4, (int)(roll * exp));
    }
}
