using System.Net;
using System.Text;
using Oblodai.Examples;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Readme;

/// <summary>The programs in <c>examples/</c> run here, against a scripted gateway.</summary>
public class ExamplesTests
{
    private static OblodaiClient Client(FakeHttpHandler gateway)
        => new(new OblodaiOptions { PublicId = "pk", Secret = "s", BaseUrl = "https://api.test" }, gateway.Client());

    [Fact]
    public async Task AcceptPaymentPollsUntilTheInvoiceIsFinal()
    {
        var gateway = new FakeHttpHandler(
            ScriptedResponse.Ok("""{"uuid":"i1","status":"created","url":"https://pay.test/i1","payer_amount":"25","payer_currency":"USDT","address":"TX"}"""),
            ScriptedResponse.Ok("""{"uuid":"i1","status":"confirm_check","amount_paid":"0"}"""),
            ScriptedResponse.Ok("""{"uuid":"i1","status":"paid","amount_paid":"25"}"""));
        using var client = Client(gateway);
        var output = new StringWriter();

        var status = await AcceptPaymentExample.RunAsync(client, output, TimeSpan.Zero);

        Assert.Equal(PaymentStatus.Paid, status);
        Assert.Contains("pay at https://pay.test/i1", output.ToString());
        Assert.Contains("paid 25 USDT", output.ToString());
        Assert.Equal(3, gateway.Calls.Count);
    }

    [Fact]
    public async Task PayoutValidatesThenCreatesWithItsOwnKeyAndPrintsARefusal()
    {
        var gateway = new FakeHttpHandler(
            ScriptedResponse.Ok("""{"valid":true,"payer_amount":"11","commission":"1","fee_bearer":"merchant"}"""),
            ScriptedResponse.Ok("""{"uuid":"p1","status":"pending"}"""));
        using var client = Client(gateway);
        var output = new StringWriter();

        await PayoutExample.RunAsync(client, output);

        Assert.Contains("p1 pending", output.ToString());
        Assert.Equal("payout-42", gateway.Calls[1].Header(RequestSigner.HeaderIdempotencyKey));

        var refusing = new FakeHttpHandler(ScriptedResponse.Error(
            409, """{"code":"payout.insufficient_funds","message":"not enough","retryable":false,"request_id":"r1"}"""));
        using var poor = Client(refusing);
        var refused = new StringWriter();
        await PayoutExample.RunAsync(poor, refused);
        Assert.Contains("[payout.insufficient_funds] not enough (request_id=r1)", refused.ToString());
    }

    [Fact]
    public async Task SandboxWalksFaucetInvoiceDepositAndTheWebhookLog()
    {
        var gateway = new FakeHttpHandler(
            ScriptedResponse.Ok("""{"asset":"USDT","amount":"1000"}"""),
            ScriptedResponse.Ok("""{"uuid":"i1","status":"created"}"""),
            ScriptedResponse.Ok("""{"invoice_id":"i1","txid":"t","confirmations":20,"amount":"25"}"""),
            ScriptedResponse.Ok("""{"uuid":"i1","status":"paid"}"""),
            ScriptedResponse.Ok("""{"valid":true,"payer_amount":"11","commission":"1"}"""),
            ScriptedResponse.Page("""[{"id":"d1","event_type":"invoice.paid","status":"delivered","attempts":1}]""", 0, 1, 20, false));
        using var client = Client(gateway);
        var output = new StringWriter();

        await SandboxExample.RunAsync(client, output);

        Assert.Contains("paid", output.ToString());
        Assert.Contains("invoice.paid delivered attempts=1", output.ToString());
        Assert.Contains("limit=20", gateway.Calls[^1].Url);
    }

    [Fact]
    public void TheWebhookReceiverVerifiesDeduplicatesAndRefuses()
    {
        var sample = Repo.WebhookSamples.EnumerateArray()
            .First(s => s.GetProperty("body").GetProperty("type").GetString() == "payment"
                        && !s.GetProperty("headers").TryGetProperty("X-Webhook-Test", out _));
        var headers = sample.GetProperty("headers").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        headers.TryAdd("X-Webhook-Event-Id", "state-1");
        var raw = Encoding.UTF8.GetBytes(sample.GetProperty("raw").GetString()!);
        var ts = long.Parse(headers["X-Webhook-Timestamp"]);
        var output = new StringWriter();
        var receiver = new WebhookReceiverExample(Repo.WebhookSamplesSecret, null, output, () => ts);
        string? Header(string name) => headers.TryGetValue(name, out var v) ? v : null;

        Assert.Equal(HttpStatusCode.OK, receiver.Handle(raw, Header));
        var first = output.ToString();
        Assert.Contains(sample.GetProperty("body").GetProperty("uuid").GetString()!, first);

        Assert.Equal(HttpStatusCode.OK, receiver.Handle(raw, Header));   // the same state again: dropped
        Assert.Equal(first, output.ToString());

        var tampered = raw.Concat(" "u8.ToArray()).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, receiver.Handle(tampered, Header));
    }
}
