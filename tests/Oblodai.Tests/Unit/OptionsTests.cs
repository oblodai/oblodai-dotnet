using Oblodai.Contract;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>Option resolution, money arithmetic and the status helpers.</summary>
public class OptionsTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
        return key => map.TryGetValue(key, out var value) ? value : null;
    }

    [Fact]
    public void ReadsCredentialsAndBaseUrlFromTheEnvironment()
    {
        var resolved = new OblodaiOptions().Resolve(Env(
            ("OBLODAI_PUBLIC_ID", "pk"),
            ("OBLODAI_SECRET", "s"),
            ("OBLODAI_BASE_URL", "https://x.test/")));

        Assert.Equal(new Credentials("pk", "s"), resolved.Credentials);
        Assert.Equal("https://x.test", resolved.BaseUrl);
    }

    [Fact]
    public void ExplicitOptionsWinOverTheEnvironment()
    {
        var resolved = new OblodaiOptions { PublicId = "code", Secret = "code-secret" }
            .Resolve(Env(("OBLODAI_PUBLIC_ID", "env"), ("OBLODAI_SECRET", "env-secret")));

        Assert.Equal("code", resolved.Credentials!.PublicId);
    }

    [Fact]
    public void RefusesPlainHttpExceptForLoopbackOrWhenAllowed()
    {
        var refused = Assert.Throws<ConfigException>(() =>
            new OblodaiOptions { BaseUrl = "http://api.oblodai.com" }.Resolve(Env()));
        Assert.Contains("https", refused.Message);
        Assert.Equal(SdkErrorCodes.BadConfig, refused.Code);

        Assert.Equal("http://localhost:8093", new OblodaiOptions { BaseUrl = "http://localhost:8093" }.Resolve(Env()).BaseUrl);
        Assert.Equal("http://127.0.0.1:8095", new OblodaiOptions { BaseUrl = "http://127.0.0.1:8095" }.Resolve(Env()).BaseUrl);
        Assert.Equal("http://[::1]:8093", new OblodaiOptions { BaseUrl = "http://[::1]:8093" }.Resolve(Env()).BaseUrl);
        Assert.Equal(
            "http://10.0.0.1",
            new OblodaiOptions { BaseUrl = "http://10.0.0.1", AllowInsecureBaseUrl = true }.Resolve(Env()).BaseUrl);
        Assert.Equal(
            "http://10.0.0.1",
            new OblodaiOptions { BaseUrl = "http://10.0.0.1" }.Resolve(Env(("OBLODAI_ALLOW_INSECURE", "1"))).BaseUrl);
    }

    [Fact]
    public void RefusesHalfAKeyPair()
    {
        Assert.Throws<ConfigException>(() => new OblodaiOptions { PublicId = "pk" }.Resolve(Env()));
        Assert.Throws<ConfigException>(() => new OblodaiOptions { PayoutSecret = "s" }.Resolve(Env()));
    }

    [Fact]
    public void PicksUpThePayoutPairAndTheAdminToken()
    {
        var resolved = new OblodaiOptions().Resolve(Env(
            ("OBLODAI_PUBLIC_ID", "pk"),
            ("OBLODAI_SECRET", "s"),
            ("OBLODAI_PAYOUT_PUBLIC_ID", "wk"),
            ("OBLODAI_PAYOUT_SECRET", "s2"),
            ("OBLODAI_ADMIN_TOKEN", "adm")));

        Assert.Equal(new Credentials("wk", "s2"), resolved.PayoutCredentials);
        Assert.Equal("adm", resolved.AdminToken);
    }

    [Fact]
    public void EnablesTheConsoleLoggerFromTheEnvironment()
    {
        Assert.IsType<ConsoleLogger>(new OblodaiOptions().Resolve(Env(("OBLODAI_LOG", "debug"))).Logger);
        Assert.Null(new OblodaiOptions().Resolve(Env(("OBLODAI_LOG", "nonsense"))).Logger);
        Assert.Null(new OblodaiOptions().Resolve(Env()).Logger);
    }

    [Fact]
    public void MoneyHelpersWorkAtArbitraryPrecision()
    {
        Assert.Equal("0.3", Money.Add("0.1", "0.2"));
        Assert.Equal("10.500000", Money.Add("10.000000", "0.5"));
        Assert.Equal("-0.000001", Money.Subtract("1", "1.000001"));
        Assert.Equal(0, Money.Compare("25", "25.000000"));
        Assert.Equal(1, Money.Compare("0.000000000000000001", "0"));
        Assert.Equal(-1, Money.Compare("-1", "0"));
        Assert.True(Money.IsZero("0.000000"));
        Assert.True(Money.AreEqual("25", "25.00"));
        Assert.Throws<FormatException>(() => Money.Add("25,5", "1"));
    }

    [Fact]
    public void StatusHelpersFollowTheGatewayVocabulary()
    {
        Assert.True(Statuses.IsPaymentPaid(PaymentStatus.PaidOver));
        Assert.False(Statuses.IsPaymentPaid(PaymentStatus.WrongAmount));
        Assert.True(Statuses.IsPaymentUnderpaid(PaymentStatus.WrongAmount));
        Assert.False(Statuses.IsPaymentFinal(PaymentStatus.ConfirmCheck));
        Assert.False(Statuses.IsPayoutFinal(PayoutStatus.Sent));
        Assert.True(Statuses.IsPayoutFinal(PayoutStatus.Confirmed));
        Assert.True(Statuses.IsPayoutSucceeded(PayoutStatus.Confirmed));
    }

    [Fact]
    public void OpenVocabulariesCarryUnknownValuesThrough()
    {
        PaymentStatus future = "quantum_settled";
        Assert.False(future.IsKnown);
        Assert.Equal("quantum_settled", future.Value);
        Assert.True(PaymentStatus.Paid.IsKnown);
        Assert.Equal(PaymentStatus.Paid, PaymentStatus.FromValue("paid"));
    }

    [Fact]
    public void IdempotencyKeysAreValidatedBeforeTheyAreSigned()
    {
        Idempotency.AssertValid("order-1");
        Assert.Throws<ValidationException>(() => Idempotency.AssertValid(string.Empty));
        Assert.Throws<ValidationException>(() => Idempotency.AssertValid(new string('k', 256)));
        Assert.Throws<ValidationException>(() => Idempotency.AssertValid("has space"));
        Assert.Throws<ValidationException>(() => Idempotency.AssertValid("tab\there"));
        Assert.Matches("^[0-9a-f-]{36}$", Idempotency.NewKey());
    }
}
