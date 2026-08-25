using System.Text.Json.Serialization;
using Oblodai.Contract;

namespace Oblodai.Models;

/// <summary>
/// Per-method limits on <see cref="ServiceMethod"/>. Both bounds are null when the asset cannot be
/// priced right now.
/// </summary>
public sealed record ServiceMethodLimit
{
    /// <summary>Currency the bounds are expressed in; absent on <c>/v1/payout/services</c>.</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>Smallest accepted amount. Decimal string; null when unpriceable.</summary>
    [JsonPropertyName("min_amount")]
    public string? MinAmount { get; init; }

    /// <summary>Largest accepted amount. Decimal string; null when unpriceable.</summary>
    [JsonPropertyName("max_amount")]
    public string? MaxAmount { get; init; }
}

/// <summary>Per-method pricing on <see cref="ServiceMethod"/>.</summary>
public sealed record ServiceMethodCommission
{
    /// <summary>Currency the fee is expressed in.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Fixed part of the fee. Decimal string; null when there is none or it is unpriceable.</summary>
    [JsonPropertyName("fee_amount")]
    public string? FeeAmount { get; init; }

    /// <summary>Percentage part of the fee, as a decimal string; null when there is none.</summary>
    [JsonPropertyName("percent")]
    public string? Percent { get; init; }

    /// <summary>Pricing mode (<c>percent</c>, <c>fixed</c>, …).</summary>
    [JsonPropertyName("fee_type")]
    public string FeeType { get; init; } = string.Empty;
}

/// <summary>Item of <c>/v1/payment/services</c> and <c>/v1/payout/services</c>.</summary>
public sealed record ServiceMethod
{
    /// <summary>Asset code.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Network the asset is offered on.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>True — the method can be used right now.</summary>
    [JsonPropertyName("is_available")]
    public bool IsAvailable { get; init; }

    /// <summary>Amount bounds for this method.</summary>
    [JsonPropertyName("limit")]
    public ServiceMethodLimit Limit { get; init; } = new();

    /// <summary>Pricing for this method.</summary>
    [JsonPropertyName("commission")]
    public ServiceMethodCommission Commission { get; init; } = new();
}
