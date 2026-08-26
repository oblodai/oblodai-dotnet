using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Oblodai.Contract;
using Oblodai.Models;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Contract;

/// <summary>
/// Wire models versus the golden bodies the gateway recorded. Each row names a route, how to reach the
/// object inside its result, the model and the fields that are optional there. Keys must match EXACTLY:
/// a field the gateway stopped sending fails here, and so does a field it started sending that the model
/// lacks. The last test is the completeness gate — every recorded success body must have a row.
/// </summary>
public class ModelTests
{
    /// <param name="Route">Route key of the fixture.</param>
    /// <param name="Path">Where the modelled object sits inside <c>result</c> (<c>items[0]</c>, <c>file</c>, …).</param>
    /// <param name="Model">The model that describes it.</param>
    /// <param name="Optional">Keys that may be absent on this route (or present without being on every route).</param>
    private sealed record Row(string Route, string Path, Type Model, string[] Optional);

    private static readonly string[] PaymentOptional = ["refunds", "refund_status"];
    private static readonly string[] PayoutOptional = ["error", "error_code", "wallet_uuid"];
    private static readonly string[] PayoutLinkOptional =
        ["claim_token", "claim_url", "batch_id", "payout_id", "claim_address", "email", "passcode"];
    private static readonly string[] PaymentLinkOptional = ["payments", "min_amount", "max_amount", "pinned_currency"];
    private static readonly string[] DocumentJobOptional = ["ready_within", "file", "error"];
    private static readonly string[] WebhookTestOptional = ["error", "url", "duration_ms"];
    private static readonly string[] None = [];

