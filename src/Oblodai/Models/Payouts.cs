using System.Text.Json.Serialization;
using Oblodai.Contract;

namespace Oblodai.Models;

/// <summary>
/// A payout, as <c>/v1/payout</c>, <c>/info</c>, <c>/history</c>, <c>/cancel</c>, mass/batch
/// elements and refunds render it. <see cref="Error"/> / <see cref="ErrorCode"/> appear on
/// <c>info</c> for failed payouts, <see cref="WalletUuid"/> on refunds of blocked static wallets.
/// Not sealed: <see cref="Resolution"/> derives from it.
/// </summary>
public record Payout
{
    /// <summary>Payout id.</summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    /// <summary>
    /// Your payout number (reference); null for a refund — a refund has no identifier of yours, see
    /// <see cref="PaymentOrderId"/>.
    /// </summary>
    [JsonPropertyName("order_id")]
    public string? OrderId { get; init; }

    /// <summary>
    /// Lifecycle: <c>pending</c> → <c>approved</c> → <c>awaiting_cosign</c> → <c>broadcasting</c> →
    /// <c>sent</c> → <c>confirmed</c> | <c>failed</c> | <c>cancelled</c>. (The gateway's field
    /// description still names the retired 1.x vocabulary; <see cref="PayoutStatus"/> is the one on the
    /// wire.)
    /// </summary>
    [JsonPropertyName("status")]
    public PayoutStatus Status { get; init; }

    /// <summary>
    /// True once the status is final (<c>confirmed</c> / <c>failed</c> / <c>cancelled</c>); the same
    /// verdict as <c>Statuses.IsPayoutFinal</c>.
    /// </summary>
    [JsonPropertyName("is_final")]
    public bool IsFinal { get; init; }

    /// <summary>
    /// Payout amount in <see cref="Currency"/>, debited from your balance. Decimal string.
    /// </summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>Payout currency code.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Blockchain network.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>Recipient address.</summary>
    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;

    /// <summary>
    /// Destination tag / memo passed at creation (TON Jetton, exchange memo). Empty — no memo.
    /// </summary>
    [JsonPropertyName("memo")]
    public string Memo { get; init; } = string.Empty;

    /// <summary>
    /// Total debited from the balance — the amount plus the commission when the merchant bears the
    /// fee; otherwise what actually reaches the recipient (<c>amount − commission</c>).
    /// Decimal string.
    /// </summary>
    [JsonPropertyName("payer_amount")]
    public string PayerAmount { get; init; } = string.Empty;

    /// <summary>
    /// Network fee withheld, in the payout currency; 0 — the gateway absorbed it. Decimal string.
    /// </summary>
    [JsonPropertyName("commission")]
    public string Commission { get; init; } = string.Empty;

    /// <summary>
    /// Who paid the network fee: <c>gateway</c> (absorbed it, commission = 0), <c>merchant</c> (the
    /// debit was increased by the fee and the recipient gets the requested amount in full) or
    /// <c>recipient</c> (the fee was withheld and the recipient gets less than requested).
    /// </summary>
    [JsonPropertyName("fee_bearer")]
    public FeeBearerResult FeeBearer { get; init; }

    /// <summary>
    /// How the payout was initiated: <c>api</c> (this SDK / API key) or <c>manual</c> (cabinet).
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// True — the payout is awaiting approval (internal scenarios; always false for an API key).
    /// </summary>
    [JsonPropertyName("approval_required")]
    public bool ApprovalRequired { get; init; }

    /// <summary>True — this is a payment refund, not a regular payout.</summary>
    [JsonPropertyName("is_refund")]
    public bool IsRefund { get; init; }

    /// <summary>Id of the payment being refunded; null if this is not a refund.</summary>
    [JsonPropertyName("refund_for")]
    public string? RefundFor { get; init; }

