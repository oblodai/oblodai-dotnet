using System.Text.Json;

namespace Oblodai.Tests.Support;

/// <summary>A recorded exchange with a real gateway: the request sent and the answer it produced.</summary>
public sealed record Fixture(string Route, int Status, JsonElement? Request, JsonElement? Response, IReadOnlyDictionary<string, string> Headers)
{
    /// <summary>The recorded <c>result</c> payload (only for a success recording).</summary>
    public JsonElement Result => Response!.Value.GetProperty("result");

    /// <summary>The recorded <c>error</c> object (only for a refusal recording).</summary>
    public JsonElement Error => Response!.Value.GetProperty("error");

    public bool IsSuccess => Status is >= 200 and < 300;

    public bool IsJson => Headers.TryGetValue("Content-Type", out var type) && type.Contains("json", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The contract snapshot as the tests read it: routes, vectors, golden bodies, webhook deliveries.</summary>
public static class Fixtures
{
    private static readonly Lazy<string> RepoRootLazy = new(FindRepoRoot);
    private static readonly Lazy<JsonDocument> ContractLazy = new(() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractDirectory, "contract.json"))));
    private static readonly Lazy<IReadOnlyDictionary<string, Fixture>> FixturesLazy = new(LoadFixtures);
    private static readonly Lazy<IReadOnlyDictionary<string, Fixture>> ErrorsLazy = new(LoadErrors);
    private static readonly Lazy<JsonDocument> WebhookSamplesLazy = new(() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContractDirectory, "webhook-samples.json"))));

    /// <summary>Repository root (the directory that holds <c>contract/</c>).</summary>
    public static string RepoRoot => RepoRootLazy.Value;

    /// <summary>The <c>contract/</c> directory.</summary>
    public static string ContractDirectory => Path.Combine(RepoRoot, "contract");

    /// <summary>The parsed contract snapshot.</summary>
    public static JsonElement Contract => ContractLazy.Value.RootElement;

    /// <summary>Golden bodies by route key.</summary>
    public static IReadOnlyDictionary<string, Fixture> All => FixturesLazy.Value;

    /// <summary>Recorded error envelopes by error code.</summary>
    public static IReadOnlyDictionary<string, Fixture> Errors => ErrorsLazy.Value;

    /// <summary>Real signed webhook deliveries (headers, parsed body and the exact bytes).</summary>
    public static JsonElement WebhookSamples => WebhookSamplesLazy.Value.RootElement;

    /// <summary>The golden body of a route.</summary>
    public static Fixture For(string route) => All.TryGetValue(route, out var fixture)
        ? fixture
        : throw new InvalidOperationException($"no fixture for {route}");

    /// <summary>The recorded <c>result</c> of a route, as raw JSON text.</summary>
    public static string ResultJson(string route) => For(route).Result.GetRawText();

    /// <summary>Every route key the snapshot declares for merchants (the SDK's coverage ledger).</summary>
    public static IReadOnlyList<string> DeclaredRoutes()
    {
        var skip = new[] { "/healthz", "/readyz", "/docs", "/openapi.json", "/internal" };
        return Contract.GetProperty("routes").EnumerateArray()
            .Select(r => (Method: r.GetProperty("method").GetString()!, Path: r.GetProperty("path").GetString()!))
            .Where(r => !skip.Any(s => r.Path.StartsWith(s, StringComparison.Ordinal)))
            .Select(r => $"{r.Method} {r.Path}")
            .ToList();
    }

    private static IReadOnlyDictionary<string, Fixture> LoadFixtures()
        => Load(Path.Combine(ContractDirectory, "fixtures"), f => f.Route);

    private static IReadOnlyDictionary<string, Fixture> LoadErrors()
        => Load(Path.Combine(ContractDirectory, "errors"), f => f.Error.GetProperty("code").GetString()!);

    private static IReadOnlyDictionary<string, Fixture> Load(string directory, Func<Fixture, string> key)
    {
        var output = new Dictionary<string, Fixture>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(file));
            var root = document.RootElement.Clone();
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("headers", out var headerElement))
            {
                foreach (var header in headerElement.EnumerateObject())
                {
                    headers[header.Name] = header.Value.GetString() ?? string.Empty;
                }
            }

            var fixture = new Fixture(
                root.GetProperty("route").GetString()!,
                root.GetProperty("status").GetInt32(),
                root.TryGetProperty("request", out var request) ? request : null,
                root.TryGetProperty("response", out var response) ? response : null,
                headers);
            output[key(fixture)] = fixture;
        }

        return output;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "contract", "contract.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("cannot find the repository root from " + AppContext.BaseDirectory);
    }
}
