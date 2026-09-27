using System.Text;
using Oblodai.Contract;
using Oblodai.Resources;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>Regression tests of the security fix wave (rulings R1, R3, R6, R10, R11).</summary>
public class SecurityFixesTests
{
    private static OblodaiClient Client(FakeHttpHandler handler, OblodaiOptions? options = null)
        => new(
            (options ?? new OblodaiOptions()) with
            {
                PublicId = "pk",
                Secret = "s",
                BaseUrl = "https://api.test",
                Retry = new RetryOptions { MaxRetries = 0 },
            },
            handler.Client());

    private static string Payments(params string[] uuids)
        => "[" + string.Join(",", uuids.Select(u => $"{{\"uuid\":\"{u}\"}}")) + "]";

    /// <summary>R3: hooks never see a claim token in the URL, nor a proxy or passcode header.</summary>
    [Fact]
    public async Task HooksNeverSeeAClaimTokenOrASecretHeader()
    {
        var seen = new List<string>();
        var handler = new FakeHttpHandler(ScriptedResponse.Ok("{}"));
        using var client = Client(handler, new OblodaiOptions
        {
            Headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer PROXY-TOKEN",
                ["X-Api-Key"] = "PROXY-KEY",
                ["X-Claim-Passcode"] = "PASS-1234",
            },
            Hooks = new Hooks
            {
                OnRequest = r => seen.Add($"{r.Url} {string.Join(",", r.Headers.Select(h => h.Key + "=" + h.Value))}"),
            },
        });

        try
        {
            await client.PayoutLinks.GetPayoutClaimAsync("CLAIMTOKEN_Xk3f9");
        }
        catch (OblodaiException)
        {
            // the answer's shape does not matter here
        }

        Assert.Contains("CLAIMTOKEN_Xk3f9", handler.Calls[0].Url);
        var printed = string.Join("\n", seen);
        Assert.NotEmpty(seen);
        foreach (var secret in new[] { "CLAIMTOKEN_Xk3f9", "PROXY-TOKEN", "PROXY-KEY", "PASS-1234" })
        {
            Assert.DoesNotContain(secret, printed);
        }

