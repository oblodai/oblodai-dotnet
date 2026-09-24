using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Oblodai.Contract;

namespace Oblodai;

/// <summary>
/// Base of every generated request and response model (<c>src/Oblodai/Generated/Models.g.cs</c>).
/// <para>
/// Parsing is tolerant by design: a field this SDK version does not know yet lands in
/// <see cref="Extra"/>, a vocabulary value it does not know stays in the value's <c>Value</c>
/// (<c>IsKnown == false</c>), and a field the contract marks required but the answer omits is left at
/// its default instead of failing the call — a new gateway release never breaks an old SDK.
/// </para>
/// <para>
/// <see cref="ToString"/> is short (at most <see cref="ToStringLimit"/> characters), skips null
/// members and never prints the value of a member whose wire name looks secret (<c>secret</c>,
/// <c>token</c>, <c>passcode</c>, <c>signature</c>, <c>claim_url</c>, …).
/// </para>
/// </summary>
public abstract partial record Model
{
    /// <summary>The longest <see cref="ToString"/> a model produces; longer text is cut and ends with <c>…}</c>.</summary>
    public const int ToStringLimit = 1024;

    /// <summary>Fields of the answer this SDK version does not know yet, exactly as received.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>
    /// Build a model from a dictionary keyed by wire names (<c>order_id</c>, not <c>OrderId</c>) — the
    /// same body a JSON request would carry. A <see cref="double"/> or <see cref="float"/> where the
    /// model has a money field (<see cref="decimal"/>) is refused with <c>sdk.float_amount</c> before
    /// anything is sent: binary floating point cannot hold amounts exactly. Pass a
    /// <see cref="decimal"/> or a decimal string instead. Where the model has no type for a value (a
    /// field this SDK version does not know, an untyped field), binary floating point is allowed only
    /// under the names the contract types as numbers (<see cref="Contract.ApiFacts.NonMoneyNumbers"/>).
    /// </summary>
    /// <typeparam name="T">The model to build.</typeparam>
    /// <param name="values">Field values by wire name; nested objects as dictionaries, lists as lists.</param>
    /// <exception cref="ConfigException"><c>sdk.float_amount</c> or <c>sdk.bad_config</c> (a value of the wrong type).</exception>
    public static T From<T>(IReadOnlyDictionary<string, object?> values)
        where T : Model
    {
        ArgumentNullException.ThrowIfNull(values);
        AssertNoFloatMoney(typeof(T), values, string.Empty);
        try
        {
            var element = JsonSerializer.SerializeToElement(values, OblodaiJson.Options);
            return element.Deserialize<T>(OblodaiJson.Options)
                   ?? throw new ConfigException(SdkErrorCodes.BadConfig, $"{typeof(T).Name}: the values decode to null");
        }
        catch (JsonException error)
        {
            throw new ConfigException(
                SdkErrorCodes.BadConfig, $"{typeof(T).Name}: {error.Message}", error.Path?.TrimStart('$', '.'));
        }
    }

    /// <summary>The model's type and its non-null members, secrets redacted, at most <see cref="ToStringLimit"/> characters.</summary>
    public sealed override string ToString()
    {
        var text = new StringBuilder(GetType().Name).Append(" { ");
        var first = true;
        foreach (var property in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.Name == nameof(EqualityContract))
            {
                continue;
            }

            var value = property.GetValue(this);
            if (value is null || (property.Name == nameof(Extra) && value is ICollection { Count: 0 }))
            {
                continue;
            }

            if (!first)
            {
                text.Append(", ");
            }

            first = false;
            var wire = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
            text.Append(property.Name).Append(" = ");
            text.Append(IsSensitive(wire) && value is string or IEnumerable ? Redaction.Placeholder : Show(value));
            if (text.Length > ToStringLimit)
            {
                break;
            }
        }

        text.Append(first ? "}" : " }");
        return text.Length > ToStringLimit ? string.Concat(text.ToString(0, ToStringLimit - 2), "…}") : text.ToString();
    }

    /// <summary>True for a wire name that looks like it carries a secret.</summary>
    /// <param name="wireName">Field name as on the wire.</param>
    internal static bool IsSensitive(string wireName) => SensitiveName().IsMatch(wireName);

    private static string Show(object value) => value switch
    {
        string s => s,
        decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        IDictionary dictionary => $"{{{dictionary.Count} fields}}",
        ICollection collection => $"[{collection.Count} items]",
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static void AssertNoFloatMoney(Type model, IReadOnlyDictionary<string, object?> values, string path)
    {
        var properties = model.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (Property: p, Wire: p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name))
            .Where(p => p.Wire is not null)
            .ToDictionary(p => p.Wire!, p => p.Property.PropertyType);
        foreach (var (name, value) in values)
        {
            if (!properties.TryGetValue(name, out var type))
            {
                AssertUntyped(value, path + name, name);
                continue;
            }

            AssertValue(type, value, path + name, name);
        }
    }

    private static void AssertValue(Type type, object? value, string path, string name)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(object) || type == typeof(JsonElement))
        {
            AssertUntyped(value, path, name);
            return;
        }

        switch (value)
        {
            case double or float or Half when type == typeof(decimal):
                throw new ConfigException(
                    SdkErrorCodes.FloatAmount,
                    $"{path} is a {value.GetType().Name}; money must be a decimal or a decimal string "
                    + "(25.10m or \"25.10\"), never binary floating point",
                    path);
            case IReadOnlyDictionary<string, object?> nested when typeof(Model).IsAssignableFrom(type):
                AssertNoFloatMoney(type, nested, path + ".");
                break;
            case IReadOnlyDictionary<string, object?> map when MapValueType(type) is { } valueType:
                foreach (var (key, item) in map)
                {
                    AssertValue(valueType, item, $"{path}.{key}", name);
                }

                break;
            case IEnumerable list and not string:
                var element = ElementType(type);
                if (element is null)
                {
                    break;
                }

                var i = 0;
                foreach (var item in list)
                {
                    AssertValue(element, item, $"{path}[{i++}]", name);
                }

                break;
        }
    }

    /// <summary>A value the model does not type: floating point only under a non-money name, at any depth.</summary>
    private static void AssertUntyped(object? value, string path, string name)
    {
        switch (value)
        {
            case double or float or Half when !ApiFacts.NonMoneyNumbers.Contains(name):
                throw new ConfigException(
                    SdkErrorCodes.FloatAmount,
                    $"{path} is a {value.GetType().Name} and the contract has no number field {name}; money must be a "
                    + "decimal or a decimal string (25.10m or \"25.10\"), never binary floating point",
                    path);
            case IReadOnlyDictionary<string, object?> nested:
                foreach (var (key, item) in nested)
                {
                    AssertUntyped(item, $"{path}.{key}", key);
                }

                break;
            case IEnumerable list and not string:
                var i = 0;
                foreach (var item in list)
                {
                    AssertUntyped(item, $"{path}[{i++}]", name);
                }

                break;
        }
    }

    private static Type? ElementType(Type type)
        => type.IsGenericType && type.GetGenericArguments() is { Length: 1 } args ? args[0] : null;

    private static Type? MapValueType(Type type)
        => type.IsGenericType && type.GetGenericArguments() is [var key, var value] && key == typeof(string) ? value : null;

    [GeneratedRegex("secret|token|passcode|signature|password|claim_url|authorization", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveName();
}
