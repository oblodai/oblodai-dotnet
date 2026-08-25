using System.Text;
using System.Text.Json;
using Oblodai.Models;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// Webhook verification against the 43 real deliveries the gateway's dispatcher signed and recorded,
/// plus the rules around them: freshness, rotation, header casing and the event union.
/// </summary>
public class WebhookTests
{
    /// <summary>The endpoint secret in force when the samples were delivered.</summary>
    private static readonly string Secret =
        Fixtures.For("POST /v1/webhooks/rotate-secret").Result.GetProperty("secret").GetString()!;

    private static readonly JsonElement[] Samples = Fixtures.WebhookSamples.EnumerateArray().ToArray();

    public static TheoryData<int> SampleIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Samples.Length; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Fact]
    public void TheSnapshotCarriesRealDeliveriesToVerify()
    {
        Assert.True(Samples.Length >= 40, $"expected the recorded deliveries, got {Samples.Length}");
        Assert.NotEmpty(Secret);
    }

    [Theory]
    [MemberData(nameof(SampleIndexes))]
    public void VerifiesEveryRecordedDelivery(int index)
    {
        var sample = Samples[index];
        var headers = Headers(sample);
        var raw = Encoding.UTF8.GetBytes(sample.GetProperty("raw").GetString()!);
        var ts = long.Parse(headers[WebhookVerifier.HeaderTimestamp]);

        var delivery = WebhookVerifier.VerifyDelivery(raw, headers, new WebhookVerifyOptions
        {
            Secret = Secret,
            Now = () => ts,
        });

        var body = sample.GetProperty("body");
        Assert.Equal(body.GetProperty("uuid").GetString(), delivery.Event.Uuid);
        Assert.Equal(body.GetProperty("type").GetString(), delivery.Event.Type);
        Assert.Equal(headers[WebhookVerifier.HeaderId], delivery.Id);
        Assert.Equal(headers[WebhookVerifier.HeaderEvent], delivery.EventType);
        Assert.Equal(ts, delivery.SentAt);
        Assert.True(delivery.Event.Sequence > 0);
        Assert.Matches("^(invoice|payout|wallet)\\.", delivery.EventType!);

        // The union decodes into the record that matches the discriminator.
        switch (body.GetProperty("type").GetString())
        {
            case "payment":
                Assert.IsType<PaymentEvent>(delivery.Event);
                break;
            case "payout":
                Assert.IsType<PayoutEvent>(delivery.Event);
                break;
            default:
                Assert.IsType<WalletEvent>(delivery.Event);
                break;
        }

        // A different secret must not verify the same bytes.
        Assert.Throws<SignatureException>(() => WebhookVerifier.Verify(raw, headers, new WebhookVerifyOptions
        {
            Secret = "some-other-secret",
            PreviousSecret = "another",
            Now = () => ts,
        }));
    }

    [Fact]
    public void RejectsABodyTamperedAfterSigning()
    {
        var sample = Samples[0];
        var headers = Headers(sample);
        var ts = long.Parse(headers[WebhookVerifier.HeaderTimestamp]);
        var tampered = Encoding.UTF8.GetBytes(sample.GetProperty("raw").GetString()!.Replace("\"sequence\": 1", "\"sequence\": 2"));

        var error = Assert.Throws<SignatureException>(() => WebhookVerifier.Verify(
            tampered, headers, new WebhookVerifyOptions { Secret = Secret, Now = () => ts }));

        Assert.Equal(SdkErrorCodes.WebhookBadSignature, error.Code);
    }

    [Fact]
    public void RejectsMissingHeaders()
    {
        var error = Assert.Throws<SignatureException>(() => WebhookVerifier.Verify(
            "{}"u8, new Dictionary<string, string> { ["X-Webhook-Signature"] = "aa" },
            new WebhookVerifyOptions { Secret = "whsec" }));

        Assert.Equal(SdkErrorCodes.WebhookMissingHeader, error.Code);
    }

    [Fact]
    public void RejectsStaleDeliveriesUnlessToleranceIsDisabled()
    {
        const long ts = 1_755_600_000;
        var body = Body();
        var headers = SignedHeaders("whsec", ts, body);

        var error = Assert.Throws<SignatureException>(() => WebhookVerifier.Verify(
            body, headers, new WebhookVerifyOptions { Secret = "whsec", Now = () => ts + 600 }));
        Assert.Equal(SdkErrorCodes.WebhookStaleTimestamp, error.Code);

        var verified = WebhookVerifier.Verify(body, headers, new WebhookVerifyOptions
        {
            Secret = "whsec",
            Now = () => ts + 600,
            ToleranceSeconds = 0,
        });
        Assert.Equal("u1", verified.Uuid);
    }

    [Fact]
    public void VerifiesDuringASecretRotationFromEitherSide()
    {
        const long ts = 1_755_600_000;
        var body = Body();
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [WebhookVerifier.HeaderTimestamp] = ts.ToString(),
            [WebhookVerifier.HeaderSignature] = RequestSigner.SignWebhook("new", ts, body),
            [WebhookVerifier.HeaderSignaturePrev] = RequestSigner.SignWebhook("old", ts, body),
        };

        // Not swapped yet: the stored secret is the old one, the Prev header carries its signature.
        Assert.Equal("u1", WebhookVerifier.Verify(body, headers, new WebhookVerifyOptions { Secret = "old", Now = () => ts }).Uuid);

        // Already swapped: the main header verifies with the new secret.
        Assert.Equal("u1", WebhookVerifier.Verify(body, headers, new WebhookVerifyOptions { Secret = "new", Now = () => ts }).Uuid);

        // Kept the old copy alongside an unrelated new one.
        Assert.Equal("u1", WebhookVerifier.Verify(body, headers, new WebhookVerifyOptions
        {
            Secret = "unrelated",
            PreviousSecret = "old",
            Now = () => ts,
        }).Uuid);
    }

    [Fact]
    public void LooksHeadersUpCaseInsensitively()
    {
        const long ts = 1_755_600_000;
        var body = Body();
        var lower = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["x-webhook-timestamp"] = ts.ToString(),
            ["x-webhook-signature"] = RequestSigner.SignWebhook("whsec", ts, body),
        };

        var verified = WebhookVerifier.Verify(body, lower, new WebhookVerifyOptions { Secret = "whsec", Now = () => ts });
        Assert.Equal("payment", verified.Type);
    }

    [Fact]
    public void ParsesTheUnionAndDetectsStaleSequences()
    {
        var parsed = WebhookVerifier.Parse(Body());

        var payment = Assert.IsType<PaymentEvent>(parsed);
        Assert.Equal("paid", payment.Status.Value);
        Assert.True(WebhookVerifier.IsStale(parsed, 7));
        Assert.False(WebhookVerifier.IsStale(parsed, 6));
        Assert.False(WebhookVerifier.IsStale(parsed, null));

        Assert.Throws<SignatureException>(() => WebhookVerifier.Parse("""{"type":"alien","uuid":"x"}"""));
        Assert.Throws<SignatureException>(() => WebhookVerifier.Parse("not json"));
        Assert.Throws<SignatureException>(() => WebhookVerifier.Parse("""{"uuid":"x"}"""));
    }

    private static byte[] Body() => Encoding.UTF8.GetBytes(
        """{"type":"payment","uuid":"u1","order_id":"o","status":"paid","is_final":true,"sequence":7,"event_at":"2026-01-01T00:00:00Z","txid":""}""");

    private static Dictionary<string, string> SignedHeaders(string secret, long ts, byte[] body)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            [WebhookVerifier.HeaderTimestamp] = ts.ToString(),
            [WebhookVerifier.HeaderSignature] = RequestSigner.SignWebhook(secret, ts, body),
        };

    private static Dictionary<string, string> Headers(JsonElement sample)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in sample.GetProperty("headers").EnumerateObject())
        {
            headers[header.Name] = header.Value.GetString()!;
        }

        return headers;
    }
}
