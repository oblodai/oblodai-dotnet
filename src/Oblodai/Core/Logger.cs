using System.Text.RegularExpressions;

namespace Oblodai;

/// <summary>Log levels the SDK emits.</summary>
public enum OblodaiLogLevel
{
    /// <summary>Per-attempt request and response lines.</summary>
    Debug,

    /// <summary>Notable but expected events.</summary>
    Info,

    /// <summary>Recoverable trouble (clock skew correction).</summary>
    Warn,

    /// <summary>Failures.</summary>
    Error,
}

/// <summary>
/// Minimal structured-logger contract: anything that takes a level, a message and a field bag fits
/// (Microsoft.Extensions.Logging, Serilog, Console). Field values that look like secrets are redacted
/// before they reach the logger, so a debug log never leaks a key, a signature or a cheque passcode.
/// </summary>
public interface IOblodaiLogger
{
    /// <summary>Write one line.</summary>
    /// <param name="level">Severity.</param>
    /// <param name="message">Short event name.</param>
    /// <param name="fields">Structured fields (already redacted).</param>
    void Log(OblodaiLogLevel level, string message, IReadOnlyDictionary<string, object?>? fields = null);
}

/// <summary>A logger that drops everything.</summary>
public sealed class NoopLogger : IOblodaiLogger
{
    /// <summary>The shared instance.</summary>
    public static readonly NoopLogger Instance = new();

    /// <inheritdoc />
    public void Log(OblodaiLogLevel level, string message, IReadOnlyDictionary<string, object?>? fields = null)
    {
    }
}

/// <summary>A console logger gated by level; <c>OBLODAI_LOG=debug|info|warn|error</c> selects it.</summary>
public sealed class ConsoleLogger : IOblodaiLogger
{
    private readonly OblodaiLogLevel _minimum;

    /// <summary>Create a console logger.</summary>
    /// <param name="minimum">Lowest level that is printed.</param>
    public ConsoleLogger(OblodaiLogLevel minimum = OblodaiLogLevel.Warn) => _minimum = minimum;

    /// <inheritdoc />
    public void Log(OblodaiLogLevel level, string message, IReadOnlyDictionary<string, object?>? fields = null)
    {
        if (level < _minimum)
        {
            return;
        }

        var line = $"[oblodai] {level.ToString().ToUpperInvariant()} {message}";
        if (fields is { Count: > 0 })
        {
            line += " " + string.Join(" ", LogRedaction.Redact(fields).Select(kv => $"{kv.Key}={kv.Value}"));
        }

        Console.Error.WriteLine(line);
    }
}

/// <summary>Hides values whose field name suggests a secret.</summary>
public static partial class LogRedaction
{
    /// <summary>Copy the bag with sensitive-looking values replaced.</summary>
    /// <param name="fields">Fields to redact.</param>
    public static IReadOnlyDictionary<string, object?> Redact(IReadOnlyDictionary<string, object?> fields)
    {
        var output = new Dictionary<string, object?>(fields.Count);
        foreach (var (key, value) in fields)
        {
            output[key] = SensitiveKey().IsMatch(key) ? "[redacted]" : value;
        }

        return output;
    }

    [GeneratedRegex("secret|signature|passcode|token|authorization|password", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveKey();
}
