using System.Text.Json;
using Oblodai.Tests.Conformance;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Contract;

/// <summary>
/// The signing protocol has one source: the contract's <c>x-oblodai-signing</c>, generated into
/// <c>SigningProtocol</c>. No hand-written source spells a header name of it — request, webhook
/// delivery or rehearsal — so a header renamed in the contract reaches the SDK by regeneration alone.
/// </summary>
public sealed class SigningSourceTests
{
    [ConformanceFact]
    public void NoSigningHeaderIsSpelledOutsideGenerated()
    {
        using var spec = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(Repo.Backend, "services", "core", "api", "openapi.json")));
        var signing = spec.RootElement.GetProperty("x-oblodai-signing");
        var webhook = signing.GetProperty("webhook");
        var names = signing.GetProperty("headers").EnumerateArray()
            .Concat(webhook.GetProperty("headers").EnumerateArray())
            .Select(n => n.GetString()!)
            .Append(webhook.GetProperty("test_header").GetString()!)
            .Select(n => n.ToLowerInvariant())
            .ToList();

        var src = Path.Combine(Repo.Root, "src");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(src, file);
            var parts = relative.Split(Path.DirectorySeparatorChar);
            if (parts.Contains("Generated") || parts.Contains("obj") || parts.Contains("bin"))
            {
                continue;
            }

            var text = File.ReadAllText(file).ToLowerInvariant();
            offenders.AddRange(names.Where(text.Contains).Select(n => $"{relative}: {n}"));
        }

        Assert.Empty(offenders);
    }
}
