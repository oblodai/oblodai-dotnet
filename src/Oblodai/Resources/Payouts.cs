using Oblodai.Contract;
using Oblodai.Models;

namespace Oblodai.Resources;

/// <summary>Outgoing transfers to external addresses.</summary>
public sealed class Payouts : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public Payouts(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary>
    /// <c>POST /v1/payout</c> — create and (for API keys) auto-approve a payout. Idempotent by
    /// <c>order_id</c> and by Idempotency-Key.
    /// <para>
    /// Codes worth branching on: <c>payout.insufficient_funds</c> (retryable — top up and repeat with the
    /// SAME key), <c>payout.funds_maturing</c> (retryable — deposits not yet mature),
    /// <c>payout.bad_address</c>, <c>payout.address_network_mismatch</c>, <c>payout.memo_required</c>,
    /// <c>payout.amount_below_fee</c>, <c>payout.frozen</c>, <c>payout.order_id_required</c>,
    /// <c>idempotency.key_reused</c>.
    /// </para>
    /// </summary>
    /// <param name="request">The payout to create.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Payout> CreateAsync(
        PayoutRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Payout>(Routes.PostV1Payout, request, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/payout/validate</c> — dry run: every check of <see cref="CreateAsync"/>, nothing
    /// reserved or sent. Same errors as <see cref="CreateAsync"/>.
    /// </summary>
    /// <param name="request">The payout to check.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PayoutValidation> ValidateAsync(
        PayoutValidateRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PayoutValidation>(Routes.PostV1PayoutValidate, request, options, cancellationToken);

    /// <summary><c>POST /v1/payout/calculate</c> — commission and net amount without creating anything.</summary>
    /// <param name="request">Amount, asset and network to price.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PayoutCalculation> CalculateAsync(
        PayoutCalculateRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PayoutCalculation>(Routes.PostV1PayoutCalculate, request, options, cancellationToken);

    /// <summary><c>POST /v1/payout/info</c> — by <c>uuid</c> or <c>order_id</c>. Refunds are payouts too (<c>is_refund</c>).</summary>
    /// <param name="lookup">Payout id, or a lookup by <c>order_id</c>.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Payout> InfoAsync(
        PayoutLookup lookup,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Payout>(
            Routes.PostV1PayoutInfo,
            new PayoutInfoRequest { Uuid = lookup.Uuid, OrderId = lookup.OrderId },
            options,
            cancellationToken);

    /// <summary>Alias of <see cref="InfoAsync"/>.</summary>
    /// <param name="lookup">Payout id, or a lookup by <c>order_id</c>.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Payout> GetAsync(
        PayoutLookup lookup,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => InfoAsync(lookup, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/payout/cancel</c> — cancel while not yet broadcast (pending/approved/awaiting_cosign);
    /// 409 <c>payout.not_pending</c> after.
    /// </summary>
    /// <param name="payout">Payout id, or the payout itself.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Payout> CancelAsync(
        PayoutRef payout,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Payout>(
            Routes.PostV1PayoutCancel,
            new PayoutCancelRequest { Uuid = payout.Uuid },
            options,
            cancellationToken);

    /// <summary><c>POST /v1/payout/approve</c> — approve a payout awaiting manual approval.</summary>
    /// <param name="payout">Payout id, or the payout itself.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Payout> ApproveAsync(
        PayoutRef payout,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Payout>(
            Routes.PostV1PayoutApprove,
            new PayoutApproveRequest { Uuid = payout.Uuid },
            options,
            cancellationToken);

    /// <summary><c>POST /v1/payout/history</c> — newest first. <c>Kind = "refund"</c> lists refunds only.</summary>
    /// <param name="filter">Kind/status filter and page window.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    public PagePromise<Payout> HistoryAsync(
        PayoutHistoryRequest? filter = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PagedPost<Payout>(Routes.PostV1PayoutHistory, filter, options, cancellationToken);

    /// <summary>Alias of <see cref="HistoryAsync"/>.</summary>
    /// <param name="filter">Kind/status filter and page window.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    public PagePromise<Payout> ListAsync(
        PayoutHistoryRequest? filter = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => HistoryAsync(filter, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/payout/mass</c> — SYNCHRONOUS batch (≤100): each element reports its own outcome in
    /// the response.
    /// <para>
    /// Call-level codes worth branching on: <c>payout.batch_too_large</c> (&gt;100),
    /// <c>payout.empty_batch</c>, <c>payout.insufficient_funds</c> (retryable), <c>payout.frozen</c>.
    /// Per-element failures arrive as <c>Items[].Message</c> with the same vocabulary — a 200 can still
    /// contain failures, so check every <c>Items[].Ok</c>.
    /// </para>
    /// </summary>
    /// <param name="request">The payouts to send.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<BatchElement<Payout>>> MassAsync(
        PayoutMassRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PlainListAsync<BatchElement<Payout>>(Routes.PostV1PayoutMass, request, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/payout/batch</c> — ASYNCHRONOUS batch (≤5000): returns a ticket; poll
    /// <c>Batches.InfoAsync</c>. <c>order_id</c> is required on every item.
    /// <para>
    /// Codes worth branching on: <c>payout.batch_too_large</c>, <c>payout.empty_batch</c>,
    /// <c>payout.order_id_required</c>, <c>payout.reference_collision</c>, <c>payout.frozen</c>,
    /// <c>idempotency.key_reused</c>. Insufficient funds surface per element in the batch result, not on
    /// this call.
    /// </para>
    /// </summary>
    /// <param name="request">The payouts to submit.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<BatchSubmitted> BatchAsync(
        PayoutBatchRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<BatchSubmitted>(Routes.PostV1PayoutBatch, request, options, cancellationToken);

    /// <summary><c>POST /v1/payout/services</c> — currencies/networks available for payouts.</summary>
    /// <param name="filter">Page window.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    public PagePromise<ServiceMethod> ServicesAsync(
        PayoutServicesRequest? filter = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PagedPost<ServiceMethod>(Routes.PostV1PayoutServices, filter, options, cancellationToken);

    /// <summary><c>POST /v1/payout/fee-config/get</c>.</summary>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PayoutFeeConfig> GetFeeConfigAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PayoutFeeConfig>(Routes.PostV1PayoutFeeConfigGet, null, options, cancellationToken);

    /// <summary><c>POST /v1/payout/fee-config/set</c> — who bears the network fee by default.</summary>
    /// <param name="request">The new setting.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PayoutFeeConfig> SetFeeConfigAsync(
        PayoutFeeConfigSetRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PayoutFeeConfig>(Routes.PostV1PayoutFeeConfigSet, request, options, cancellationToken);

    /// <summary><c>POST /v1/payout/refund-fee-config/get</c>.</summary>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<RefundFeeConfig> GetRefundFeeConfigAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<RefundFeeConfig>(Routes.PostV1PayoutRefundFeeConfigGet, null, options, cancellationToken);

    /// <summary><c>POST /v1/payout/refund-fee-config/set</c> — who bears the fee on refunds.</summary>
    /// <param name="request">The new setting.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<RefundFeeConfig> SetRefundFeeConfigAsync(
        PayoutRefundFeeConfigSetRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<RefundFeeConfig>(Routes.PostV1PayoutRefundFeeConfigSet, request, options, cancellationToken);
}