        Assert.Contains("/v1/claim/[redacted]", printed);
    }

    [Fact]
    public void RedactUrlHidesBearerPartsAndUserinfo()
    {
        Assert.Equal(
            "https://api.test/v1/documents/payout/p1?exp=[redacted]&sig=[redacted]&x=keep",
            Redaction.RedactUrl("https://u:p@api.test/v1/documents/payout/p1?exp=1&sig=S1&x=keep", null));
        Assert.Equal("https://api.test/v1/aml/[redacted]", Redaction.RedactUrl("https://api.test/v1/aml/T0K", "/v1/aml/{token}"));
    }

    /// <summary>L3 and R3: the CLI device code and a signed document link never print.</summary>
    [Fact]
    public void TheDeviceCodeAndSignedLinksNeverPrint()
    {
        var auth = OblodaiJson.Deserialize<CLIDeviceAuthorization>(System.Text.Json.JsonDocument.Parse(
            """{"device_code":"DEVICE-SECRET-q3X0","expires_in":600,"interval":5,"user_code":"ABCD","verification_uri":"https://x.test","verification_uri_complete":"https://x.test?c=ABCD"}""").RootElement);
        Assert.DoesNotContain("DEVICE-SECRET-q3X0", auth.ToString());

        var raw = Encoding.UTF8.GetBytes(
            """{"type":"payout","uuid":"p1","status":"confirmed","sequence":3,"event_at":"2026-01-01T00:00:00Z","document_url":"https://api.test/v1/documents/payout/p1?exp=1&sig=SIGVALUE"}""");
        var payout = WebhookVerifier.Parse(raw);
        Assert.DoesNotContain("SIGVALUE", payout.ToString());
    }

    /// <summary>R1: the dedupe key comes from the signed body; rewritten id headers do not change it.</summary>
    [Fact]
    public void TheDedupeKeyComesFromTheSignedBody()
    {
        const long ts = 1_755_600_000;
        var body = Encoding.UTF8.GetBytes(
            """{"type":"payment","uuid":"u1","status":"paid","sequence":7,"event_at":"2026-01-01T00:00:00Z"}""");
        var options = new WebhookVerifyOptions { Secret = "whsec", Now = () => ts };
        Dictionary<string, string> Headers(byte[] payload, string eventId) => new(StringComparer.OrdinalIgnoreCase)
        {
            [WebhookVerifier.HeaderTimestamp] = ts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [WebhookVerifier.HeaderSignature] = RequestSigner.SignWebhook("whsec", ts, payload),
            [WebhookVerifier.HeaderEventId] = eventId,
        };

        var a = WebhookVerifier.VerifyDelivery(body, Headers(body, "e-1"), options);
        var b = WebhookVerifier.VerifyDelivery(body, Headers(body, "e-forged"), options);
        Assert.Equal(a.EventKey, b.EventKey);
        Assert.Equal("payment:u1:7", a.EventKey);
        Assert.Equal("e-forged", b.UnverifiedEventId);

        var signedState = Encoding.UTF8.GetBytes(
            """{"type":"payment","uuid":"u1","status":"paid","sequence":7,"event_at":"2026-01-01T00:00:00Z","event_id":"st-42"}""");
        Assert.Equal("st-42", WebhookVerifier.VerifyDelivery(signedState, Headers(signedState, "e-forged"), options).EventKey);
    }

    /// <summary>R6: a hostile Content-Disposition yields a bare, safe name.</summary>
    [Theory]
    [InlineData("attachment; filename=\"../../etc/passwd\"", "passwd")]
    [InlineData("attachment; filename*=UTF-8''..%2F..%2Fx%0A.pdf", "x.pdf")]
    [InlineData("attachment; filename=\"C:\\\\tmp\\\\a.csv\"", "a.csv")]
    [InlineData("attachment; filename=\"..\"", null)]
    [InlineData("attachment; filename=\".\"", null)]
    [InlineData("attachment; filename=\"dir/\"", null)]
    public async Task AHostileFilenameIsReducedToASafeBaseName(string disposition, string? want)
    {
        var handler = new FakeHttpHandler(new ScriptedResponse
        {
            Body = "%PDF",
            ContentType = "application/pdf",
            Headers = new Dictionary<string, string> { ["Content-Disposition"] = disposition },
        });
        using var client = Client(handler);

        var file = await client.Documents.GetFeesAsync();
        Assert.Equal(want, file.Filename);
    }

    /// <summary>R6: saving never overwrites by default and writes owner-only.</summary>
    [Fact]
    public async Task SavingNeverOverwritesAndIsOwnerOnly()
    {
        var dir = Directory.CreateTempSubdirectory("oblodai-r6-");
        try
        {
            var file = new FileResult(Encoding.UTF8.GetBytes("%PDF"), "application/pdf", "a.pdf");
            var existing = Path.Combine(dir.FullName, "doc.pdf");
            await File.WriteAllTextAsync(existing, "keep");
            await Assert.ThrowsAsync<IOException>(() => file.WriteToAsync(existing));
            Assert.Equal("keep", await File.ReadAllTextAsync(existing));

            var fresh = Path.Combine(dir.FullName, "new.pdf");
            await file.WriteToAsync(fresh);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(fresh));
            }

            await file.WriteToAsync(existing, overwrite: true);
            Assert.Equal("%PDF", await File.ReadAllTextAsync(existing));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>R10: a body over the contract's max body never leaves the process.</summary>
    [Fact]
    public async Task ABodyOverTheContractLimitNeverLeavesTheProcess()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Ok("{}"));
        using var client = Client(handler);

        var huge = new string('9', SigningProtocol.MaxBody + 1);
        var error = await Assert.ThrowsAsync<ConfigException>(() => client.Transport.CallElementAsync(
            Routes.CreatePayment,
            new CallOptions { Body = new Dictionary<string, object?> { ["amount"] = huge, ["currency"] = "USDT" } }));
        Assert.Equal(SdkErrorCodes.BodyTooLarge, error.Code);
        Assert.Empty(handler.Calls);
    }

    /// <summary>R11: a page shorter than the limit, or has_pages=false, does not end the walk before total.</summary>
    [Fact]
    public async Task PaginationStopsOnlyOnAnEmptyPageOrTheTotal()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Page(Payments("a", "b"), 0, 5, 2, true),
            ScriptedResponse.Page(Payments("c"), 2, 5, 2, false),
            ScriptedResponse.Page(Payments("d", "e"), 3, 5, 2, false));
        using var client = Client(handler);

        var seen = new List<string>();
        await foreach (var payment in client.Payments.ListHistoryAsync(limit: 2))
        {
            seen.Add(payment.Uuid);
        }

        Assert.Equal(["a", "b", "c", "d", "e"], seen);
        Assert.Equal(3, handler.Calls.Count);
    }

    /// <summary>L4: an amount with more precision than decimal holds is refused, never rounded.</summary>
    [Fact]
    public void AnOverPreciseAmountIsRefusedNotRounded()
    {
        decimal Read(string json) => OblodaiJson.Deserialize<decimal>(System.Text.Json.JsonDocument.Parse(json).RootElement);

        Assert.Equal(25.10m, Read("\"25.10\""));
        Assert.Equal(25.1m, Read("25.1"));
        Assert.Equal(1000000000000.123456m, Read("\"1000000000000.123456000\""));
        Assert.Throws<ContractException>(() => Read("\"1000000000000.123456789012345678\""));
        Assert.Throws<ContractException>(() => Read("1000000000000.123456789012345678"));
        Assert.Throws<ContractException>(() => Read("\"1e-30\""));
    }
}
