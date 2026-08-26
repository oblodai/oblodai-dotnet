using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oblodai;

/// <summary>
/// One placeholder for every path a secret could escape by. Records print their members by default and
/// <see cref="JsonSerializer"/> writes every public property, so a client, a set of options or a model
/// that carries a secret would leak it the first time someone wrote it to a log, a crash dump or a
/// diagnostics endpoint. Both paths are overridden instead of trusted.
/// </summary>
public static class Redaction
{
    /// <summary>What a redacted value prints as.</summary>
    public const string Placeholder = "[redacted]";

    [ThreadStatic]
    private static bool revealing;

    /// <summary>True while <see cref="OblodaiJson.SerializeWithSecrets"/> is running on this thread.</summary>
    public static bool IsRevealing => revealing;

    /// <summary>Append <c>Name = [redacted]</c> to a record's <c>ToString()</c> buffer.</summary>
    /// <param name="builder">The buffer.</param>
    /// <param name="name">Property name.</param>
    /// <param name="present">False when the value is absent, so the placeholder does not imply one.</param>
    public static StringBuilder AppendRedacted(this StringBuilder builder, string name, bool present = true)
        => builder.Append(name).Append(" = ").Append(present ? Placeholder : "null");

    /// <summary>Run <paramref name="action"/> with secret-bearing properties serialized in full.</summary>
    /// <typeparam name="T">What the action returns.</typeparam>
    /// <param name="action">The serialization to run.</param>
    internal static T Reveal<T>(Func<T> action)
    {
        var previous = revealing;
        revealing = true;
        try
        {
            return action();
        }
        finally
        {
            revealing = previous;
        }
    }
}
