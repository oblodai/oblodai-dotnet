using System.Reflection;
using System.Text.RegularExpressions;
using Oblodai.Contract;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Contract;

/// <summary>
/// Every <c>family.reason</c> a method's documentation tells a caller to branch on has to be a code the
/// gateway can actually emit. A doc naming a code that does not exist is worse than no doc: the caller
/// writes a <c>catch</c> that never fires and treats a real failure as an unexpected one. The XML doc
/// file the package ships is the source here, so this checks exactly what a user reads in their IDE.
/// </summary>
public partial class DocumentedCodeTests
{
    /// <summary>Codes the SDK itself raises, which are deliberately not in the gateway's catalogue.</summary>
    private static readonly string[] SdkFamilies = ["sdk.", "transport.", "webhook."];

    /// <summary>The placeholder the docs use when explaining the SHAPE of a code rather than naming one.</summary>
    private static readonly HashSet<string> Placeholders = new(StringComparer.Ordinal) { "family.reason" };

    /// <summary>
    /// Webhook EVENT types, which share the <c>family.reason</c> shape but are not error codes:
    /// <c>invoice.&lt;status&gt;</c>, <c>payout.&lt;status&gt;</c>, <c>wallet.paid</c>.
    /// </summary>
    private static readonly HashSet<string> EventTypes =
        Fixtures.Contract.GetProperty("event_types").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"<c>([a-z][a-z0-9_]*\.[a-z][a-z0-9_]*)</c>")]
    private static partial Regex CodeShaped();

    [Fact]
    public void EveryErrorCodeNamedInAMethodDocIsOneTheGatewayCanEmit()
    {
        var catalogue = ErrorCodes.All.ToHashSet(StringComparer.Ordinal);
        var xml = File.ReadAllText(XmlDocPath());
        var unknown = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match match in CodeShaped().Matches(xml))
        {
            var code = match.Groups[1].Value;
            if (catalogue.Contains(code)
                || EventTypes.Contains(code)
                || Placeholders.Contains(code)
                || SdkFamilies.Any(f => code.StartsWith(f, StringComparison.Ordinal)))
            {
                continue;
            }

            unknown.Add(code);
        }

        Assert.True(
            unknown.Count == 0,
            "documentation names error codes the contract catalogue does not have: " + string.Join(", ", unknown));
    }

    [Fact]
    public void TheDocsNameCodesAtAllSoTheCheckAboveIsNotVacuous()
    {
        var xml = File.ReadAllText(XmlDocPath());
        var named = CodeShaped().Matches(xml)
            .Select(m => m.Groups[1].Value)
            .Where(c => ErrorCodes.All.Contains(c))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("payout.insufficient_funds", named);
        Assert.Contains("refund.nothing_to_refund", named);
        Assert.True(named.Count > 40, $"only {named.Count} catalogued codes are documented anywhere");
    }

    [Fact]
    public void TheGeneratedDocumentationIsEnglishAndCopiesNoForeignExample()
    {
        // The contract's field docs and examples come from the gateway's own Russian source. The English
        // descriptions live in contract/descriptions.en.json; an example the codegen cannot render in
        // ASCII is dropped rather than pasted into a <summary> the reader cannot use.
        var generated = Directory.EnumerateFiles(
            Path.Combine(Fixtures.RepoRoot, "src", "Oblodai", "Contract"), "*.g.cs");

        foreach (var file in generated)
        {
            var offending = File.ReadAllText(file)
                .Where(c => c > '\u02ff' && c is not ('\u2013' or '\u2014' or '\u2026' or '\u2192' or '\u00b1'))
                .Distinct()
                .ToList();

            Assert.True(
                offending.Count == 0,
                $"{Path.GetFileName(file)}: generated documentation contains non-Latin text "
                + $"({string.Join(" ", offending.Select(c => $"U+{(int)c:X4}"))})");
        }
    }

    private static string XmlDocPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Oblodai.xml");
        Assert.True(File.Exists(path), $"the package's XML documentation is not next to the assembly ({path})");
        return path;
    }
}
