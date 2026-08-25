using System.Globalization;

namespace Oblodai;

/// <summary>A source of unix-second timestamps for signing. Injectable so tests can pin it.</summary>
public interface IClock
{
    /// <summary>Current unix time in seconds.</summary>
    long NowUnixSeconds();
}

/// <summary>The machine clock.</summary>
public sealed class SystemClock : IClock
{
    /// <summary>The shared instance.</summary>
    public static readonly SystemClock Instance = new();

    /// <inheritdoc />
    public long NowUnixSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

/// <summary>
/// Signing clock with skew correction. The gateway rejects timestamps more than ±300 s from its own
/// time; a host with a drifting clock would get <c>merchant.bad_signature</c> on every call. The
/// transport learns the server's time from the <c>Date</c> header of a signature-failure response,
/// re-signs once, and keeps the offset only if that re-signed attempt got past authentication.
/// </summary>
public sealed class SkewCorrectingClock : IClock
{
    /// <summary>Offsets beyond this are implausible drift and are ignored (a broken proxy <c>Date</c>).</summary>
    public const long MaxPlausibleOffsetSeconds = 24 * 3600;

    private readonly IClock _base;
    private long _offsetSeconds;

    /// <summary>Wrap a base clock.</summary>
    /// <param name="baseClock">Underlying clock; the machine clock by default.</param>
    public SkewCorrectingClock(IClock? baseClock = null) => _base = baseClock ?? SystemClock.Instance;

    /// <summary>Server-minus-local offset currently applied, seconds.</summary>
    public long Offset => Interlocked.Read(ref _offsetSeconds);

    /// <inheritdoc />
    public long NowUnixSeconds() => _base.NowUnixSeconds() + Offset;

    /// <summary>
    /// Measure the offset implied by a response <c>Date</c> header; null when absent, unparsable or
    /// implausible.
    /// </summary>
    /// <param name="dateHeader">The <c>Date</c> header value.</param>
    public long? ObserveServerDate(string? dateHeader)
    {
        if (string.IsNullOrWhiteSpace(dateHeader)
            || !DateTimeOffset.TryParse(dateHeader, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var server))
        {
            return null;
        }

        return ObserveServerDate(server);
    }

    /// <summary>Measure the offset implied by a parsed response date; null when implausible.</summary>
    /// <param name="serverTime">The server's own time, as its <c>Date</c> header reported it.</param>
    public long? ObserveServerDate(DateTimeOffset? serverTime)
    {
        if (serverTime is null)
        {
            return null;
        }

        var offset = serverTime.Value.ToUnixTimeSeconds() - _base.NowUnixSeconds();
        return Math.Abs(offset) > MaxPlausibleOffsetSeconds ? null : offset;
    }

    /// <summary>Apply an offset (or revert to a previous one).</summary>
    /// <param name="offsetSeconds">Server-minus-local offset in seconds.</param>
    public void Correct(long offsetSeconds) => Interlocked.Exchange(ref _offsetSeconds, offsetSeconds);

    /// <summary>Drop any correction.</summary>
    public void Reset() => Correct(0);
}
