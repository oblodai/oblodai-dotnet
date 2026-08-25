using Oblodai.Contract;

namespace Oblodai;

/// <summary>Reading the two lifecycles without memorising their vocabularies.</summary>
public static class Statuses
{
    /// <summary>Invoice statuses after which nothing else can happen.</summary>
    public static readonly IReadOnlyList<PaymentStatus> FinalPaymentStatuses =
    [
        PaymentStatus.Paid,
        PaymentStatus.PaidOver,
        PaymentStatus.WrongAmount,
        PaymentStatus.Expired,
        PaymentStatus.Cancelled,
    ];

    /// <summary>Payout statuses after which nothing else can happen.</summary>
    public static readonly IReadOnlyList<PayoutStatus> FinalPayoutStatuses =
    [
        PayoutStatus.Confirmed,
        PayoutStatus.Failed,
        PayoutStatus.Cancelled,
    ];

    /// <summary>True once the invoice can no longer change state.</summary>
    /// <param name="status">Invoice status.</param>
    public static bool IsPaymentFinal(PaymentStatus status) => FinalPaymentStatuses.Contains(status);

    /// <summary>
    /// <c>paid</c> or <c>paid_over</c> — the merchant has the money. <c>wrong_amount</c> is NOT paid:
    /// resolve it with <c>Refunds.ResolveAsync</c>.
    /// </summary>
    /// <param name="status">Invoice status.</param>
    public static bool IsPaymentPaid(PaymentStatus status)
        => status == PaymentStatus.Paid || status == PaymentStatus.PaidOver;

    /// <summary>The invoice is underpaid and waiting for a merchant decision.</summary>
    /// <param name="status">Invoice status.</param>
    public static bool IsPaymentUnderpaid(PaymentStatus status) => status == PaymentStatus.WrongAmount;

    /// <summary>True once the payout can no longer change state.</summary>
    /// <param name="status">Payout status.</param>
    public static bool IsPayoutFinal(PayoutStatus status) => FinalPayoutStatuses.Contains(status);

    /// <summary>The payout reached the chain and is irreversible.</summary>
    /// <param name="status">Payout status.</param>
    public static bool IsPayoutSucceeded(PayoutStatus status) => status == PayoutStatus.Confirmed;
}
