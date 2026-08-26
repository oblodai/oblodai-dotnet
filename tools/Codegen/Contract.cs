using System.Text.Json;
using System.Text.RegularExpressions;

namespace Oblodai.Codegen;

/// <summary>One route as the gateway's conformance table declares it.</summary>
internal sealed record Route(
    string Method,
    string Path,
    string Auth,
    bool Idempotent,
    bool Safe,
    bool Bare,
    string? List,
    JsonElement? RequestSchema)
{
    public string Key => $"{Method} {Path}";
}

/// <summary>The contract snapshot: routes, vocabularies, error codes and the English field docs.</summary>
internal sealed class Contract
{
    private static readonly Regex NonMerchant = new(@"^/(healthz|readyz|docs|openapi\.json|internal)", RegexOptions.Compiled);

    private Contract(JsonDocument document, JsonDocument descriptions, byte[] rawBytes)
    {
        Document = document;
        Descriptions = descriptions;
        RawBytes = rawBytes;

        Routes = document.RootElement.GetProperty("routes").EnumerateArray()
            .Select(r => new Route(
                r.GetProperty("method").GetString()!,
                r.GetProperty("path").GetString()!,
                r.GetProperty("auth").GetString()!,
                r.GetProperty("idempotent").GetBoolean(),
                ReadSafe(r),
                r.GetProperty("bare").GetBoolean(),
                r.TryGetProperty("list", out var list) ? list.GetString() : null,
                r.TryGetProperty("request_schema", out var schema) ? schema : null))
            .Where(r => !NonMerchant.IsMatch(r.Path))
            .OrderBy(r => r.Path, StringComparer.Ordinal)
            .ThenBy(r => r.Method, StringComparer.Ordinal)
            .ToList();
    }

    public JsonDocument Document { get; }

    public JsonDocument Descriptions { get; }

    public byte[] RawBytes { get; }

    public IReadOnlyList<Route> Routes { get; }

    public string CoreCommit => Document.RootElement.GetProperty("core_commit").GetString()!;

    public string ExportedAt => Document.RootElement.GetProperty("exported_at").GetString()!;

    public IReadOnlyList<string> Strings(string property)
        => Document.RootElement.GetProperty(property).EnumerateArray().Select(e => e.GetString()!).ToList();

    public IReadOnlyList<string> Enum(string name)
        => Document.RootElement.GetProperty("enums").GetProperty(name).EnumerateArray().Select(e => e.GetString()!).ToList();

    /// <summary>English description of a request field, from <c>descriptions.en.json</c>.</summary>
    public string? RequestDescription(string routeKey, string fieldPath)
    {
        if (!Descriptions.RootElement.TryGetProperty("request", out var request)
            || !request.TryGetProperty(routeKey, out var route)
            || !route.TryGetProperty(fieldPath, out var text))
        {
            return null;
        }

        return text.GetString();
    }

    /// <summary>
    /// The core's own hand-classified <c>safe</c> flag: the route is read-only and may be re-sent after
    /// a transport failure without an idempotency key. Never guessed from the path — a route that omits
    /// the flag fails codegen rather than being assumed unsafe (or, worse, safe).
    /// </summary>
    /// <param name="route">One entry of the contract's <c>routes</c> array.</param>
    /// <exception cref="InvalidOperationException">The route has no boolean <c>safe</c> field.</exception>
    private static bool ReadSafe(JsonElement route)
    {
        var key = $"{route.GetProperty("method").GetString()} {route.GetProperty("path").GetString()}";
        if (!route.TryGetProperty("safe", out var safe) || safe.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidOperationException(
                $"contract/contract.json: route \"{key}\" has no boolean \"safe\" field — re-export the contract "
                + "from a core that classifies retry safety (the SDK never guesses it from the path)");
        }

        return safe.GetBoolean();
    }

    public static Contract Load(string repoRoot)
    {
        var contractPath = Path.Combine(repoRoot, "contract", "contract.json");
        var descriptionsPath = Path.Combine(repoRoot, "contract", "descriptions.en.json");
        var raw = File.ReadAllBytes(contractPath);
        var descriptions = File.Exists(descriptionsPath)
            ? JsonDocument.Parse(File.ReadAllBytes(descriptionsPath))
            : JsonDocument.Parse("""{"request":{},"response":{}}""");
        return new Contract(JsonDocument.Parse(raw), descriptions, raw);
    }
}
