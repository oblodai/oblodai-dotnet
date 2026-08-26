using System.Text.Json;
using Oblodai.Contract;
using Oblodai.Models;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Contract;

/// <summary>
/// Checks over the recorded journey as a whole rather than one model at a time: that every fixture
/// belongs to a route the registry knows, that the recorded refusals use documented codes and the
/// documented envelope, and that the journey itself only ever sent fields the contract declares.
/// </summary>
public class FixtureTests
{
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
    public void EveryRecordedFixtureBelongsToAKnownRoute()
    {
        foreach (var route in Fixtures.All.Keys)
        {
            Assert.True(Routes.All.ContainsKey(route), $"{route}: fixture for a route the registry does not declare");
        }
    }

    [Fact]
    public void TheNamedFeeShapeMatchesTheTrioEveryPricedResultEmbeds()
    {
        // FeeInfo names the {commission, fee_bearer, fee_type} trio that priced results carry inline.
        // No route returns it on its own, so this is what keeps the type honest.
        var priced = Fixtures.For("POST /v1/payout/calculate").Result;
        foreach (var key in ModelReflection.WireKeys(typeof(FeeInfo)))
        {
            Assert.True(priced.TryGetProperty(key, out _), $"FeeInfo declares \"{key}\", which no priced result carries");
        }
    }
}
