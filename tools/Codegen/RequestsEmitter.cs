using System.Text;
using System.Text.Json;

namespace Oblodai.Codegen;

/// <summary>
/// Emits one record per documented request body, with the gateway's own field names, required flags,
/// English descriptions and examples. Records are the typed surface the resources take.
/// </summary>
internal sealed class RequestsEmitter
{
    /// <summary>Request fields drawn from a generated vocabulary.</summary>
    private static readonly Dictionary<string, string> FieldVocabularies = new(StringComparer.Ordinal)
    {
        ["network"] = "Network",
        ["pinned_network"] = "Network",
        ["on_error"] = "BatchOnError",
        ["fee_bearer"] = "FeeBearer",
        ["amount_mode"] = "AmountMode",
    };

    /// <summary>Vocabularies that only apply on one route.</summary>
    private static readonly Dictionary<string, string> RouteFieldVocabularies = new(StringComparer.Ordinal)
    {
        ["POST /v1/payment/history#status"] = "PaymentStatus",
        ["POST /v1/payout/history#status"] = "PayoutStatus",
        ["POST /v1/test-webhook/payment#status"] = "PaymentStatus",
        ["POST /v1/test-webhook/payout#status"] = "PayoutStatus",
        ["POST /v1/payment/testing-webhook#status"] = "PaymentStatus",
    };

    /// <summary>Free-form string fields whose accepted values are worth spelling out in the docs.</summary>
    private static readonly Dictionary<string, string> RouteFieldNotes = new(StringComparer.Ordinal)
    {
        ["POST /v1/payout/history#kind"] = "One of <c>payout</c>, <c>refund</c>.",
        ["POST /v1/payment/resolve#action"] = "One of <c>accept</c>, <c>refund</c>.",
        ["POST /v1/test-webhook/wallet#status"] = "Always <c>paid</c>.",
    };

    /// <summary>
    /// Fields the handler requires although the shared DTO marks them optional (batch items reuse the
    /// single-create DTO, where the gateway backfills the key from the Idempotency-Key header).
    /// </summary>
    private static readonly Dictionary<string, string[]> RequiredOverrides = new(StringComparer.Ordinal)
    {
        ["POST /v1/payment/batch"] = ["payments.order_id"],
        ["POST /v1/payout/batch"] = ["payouts.order_id"],
        ["POST /v1/refund/batch"] = ["refunds.reference"],
        ["POST /v1/transfer/batch"] = ["transfers.order_id", "transfers.amount", "transfers.currency"],
        ["POST /v1/payout/link/batch"] = ["items.reference"],
        ["POST /v1/transfer/to-user"] = ["amount", "currency"],
        ["POST /v1/claim/{token}"] = ["address"],
    };

    /// <summary>
    /// Fields the shared DTO marks required although the handler does not need them. <c>/v1/payout/validate</c>
    /// reuses the create DTO but reserves nothing, so a dry run needs no merchant reference — the reference
    /// SDK makes the same exception.
    /// </summary>
    private static readonly Dictionary<string, string[]> OptionalOverrides = new(StringComparer.Ordinal)
    {
        ["POST /v1/payout/validate"] = ["order_id"],
    };

    /// <summary>Amount-like fields: decimal strings, never floats.</summary>
    private static readonly HashSet<string> MoneyFields = new(StringComparer.Ordinal)
    {
        "amount", "min_amount", "max_amount", "amount_fixed", "payer_amount",
    };

    /// <summary>Request schemas for routes whose gateway DTO is not declared in the docs generator.</summary>
    private const string MerchantsOverride = """
        {
          "type": "object",
          "required": ["email"],
          "properties": {
            "email": { "type": "string", "example": "owner@shop.example" },
            "name": { "type": "string", "example": "Acme" }
          }
        }
        """;

    private readonly Contract _contract;
    private readonly List<string> _missingDescriptions = [];
    private readonly Dictionary<string, string> _emitted = new(StringComparer.Ordinal);
    private readonly StringBuilder _output = new();

