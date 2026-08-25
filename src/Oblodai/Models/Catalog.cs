using System.Text.Json.Serialization;
using Oblodai.Contract;

namespace Oblodai.Models;

/// <summary>One network an asset is available on.</summary>
public sealed record CurrencyNetwork
{
    /// <summary>The network.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>What the asset is here: <c>native</c> or <c>token</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    /// <summary>Token contract address; present for tokens only.</summary>
    [JsonPropertyName("contract")]
    public string? Contract { get; init; }

    /// <summary>Confirmations required before a deposit is credited.</summary>
    [JsonPropertyName("min_confirmations")]
    public int MinConfirmations { get; init; }

    /// <summary>True — deposits and payouts are both possible right now.</summary>
    [JsonPropertyName("available")]
    public bool Available { get; init; }

    /// <summary>True — deposits are possible right now.</summary>
    [JsonPropertyName("deposit_available")]
    public bool DepositAvailable { get; init; }

    /// <summary>True — payouts are possible right now.</summary>
    [JsonPropertyName("payout_available")]
    public bool PayoutAvailable { get; init; }

    /// <summary>True — this is the network offered first on the pay page.</summary>
    [JsonPropertyName("default_offer")]
    public bool DefaultOffer { get; init; }
}

/// <summary>A settlement asset and the networks it lives on.</summary>
public sealed record CurrencyInfo
{
    /// <summary>Asset code.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>The asset's own scale — how many decimal places its amounts carry.</summary>
    [JsonPropertyName("decimals")]
    public int Decimals { get; init; }

    /// <summary>Networks the asset is available on.</summary>
    [JsonPropertyName("networks")]
    public IReadOnlyList<CurrencyNetwork> Networks { get; init; } = [];
}

/// <summary>A currency an invoice may be PRICED in (fiat or a coin).</summary>
public sealed record PricingCurrency
{
    /// <summary>Currency code.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>How many decimal places its amounts carry.</summary>
    [JsonPropertyName("decimals")]
    public int Decimals { get; init; }

    /// <summary>True — a fiat currency rather than a coin.</summary>
    [JsonPropertyName("fiat")]
    public bool Fiat { get; init; }
}

/// <summary>
/// <c>GET /v1/currencies</c> — the whole catalogue: what can be settled and what can be priced in.
/// </summary>
public sealed record Currencies
{
    /// <summary>Settlement assets and their networks (wire name <c>currencies</c>).</summary>
    [JsonPropertyName("currencies")]
    public IReadOnlyList<CurrencyInfo> Assets { get; init; } = [];

    /// <summary>Currencies an invoice may be priced in.</summary>
    [JsonPropertyName("pricing_currencies")]
    public IReadOnlyList<PricingCurrency> PricingCurrencies { get; init; } = [];
}

/// <summary><c>/v1/exchange-rate/list</c> item: 1 <see cref="From"/> = <see cref="Course"/> <see cref="To"/>.</summary>
public sealed record ExchangeRate
{
    /// <summary>Base currency.</summary>
    [JsonPropertyName("from")]
    public string From { get; init; } = string.Empty;

    /// <summary>Quote currency.</summary>
    [JsonPropertyName("to")]
    public string To { get; init; } = string.Empty;

    /// <summary>The rate. Decimal string.</summary>
    [JsonPropertyName("course")]
    public string Course { get; init; } = string.Empty;
}
