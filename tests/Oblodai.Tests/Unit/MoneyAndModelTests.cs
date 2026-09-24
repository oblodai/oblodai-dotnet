using System.Reflection;
using System.Text.Json;
using Oblodai.Resources;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// Money is <see cref="decimal"/> or a decimal string and never binary floating point; models parse
/// tolerantly and print short.
/// </summary>
public class MoneyAndModelTests
{
    private static OblodaiClient Client(FakeHttpHandler handler)
        => new(new OblodaiOptions { PublicId = "pk", Secret = "s", BaseUrl = "https://api.test" }, handler.Client());

    [Fact]
    public void EveryGeneratedMoneyParameterIsDecimalOrStringSoADoubleDoesNotCompile()
    {
        // `CreateAsync(amount: 25.5, …)` is a compile error: there is no implicit double → decimal
        // (nor → string, where the contract keeps an amount as text).
        var money = typeof(Payments).Assembly.GetTypes()
            .Where(t => t.Namespace == "Oblodai.Resources" && t.IsSubclassOf(typeof(Resource)))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetParameters())
            .Where(p => p.Name is "amount" or "minAmount" or "maxAmount")
            .ToList();

        Assert.NotEmpty(money);
        Assert.All(money, p => Assert.Contains(
            Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType, new[] { typeof(decimal), typeof(string) }));
        Assert.Contains(money, p => p.ParameterType == typeof(decimal));
    }

    [Fact]
    public async Task AFloatInADictionaryBodyFailsBeforeTheNetwork()
    {
        var handler = new FakeHttpHandler();
        using var client = Client(handler);

        var error = Assert.Throws<ConfigException>(() => Model.From<PaymentRequest>(
            new Dictionary<string, object?> { ["amount"] = 25.5, ["currency"] = "USDT" }));

        Assert.Equal(SdkErrorCodes.FloatAmount, error.Code);
        Assert.Equal("amount", error.Field);
        Assert.Empty(handler.Calls);

        // A double where the model has a double (not money) is fine.
        var request = Model.From<PaymentRequest>(new Dictionary<string, object?>
        {
            ["amount"] = "25.10",
            ["currency"] = "USDT",
            ["accuracy_payment_percent"] = 1.5,
        });
        await Task.CompletedTask;
        Assert.Equal(25.10m, request.Amount);
        Assert.Equal(1.5, request.AccuracyPaymentPercent);
    }

    [Fact]
    public void AFloatNestedInAListOfModelsIsCaughtToo()
    {
        var error = Assert.Throws<ConfigException>(() => Model.From<MassPayoutRequest>(new Dictionary<string, object?>
        {
            ["payouts"] = new List<object?>
            {
                new Dictionary<string, object?> { ["amount"] = 1.25f, ["currency"] = "USDT", ["address"] = "T", ["order_id"] = "o" },
            },
        }));

        Assert.Equal(SdkErrorCodes.FloatAmount, error.Code);
        Assert.Equal("payouts[0].amount", error.Field);
    }

    [Fact]
    public async Task ADecimalGoesToTheWireAsAStringWithItsScale()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Ok("""{"uuid":"x","amount":"25.10"}"""));
        using var client = Client(handler);

        var payment = await client.Payments.CreateAsync(amount: 25.10m, currency: "USDT");

        Assert.Equal("25.10", JsonDocument.Parse(handler.Calls[0].Body!).RootElement.GetProperty("amount").GetString());
        Assert.Equal("25.10", Money.Format(payment.Amount));
    }

    [Fact]
    public void MoneyHelpersTakeDecimalsAndStringsAndRefuseFloats()
    {
        Assert.Equal(25.10m, Money.Of("25.10"));
        Assert.Equal(25.10m, Money.Of(25.10m));
        Assert.Equal(25m, Money.Of(25));
        Assert.Equal("25.10", Money.Format(Money.Parse("25.10")));
        Assert.Equal(SdkErrorCodes.FloatAmount, Assert.Throws<ConfigException>(() => Money.Of(0.1)).Code);
        Assert.Equal(SdkErrorCodes.FloatAmount, Assert.Throws<ConfigException>(() => Money.Of(0.1f)).Code);
        Assert.Equal(SdkErrorCodes.BadAmount, Assert.Throws<ConfigException>(() => Money.Parse("1e3")).Code);
        Assert.Equal("0.3", Money.Add("0.1", "0.2"));
    }

    [Fact]
    public void AnUnknownFieldLandsInExtraAndAnUnknownEnumValueIsKept()
    {
        var payment = JsonSerializer.Deserialize<PaymentView>(
            """{"uuid":"x","status":"teleported","amount":"1","brand_new":{"a":1}}""", OblodaiJson.Options)!;

        Assert.Equal("teleported", payment.Status.Value);
        Assert.False(payment.Status.IsKnown);
        Assert.True(PaymentStatus.Paid.IsKnown);
        Assert.Equal(1, payment.Extra!["brand_new"].GetProperty("a").GetInt32());
    }

    [Fact]
    public void AFieldTheContractCallsRequiredButTheAnswerOmitsDoesNotFailTheCall()
    {
        var payment = JsonSerializer.Deserialize<PaymentView>("""{"uuid":"x"}""", OblodaiJson.Options)!;

        Assert.Equal("x", payment.Uuid);
        Assert.Equal(string.Empty, payment.Address ?? string.Empty);
    }

    [Fact]
    public void ToStringIsShortSkipsNullsAndRedactsSecrets()
    {
        var link = JsonSerializer.Deserialize<PayoutLinkCreated>(
            """{"link_id":"l1","claim_token":"tok_secret","claim_url":"https://x/c/tok_secret","passcode":null,"passcode_protected":false}""",
            OblodaiJson.Options)!;

        var text = link.ToString();

        Assert.StartsWith("PayoutLinkCreated { ", text);
        Assert.Contains("LinkId = l1", text);
        Assert.DoesNotContain("tok_secret", text);
        Assert.DoesNotContain("Passcode = ", text);
        Assert.Contains("PasscodeProtected = False", text);

        var big = JsonSerializer.Deserialize<PaymentView>(
            "{\"uuid\":\"x\",\"additional_data\":\"" + new string('a', 5000) + "\"}", OblodaiJson.Options)!;
        Assert.True(big.ToString().Length <= Model.ToStringLimit);
        Assert.EndsWith("…}", big.ToString());
    }
}
