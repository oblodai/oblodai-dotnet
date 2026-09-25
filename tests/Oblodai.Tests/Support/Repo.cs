using System.Text.Json;
using Oblodai.Contract;

namespace Oblodai.Tests.Support;

/// <summary>Where the tests find this repository, the backend checkout and the recorded test data.</summary>
public static class Repo
{
    private static readonly Lazy<string> RootLazy = new(FindRoot);
    private static readonly Lazy<JsonDocument> WebhookSamplesLazy = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "tests", "Oblodai.Tests", "Data", "webhook-samples.json"))));

    /// <summary>The repository root (the directory holding <c>Oblodai.sln</c>).</summary>
    public static string Root => RootLazy.Value;

    /// <summary>
    /// The backend checkout with <c>tools/sdkgen</c> and <c>services/core/api/openapi.json</c>:
    /// <c>$OBLODAI_BACKEND</c>, else <c>../oblodai-backend</c> next to this repository.
    /// </summary>
    public static string Backend
        => Environment.GetEnvironmentVariable("OBLODAI_BACKEND") is { Length: > 0 } configured
            ? configured
            : Path.GetFullPath(Path.Combine(Root, "..", "oblodai-backend"));

    /// <summary>True when <see cref="Backend"/> was named explicitly: a missing suite then fails instead of skipping.</summary>
    public static bool BackendRequired => Environment.GetEnvironmentVariable("OBLODAI_BACKEND") is { Length: > 0 };

    /// <summary>
    /// Real deliveries the gateway's dispatcher signed and recorded, with the endpoint secret
    /// <see cref="WebhookSamplesSecret"/>: <c>[{ headers, body }]</c>.
    /// </summary>
    public static JsonElement WebhookSamples => WebhookSamplesLazy.Value.RootElement;

    /// <summary>
    /// The samples keep the header names they were recorded under; each maps to its role's current name
    /// from the contract, so a header the core renames reaches the samples by regeneration alone.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> RecordedWebhookHeaders = new Dictionary<string, string>
    {
        ["X-Webhook-Timestamp"] = SigningProtocol.HeaderWebhookTimestamp,
        ["X-Webhook-Signature"] = SigningProtocol.HeaderWebhookSignature,
        ["X-Webhook-Signature-Prev"] = SigningProtocol.HeaderWebhookSignaturePrev,
        ["X-Webhook-Event"] = SigningProtocol.HeaderWebhookEvent,
        ["X-Webhook-Id"] = SigningProtocol.HeaderWebhookId,
        ["X-Webhook-Event-Id"] = SigningProtocol.HeaderWebhookEventId,
        ["X-Webhook-Event-Time"] = SigningProtocol.HeaderWebhookEventTime,
        ["X-Webhook-Test"] = SigningProtocol.HeaderWebhookTest,
    };

    /// <summary>A recorded delivery's headers under the contract's current names, looked up case-insensitively.</summary>
    public static Dictionary<string, string> WebhookSampleHeaders(JsonElement sample)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in sample.GetProperty("headers").EnumerateObject())
        {
            headers[RecordedWebhookHeaders.TryGetValue(header.Name, out var current) ? current : header.Name] =
                header.Value.GetString()!;
        }

        return headers;
    }

    /// <summary>The endpoint secret in force when <see cref="WebhookSamples"/> were delivered.</summary>
    public const string WebhookSamplesSecret = "70200ecc6784c713e4fcda1c7b4d3e520713bb109edeacc541c3a90fa8cfd91f";

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oblodai.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Oblodai.sln not found above the test binaries");
    }
}