    private static readonly Row[] Rows =
    [
        new("POST /v1/payment", "", typeof(Payment), PaymentOptional),
        new("POST /v1/payment/info", "", typeof(Payment), PaymentOptional),
        new("POST /v1/payment/cancel", "", typeof(Payment), PaymentOptional),
        new("POST /v1/payment/history", "items[0]", typeof(Payment), PaymentOptional),
        new("GET /v1/pay/{id}", "", typeof(PublicPayment), None),
        new("POST /v1/pay/{id}/select", "", typeof(PublicPayment), None),
        new("POST /v1/link/{id}/checkout", "", typeof(PublicPayment), None),
        new("POST /v1/payment/qr", "", typeof(QrCode), None),
        new("GET /v1/pay/{id}/qr", "", typeof(QrCode), None),
        new("POST /v1/payment/services", "items[0]", typeof(ServiceMethod), None),
        new("POST /v1/payout/services", "items[0]", typeof(ServiceMethod), None),
        new("POST /v1/payment/batch", "", typeof(BatchSubmitted), None),
        new("POST /v1/payout/batch", "", typeof(BatchSubmitted), None),
        new("POST /v1/refund/batch", "", typeof(BatchSubmitted), None),
        new("POST /v1/transfer/batch", "", typeof(BatchSubmitted), None),
        new("POST /v1/batch/info", "", typeof(BatchInfo), None),
        new("POST /v1/payout", "", typeof(Payout), PayoutOptional),
        new("POST /v1/payout/info", "", typeof(Payout), PayoutOptional),
        new("POST /v1/payout/cancel", "", typeof(Payout), PayoutOptional),
        new("POST /v1/payout/history", "items[0]", typeof(Payout), PayoutOptional),
        new("POST /v1/payout/mass", "items[0].result", typeof(Payout), PayoutOptional),
        new("POST /v1/payment/refund", "", typeof(Payout), PayoutOptional),
        new("POST /v1/payout/calculate", "", typeof(PayoutCalculation), None),
        new("POST /v1/payout/validate", "", typeof(PayoutValidation), ["funded_by"]),
        new("POST /v1/payout/link", "", typeof(PayoutLink), PayoutLinkOptional),
        new("POST /v1/payout/link/info", "", typeof(PayoutLink), PayoutLinkOptional),
        new("POST /v1/payout/link/list", "items[0]", typeof(PayoutLink), PayoutLinkOptional),
        new("POST /v1/payout/link/cancel", "", typeof(PayoutLink), PayoutLinkOptional),
        new("POST /v1/payout/link/batch", "items[0].result", typeof(PayoutLink), PayoutLinkOptional),
        new("GET /v1/claim/{token}", "", typeof(ClaimPreview), None),
        new("POST /v1/claim/{token}", "", typeof(ClaimResult), None),
        new("POST /v1/payment/link", "", typeof(PaymentLinkCreated), None),
        new("POST /v1/payment/link/info", "", typeof(PaymentLink), PaymentLinkOptional),
        new("POST /v1/payment/link/list", "items[0]", typeof(PaymentLink), PaymentLinkOptional),
        new("GET /v1/link/{id}", "", typeof(PublicPaymentLink), ["min_amount", "max_amount", "pinned_currency"]),
        new("POST /v1/balance", "", typeof(Balance), None),
        new("POST /v1/referral/info", "", typeof(ReferralInfo), None),
        new("POST /v1/auto-withdraw/list", "items[0]", typeof(AutoWithdrawRule), None),
        new("POST /v1/auto-withdraw/set", "items[0]", typeof(AutoWithdrawRule), None),
        new("POST /v1/api-allowlist/list", "", typeof(ApiAllowlist), None),
        new("POST /v1/api-allowlist/add", "", typeof(ApiAllowlist), None),
        new("POST /v1/api-allowlist/remove", "", typeof(ApiAllowlist), None),
        new("POST /v1/api-allowlist/enable", "", typeof(ApiAllowlist), None),
        new("POST /v1/payment/discount/list", "items[0]", typeof(DiscountRule), None),
        new("POST /v1/payment/discount/set", "", typeof(DiscountRule), None),
        new("POST /v1/split/rule/list", "items[0]", typeof(SplitRule), ["merchant_id"]),
        new("POST /v1/split/rule", "", typeof(SplitRule), ["merchant_id", "active", "address", "network", "note", "reversible"]),
        new("GET /v1/currencies", "", typeof(Currencies), None),
        new("GET /v1/currencies", "currencies[0].networks[0]", typeof(CurrencyNetwork), ["contract"]),
        new("POST /v1/exchange-rate/list", "items[0]", typeof(ExchangeRate), None),
        new("POST /v1/webhooks", "", typeof(WebhookEndpoint), None),
        new("POST /v1/webhooks/rotate-secret", "", typeof(WebhookSecretRotated), None),
        new("POST /v1/webhooks/deliveries", "items[0]", typeof(WebhookDelivery), ["payload"]),
        new("GET /v1/sandbox/webhooks", "items[0]", typeof(WebhookDelivery), ["sequence"]),
        new("POST /v1/payment/resolve", "", typeof(Resolution), ["error", "error_code", "wallet_uuid", "payment_uuid", "amount_kept"]),
        new("POST /v1/payment/send-email", "", typeof(EmailSent), None),
        new("POST /v1/payment/resend", "", typeof(OkResult), None),
        new("POST /v1/payment/accepted/set", "", typeof(OkResult), None),
        new("POST /v1/split/rule/delete", "", typeof(OkResult), None),
        new("POST /v1/payment/accepted/list", "items[0]", typeof(AcceptedMethod), ["reason"]),
        new("POST /v1/payment/accuracy/get", "", typeof(AccuracyConfig), None),
        new("POST /v1/payment/accuracy/set", "", typeof(AccuracyConfig), None),
        new("POST /v1/payment/autorefund/get", "", typeof(AutoRefundConfig), None),
        new("POST /v1/payment/autorefund/set", "", typeof(AutoRefundConfig), ["configured"]),
        new("POST /v1/payment/fee-config/get", "", typeof(PaymentFeeConfig), None),
        new("POST /v1/payment/fee-config/set", "", typeof(PaymentFeeConfig), ["enabled"]),
        new("POST /v1/payout/fee-config/get", "", typeof(PayoutFeeConfig), None),
        new("POST /v1/payout/fee-config/set", "", typeof(PayoutFeeConfig), ["configured"]),
        new("POST /v1/payout/refund-fee-config/get", "", typeof(RefundFeeConfig), None),
        new("POST /v1/payout/refund-fee-config/set", "", typeof(RefundFeeConfig), ["configured"]),
        new("POST /v1/payment/link/toggle", "", typeof(PaymentLinkToggled), None),
        new("POST /v1/split/config/get", "", typeof(SplitConfig), None),
        new("POST /v1/split/config/set", "", typeof(SplitConfig), None),
        new("POST /v1/split/recipient/optin", "", typeof(SplitOptIn), None),
        new("POST /v1/split/recipient/optin/get", "", typeof(SplitOptIn), None),
        new("POST /v1/vrcs", "", typeof(VrcsStatus), None),
        new("POST /v1/wallet", "", typeof(Wallet), ["destination_tag", "memo", "address_xaddress", "address_muxed"]),
        new("POST /v1/wallet/block", "", typeof(WalletBlocked), None),
        new("POST /v1/wallet/qr", "", typeof(WalletQr), None),
        new("POST /v1/wallet/blocked-address-refund", "", typeof(Payout), PayoutOptional),
        new("POST /v1/transfer/to-personal", "", typeof(TransferToPersonal), None),
        new("POST /v1/transfer/to-user", "", typeof(TransferToUser), None),
        new("POST /v1/documents/jobs", "", typeof(DocumentJob), DocumentJobOptional),
        new("POST /v1/documents/jobs/info", "", typeof(DocumentJob), DocumentJobOptional),
        new("POST /v1/documents/jobs/info", "file", typeof(DocumentJobFile), None),
        new("POST /v1/test-webhook/payment", "", typeof(WebhookTestResult), WebhookTestOptional),
        new("POST /v1/test-webhook/payout", "", typeof(WebhookTestResult), WebhookTestOptional),
        new("POST /v1/test-webhook/wallet", "", typeof(WebhookTestResult), WebhookTestOptional),
        new("POST /v1/payment/testing-webhook", "", typeof(WebhookTestResult), WebhookTestOptional),
        new("POST /v1/sandbox/faucet", "", typeof(FaucetResult), None),
        new("POST /v1/sandbox/deposit", "", typeof(SandboxDeposit), None),
        new("POST /v1/sandbox/reset", "", typeof(SandboxReset), None),
        new("POST /v1/sandbox/webhooks/replay", "", typeof(SandboxReplay), None),
        new("POST /v1/merchants", "", typeof(MerchantOnboarded), None),
        new("POST /v1/merchants", "api_key", typeof(ApiKeyPair), None),
        new("POST /v1/merchants/{id}/sandbox", "", typeof(SandboxStore), None),
    ];

