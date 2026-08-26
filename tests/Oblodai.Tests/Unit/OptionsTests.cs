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
        Assert.Throws<ConfigException>(() => new OblodaiOptions { Secret = "s" }.Resolve(Env()));
        Assert.Throws<ConfigException>(() => new OblodaiOptions().Resolve(Env(("OBLODAI_SECRET", "s"))));
    }

    [Fact]
    public void PicksUpTheAdminToken()
    {
        var resolved = new OblodaiOptions().Resolve(Env(
            ("OBLODAI_PUBLIC_ID", "pk"),
            ("OBLODAI_SECRET", "s"),
            ("OBLODAI_ADMIN_TOKEN", "adm")));

        Assert.Equal(new Credentials("pk", "s"), resolved.Credentials);
        Assert.Equal("adm", resolved.AdminToken);
    }

    /// <summary>
    /// The whole environment vocabulary, so a variable cannot be added (or a removed one revived) without
    /// this list saying so. A merchant has ONE API key; there is no second pair to configure.
    /// </summary>
    [Fact]
    public void TheEnvironmentVocabularyIsExactlyTheSixDocumentedNames()
    {
        var seen = new List<string>();
        new OblodaiOptions().Resolve(name =>
        {
            seen.Add(name);
            return null;
        });

        Assert.Equal(
            new[]
            {
                "OBLODAI_ADMIN_TOKEN",
                "OBLODAI_ALLOW_INSECURE",
                "OBLODAI_BASE_URL",
                "OBLODAI_LOG",
                "OBLODAI_PUBLIC_ID",
                "OBLODAI_SECRET",
            },
            seen.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray());
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
        // Every rejection is the SDK's own error, so `catch (OblodaiException)` around SDK calls holds.
        foreach (var bad in new[] { "25,5", string.Empty, ".", "1.", "-", "1e6", " 1", "1 ", "٣", "0x10", "1.2.3" })
        {
            var error = Assert.Throws<ConfigException>(() => Money.Add(bad, "1"));
            Assert.Equal(SdkErrorCodes.BadAmount, error.Code);
        }

        var tooLong = Assert.Throws<ConfigException>(() => Money.Compare(new string('9', 65), "1"));
        Assert.Equal(SdkErrorCodes.BadAmount, tooLong.Code);
        Assert.Equal(1, Money.Compare(new string('9', 64), "1"));
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
        var future = (PaymentStatus)"quantum_settled";
        Assert.False(future.IsKnown);
        Assert.Equal("quantum_settled", future.Value);
        Assert.True(PaymentStatus.Paid.IsKnown);
        Assert.Equal(PaymentStatus.Paid, PaymentStatus.FromValue("paid"));
    }

    [Fact]
    public void IdempotencyKeysAreValidatedBeforeTheyAreSigned()
    {
        Idempotency.AssertValid("order-1");
        Assert.Throws<ConfigException>(() => Idempotency.AssertValid(string.Empty));
        Assert.Throws<ConfigException>(() => Idempotency.AssertValid(new string('k', 256)));
        Assert.Throws<ConfigException>(() => Idempotency.AssertValid("has space"));
        Assert.Throws<ConfigException>(() => Idempotency.AssertValid("tab\there"));
        Assert.Matches("^[0-9a-f-]{36}$", Idempotency.NewKey());
    }
}
