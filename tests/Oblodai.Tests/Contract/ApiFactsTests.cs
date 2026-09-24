using Oblodai.Contract;
using Xunit;

namespace Oblodai.Tests.Contract;

/// <summary>
/// The runtime keeps no copy of the API's facts: long-running operations, webhook kinds, status
/// classes and the numbers that are not money all come from <see cref="ApiFacts"/> and the generated
/// vocabularies, and the hand-written helpers agree with them.
/// </summary>
public class ApiFactsTests
{
    [Fact]
    public void LongRunningReadsTheGeneratedTable()
    {
        Assert.NotEmpty(ApiFacts.Polls);
        Assert.Equal(
            ApiFacts.Polls.Select(p => (p.Key, p.Value.Operation)).OrderBy(p => p.Key, StringComparer.Ordinal),
            LongRunning.Operations.Select(p => (p.Key, p.Value)).OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.True(LongRunning.TerminalStatuses.SetEquals(ApiFacts.Polls.Values.SelectMany(p => p.Terminal)));
        foreach (var (create, poll) in ApiFacts.Polls)
        {
            Assert.True(Resources.Routes.All.ContainsKey(create), create);
            Assert.True(Resources.Routes.All[poll.Operation].Safe, poll.Operation);
            Assert.True(poll.Download is null || Resources.Routes.All[poll.Download].Bare, poll.Download);
        }
    }

    [Fact]
    public void EveryWebhookKindParsesIntoItsGeneratedModel()
    {
        Assert.Equal(ApiFacts.WebhookModels.Keys.Order(StringComparer.Ordinal), ApiFacts.WebhookKinds);
        Assert.All(ApiFacts.WebhookEvents.Values, kind => Assert.Contains(kind, ApiFacts.WebhookKinds));
        foreach (var (kind, model) in ApiFacts.WebhookModels)
        {
            var parsed = WebhookVerifier.Parse($$"""{"type":"{{kind}}","event_at":"2026-09-25T00:00:00Z","sequence":5,"test":true}""");

            Assert.IsType(model, parsed);
            Assert.True(WebhookVerifier.IsKnownEvent(parsed), kind);
            Assert.Equal(kind, parsed.Type);
            Assert.Equal("2026-09-25T00:00:00Z", parsed.EventAt);
            Assert.Equal(5, parsed.EventSequence);
            Assert.True(WebhookVerifier.IsTestEvent(parsed));
        }
    }

    [Fact]
    public void ConversionEventsAreKnown()
    {
        Assert.Equal("conversion", ApiFacts.WebhookEvents["conversion.completed"]);
        Assert.Equal("conversion", ApiFacts.WebhookEvents["conversion.refunded"]);

        var parsed = WebhookVerifier.Parse("""{"type":"conversion","event_at":"2026-09-25T00:00:00Z","sequence":9}""");

        Assert.IsType<ConversionWebhook>(parsed);
        Assert.True(WebhookVerifier.IsKnownEvent(parsed));
    }

    [Fact]
    public void StatusHelpersAreTheGeneratedClasses()
    {
        foreach (var status in PaymentStatus.Known)
        {
            Assert.Equal(PaymentStatus.Final.Contains(status), Statuses.IsPaymentFinal(status));
            Assert.Equal(PaymentStatus.Success.Contains(status), Statuses.IsPaymentPaid(status));
            Assert.True(!status.IsSuccess || status.IsFinal, status.Value);
        }

        foreach (var status in PayoutStatus.Known)
        {
            Assert.Equal(PayoutStatus.Final.Contains(status), Statuses.IsPayoutFinal(status));
            Assert.Equal(PayoutStatus.Success.Contains(status), Statuses.IsPayoutSucceeded(status));
        }

        Assert.Equal(PaymentStatus.Final, Statuses.FinalPaymentStatuses);
        Assert.Equal(PayoutStatus.Final, Statuses.FinalPayoutStatuses);
        Assert.False(Statuses.IsPaymentFinal(PaymentStatus.FromValue("settled_later")));
        Assert.False(Statuses.IsPaymentPaid(PaymentStatus.FromValue("settled_later")));
        Assert.True(Statuses.IsPaymentFinal(PaymentStatus.WrongAmount) && !Statuses.IsPaymentPaid(PaymentStatus.WrongAmount));
        Assert.Empty(BatchStatus.Success);
    }

    [Fact]
    public void FloatingPointOutsideTheModelIsAllowedOnlyUnderANonMoneyName()
    {
        Assert.Contains("accuracy_payment_percent", ApiFacts.NonMoneyNumbers);

        // A field the model does not know (it would travel in Extra) is money unless the contract types it as a number.
        var error = Assert.Throws<ConfigException>(() => Model.From<CancelPayoutRequest>(new Dictionary<string, object?>
        {
            ["uuid"] = "p-1",
            ["fee_cap"] = 0.5,
        }));
        Assert.Equal(SdkErrorCodes.FloatAmount, error.Code);
        Assert.Equal("fee_cap", error.Field);

        var nested = Assert.Throws<ConfigException>(() => Model.From<CancelPayoutRequest>(new Dictionary<string, object?>
        {
            ["uuid"] = "p-1",
            ["future"] = new Dictionary<string, object?> { ["limits"] = new List<object?> { 2.5 } },
        }));
        Assert.Equal("future.limits[0]", nested.Field);

        var allowed = Model.From<CancelPayoutRequest>(new Dictionary<string, object?>
        {
            ["uuid"] = "p-1",
            ["accuracy_payment_percent"] = 0.5,
        });
        Assert.Equal(0.5, allowed.Extra!["accuracy_payment_percent"].GetDouble());
    }
}
