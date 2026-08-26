using System.Reflection;
using Oblodai.Contract;
using Oblodai.Models;
using Oblodai.Resources;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Contract;

/// <summary>
/// Shape rules of the public surface that no single route test would catch: an alias whose parameters
/// drifted from the method it aliases, a paged route whose method forgot to expose the window, and the
/// id forms every lookup accepts.
/// </summary>
public class SurfaceTests
{
    /// <param name="Resource">The namespace type.</param>
    /// <param name="Alias">The alias method.</param>
    /// <param name="Canonical">The method it aliases.</param>
    private sealed record AliasPair(Type Resource, string Alias, string Canonical);

    private static readonly AliasPair[] Aliases =
    [
        new(typeof(Payments), "GetAsync", "InfoAsync"),
        new(typeof(Payments), "ListAsync", "HistoryAsync"),
        new(typeof(Payouts), "GetAsync", "InfoAsync"),
        new(typeof(Payouts), "ListAsync", "HistoryAsync"),
        new(typeof(PayoutLinks), "GetAsync", "InfoAsync"),
        new(typeof(PaymentLinks), "GetAsync", "InfoAsync"),
    ];

    public static TheoryData<int> AliasIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Aliases.Length; i++)
        {
            data.Add(i);
        }

        return data;
    }

    /// <summary>
    /// An alias that takes a narrower argument than the method it aliases is a trap: the caller who
    /// found <c>GetAsync</c> first never learns that <c>InfoAsync</c> accepts more.
    /// </summary>
    /// <param name="index">Row of <see cref="Aliases"/>.</param>
    [Theory]
    [MemberData(nameof(AliasIndexes))]
    public void AnAliasHasTheSameSignatureAsTheMethodItAliases(int index)
    {
        var (resource, alias, canonical) = Aliases[index];
        var aliasMethod = resource.GetMethod(alias, BindingFlags.Public | BindingFlags.Instance)!;
        var canonicalMethod = resource.GetMethod(canonical, BindingFlags.Public | BindingFlags.Instance)!;

        Assert.Equal(canonicalMethod.ReturnType, aliasMethod.ReturnType);
        Assert.Equal(
            canonicalMethod.GetParameters().Select(p => (p.Name, p.ParameterType, p.HasDefaultValue)),
            aliasMethod.GetParameters().Select(p => (p.Name, p.ParameterType, p.HasDefaultValue)));
    }

    [Fact]
    public void EveryPagedRouteHasAMethodThatCanAskForAWindow()
    {
        // A paged route whose method cannot take limit/offset can only ever return page one.
        var paged = Routes.All.Values.Where(r => r.List == ListKind.Paged).Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(paged);

        var windowed = typeof(OblodaiClient).GetProperties()
            .Select(p => p.PropertyType)
            .Where(t => t.IsSubclassOf(typeof(Resource)))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(m => m.ReturnType.IsGenericType
                        && m.ReturnType.GetGenericTypeDefinition() == typeof(PagePromise<>))
            .ToList();

        // One method per paged route, plus the two ListAsync aliases of HistoryAsync.
        var aliasMethods = Aliases.Count(a => a.Alias == "ListAsync");
        Assert.Equal(paged.Count + aliasMethods, windowed.Count);
        Assert.All(windowed, method =>
        {
            var first = method.GetParameters().First();
            Assert.True(
                first.ParameterType == typeof(PageParams) || first.ParameterType.Name.EndsWith("Request", StringComparison.Ordinal),
                $"{method.DeclaringType!.Name}.{method.Name} cannot be given a page window");
            Assert.True(first.HasDefaultValue, $"{method.DeclaringType!.Name}.{method.Name}: the window must be optional");
        });
    }

    [Fact]
    public async Task IdsAreAcceptedAsAStringOrAsTheObjectYouAlreadyHold()
    {
        var handler = new FakeHttpHandler(
            ScriptedResponse.Ok(Fixtures.ResultJson("POST /v1/payout/cancel")),
            ScriptedResponse.Ok(Fixtures.ResultJson("POST /v1/payout/cancel")),
            ScriptedResponse.Ok(Fixtures.ResultJson("POST /v1/payout/link/cancel")),
            ScriptedResponse.Ok(Fixtures.ResultJson("POST /v1/payout/link/cancel")),
            ScriptedResponse.Ok(Fixtures.ResultJson("POST /v1/payment/link/toggle")),
            ScriptedResponse.Ok(Fixtures.ResultJson("POST /v1/payment/info")));

        using var client = new OblodaiClient(
            new OblodaiOptions { PublicId = "pk", Secret = "s", BaseUrl = "https://api.test" },
            handler.Client());

        var payout = await client.Payouts.CancelAsync("p1");
        await client.Payouts.CancelAsync(payout);

        var link = await client.PayoutLinks.CancelAsync("l1");
        await client.PayoutLinks.CancelAsync(link);

        var toggled = await client.PaymentLinks.ToggleAsync("l2", false);
        await client.Payments.InfoAsync(new PaymentLookup { OrderId = "o1" });

        Assert.Equal(payout.Uuid, ReadBodyField(handler.Calls[1].Body!, "uuid"));
        Assert.Equal(link.LinkId, ReadBodyField(handler.Calls[3].Body!, "link_id"));
        Assert.NotEmpty(toggled.LinkId);
    }

    private static string ReadBodyField(string body, string name)
        => System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty(name).GetString()!;
}