    public RequestsEmitter(Contract contract) => _contract = contract;

    /// <summary>Route keys that lack an English description for at least one documented field.</summary>
    public IReadOnlyList<string> MissingDescriptions => _missingDescriptions;

    /// <summary>Route key → generated request record name.</summary>
    public IReadOnlyDictionary<string, string> RequestTypes { get; private set; } = new Dictionary<string, string>();

    public string Emit()
    {
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        using var overrideDocument = JsonDocument.Parse(MerchantsOverride);

        _output.Append(Emitters.Header(_contract));
        _output.AppendLine("using System.Text.Json.Serialization;");
        _output.AppendLine();
        _output.AppendLine("namespace Oblodai.Contract;");
        _output.AppendLine();

        foreach (var route in _contract.Routes)
        {
            var schema = route.RequestSchema
                         ?? (route.Key == "POST /v1/merchants" ? overrideDocument.RootElement : null);
            if (schema is null)
            {
                continue;
            }

            var typeName = Naming.RequestType(route.Path);
            if (types.TryGetValue(typeName, out var owner))
            {
                throw new InvalidOperationException($"request type {typeName} is claimed by both {owner} and {route.Key}");
            }

            types[typeName] = route.Key;
            EmitRecord(typeName, $"Request body of <c>{route.Key}</c>.", schema.Value, route.Key, string.Empty);
        }

        RequestTypes = types.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);
        return _output.ToString();
    }

    private void EmitRecord(string typeName, string doc, JsonElement schema, string routeKey, string prefix)
    {
        if (_emitted.TryGetValue(typeName, out var existing) && existing != routeKey + "#" + prefix)
        {
            throw new InvalidOperationException($"nested record {typeName} would be generated twice ({existing})");
        }

        _emitted[typeName] = routeKey + "#" + prefix;

        var properties = schema.TryGetProperty("properties", out var props)
            ? props.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToList()
            : [];

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var req))
        {
            foreach (var name in req.EnumerateArray())
            {
                required.Add(name.GetString()!);
            }
        }

        foreach (var field in OptionalOverrides.GetValueOrDefault(routeKey, []))
        {
            if (field.StartsWith(prefix, StringComparison.Ordinal) && !field[prefix.Length..].Contains('.'))
            {
                required.Remove(field[prefix.Length..]);
            }
        }

        foreach (var field in RequiredOverrides.GetValueOrDefault(routeKey, []))
        {
            if (field.StartsWith(prefix, StringComparison.Ordinal) && !field[prefix.Length..].Contains('.'))
            {
                required.Add(field[prefix.Length..]);
            }
        }

        var nested = new List<(string TypeName, string Doc, JsonElement Schema, string Prefix)>();
        var body = new StringBuilder();

        foreach (var property in properties)
        {
            var wireName = property.Name;
            var fieldPath = prefix + wireName;
            var description = _contract.RequestDescription(routeKey, fieldPath);
            if (description is null && property.Value.TryGetProperty("description", out _))
            {
                _missingDescriptions.Add($"{routeKey}#{fieldPath}");
            }

            var note = RouteFieldNotes.GetValueOrDefault($"{routeKey}#{wireName}");
            // The contract's examples come from the gateway's own Russian field docs. An English
            // documentation set does not quote them: a non-ASCII example is dropped rather than pasted
            // into a <summary> an English-speaking caller has to read past.
            var example = property.Value.TryGetProperty("example", out var ex) && IsAscii(ex.GetRawText())
                ? $" Example: <c>{Naming.Xml(ex.GetRawText())}</c>."
                : string.Empty;
            var isMoney = MoneyFields.Contains(wireName);
            var moneyNote = isMoney ? " Decimal string, never a float." : string.Empty;
            var text = string.Join(" ", new[] { description is null ? null : Naming.Xml(description), note, moneyNote.Trim(), example.Trim() }
                .Where(s => !string.IsNullOrEmpty(s)));

            var isRequired = required.Contains(wireName);
            var type = TypeOf(property.Value, typeName, wireName, routeKey, prefix, isRequired, nested);

            body.AppendLine($"    /// <summary>{(text.Length == 0 ? $"<c>{wireName}</c>." : text)}</summary>");
            body.AppendLine($"    [JsonPropertyName(\"{wireName}\")]");
            body.AppendLine($"    public {(isRequired ? "required " : string.Empty)}{type} {Naming.Member(wireName)} {{ get; init; }}");
            body.AppendLine();
        }

        _output.AppendLine($"/// <summary>{doc}</summary>");
        _output.AppendLine($"public sealed record {typeName}");
        _output.AppendLine("{");
        _output.Append(body.ToString().TrimEnd());
        _output.AppendLine();
        _output.AppendLine("}");
        _output.AppendLine();

        foreach (var (name, nestedDoc, nestedSchema, nestedPrefix) in nested)
        {
            EmitRecord(name, nestedDoc, nestedSchema, routeKey, nestedPrefix);
        }
    }

    /// <summary>True when every character can be written in a plain-ASCII English document.</summary>
    /// <param name="value">Candidate text.</param>
    private static bool IsAscii(string value) => value.All(char.IsAscii);

    private string TypeOf(
        JsonElement schema,
        string parentType,
        string wireName,
        string routeKey,
        string prefix,
        bool isRequired,
        List<(string TypeName, string Doc, JsonElement Schema, string Prefix)> nested)
    {
        var optional = isRequired ? string.Empty : "?";
        var type = schema.TryGetProperty("type", out var t) ? t.GetString() : null;
        switch (type)
        {
            case "string":
                var vocabulary = RouteFieldVocabularies.GetValueOrDefault($"{routeKey}#{wireName}")
                                 ?? FieldVocabularies.GetValueOrDefault(wireName);
                return vocabulary is null ? "string" + optional : vocabulary + optional;
            case "integer":
                return "int" + optional;
            case "number":
                return "decimal" + optional;
            case "boolean":
                return "bool" + optional;
            case "array":
                {
                    var itemsSchema = schema.GetProperty("items");
                    var itemType = ItemTypeOf(itemsSchema, parentType, wireName, routeKey, prefix, nested);
                    return $"IReadOnlyList<{itemType}>{optional}";
                }

            case "object":
                {
                    if (schema.TryGetProperty("properties", out _))
                    {
                        var name = NestedName(parentType, wireName, string.Empty);
                        nested.Add((name, $"Item of <c>{wireName}</c> in <c>{routeKey}</c>.", schema, prefix + wireName + "."));
                        return name + optional;
                    }

                    if (schema.TryGetProperty("additionalProperties", out var additional))
                    {
                        var valueType = ItemTypeOf(additional, parentType, wireName, routeKey, prefix, nested);
                        return $"IReadOnlyDictionary<string, {valueType}>{optional}";
                    }

                    return "IReadOnlyDictionary<string, object?>" + optional;
                }

            default:
                return "object" + optional;
        }
    }

    private string ItemTypeOf(
        JsonElement schema,
        string parentType,
        string wireName,
        string routeKey,
        string prefix,
        List<(string TypeName, string Doc, JsonElement Schema, string Prefix)> nested)
    {
        var type = schema.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (type == "object" && schema.TryGetProperty("properties", out _))
        {
            var name = NestedName(parentType, wireName, "Item");
            nested.Add((name, $"One item of <c>{wireName}</c> in <c>{routeKey}</c>.", schema, prefix + wireName + "."));
            return name;
        }

        return type switch
        {
            "string" => FieldVocabularies.GetValueOrDefault(wireName) ?? "string",
            "integer" => "int",
            "number" => "decimal",
            "boolean" => "bool",
            _ => "object",
        };
    }

    private static string NestedName(string parentType, string wireName, string suffix)
    {
        var stem = parentType.EndsWith("Request", StringComparison.Ordinal)
            ? parentType[..^"Request".Length]
            : parentType;
        return stem + Naming.Pascal(wireName) + suffix;
    }
}
