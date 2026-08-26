using System.Text;
using Oblodai.Models;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// The order and the guards of webhook verification. A receiver is an unauthenticated endpoint on the
/// public internet: what it does before it has authenticated the sender, and what it tells the sender
/// when it fails, are both part of its security.
/// </summary>
public class WebhookHardeningTests
{
    private const string Secret = "whsec_1";
    private const long Ts = 1_800_000_000;

    private static readonly byte[] Body = Encoding.UTF8.GetBytes(
        """{"type":"payment","uuid":"u1","order_id":"o","status":"paid","is_final":true,"sequence":7,"event_at":"2026-01-01T00:00:00Z","txid":""}""");

    private static WebhookVerifyOptions Options(string? secret = Secret, int tolerance = 300)
        => new() { Secret = secret ?? string.Empty, ToleranceSeconds = tolerance, Now = () => Ts };

    private static Dictionary<string, string> Headers(string? signature = null, long? ts = null)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            [WebhookVerifier.HeaderTimestamp] = (ts ?? Ts).ToString(),
            [WebhookVerifier.HeaderSignature] = signature ?? RequestSigner.SignWebhook(Secret, ts ?? Ts, Body),
        };

    [Fact]
    public void AnEmptySecretIsRefusedBeforeAnyCryptoRuns()
    {
        // HMAC with an empty key is still a well-defined function of the body, so every sender on the
        // internet could produce a matching signature. This has to fail as configuration, not verify.
        var error = Assert.Throws<ConfigException>(
            () => WebhookVerifier.Verify(Body, Headers(), Options(secret: string.Empty)));

        Assert.Equal(SdkErrorCodes.BadConfig, error.Code);
        Assert.Equal(nameof(WebhookVerifyOptions.Secret), error.Field);
    }

    [Fact]
    public void AnEmptyPreviousSecretIsRefusedToo()
    {
        var options = Options() with { PreviousSecret = string.Empty };

        var error = Assert.Throws<ConfigException>(() => WebhookVerifier.Verify(Body, Headers(), options));

        Assert.Equal(nameof(WebhookVerifyOptions.PreviousSecret), error.Field);
    }

    [Fact]
    public void ANegativeToleranceIsAMistakeNotAWayToDisableTheCheck()
    {
        var error = Assert.Throws<ConfigException>(
            () => WebhookVerifier.Verify(Body, Headers(), Options(tolerance: -1)));

        Assert.Equal(nameof(WebhookVerifyOptions.ToleranceSeconds), error.Field);
        Assert.Contains("use 0 to disable", error.Message);
    }

    [Fact]
    public void ZeroToleranceDisablesTheFreshnessCheckAndSaysSo()
    {
        var stale = Ts - 10 * 24 * 3600;
        var headers = Headers(ts: stale);

        var verified = WebhookVerifier.Verify(Body, headers, Options(tolerance: 0));

        Assert.Equal("u1", verified.Uuid);
    }

    [Fact]
    public void TheSignatureIsCheckedBeforeTheTimestampSoTheWindowIsNotAPreAuthOracle()
    {
        // A stale delivery with a WRONG signature must be reported as a signature failure. Reporting it
        // as "stale" would let an unauthenticated prober learn the receiver's clock offset by bisection.
        var stale = Ts - 10_000;
        var headers = Headers(signature: RequestSigner.SignWebhook("wrong-secret", stale, Body), ts: stale);

        var error = Assert.Throws<SignatureException>(() => WebhookVerifier.Verify(Body, headers, Options()));

        Assert.Equal(SdkErrorCodes.WebhookBadSignature, error.Code);
    }

    [Fact]
    public void AStaleButAuthenticDeliveryIsStillReportedAsStale()
    {
        var stale = Ts - 10_000;
        var headers = Headers(ts: stale);

        var error = Assert.Throws<SignatureException>(() => WebhookVerifier.Verify(Body, headers, Options()));

        Assert.Equal(SdkErrorCodes.WebhookStaleTimestamp, error.Code);
    }

    [Theory]
    [InlineData("  {0}  ")]
    [InlineData("{0}")]
    [InlineData("UPPER")]
    public void ASignatureHeaderIsAcceptedTrimmedAndInEitherCase(string shape)
    {
        var signature = RequestSigner.SignWebhook(Secret, Ts, Body);
        var provided = shape == "UPPER" ? signature.ToUpperInvariant() : string.Format(shape, signature);

        var verified = WebhookVerifier.Verify(Body, Headers(signature: provided), Options());

        Assert.Equal("u1", verified.Uuid);
    }

    [Fact]
    public void AnOxPrefixIsNotTheEncodingTheGatewaySendsAndIsRefused()
    {
        var signature = "0x" + RequestSigner.SignWebhook(Secret, Ts, Body);

        var error = Assert.Throws<SignatureException>(
            () => WebhookVerifier.Verify(Body, Headers(signature: signature), Options()));

        // Stripping it silently would hide a real encoding mismatch between the two sides.
        Assert.Equal(SdkErrorCodes.WebhookMissingHeader, error.Code);
    }

    [Fact]
    public void AnAuthenticDeliveryWithAnUnreadableSequenceIsNotDropped()
    {
        var body = Encoding.UTF8.GetBytes(
            """{"type":"payment","uuid":"u1","status":"paid","sequence":"not-a-number","is_final":true}""");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [WebhookVerifier.HeaderTimestamp] = Ts.ToString(),
            [WebhookVerifier.HeaderSignature] = RequestSigner.SignWebhook(Secret, Ts, body),
        };

        var verified = WebhookVerifier.Verify(body, headers, Options());

        Assert.Null(verified.Sequence);
        Assert.False(WebhookVerifier.IsStale(verified, 99));
        Assert.False(WebhookVerifier.IsStale(verified, null));
    }

    [Fact]
    public void AnEventFamilyThisSnapshotDoesNotKnowIsDeliveredNotThrownOn()
    {
        var body = Encoding.UTF8.GetBytes("""{"type":"treasury","uuid":"t1","sequence":9,"test":true}""");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [WebhookVerifier.HeaderTimestamp] = Ts.ToString(),
            [WebhookVerifier.HeaderSignature] = RequestSigner.SignWebhook(Secret, Ts, body),
        };

        var delivery = WebhookVerifier.VerifyDelivery(body, headers, Options());

        var unknown = Assert.IsType<UnknownWebhookEvent>(delivery.Event);
        Assert.Equal("treasury", unknown.Type);
        Assert.True(delivery.IsTest);
        Assert.False(WebhookVerifier.IsKnownEvent(unknown));
        Assert.True(WebhookVerifier.IsStale(unknown, 9));
    }

    [Fact]
    public void ABodyThatVerifiedButCannotBeReadIsAContractFailureNotASignatureFailure()
    {
        var body = Encoding.UTF8.GetBytes("""{"type":"payment"}""");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [WebhookVerifier.HeaderTimestamp] = Ts.ToString(),
            [WebhookVerifier.HeaderSignature] = RequestSigner.SignWebhook(Secret, Ts, body),
        };

        var error = Assert.Throws<WebhookPayloadException>(() => WebhookVerifier.Verify(body, headers, Options()));

        Assert.Equal(SdkErrorCodes.WebhookBadPayload, error.Code);
        Assert.IsAssignableFrom<ContractException>(error);
        Assert.IsNotType<SignatureException>(error);
    }

    [Fact]
    public void TheRotationOverlapStillVerifiesWithEitherSecretInEitherHeader()
    {
        var options = Options() with { PreviousSecret = "whsec_0" };
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [WebhookVerifier.HeaderTimestamp] = Ts.ToString(),
            [WebhookVerifier.HeaderSignature] = RequestSigner.SignWebhook("whsec_0", Ts, Body),
        };

        Assert.Equal("u1", WebhookVerifier.Verify(Body, headers, options).Uuid);

        headers[WebhookVerifier.HeaderSignature] = "0".PadLeft(64, '0');
        headers[WebhookVerifier.HeaderSignaturePrev] = RequestSigner.SignWebhook("whsec_0", Ts, Body);
        Assert.Equal("u1", WebhookVerifier.Verify(Body, headers, options).Uuid);
    }
}
