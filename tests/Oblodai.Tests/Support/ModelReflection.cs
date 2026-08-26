using System.Reflection;
using System.Text.Json.Serialization;

namespace Oblodai.Tests.Support;

/// <summary>Reads what a model claims to carry on the wire, so tests can compare it with what arrived.</summary>
public static class ModelReflection
{
    /// <summary>The wire names a model declares, including the ones it inherits.</summary>
    /// <param name="model">The model type.</param>
    public static HashSet<string> WireKeys(Type model)
        => model
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name)
            .ToHashSet(StringComparer.Ordinal);
}
