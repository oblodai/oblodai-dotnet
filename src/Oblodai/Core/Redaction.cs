using System.Text;

namespace Oblodai;

/// <summary>
/// One placeholder for secrets in printed values. Records print their members by default, so options or
/// a model that carries a secret would leak it the first time someone wrote it to a log or a crash dump;
/// their <c>ToString()</c> is overridden instead of trusted.
/// </summary>
public static class Redaction
{
    /// <summary>What a redacted value prints as.</summary>
    public const string Placeholder = "[redacted]";

    /// <summary>Append <c>Name = [redacted]</c> to a record's <c>ToString()</c> buffer.</summary>
    /// <param name="builder">The buffer.</param>
    /// <param name="name">Property name.</param>
    /// <param name="present">False when the value is absent, so the placeholder does not imply one.</param>
    public static StringBuilder AppendRedacted(this StringBuilder builder, string name, bool present = true)
        => builder.Append(name).Append(" = ").Append(present ? Placeholder : "null");
}
