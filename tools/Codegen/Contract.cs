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

    /// <summary>
    /// The whole auth vocabulary: <c>public</c> is unsigned, <c>key</c> is signed with the merchant's
    /// one API key, <c>onboard</c> is gated by <c>X-Admin-Token</c>. A merchant has a single key, so a
    /// snapshot that still distinguishes payment from payout keys is a stale export and is refused
    /// rather than mapped onto a kind the SDK no longer has.
    /// </summary>
    private static readonly HashSet<string> AuthValues = new(StringComparer.Ordinal) { "public", "key", "onboard" };

    private Contract(JsonDocument document, JsonDocument descriptions, byte[] rawBytes)
    {
        Document = document;
        Descriptions = descriptions;
        RawBytes = rawBytes;

        Routes = document.RootElement.GetProperty("routes").EnumerateArray()
            .Select(r => new Route(
                r.GetProperty("method").GetString()!,
                r.GetProperty("path").GetString()!,
                ReadAuth(r),
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
    /// The route's auth gate, checked against the whole vocabulary the SDK knows. Anything else — the
    /// <c>payment</c>/<c>payout</c>/<c>any</c> kinds of the split-key era included — fails codegen: the
    /// client holds one key pair, so silently treating an unknown gate as "signed" would send the only
    /// credential it has at a route the gateway gates differently.
    /// </summary>
    /// <param name="route">One entry of the contract's <c>routes</c> array.</param>
    /// <exception cref="InvalidOperationException">The route declares an auth value outside the vocabulary.</exception>
    private static string ReadAuth(JsonElement route)
    {
        var key = $"{route.GetProperty("method").GetString()} {route.GetProperty("path").GetString()}";
        var auth = route.TryGetProperty("auth", out var value) ? value.GetString() : null;
        if (auth is null || !AuthValues.Contains(auth))
        {
            throw new InvalidOperationException(
                $"contract/contract.json: route \"{key}\" declares auth \"{auth}\", which is not one of "
                + $"{string.Join(", ", AuthValues.Order(StringComparer.Ordinal))} — re-export the contract from a "
                + "core that has one API key per merchant");
        }

        return auth;
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
