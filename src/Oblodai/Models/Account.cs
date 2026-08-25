using System.Text.Json.Serialization;
using Oblodai.Contract;

namespace Oblodai.Models;

/// <summary>One asset's available balance.</summary>
public sealed record BalanceEntry
{
    /// <summary>Asset code.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Available (spendable) balance. Decimal string.</summary>
    [JsonPropertyName("balance")]
    public string Balance { get; init; } = string.Empty;
}

/// <summary>The balance sheets a merchant holds. Today only <see cref="Merchant"/> is populated.</summary>
public sealed record BalanceGroup
{
    /// <summary>The merchant (business) balance, one row per asset.</summary>
    [JsonPropertyName("merchant")]
    public IReadOnlyList<BalanceEntry> Merchant { get; init; } = [];
}

/// <summary><c>/v1/balance</c> — the wire wraps the sheets in a <c>balance</c> object.</summary>
public sealed record Balance
{
    /// <summary>The balance sheets (wire name <c>balance</c>).</summary>
    [JsonPropertyName("balance")]
    public BalanceGroup Balances { get; init; } = new();
}

/// <summary>The rolling-week slice of <see cref="ReferralInfo"/>.</summary>
public sealed record ReferralWeek
{
    /// <summary>Merchants referred in the last week.</summary>
    [JsonPropertyName("referred_count")]
    public int ReferredCount { get; init; }

    /// <summary>Earnings in the last week, keyed by asset. Values are decimal strings.</summary>
    [JsonPropertyName("earnings_by_asset")]
    public IReadOnlyDictionary<string, string> EarningsByAsset { get; init; }
        = new Dictionary<string, string>();
}

/// <summary><c>/v1/referral/info</c> — your referral code and what it has earned.</summary>
public sealed record ReferralInfo
{
    /// <summary>Your referral code.</summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    /// <summary>Ready-made sign-up link carrying the code.</summary>
    [JsonPropertyName("link")]
    public string Link { get; init; } = string.Empty;

    /// <summary>Referral tiers, in basis points, from the first level outward.</summary>
    [JsonPropertyName("tier_bps")]
    public IReadOnlyList<int> TierBps { get; init; } = [];

    /// <summary>How many merchants signed up with your code.</summary>
    [JsonPropertyName("referred_count")]
    public int ReferredCount { get; init; }

    /// <summary>Lifetime earnings, keyed by asset. Values are decimal strings.</summary>
    [JsonPropertyName("earnings_by_asset")]
    public IReadOnlyDictionary<string, string> EarningsByAsset { get; init; }
        = new Dictionary<string, string>();

    /// <summary>The same figures for the last week.</summary>
    [JsonPropertyName("week")]
    public ReferralWeek Week { get; init; } = new();
}

/// <summary>
/// <c>/v1/vrcs</c> — volatility risk control: auto-convert volatile deposits to USDT on arrival.
/// </summary>
public sealed record VrcsStatus
{
    /// <summary>True — incoming volatile assets are converted.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
}

/// <summary>A static (permanent) deposit wallet — <c>/v1/wallet</c>.</summary>
public sealed record Wallet
{
    /// <summary>Static wallet id.</summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    /// <summary>
    /// Permanent address for top-ups. On XRP it is the classic r-address of the SHARED wallet — a
    /// top-up must carry <see cref="DestinationTag"/>.
    /// </summary>
    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;

    /// <summary>Blockchain network.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>Top-up currency.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// Your customer identifier the address is pinned to — part of the
    /// <c>currency + network + order_id</c> idempotency triple.
    /// </summary>
    [JsonPropertyName("order_id")]
    public string OrderId { get; init; } = string.Empty;

    /// <summary>Hosted page showing the address and QR. Reserved; usually empty.</summary>
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    /// <summary>Signed link to the PDF with the wallet details.</summary>
    [JsonPropertyName("document_url")]
    public string DocumentUrl { get; init; } = string.Empty;

    /// <summary>
    /// XRP only: numeric destination tag of this wallet — the customer must include it in every
    /// transfer.
    /// </summary>
    [JsonPropertyName("destination_tag")]
    public string? DestinationTag { get; init; }

    /// <summary>
    /// XLM and TON only: numeric memo of this wallet — the customer must include it in every
    /// transfer.
    /// </summary>
    [JsonPropertyName("memo")]
    public string? Memo { get; init; }

    /// <summary>XRP only: address and tag in one string (X-address, XLS-5).</summary>
    [JsonPropertyName("address_xaddress")]
    public string? AddressXaddress { get; init; }

    /// <summary>XLM only: address and memo in one string (muxed M…, SEP-23).</summary>
    [JsonPropertyName("address_muxed")]
    public string? AddressMuxed { get; init; }
}

/// <summary><c>/v1/wallet/block</c> — a static wallet was blocked or unblocked.</summary>
public sealed record WalletBlocked
{
    /// <summary>Static wallet id.</summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    /// <summary>The wallet's address.</summary>
    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;

    /// <summary>True — deposits to this address are refused and returned.</summary>
    [JsonPropertyName("blocked")]
    public bool Blocked { get; init; }
}

/// <summary><c>/v1/wallet/qr</c> — the wallet address as a QR data URI.</summary>
public sealed record WalletQr
{
    /// <summary>The QR as a PNG <c>data:image/png;base64,…</c> URI.</summary>
    [JsonPropertyName("image")]
    public string Image { get; init; } = string.Empty;
}
