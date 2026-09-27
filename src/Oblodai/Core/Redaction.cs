using System.Text;
using System.Text.RegularExpressions;

namespace Oblodai;

/// <summary>
/// One placeholder for secrets in printed values. Records print their members by default, so options or
/// a model that carries a secret would leak it the first time someone wrote it to a log or a crash dump;
/// their <c>ToString()</c> is overridden instead of trusted. <see cref="RedactUrl"/> hides the bearer parts
/// of a URL (claim tokens, signed-link parameters, userinfo).
/// </summary>
public static partial class Redaction
{
    /// <summary>What a redacted value prints as.</summary>
    public const string Placeholder = "[redacted]";

    /// <summary>Query parameters of a signed link (<c>exp</c>/<c>sig</c>) and of bearer URLs.</summary>
    private static readonly HashSet<string> SensitiveQuery =
        new(StringComparer.OrdinalIgnoreCase) { "sig", "exp", "token", "code", "passcode", "signature" };

    /// <summary>Path parameters that carry a bearer secret (<c>/v1/claim/{token}</c>).</summary>
    private static readonly HashSet<string> SensitivePathParams = new(StringComparer.Ordinal) { "token", "code", "passcode" };

    /// <summary>Path segments whose NEXT segment is a bearer secret, for URLs seen without their route.</summary>
    private static readonly HashSet<string> SecretAfterSegment = new(StringComparer.Ordinal) { "claim", "aml" };

    /// <summary>Append <c>Name = [redacted]</c> to a record's <c>ToString()</c> buffer.</summary>
    /// <param name="builder">The buffer.</param>
    /// <param name="name">Property name.</param>
    /// <param name="present">False when the value is absent, so the placeholder does not imply one.</param>
    public static StringBuilder AppendRedacted(this StringBuilder builder, string name, bool present = true)
        => builder.Append(name).Append(" = ").Append(present ? Placeholder : "null");

    /// <summary>
    /// True for a field or header name that carries a secret: secrets, signatures, passcodes, tokens,
    /// authorization, passwords, claim and signed document links, device codes, API keys and cookies.
    /// </summary>
    /// <param name="name">A wire field name, property name or header name.</param>
    public static bool IsSensitiveName(string name) => SensitiveNamePattern().IsMatch(name);

    /// <summary>
    /// A copy of <paramref name="url"/> safe to log or show: no userinfo, the bearer path segments of claim
    /// and AML links (and every <c>{token}</c>/<c>{code}</c>/<c>{passcode}</c> segment of
    /// <paramref name="routePath"/>, when given) and the signed-link query parameters replaced by
    /// <see cref="Placeholder"/>.
    /// </summary>
    /// <param name="url">An absolute (or path-only) URL.</param>
    /// <param name="routePath">The route's path template, or null.</param>
    public static string RedactUrl(string url, string? routePath)
    {
        string prefix;
        string rest;
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https")
        {
            prefix = $"{parsed.Scheme}://{parsed.Host}{(parsed.IsDefaultPort ? string.Empty : ":" + parsed.Port)}";
            rest = parsed.PathAndQuery;
        }
        else if (url.StartsWith('/'))
        {
            prefix = string.Empty;
            rest = url;
        }
        else
        {
            return "[unparseable url]";
        }

        var queryAt = rest.IndexOf('?');
        var path = queryAt < 0 ? rest : rest[..queryAt];
        var query = queryAt < 0 ? null : rest[(queryAt + 1)..];

        var segments = path.Split('/');
        var hide = new bool[segments.Length];
        for (var i = 1; i < segments.Length; i++)
        {
            hide[i] = SecretAfterSegment.Contains(segments[i - 1]);
        }

        if (routePath is not null)
        {
            var template = routePath.Split('/');
            var offset = segments.Length - template.Length;
            for (var j = 0; offset >= 0 && j < template.Length; j++)
            {
                var part = template[j];
                if (part.Length > 2 && part[0] == '{' && part[^1] == '}')
                {
                    var name = part[1..^1];
                    if (SensitivePathParams.Contains(name) || IsSensitiveName(name))
                    {
                        hide[offset + j] = true;
                    }
                }
            }
        }

        for (var i = 0; i < segments.Length; i++)
        {
            if (hide[i] && segments[i].Length > 0)
            {
                segments[i] = Placeholder;
            }
        }

        var output = new StringBuilder(prefix).Append(string.Join('/', segments));
        if (query is not null)
        {
            output.Append('?');
            var pairs = query.Split('&');
            for (var i = 0; i < pairs.Length; i++)
            {
                if (i > 0)
                {
                    output.Append('&');
                }

                var eq = pairs[i].IndexOf('=');
                var key = eq < 0 ? pairs[i] : pairs[i][..eq];
                output.Append(eq >= 0 && (SensitiveQuery.Contains(key) || IsSensitiveName(key))
                    ? key + "=" + Placeholder
                    : pairs[i]);
            }
        }

        return output.ToString();
    }

    [GeneratedRegex(
        "secret|signature|passcode|token|authorization|password|claim_?url|document_?url|device_?code|api[_-]?key|cookie",
        RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveNamePattern();
}
