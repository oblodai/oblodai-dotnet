namespace Oblodai;

/// <summary>
/// Reading the two lifecycles without memorising their vocabularies. The classes come from the
/// contract (<c>x-status-classes</c>, generated into <see cref="PaymentStatus.Final"/>,
/// <see cref="PaymentStatus.IsSuccess"/>, …); these helpers are thin names over them.
/// </summary>
public static class Statuses
{
    /// <summary>Invoice statuses after which nothing else can happen.</summary>
    public static readonly IReadOnlyList<PaymentStatus> FinalPaymentStatuses = PaymentStatus.Final;

    /// <summary>Payout statuses after which nothing else can happen.</summary>
    public static readonly IReadOnlyList<PayoutStatus> FinalPayoutStatuses = PayoutStatus.Final;

    /// <summary>True once the invoice can no longer change state.</summary>
    /// <param name="status">Invoice status.</param>
    public static bool IsPaymentFinal(PaymentStatus status) => status.IsFinal;

    /// <summary>
    /// The merchant has the money (<c>paid</c> or <c>paid_over</c>). <c>wrong_amount</c> is NOT paid:
    /// resolve it with <c>Refunds.ResolveAsync</c>.
    /// </summary>
    /// <param name="status">Invoice status.</param>
    public static bool IsPaymentPaid(PaymentStatus status) => status.IsSuccess;

    /// <summary>The invoice is underpaid and waiting for a merchant decision.</summary>
    /// <param name="status">Invoice status.</param>
    public static bool IsPaymentUnderpaid(PaymentStatus status) => status == PaymentStatus.WrongAmount;

    /// <summary>True once the payout can no longer change state.</summary>
    /// <param name="status">Payout status.</param>
    public static bool IsPayoutFinal(PayoutStatus status) => status.IsFinal;

    /// <summary>The payout reached the chain and is irreversible.</summary>
    /// <param name="status">Payout status.</param>
    public static bool IsPayoutSucceeded(PayoutStatus status) => status.IsSuccess;
}
