using Oblodai.Contract;
using Oblodai.Models;

namespace Oblodai.Resources;

/// <summary>
/// Identify a payout by its <c>uuid</c> or by your <c>order_id</c> (one of them is required; the
/// <c>uuid</c> wins when both are set). A bare string converts to a lookup by <c>uuid</c>.
/// </summary>
public sealed record PayoutLookup
{
    /// <summary>Payout id in Oblodai.</summary>
    public string? Uuid { get; init; }

    /// <summary>Your own order reference.</summary>
    public string? OrderId { get; init; }

    /// <summary>A bare string is taken as the <c>uuid</c>.</summary>
    /// <param name="uuid">Payout id in Oblodai.</param>
    public static implicit operator PayoutLookup(string uuid) => new() { Uuid = uuid };

    /// <summary>Named form of the string conversion.</summary>
    /// <param name="uuid">Payout id in Oblodai.</param>
    public static PayoutLookup FromUuid(string uuid) => new() { Uuid = uuid };
}

/// <summary>Outgoing transfers to external addresses. Every route here needs the payout key.</summary>
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
    /// <c>order_id</c> and by Idempotency-Key. Errors to handle: <c>payout.insufficient_funds</c>
    /// (retryable), <c>payout.funds_maturing</c>, <c>payout.bad_address</c>, <c>payout.memo_required</c>.
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
    /// <param name="uuid">Payout id.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Payout> CancelAsync(
        string uuid,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Payout>(
            Routes.PostV1PayoutCancel,
            new PayoutCancelRequest { Uuid = uuid },
            options,
            cancellationToken);

    /// <summary><c>POST /v1/payout/approve</c> — approve a payout awaiting manual approval.</summary>
    /// <param name="uuid">Payout id.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Payout> ApproveAsync(
        string uuid,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Payout>(
            Routes.PostV1PayoutApprove,
            new PayoutApproveRequest { Uuid = uuid },
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
