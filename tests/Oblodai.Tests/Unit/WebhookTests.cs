using System.Text;
using System.Text.Json;
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
    private const string Secret = Repo.WebhookSamplesSecret;

    private static readonly JsonElement[] Samples = Repo.WebhookSamples.EnumerateArray().ToArray();

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

        // The snapshot must keep both kinds around, or the rehearsal flag would go untested.
        var rehearsals = Samples.Count(s => s.GetProperty("body").TryGetProperty("test", out var t) && t.GetBoolean());
        Assert.InRange(rehearsals, 1, Samples.Length - 1);
    }

    /// <summary>
    /// The header alone marks a rehearsal too: a receiver behind a proxy that strips the body flag still
    /// sees <c>IsTest</c>, and a live delivery is never mistaken for one.
    /// </summary>
    [Fact]
    public void MarksARehearsalFromTheHeaderAsWellAsTheBody()
    {
        const long ts = 1_755_600_000;
        var body = Body();
        var headers = SignedHeaders("whsec", ts, body);
        var options = new WebhookVerifyOptions { Secret = "whsec", Now = () => ts };

        Assert.False(WebhookVerifier.VerifyDelivery(body, headers, options).IsTest);
        Assert.False(WebhookVerifier.IsTestEvent(WebhookVerifier.Parse(body)));

        headers[WebhookVerifier.HeaderTest] = "true";
        Assert.True(WebhookVerifier.VerifyDelivery(body, headers, options).IsTest);

        // And from the signed body, with no header at all.
        var testBody = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(Body()).Replace("\"sequence\":7", "\"sequence\":7,\"test\":true"));
        var testHeaders = SignedHeaders("whsec", ts, testBody);
        var delivery = WebhookVerifier.VerifyDelivery(testBody, testHeaders, options);
        Assert.True(delivery.IsTest);
        Assert.True(WebhookVerifier.IsTestEvent(delivery.Event));
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
        Assert.Equal(body.GetProperty("uuid").GetString(), delivery.Event.ObjectId);
        Assert.Equal(body.GetProperty("type").GetString(), delivery.Event.Type);
        Assert.Equal(headers[WebhookVerifier.HeaderId], delivery.Id);
        Assert.Equal(headers[WebhookVerifier.HeaderEvent], delivery.EventType);
        Assert.Equal(ts, delivery.SentAt);
        Assert.Matches("^(invoice|payout|wallet)\\.", delivery.EventType!);

        // A rehearsal delivery is signed like a live one; only the flag tells them apart. It also sits
        // outside the event stream, so it carries sequence 0 where a live delivery carries a real one.
        var isRehearsal = body.TryGetProperty("test", out var test) && test.GetBoolean();
        Assert.True(
            isRehearsal ? delivery.Event.EventSequence == 0 : delivery.Event.EventSequence > 0,
            $"sample {index}: sequence {delivery.Event.EventSequence} does not match test={isRehearsal}");
        Assert.Equal(isRehearsal, delivery.IsTest);
        Assert.Equal(isRehearsal, WebhookVerifier.IsTestEvent(delivery.Event));
        Assert.Equal(isRehearsal, headers.ContainsKey(WebhookVerifier.HeaderTest));

        // The union decodes into the record that matches the discriminator.
        switch (body.GetProperty("type").GetString())
        {
            case "payment":
                Assert.IsType<PaymentWebhook>(delivery.Event);
                break;
            case "payout":
                Assert.IsType<PayoutWebhook>(delivery.Event);
                break;
            default:
                Assert.IsType<WalletWebhook>(delivery.Event);
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
        var raw = sample.GetProperty("raw").GetString()!;
        var sequence = sample.GetProperty("body").GetProperty("sequence").GetInt64();
        var tampered = Encoding.UTF8.GetBytes(
            raw.Replace($"\"sequence\": {sequence}", $"\"sequence\": {sequence + 1}", StringComparison.Ordinal));
        Assert.NotEqual(raw, Encoding.UTF8.GetString(tampered));

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
        Assert.Equal("u1", verified.ObjectId);
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
        Assert.Equal("u1", WebhookVerifier.Verify(body, headers, new WebhookVerifyOptions { Secret = "old", Now = () => ts }).ObjectId);

        // Already swapped: the main header verifies with the new secret.
        Assert.Equal("u1", WebhookVerifier.Verify(body, headers, new WebhookVerifyOptions { Secret = "new", Now = () => ts }).ObjectId);

        // Kept the old copy alongside an unrelated new one.
        Assert.Equal("u1", WebhookVerifier.Verify(body, headers, new WebhookVerifyOptions
        {
            Secret = "unrelated",
            PreviousSecret = "old",
            Now = () => ts,
        }).ObjectId);
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

        var payment = Assert.IsType<PaymentWebhook>(parsed);
        Assert.Equal("paid", payment.Status.Value);
        Assert.True(WebhookVerifier.IsStale(parsed, 7));
        Assert.False(WebhookVerifier.IsStale(parsed, 6));
        Assert.False(WebhookVerifier.IsStale(parsed, null));

        // An event family this snapshot does not know is carried through, not thrown on: the gateway
        // adds them without asking, and an authentic delivery must not become an exception.
        var alien = Assert.IsType<UnknownWebhookEvent>(WebhookVerifier.Parse("""{"type":"alien","uuid":"x"}"""));
        Assert.Equal("alien", alien.Type);
        Assert.Equal("x", alien.Extra!["uuid"].GetString());
        // Its id field is not guessed: the contract names one per known kind only.
        Assert.Null(((IWebhookEvent)alien).ObjectId);
        Assert.False(WebhookVerifier.IsStale(alien, 99));
        Assert.False(WebhookVerifier.IsTestEvent(alien));
        Assert.False(WebhookVerifier.IsKnownEvent(alien));
        Assert.True(WebhookVerifier.IsKnownEvent(parsed));

        // A body that verified but cannot be read is a CONTRACT failure, never a signature one: a
        // receiver answering 401 to signature failures would tell the gateway to retire the endpoint.
        foreach (var body in new[] { "not json", """{"uuid":"x"}""", """{"type":5,"uuid":"x"}""" })
        {
            var bad = Assert.Throws<WebhookPayloadException>(() => WebhookVerifier.Parse(body));
            Assert.IsAssignableFrom<ContractException>(bad);
            Assert.Equal(SdkErrorCodes.WebhookBadPayload, bad.Code);
            Assert.IsNotType<SignatureException>(bad);
            Assert.NotEqual(SdkErrorCodes.WebhookBadSignature, bad.Code);
        }
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
