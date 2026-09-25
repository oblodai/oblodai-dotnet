using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Oblodai.Contract;
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

    /// <summary>
    /// Declarations of other limits that share a value with the signing ones today; their lines are not
    /// scanned. <c>MaxPreallocatedBytes</c> sizes a response buffer, it is not the request body limit.
    /// </summary>
    private static readonly string[] OtherLimits = ["const int MaxPreallocatedBytes ="];

    /// <summary>
    /// No hand-written source spells a literal of the body or idempotency-key limit — decimal (with or
    /// without an integer suffix), or <c>1 &lt;&lt; n</c> for a power of two; digit separators
    /// (<c>1_048_576</c>) do not hide one. Both are read from <c>SigningProtocol</c>, so a changed limit
    /// reaches the SDK by regeneration alone. The skew is not scanned for: its value is also an HTTP
    /// status class (<c>&lt; 300</c>); the alias tests hold it.
    /// </summary>
    [Fact]
    public void NoSigningLimitIsSpelledOutsideGenerated()
    {
        var patterns = new[] { (long)SigningProtocol.MaxBody, SigningProtocol.MaxIdempotencyKeyLength }
            .Select(limit => new Regex($@"(?<![\w.]){limit}[lLuU]*(?![\w.])"))
            .ToList();
        if (BitOperations.IsPow2(SigningProtocol.MaxBody))
        {
            patterns.Add(new Regex($@"\b1[lLuU]*\s*<<\s*{BitOperations.TrailingZeroCount(SigningProtocol.MaxBody)}\b"));
        }

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

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (OtherLimits.Any(other => lines[i].Contains(other, StringComparison.Ordinal)))
                {
                    continue;
                }

                var text = Regex.Replace(lines[i], @"(?<=\d)_(?=\d)", "");
                offenders.AddRange(patterns.Where(p => p.IsMatch(text)).Select(p => $"{relative}:{i + 1}: {p}"));
            }
        }

        Assert.Empty(offenders);
    }
}
