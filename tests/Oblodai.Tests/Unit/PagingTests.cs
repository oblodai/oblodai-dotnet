using System.Text.Json;
using Oblodai.Contract;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// What a list method promises: one page when awaited, every page when iterated, nothing at all until
/// it is consumed — and never an idempotency key on a page request.
/// </summary>
public class PagingTests
{
    private static OblodaiClient Client(FakeHttpHandler handler)
        => new(
            new OblodaiOptions
            {
                PublicId = "pk",
                Secret = "s",
                BaseUrl = "https://api.test",
                Retry = new RetryOptions { BaseDelayMs = 1, MaxDelayMs = 2 },
            },
            handler.Client());

    private static string Payments(params string[] uuids)
        => "[" + string.Join(",", uuids.Select(u => $"{{\"uuid\":\"{u}\"}}")) + "]";

    [Fact]
    public async Task AwaitGivesTheFirstPageOnly()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Page(Payments("a", "b"), 0, 5, 2, true));
        using var client = Client(handler);

        var page = await client.Payments.ListHistoryAsync(limit: 2);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(5, page.Paginate.Total);
        Assert.True(page.Paginate.HasPages);
        Assert.Single(handler.Calls);
        Assert.Equal(2, JsonDocument.Parse(handler.Calls[0].Body!).RootElement.GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task IterationWalksEveryPageLazily()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Page(Payments("a", "b"), 0, 5, 2, true),
            ScriptedResponse.Page(Payments("c", "d"), 2, 5, 2, true),
            ScriptedResponse.Page(Payments("e"), 4, 5, 2, false));
        using var client = Client(handler);

        var seen = new List<string>();
        await foreach (var payment in client.Payments.ListHistoryAsync(limit: 2))
        {
            seen.Add(payment.Uuid);
        }

        Assert.Equal(["a", "b", "c", "d", "e"], seen);
        Assert.Equal(3, handler.Calls.Count);

        var second = JsonDocument.Parse(handler.Calls[1].Body!).RootElement;
        Assert.Equal(2, second.GetProperty("limit").GetInt32());
        Assert.Equal(2, second.GetProperty("offset").GetInt32());
    }

    [Fact]
    public async Task AllAsyncCollectsWithACap()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Page(Payments("a", "b"), 0, 3, 2, true),
            ScriptedResponse.Page(Payments("c"), 2, 3, 2, false));
        using var client = Client(handler);

        var all = await client.Payments.ListHistoryAsync(limit: 2).AllAsync();
        Assert.Equal(3, all.Count);

        var capped = new FakeHttpHandler(ScriptedResponse.Page(Payments("a", "b"), 0, 9, 2, true));
        using var cappedClient = Client(capped);
        var first = await cappedClient.Payments.ListHistoryAsync(limit: 2).AllAsync(2);

        Assert.Equal(2, first.Count);
        Assert.Single(capped.Calls);
    }

    [Fact]
    public async Task RequestsNothingUntilConsumedAndSurfacesTheFailureOnlyThen()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Error(404, """{"code":"payment.not_found","retryable":false}"""));
        using var client = Client(handler);

        var pending = client.Payments.ListHistoryAsync();
        Assert.Empty(handler.Calls);

        // An unconsumed failing list must never take the process down; consuming it raises the error.
        var error = await Assert.ThrowsAsync<NotFoundException>(async () => await pending);
        Assert.Equal("payment.not_found", error.Code);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public void RefusesACallerIdempotencyKeyOnAListRouteInsteadOfDroppingIt()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Page("[]", 0, 0, 50, false));
        using var client = Client(handler);

        // Silently dropping the key would leave the caller believing a lost page request is safe to
        // repeat under it. The refusal happens at the call site, before any page is fetched.
        var post = Assert.Throws<ConfigException>(
            () => client.Payouts.ListHistoryAsync(options: new RequestOptions { IdempotencyKey = "k" }));
        Assert.Equal(SdkErrorCodes.IdempotencyUnsupported, post.Code);

        var get = Assert.Throws<ConfigException>(
            () => client.Sandbox.ListWebhooksAsync(options: new RequestOptions { IdempotencyKey = "k" }));
        Assert.Equal(SdkErrorCodes.IdempotencyUnsupported, get.Code);

        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task KeepsTheFilterOnEveryPage()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Page(Payments("a"), 0, 2, 1, true),
            ScriptedResponse.Page(Payments("b"), 1, 2, 1, false));
        using var client = Client(handler);

        await client.Payouts.ListHistoryAsync(kind: PayoutKind.Refund, status: "confirmed", limit: 1).AllAsync();

        foreach (var call in handler.Calls)
        {
            var body = JsonDocument.Parse(call.Body!).RootElement;
            Assert.Equal("refund", body.GetProperty("kind").GetString());
            Assert.Equal("confirmed", body.GetProperty("status").GetString());
            Assert.Equal(1, body.GetProperty("limit").GetInt32());
        }

        Assert.Equal(0, JsonDocument.Parse(handler.Calls[0].Body!).RootElement.GetProperty("offset").GetInt32());
        Assert.Equal(1, JsonDocument.Parse(handler.Calls[1].Body!).RootElement.GetProperty("offset").GetInt32());
    }

    [Fact]
    public async Task StopsOnAShortPageEvenWhenTheGatewayClaimsMore()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Page("[]", 0, 99, 50, true));
        using var client = Client(handler);

        var all = await client.Payments.ListHistoryAsync().AllAsync();

        Assert.Empty(all);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task PagesAGetListThroughTheQueryString()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Page("""[{"id":"d1"}]""", 0, 2, 1, true),
            ScriptedResponse.Page("""[{"id":"d2"}]""", 1, 2, 1, false));
        using var client = Client(handler);

        var deliveries = await client.Sandbox.ListWebhooksAsync(limit: 1).AllAsync();

        Assert.Equal(2, deliveries.Count);
        Assert.Contains("limit=1&offset=0", handler.Calls[0].Url);
        Assert.Contains("limit=1&offset=1", handler.Calls[1].Url);
        Assert.All(handler.Calls, call => Assert.Null(call.Body));
    }

    [Fact]
    public async Task ByPageWalksPageByPageReusingAnAlreadyFetchedFirstPage()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Page(Payments("a", "b"), 0, 3, 2, true),
            ScriptedResponse.Page(Payments("c"), 2, 3, 2, false));
        using var client = Client(handler);

        var list = client.Payments.ListHistoryAsync(limit: 2);
        var first = await list;
        var pages = new List<Page<PaymentView>>();
        await foreach (var page in list.ByPageAsync())
        {
            pages.Add(page);
        }

        Assert.Same(first, pages[0]);
        Assert.Equal([2, 1], pages.Select(p => p.Items.Count));
        Assert.Equal(2, handler.Calls.Count);
    }

    [Fact]
    public async Task ItemsAreGeneratedModelsWithTheirFields()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Page("""[{"uuid":"a","status":"paid","amount":"25.10","new_field":1}]""", 0, 1, 1, false));
        using var client = Client(handler);

        var payment = Assert.Single(await client.Payments.ListHistoryAsync().AllAsync());

        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(25.10m, payment.Amount);
        Assert.True(payment.Extra!.ContainsKey("new_field"));
    }
}
