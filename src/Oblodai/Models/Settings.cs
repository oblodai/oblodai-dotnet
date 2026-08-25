using System.Text.Json.Serialization;
using Oblodai.Contract;

namespace Oblodai.Models;

/// <summary>
/// An automatic-withdrawal rule — <c>/v1/auto-withdraw/set</c>, <c>/list</c>, <c>/delete</c>.
/// </summary>
public sealed record AutoWithdrawRule
{
    /// <summary>Asset the rule watches.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Network the payout goes out on.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>Where the funds are sent.</summary>
    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;

    /// <summary>Balance the rule fires at. Decimal string.</summary>
    [JsonPropertyName("min_amount")]
    public string MinAmount { get; init; } = string.Empty;
}

/// <summary>
/// <c>/v1/api-allowlist/*</c> — the IP allowlist for API keys. Entries are CIDRs.
/// </summary>
public sealed record ApiAllowlist
{
    /// <summary>True — requests from addresses outside <see cref="Items"/> are refused.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    /// <summary>The allowed CIDRs.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<string> Items { get; init; } = [];
}

/// <summary>
/// A per-method price adjustment — <c>/v1/payment/discount/set</c> and <c>/list</c>.
/// </summary>
public sealed record DiscountRule
{
    /// <summary>Asset the rule applies to.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Network the rule applies to.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>Positive — a discount for the payer; negative — a markup.</summary>
    [JsonPropertyName("discount_percent")]
    public int DiscountPercent { get; init; }
}

/// <summary>
/// <c>/v1/payment/accuracy/get</c> and <c>/set</c> — how much short a payment may fall and still
/// count as paid.
/// </summary>
public sealed record AccuracyConfig
{
    /// <summary>True — the tolerance is applied.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    /// <summary>Tolerated shortfall, percent of the invoiced amount.</summary>
    [JsonPropertyName("accuracy_percent")]
    public int AccuracyPercent { get; init; }
}

/// <summary>
/// <c>/v1/payment/autorefund/get</c> and <c>/set</c> — automatic refunds of over- and underpayments.
/// </summary>
public sealed record AutoRefundConfig
{
    /// <summary>True — overpayments are refunded automatically.</summary>
    [JsonPropertyName("overpay")]
    public bool Overpay { get; init; }

    /// <summary>True — underpayments are refunded automatically.</summary>
    [JsonPropertyName("underpay")]
    public bool Underpay { get; init; }

    /// <summary>Whether the merchant ever set it. <c>get</c> only.</summary>
    [JsonPropertyName("configured")]
    public bool? Configured { get; init; }
}

/// <summary>
/// <c>/v1/payment/accepted/list</c> item — a currency/network pair and whether you accept it.
/// </summary>
public sealed record AcceptedMethod
{
    /// <summary>Asset code.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Network the asset is offered on.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>True — payers may choose this method.</summary>
    [JsonPropertyName("available")]
    public bool Available { get; init; }

    /// <summary>Why it is unavailable, when it is.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

/// <summary>
/// A revenue-split rule — <c>/v1/split/rule</c> (create, which answers with the id and percent only)
/// and <c>/v1/split/rule/list</c>.
/// </summary>
public sealed record SplitRule
{
    /// <summary>Rule id.</summary>
    [JsonPropertyName("rule_id")]
    public string RuleId { get; init; } = string.Empty;

    /// <summary>Share of every payment, percent, as a decimal string.</summary>
    [JsonPropertyName("percent")]
    public string Percent { get; init; } = string.Empty;

    /// <summary>False — the rule exists but is not applied. Absent on create.</summary>
    [JsonPropertyName("active")]
    public bool? Active { get; init; }

    /// <summary>Payout address for an off-platform recipient. Absent on create.</summary>
    [JsonPropertyName("address")]
    public string? Address { get; init; }

    /// <summary>Network the share is paid on. Absent on create.</summary>
    [JsonPropertyName("network")]
    public Network? Network { get; init; }

    /// <summary>Set for on-platform partner rules (which are reversible on refund).</summary>
    [JsonPropertyName("merchant_id")]
    public string? MerchantId { get; init; }

    /// <summary>Your note on the rule. Absent on create.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; init; }

    /// <summary>True — the share is clawed back when the payment is refunded. Absent on create.</summary>
    [JsonPropertyName("reversible")]
    public bool? Reversible { get; init; }
}

/// <summary><c>/v1/split/config/get</c> and <c>/set</c>.</summary>
public sealed record SplitConfig
{
    /// <summary>
    /// How long a split share is held before it is paid out, so a refund can still reverse it.
    /// </summary>
    [JsonPropertyName("refund_hold_seconds")]
    public int RefundHoldSeconds { get; init; }
}

/// <summary><c>/v1/split/recipient/optin</c> and <c>/optin/get</c>.</summary>
public sealed record SplitOptIn
{
    /// <summary>True — this merchant accepts being a split recipient.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
}

/// <summary>The reporting period of a <see cref="DocumentJob"/>.</summary>
public sealed record DocumentJobPeriod
{
    /// <summary>Start of the period (inclusive).</summary>
    [JsonPropertyName("from")]
    public string From { get; init; } = string.Empty;

    /// <summary>End of the period (inclusive).</summary>
    [JsonPropertyName("to")]
    public string To { get; init; } = string.Empty;
}

/// <summary>The finished artefact of a <see cref="DocumentJob"/>.</summary>
public sealed record DocumentJobFile
{
    /// <summary>
    /// Signed download link — opens without an API key. <c>documents.jobFile</c> fetches the same
    /// bytes through the client.
    /// </summary>
    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; init; } = string.Empty;

    /// <summary>When the signed link stops working (RFC 3339).</summary>
    [JsonPropertyName("expires_at")]
    public string ExpiresAt { get; init; } = string.Empty;

    /// <summary>How many rows the report holds.</summary>
    [JsonPropertyName("rows")]
    public long Rows { get; init; }

    /// <summary>Size of the artefact in bytes.</summary>
    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; init; }
}

/// <summary>
/// An asynchronous report — <c>/v1/documents/jobs</c> (submit) and <c>/v1/documents/jobs/info</c>
/// (poll). <see cref="File"/> appears once <see cref="Status"/> is <c>done</c>.
/// </summary>
public sealed record DocumentJob
{
    /// <summary>Job id — poll <c>/v1/documents/jobs/info</c> with it.</summary>
    [JsonPropertyName("job_id")]
    public string JobId { get; init; } = string.Empty;

    /// <summary>Which report was asked for.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    /// <summary>Output format (<c>csv</c>, <c>pdf</c>, …).</summary>
    [JsonPropertyName("format")]
    public string Format { get; init; } = string.Empty;

    /// <summary>Language the report is rendered in.</summary>
    [JsonPropertyName("lang")]
    public string Lang { get; init; } = string.Empty;

    /// <summary>
    /// Lifecycle: <c>queued</c> | <c>processing</c> | <c>done</c> | <c>failed</c>.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>The period the report covers.</summary>
    [JsonPropertyName("period")]
    public DocumentJobPeriod Period { get; init; } = new();

    /// <summary>Human hint on how long the queue will take (for example <c>"15s"</c>).</summary>
    [JsonPropertyName("ready_within")]
    public string? ReadyWithin { get; init; }

    /// <summary>The artefact; set once <see cref="Status"/> is <c>done</c>.</summary>
    [JsonPropertyName("file")]
    public DocumentJobFile? File { get; init; }

    /// <summary>Why the job failed; null while nothing failed.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>Submission time (RFC 3339).</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>Time of the last change (RFC 3339).</summary>
    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = string.Empty;
}