    /// <summary>
    /// Your <c>order_id</c> of the payment the refund was made for; null for a regular payout.
    /// Reconcile a refund with the order by this field.
    /// </summary>
    [JsonPropertyName("payment_order_id")]
    public string? PaymentOrderId { get; init; }

    /// <summary>On-chain transaction hash (appears after sending).</summary>
    [JsonPropertyName("txid")]
    public string Txid { get; init; } = string.Empty;

    /// <summary>
    /// Signed link to the PDF cheque — opens without an API key. Empty if document generation is
    /// not enabled.
    /// </summary>
    [JsonPropertyName("document_url")]
    public string DocumentUrl { get; init; } = string.Empty;

    /// <summary>Creation time (RFC 3339).</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>Time of the last change (RFC 3339).</summary>
    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = string.Empty;

    /// <summary>Why the payout failed. <c>/v1/payout/info</c> only; null while nothing failed.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>Machine-readable failure code. <c>/v1/payout/info</c> only.</summary>
    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; init; }

    /// <summary>
    /// The static wallet the funds came back from. Set on refunds of blocked static-wallet deposits
    /// (<c>/v1/wallet/blocked-address-refund</c>).
    /// </summary>
    [JsonPropertyName("wallet_uuid")]
    public string? WalletUuid { get; init; }
}

/// <summary>
/// <c>POST /v1/payment/resolve</c> — how an under- or overpayment was settled. On
/// <c>action: "refund"</c> the body IS the refund payout (every <see cref="Payout"/> field is
/// filled) plus <see cref="ResolutionKind"/> = <c>refunded</c>. On <c>action: "accept"</c> the
/// underpayment was kept as full settlement and only <see cref="ResolutionKind"/>,
/// <see cref="PaymentUuid"/>, <see cref="Payout.OrderId"/>, <see cref="Payout.Currency"/> and
/// <see cref="AmountKept"/> are meaningful.
/// </summary>
public sealed record Resolution : Payout
{
    /// <summary>How it was settled: <c>accepted</c> or <c>refunded</c>.</summary>
    [JsonPropertyName("resolution")]
    public string ResolutionKind { get; init; } = string.Empty;

    /// <summary>Id of the invoice that was resolved. <c>accepted</c> only.</summary>
    [JsonPropertyName("payment_uuid")]
    public string? PaymentUuid { get; init; }

    /// <summary>
    /// How much was kept as full settlement, in the price currency. <c>accepted</c> only.
    /// Decimal string.
    /// </summary>
    [JsonPropertyName("amount_kept")]
    public string? AmountKept { get; init; }
}

/// <summary>
/// <c>/v1/payout/calculate</c> — what a payout would cost. Amounts are null when the asset cannot
/// be priced right now.
/// </summary>
public sealed record PayoutCalculation
{
    /// <summary>Amount that would leave your balance, in <see cref="Currency"/>. Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string? Amount { get; init; }

    /// <summary>Payout currency code.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Blockchain network.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>Network fee that would be charged. Decimal string.</summary>
    [JsonPropertyName("commission")]
    public string? Commission { get; init; }

    /// <summary>What would actually reach the recipient. Decimal string.</summary>
    [JsonPropertyName("payer_amount")]
    public string? PayerAmount { get; init; }

    /// <summary>Who would pay the network fee.</summary>
    [JsonPropertyName("fee_bearer")]
    public FeeBearerResult FeeBearer { get; init; }

    /// <summary>Pricing mode the fee was computed with.</summary>
    [JsonPropertyName("fee_type")]
    public string FeeType { get; init; } = string.Empty;
}

/// <summary>
/// <c>/v1/payout/validate</c> — the dry run. It raises the same errors the create call would.
/// </summary>
public sealed record PayoutValidation
{
    /// <summary>True — the payout would be accepted as described.</summary>
    [JsonPropertyName("valid")]
    public bool Valid { get; init; }

