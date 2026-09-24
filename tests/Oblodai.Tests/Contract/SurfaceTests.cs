using System.Reflection;
using Oblodai.Resources;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Contract;

/// <summary>
/// The public surface is what <c>names.lock</c> pins: every locked <c>resource.method</c> is
/// <c>client.Resource.MethodAsync</c>, every route has a method, and the route table is the contract's.
/// </summary>
public class SurfaceTests
{
    private static readonly string[] Locked = File.ReadAllLines(Path.Combine(Repo.Root, "names.lock"))
        .Select(l => l.Trim())
        .Where(l => l.Length > 0 && !l.StartsWith('#'))
        .ToArray();

    public static TheoryData<string> LockedNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Locked)
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LockedNames))]
    public void EveryLockedNameIsAClientMethod(string locked)
    {
        var (resource, method) = (locked.Split('.')[0], locked.Split('.')[1]);

        var property = typeof(OblodaiClient).GetProperty(Pascal(resource));
        Assert.NotNull(property);
        var methods = property!.PropertyType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == Pascal(method) + "Async")
            .ToList();
        Assert.NotEmpty(methods);
        Assert.All(methods, m => Assert.Equal(typeof(CancellationToken), m.GetParameters()[^1].ParameterType));
        Assert.All(methods, m => Assert.Equal(typeof(RequestOptions), m.GetParameters()[^2].ParameterType));
    }

    [Fact]
    public void OneRoutePerLockedNameWithUniqueOperationIds()
    {
        Assert.Equal(Locked.Length, Routes.All.Count);
        Assert.All(Routes.All, pair => Assert.Equal(pair.Key, pair.Value.OperationId));
        Assert.Equal(Routes.All.Count, Routes.All.Values.Select(r => $"{r.Method} {r.Path}").Distinct().Count());
    }

    [Fact]
    public void EveryResourceOnTheClientIsGeneratedAndBoundToItsTransport()
    {
        using var client = new OblodaiClient(new OblodaiOptions { BaseUrl = "https://api.test" });
        var resources = typeof(OblodaiClient).GetProperties()
            .Where(p => typeof(Resource).IsAssignableFrom(p.PropertyType))
            .ToList();

        Assert.Equal(Locked.Select(l => l.Split('.')[0]).Distinct().Count(), resources.Count);
        Assert.All(resources, p => Assert.NotNull(p.GetValue(client)));
    }

    [Fact]
    public void RoutesThatPageAreTheOnesThatReturnALazyList()
    {
        var paged = typeof(OblodaiClient).GetProperties()
            .Where(p => typeof(Resource).IsAssignableFrom(p.PropertyType))
            .SelectMany(p => p.PropertyType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.ReturnType.IsGenericType && m.ReturnType.GetGenericTypeDefinition() == typeof(PagePromise<>))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .Distinct()
            .Count();

        Assert.Equal(Routes.All.Values.Count(r => r.List == Oblodai.Contract.ListKind.Paged), paged);
    }

    private static string Pascal(string snake)
        => string.Concat(snake.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
