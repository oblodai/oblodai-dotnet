using System.Text;

namespace Oblodai.Codegen;

/// <summary>Turns wire identifiers into C# ones, deterministically (the drift check depends on it).</summary>
internal static class Naming
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    /// <summary>PascalCase from a snake_case, kebab-case or dotted wire name.</summary>
    public static string Pascal(string value)
    {
        var output = new StringBuilder(value.Length);
        var upper = true;
        foreach (var c in value)
        {
            if (c is '_' or '-' or '.' or '/' or ' ' or '{' or '}')
            {
                upper = true;
                continue;
            }

            output.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return output.ToString();
    }

    /// <summary>A safe C# member name for a wire property.</summary>
    public static string Member(string wireName)
    {
        var name = Pascal(wireName);
        if (name.Length == 0)
        {
            name = "Value";
        }

        if (char.IsDigit(name[0]))
        {
            name = "N" + name;
        }

        return Keywords.Contains(name) ? "@" + name : name;
    }

    /// <summary>The registry member name for a route: <c>POST /v1/payment/link</c> → <c>PostV1PaymentLink</c>.</summary>
    public static string RouteMember(string method, string path)
        => Pascal(method.ToLowerInvariant()) + Pascal(path);

    /// <summary>
    /// The request record name for a route: <c>POST /v1/payout/link/batch</c> → <c>PayoutLinkBatchRequest</c>.
    /// Path parameters do not take part, so the name reads like the operation.
    /// </summary>
    public static string RequestType(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !s.StartsWith('{') && s != "v1")
            .Select(Pascal);
        return string.Concat(segments) + "Request";
    }

    /// <summary>Escapes text for an XML documentation comment.</summary>
    public static string Xml(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