    /// <summary>Routes the API guarantees to refuse for API keys, so no success body exists to model.</summary>
    private static readonly HashSet<string> NotModelled = new(StringComparer.Ordinal)
    {
        // API-key payouts auto-approve; approve serves the cabinet's maker-checker flow.
        "POST /v1/payout/approve",

        // The result is a bare {items} list of AutoWithdrawRule, already covered by the /set and /list rows.
        "POST /v1/auto-withdraw/delete",
    };

    public static TheoryData<int> RowIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Rows.Length; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RowIndexes))]
    public void ModelKeysEqualTheGoldenBodyKeys(int index)
    {
        var row = Rows[index];
        var fixture = Fixtures.For(row.Route);
        Assert.True(fixture.IsSuccess, $"{row.Route}: fixture is a refusal");

        var element = Navigate(fixture.Result, row.Path, row.Route);
        var wire = element.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var declared = WireKeys(row.Model);
        var optional = row.Optional.ToHashSet(StringComparer.Ordinal);

        var missingOnWire = declared.Where(k => !wire.Contains(k) && !optional.Contains(k)).OrderBy(k => k).ToList();
        var unknownOnWire = wire.Where(k => !declared.Contains(k) && !optional.Contains(k)).OrderBy(k => k).ToList();

        Assert.True(
            missingOnWire.Count == 0 && unknownOnWire.Count == 0,
            $"{row.Route} ({row.Model.Name}): model declares but the wire never sent [{string.Join(", ", missingOnWire)}]; "
            + $"wire sent but the model lacks [{string.Join(", ", unknownOnWire)}]");
    }

    [Theory]
    [MemberData(nameof(RowIndexes))]
    public void TheGoldenBodyDecodesIntoTheModel(int index)
    {
        var row = Rows[index];
        var element = Navigate(Fixtures.For(row.Route).Result, row.Path, row.Route);
        var decoded = JsonSerializer.Deserialize(element, row.Model, OblodaiJson.Options);
        Assert.NotNull(decoded);

        // Round-tripping must not invent or drop wire keys either.
        var round = JsonSerializer.SerializeToElement(decoded, row.Model, OblodaiJson.Options);
        var wire = element.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var written = round.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.True(
            written.IsSubsetOf(wire),
            $"{row.Route}: re-serializing produced keys the wire never had: {string.Join(", ", written.Except(wire))}");
    }

    /// <summary>
    /// The event records versus the bodies the dispatcher actually signed. <c>test</c> rides only on
    /// rehearsal deliveries, so it is optional here — every other key must line up exactly.
    /// </summary>
    [Fact]
    public void EventModelKeysEqualTheSignedWebhookBodies()
    {
        var optional = new HashSet<string>(StringComparer.Ordinal) { "test" };
        foreach (var sample in Fixtures.WebhookSamples.EnumerateArray())
        {
            var body = sample.GetProperty("body");
            var type = body.GetProperty("type").GetString();
            var model = type switch
            {
                "payment" => typeof(PaymentEvent),
                "payout" => typeof(PayoutEvent),
                _ => typeof(WalletEvent),
            };

            var wire = body.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var declared = WireKeys(model);

            var missingOnWire = declared.Where(k => !wire.Contains(k) && !optional.Contains(k)).OrderBy(k => k).ToList();
            var unknownOnWire = wire.Where(k => !declared.Contains(k)).OrderBy(k => k).ToList();

            Assert.True(
                missingOnWire.Count == 0 && unknownOnWire.Count == 0,
                $"{type} event ({model.Name}): model declares but the wire never sent "
                + $"[{string.Join(", ", missingOnWire)}]; wire sent but the model lacks [{string.Join(", ", unknownOnWire)}]");
        }
    }

    [Fact]
    public void EveryRecordedSuccessBodyIsCoveredByARow()
    {
        var covered = Rows.Select(r => r.Route).ToHashSet(StringComparer.Ordinal);
        var uncovered = Fixtures.All.Values
            .Where(f => f.IsSuccess && f.IsJson && !NotModelled.Contains(f.Route) && !covered.Contains(f.Route))
            .Select(f => f.Route)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        Assert.True(uncovered.Count == 0, $"recorded success bodies without a model row: {string.Join(", ", uncovered)}");
    }

    [Fact]
    public void EveryRecordedFixtureBelongsToAKnownRoute()
    {
        foreach (var route in Fixtures.All.Keys)
        {
            Assert.True(Routes.All.ContainsKey(route), $"{route}: fixture for a route the registry does not declare");
        }
    }

    [Fact]
    public void StatusesInTheGoldenBodiesAreInTheVocabulary()
    {
        foreach (var payment in Fixtures.For("POST /v1/payment/history").Result.GetProperty("items").EnumerateArray())
        {
            Assert.True(PaymentStatus.FromValue(payment.GetProperty("status").GetString()!).IsKnown);
        }

        foreach (var payout in Fixtures.For("POST /v1/payout/history").Result.GetProperty("items").EnumerateArray())
        {
            Assert.True(PayoutStatus.FromValue(payout.GetProperty("status").GetString()!).IsKnown);
        }

        foreach (var link in Fixtures.For("POST /v1/payout/link/list").Result.GetProperty("items").EnumerateArray())
        {
            Assert.True(PayoutLinkStatus.FromValue(link.GetProperty("status").GetString()!).IsKnown);
        }

        foreach (var delivery in Fixtures.For("POST /v1/webhooks/deliveries").Result.GetProperty("items").EnumerateArray())
        {
            Assert.True(DeliveryStatus.FromValue(delivery.GetProperty("status").GetString()!).IsKnown);
        }
    }

    [Fact]
    public void EveryRecordedErrorCodeIsKnownAndCarriesTheDocumentedEnvelope()
    {
        Assert.NotEmpty(Fixtures.Errors);
        foreach (var (code, fixture) in Fixtures.Errors)
        {
            Assert.Contains(code, ErrorCodes.All);
            var error = fixture.Error;
            Assert.Equal(code, error.GetProperty("code").GetString());
            Assert.True(error.GetProperty("retryable").ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.Equal(JsonValueKind.String, error.GetProperty("request_id").ValueKind);
            if (fixture.Status == 429)
            {
                Assert.True(error.GetProperty("retry_after").GetInt32() > 0);
            }
        }
    }

    [Fact]
    public void RecordedRequestBodiesOnlyUseDocumentedFields()
    {
        var schemas = Fixtures.Contract.GetProperty("routes").EnumerateArray()
            .Where(r => r.TryGetProperty("request_schema", out _))
            .ToDictionary(
                r => $"{r.GetProperty("method").GetString()} {r.GetProperty("path").GetString()}",
                r => r.GetProperty("request_schema"),
                StringComparer.Ordinal);

        foreach (var fixture in Fixtures.All.Values)
        {
            if (fixture.Request is not { ValueKind: JsonValueKind.Object } request
                || !schemas.TryGetValue(fixture.Route, out var schema)
                || !schema.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            foreach (var field in request.EnumerateObject())
            {
                Assert.True(
                    properties.TryGetProperty(field.Name, out _),
                    $"{fixture.Route}: the recorded journey sent an undocumented field \"{field.Name}\"");
            }
        }
    }

    /// <summary>The wire names a model declares, including the ones it inherits.</summary>
    private static HashSet<string> WireKeys(Type model)
        => model
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name)
            .ToHashSet(StringComparer.Ordinal);

    private static JsonElement Navigate(JsonElement element, string path, string route)
    {
        if (path.Length == 0)
        {
            return element;
        }

        var current = element;
        foreach (var step in path.Split('.'))
        {
            var name = step;
            var bracket = step.IndexOf('[');
            if (bracket >= 0)
            {
                name = step[..bracket];
                var index = int.Parse(step[(bracket + 1)..^1]);
                current = current.GetProperty(name);
                Assert.True(current.GetArrayLength() > index, $"{route}: {path} has no element {index}");
                current = current[index];
            }
            else
            {
                current = current.GetProperty(name);
            }
        }

        return current;
    }
}
