using System.Globalization;
using Oblodai.Contract;
using Oblodai.Resources;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// The library is published with <c>InvariantGlobalization</c>, but the application that consumes it is
/// not: a service running under <c>de-DE</c> formats 1.5 as "1,5" and parses "1.5" as fifteen. Every
/// number the SDK puts on the wire or reads off it has to be invariant regardless of the ambient
/// culture, so the test project turns globalization back on and pins a hostile one.
/// </summary>
public class CultureTests
{
    private static readonly CultureInfo CommaDecimal = new("de-DE");

    private static T Under<T>(CultureInfo culture, Func<T> action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AmountsCompareAndAddTheSameUnderACommaDecimalCulture()
    {
        Assert.True(Under(CommaDecimal, () => Money.AreEqual("25", "25.000000")));
        Assert.Equal("0.3", Under(CommaDecimal, () => Money.Add("0.1", "0.2")));
        Assert.Equal(1, Under(CommaDecimal, () => Money.Compare("1.5", "1.05")));

        // "1,5" is a valid number in this culture and still not a valid amount on the wire.
        var error = Under(CommaDecimal, () => Assert.Throws<ConfigException>(() => Money.Compare("1,5", "1")));
        Assert.Equal(SdkErrorCodes.BadAmount, error.Code);
    }

    [Fact]
    public void RetryAfterIsReadAsInvariantWhateverTheAmbientCultureThinksADotMeans()
    {
        Assert.Equal(2, Under(CommaDecimal, () => EnvelopeDecoder.ParseRetryAfter("1.5")));
        Assert.Equal(
            120,
            Under(CommaDecimal, () => EnvelopeDecoder.ParseRetryAfter("Thu, 01 Jan 1970 00:02:00 GMT", DateTimeOffset.UnixEpoch)));
    }

    [Fact]
    public void TheSignedTimestampIsInvariantDigitsAndNothingElse()
    {
        var built = Under(CommaDecimal, () => RequestBuilder.Build(new BuildInput
        {
            BaseUrl = "https://api.test",
            Route = Routes.GetBalance,
            Credentials = new Credentials("pk", "s"),
            Ts = 1_800_000_000,
            UserAgent = "ua",
        }));

        Assert.Equal("1800000000", built.Headers[RequestSigner.HeaderTimestamp]);
        Assert.Matches("^[0-9a-f]{64}$", built.Headers[RequestSigner.HeaderSignature]);
    }
}