    /// <summary>Amount that would leave your balance. Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>Payout currency code.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Blockchain network.</summary>
    [JsonPropertyName("network")]
    public Network Network { get; init; }

    /// <summary>Network fee that would be charged. Decimal string.</summary>
    [JsonPropertyName("commission")]
    public string Commission { get; init; } = string.Empty;

    /// <summary>What would actually reach the recipient. Decimal string.</summary>
    [JsonPropertyName("payer_amount")]
    public string PayerAmount { get; init; } = string.Empty;

    /// <summary>Who would pay the network fee.</summary>
    [JsonPropertyName("fee_bearer")]
    public FeeBearerResult FeeBearer { get; init; }

    /// <summary>
    /// Which balance would fund it (<c>business</c> / <c>personal</c>), when the gateway reports it.
    /// </summary>
    [JsonPropertyName("funded_by")]
    public string? FundedBy { get; init; }

    /// <summary>
    /// Non-empty when part of the balance is still maturing (the reorg window has not passed).
    /// </summary>
    [JsonPropertyName("maturity_note")]
    public string MaturityNote { get; init; } = string.Empty;
}

/// <summary><c>/v1/transfer/to-personal</c>: business balance → the owner's personal balance.</summary>
public sealed record TransferToPersonal
{
    /// <summary>Transfer id.</summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    /// <summary>Asset moved.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Amount moved. Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>Always <c>to_personal</c>.</summary>
    [JsonPropertyName("direction")]
    public string Direction { get; init; } = string.Empty;

    /// <summary>Personal balance after the transfer. Decimal string.</summary>
    [JsonPropertyName("personal_balance")]
    public string PersonalBalance { get; init; } = string.Empty;

    /// <summary>Signed link to the PDF cheque for this operation.</summary>
    [JsonPropertyName("document_url")]
    public string DocumentUrl { get; init; } = string.Empty;
}

/// <summary><c>/v1/transfer/to-user</c>: business balance → another user's personal balance.</summary>
public sealed record TransferToUser
{
    /// <summary>Transfer id.</summary>
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    /// <summary>Asset moved.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    /// <summary>Amount moved. Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>The recipient user.</summary>
    [JsonPropertyName("to_user_id")]
    public string ToUserId { get; init; } = string.Empty;

    /// <summary>Signed link to the PDF cheque for this operation.</summary>
    [JsonPropertyName("document_url")]
    public string DocumentUrl { get; init; } = string.Empty;
}

/// <summary><c>/v1/payout/fee-config/get</c> and <c>/set</c> — who bears the payout network fee.</summary>
public sealed record PayoutFeeConfig
{
    /// <summary>True — the fee is withheld from the recipient rather than added to your debit.</summary>
    [JsonPropertyName("fee_on_recipient")]
    public bool FeeOnRecipient { get; init; }

    /// <summary>Whether the merchant ever set it. <c>get</c> only.</summary>
    [JsonPropertyName("configured")]
    public bool? Configured { get; init; }
}

/// <summary>
/// <c>/v1/payout/refund-fee-config/get</c> and <c>/set</c> — who bears the refund network fee.
/// </summary>
public sealed record RefundFeeConfig
{
    /// <summary>True — the fee is withheld from the customer's refund.</summary>
    [JsonPropertyName("fee_on_customer")]
    public bool FeeOnCustomer { get; init; }

    /// <summary>Whether the merchant ever set it. <c>get</c> only.</summary>
    [JsonPropertyName("configured")]
    public bool? Configured { get; init; }
}

/// <summary>
/// <c>/v1/payment/fee-config/get</c> and <c>/set</c> — how much of the payment fee the payer covers.
/// </summary>
public sealed record PaymentFeeConfig
{
    /// <summary>Share of the fee added to the payer's amount, percent.</summary>
    [JsonPropertyName("payer_pays_percent")]
    public int PayerPaysPercent { get; init; }

    /// <summary>Whether the rule is on. <c>get</c> only.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }
}
