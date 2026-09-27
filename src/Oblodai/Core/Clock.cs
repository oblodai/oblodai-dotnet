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
/// Signing clock with skew correction. The gateway rejects timestamps more than
/// <see cref="Contract.SigningProtocol.SkewSeconds"/> away from its own time; a host with a drifting clock would get <c>merchant.bad_signature</c> on every call. The
/// transport learns the server's time from the <c>Date</c> header of a signature-failure response,
/// re-signs once, and adopts the offset only if that re-signed attempt succeeded (2xx). An offset beyond
/// ±<see cref="MaxCorrectionSeconds"/> is never used: a hostile or broken responder could otherwise push
/// every later signature hours into the future (delayed replay).
/// </summary>
public sealed class SkewCorrectingClock : IClock
{
    /// <summary>
    /// The largest correction ever applied, in seconds (15 minutes): an offset beyond it, or a move of the
    /// offset beyond it, is ignored.
    /// </summary>
    public const long MaxCorrectionSeconds = 900;

    /// <summary>Kept for compatibility; equal to <see cref="MaxCorrectionSeconds"/>.</summary>
    [Obsolete("use MaxCorrectionSeconds")]
    public const long MaxPlausibleOffsetSeconds = MaxCorrectionSeconds;

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
        return Math.Abs(offset) > MaxCorrectionSeconds ? null : offset;
    }

    /// <summary>The underlying clock, without the correction — what an attempt's timestamp is built from.</summary>
    public long BaseNowUnixSeconds() => _base.NowUnixSeconds();

    /// <summary>Apply an offset (or revert to a previous one); one beyond ±<see cref="MaxCorrectionSeconds"/> is ignored.</summary>
    /// <param name="offsetSeconds">Server-minus-local offset in seconds.</param>
    public void Correct(long offsetSeconds)
    {
        if (Math.Abs(offsetSeconds) <= MaxCorrectionSeconds)
        {
            Interlocked.Exchange(ref _offsetSeconds, offsetSeconds);
        }
    }

    /// <summary>
    /// Revert to <paramref name="offsetSeconds"/> only while the shared offset is still
    /// <paramref name="expected"/>. One client is shared by every request in a process, so a call
    /// undoing its own failed correction must not undo a correction another call has since installed.
    /// </summary>
    /// <param name="expected">The offset this caller installed.</param>
    /// <param name="offsetSeconds">What to put back.</param>
    /// <returns>True when the swap happened.</returns>
    public bool CorrectIfUnchanged(long expected, long offsetSeconds)
        => Interlocked.CompareExchange(ref _offsetSeconds, offsetSeconds, expected) == expected;

    /// <summary>Drop any correction.</summary>
    public void Reset() => Correct(0);
}
