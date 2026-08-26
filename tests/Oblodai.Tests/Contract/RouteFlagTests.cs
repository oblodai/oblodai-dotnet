using System.Text.Json;
using Oblodai.Contract;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Contract;

/// <summary>
/// The generated route registry against the contract that produced it, flag by flag. Whether a route
/// is signed at all, whether the gateway deduplicates it and whether a lost response may be re-sent
/// are the three facts that decide if a failed call can double a payout, so they are compared with
/// the source rather than with the last codegen run.
/// </summary>
public class RouteFlagTests
{
    public static TheoryData<string> RouteKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in Routes.All.Keys)
        {
            data.Add(key);
        }

        return data;
    }

    /// <summary>
    /// Every FLAG of every route, not just the key set. The key set matching proves the registry knows
    /// the same routes; it says nothing about which gate a route sits behind, whether the gateway
    /// deduplicates it, or whether a lost response may be re-sent — and those three decide whether a
    /// failed call can double a payout. <c>safe</c> comes from the core's own hand classification in
    /// <c>contract.json</c>; nothing here derives it from the path.
    /// </summary>
    /// <param name="key">Route key.</param>
    [Theory]
    [MemberData(nameof(RouteKeys))]
    public void EveryRouteFlagEqualsTheContract(string key)
    {
        var declared = Fixtures.Contract.GetProperty("routes").EnumerateArray()
            .Single(r => $"{r.GetProperty("method").GetString()} {r.GetProperty("path").GetString()}" == key);
        var route = Routes.All[key];

        Assert.Equal(declared.GetProperty("method").GetString(), route.Method);
        Assert.Equal(declared.GetProperty("path").GetString(), route.Path);
        Assert.Equal(declared.GetProperty("auth").GetString(), route.Auth.ToString().ToLowerInvariant());
        Assert.Equal(declared.GetProperty("idempotent").GetBoolean(), route.Idempotent);
        Assert.Equal(declared.GetProperty("safe").GetBoolean(), route.Safe);
        Assert.Equal(declared.GetProperty("bare").GetBoolean(), route.Bare);
        Assert.Equal(
            declared.TryGetProperty("list", out var list) ? list.GetString() : "none",
            route.List.ToString().ToLowerInvariant());
    }

    /// <summary>
    /// The flag check is only worth its runtime if it actually fails on a wrong flag. Every flag is
    /// flipped in turn against a real route and the comparison must reject each one.
    /// </summary>
    [Fact]
    public void TheFlagCheckRejectsEveryFlippedFlag()
    {
        var real = Routes.All["POST /v1/payout"];
        var mutants = new[]
        {
            real with { Auth = RouteAuth.Public },
            real with { Idempotent = !real.Idempotent },
            real with { Safe = !real.Safe },
            real with { Bare = !real.Bare },
            real with { List = ListKind.Paged },
            real with { Path = "/v1/payout/x" },
            real with { Method = "GET" },
        };

        var declared = Fixtures.Contract.GetProperty("routes").EnumerateArray()
            .Single(r => r.GetProperty("path").GetString() == "/v1/payout" && r.GetProperty("method").GetString() == "POST");

        Assert.True(Matches(declared, real));
        foreach (var mutant in mutants)
        {
            Assert.False(Matches(declared, mutant), $"a flipped flag went unnoticed: {mutant}");
        }
    }

    /// <summary>
    /// The auth vocabulary is closed: <c>public</c>, <c>key</c>, <c>onboard</c> and nothing else. A
    /// merchant holds ONE API key, so a snapshot that still splits payment from payout credentials
    /// would leave the SDK signing with the only pair it has at a gate that wants another — this fails
    /// on the export rather than at runtime.
    /// </summary>
    [Fact]
    public void TheContractDeclaresNoAuthGateBeyondPublicKeyAndOnboard()
    {
        var declared = Fixtures.Contract.GetProperty("routes").EnumerateArray()
            .Select(r => r.GetProperty("auth").GetString()!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "key", "onboard", "public" }, declared);
        Assert.Equal(
            new[] { RouteAuth.Key, RouteAuth.Onboard, RouteAuth.Public },
            Enum.GetValues<RouteAuth>().OrderBy(a => a.ToString(), StringComparer.Ordinal).ToArray());
    }

    /// <summary>The comparison <see cref="EveryRouteFlagEqualsTheContract"/> makes, as a predicate.</summary>
    /// <param name="declared">The route as contract.json declares it.</param>
    /// <param name="route">The route as the generated registry has it.</param>
    private static bool Matches(JsonElement declared, RouteSpec route)
        => declared.GetProperty("method").GetString() == route.Method
           && declared.GetProperty("path").GetString() == route.Path
           && declared.GetProperty("auth").GetString() == route.Auth.ToString().ToLowerInvariant()
           && declared.GetProperty("idempotent").GetBoolean() == route.Idempotent
           && declared.GetProperty("safe").GetBoolean() == route.Safe
           && declared.GetProperty("bare").GetBoolean() == route.Bare
           && (declared.TryGetProperty("list", out var list) ? list.GetString() : "none")
              == route.List.ToString().ToLowerInvariant();
}
