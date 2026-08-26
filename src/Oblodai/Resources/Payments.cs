using Oblodai.Contract;
using Oblodai.Models;

namespace Oblodai.Resources;

/// <summary>
/// Invoices: create, look up, cancel, list, and the payer-facing checkout endpoints (which need no
/// credentials at all).
/// </summary>
public sealed class Payments : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public Payments(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary>
    /// <c>POST /v1/payment</c> — create an invoice. Idempotent by <c>order_id</c> and by Idempotency-Key.
    /// <para>
    /// Codes worth branching on: <c>payment.bad_amount</c>, <c>payment.below_minimum</c>,
    /// <c>payment.minimum_unavailable</c> (rate feed down — retryable), <c>payment.unsupported_network</c>,
    /// <c>payment.network_required</c> (multi-network asset, no <c>network</c> given),
    /// <c>request.unknown_currency</c>, <c>idempotency.key_reused</c> (same key, different body).
    /// </para>
    /// </summary>
    /// <param name="request">Invoice to create.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Payment> CreateAsync(
        PaymentRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Payment>(Routes.PostV1Payment, request, options, cancellationToken);

    /// <summary><c>POST /v1/payment/info</c> — by <c>uuid</c> or <c>order_id</c>; includes <c>refunds</c> and <c>refund_status</c>.</summary>
    /// <param name="lookup">Invoice id, or a lookup by <c>order_id</c>.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Payment> InfoAsync(
        PaymentLookup lookup,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Payment>(
            Routes.PostV1PaymentInfo,
            new PaymentInfoRequest { Uuid = lookup.Uuid, OrderId = lookup.OrderId },
            options,
            cancellationToken);

    /// <summary>Alias of <see cref="InfoAsync"/>.</summary>
    /// <param name="lookup">Invoice id, or a lookup by <c>order_id</c>.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Payment> GetAsync(
        PaymentLookup lookup,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => InfoAsync(lookup, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/payment/cancel</c> — cancel an unpaid invoice (409 <c>invoice.not_payable</c> once a
    /// deposit was seen).
    /// </summary>
    /// <param name="lookup">Invoice id, or a lookup by <c>order_id</c>.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Payment> CancelAsync(
        PaymentLookup lookup,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Payment>(
            Routes.PostV1PaymentCancel,
            new PaymentCancelRequest { Uuid = lookup.Uuid, OrderId = lookup.OrderId },
            options,
            cancellationToken);

    /// <summary>
    /// <c>POST /v1/payment/history</c> — newest first; <c>await</c> for a page, <c>await foreach</c> for
    /// everything. The filter's <c>kind</c> field is ignored on this route.
    /// </summary>
    /// <param name="filter">Status filter and page window.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    public PagePromise<Payment> HistoryAsync(
        PaymentHistoryRequest? filter = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PagedPost<Payment>(Routes.PostV1PaymentHistory, filter, options, cancellationToken);

    /// <summary>Alias of <see cref="HistoryAsync"/>.</summary>
    /// <param name="filter">Status filter and page window.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    public PagePromise<Payment> ListAsync(
        PaymentHistoryRequest? filter = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => HistoryAsync(filter, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/payment/batch</c> — create up to 5000 invoices asynchronously; track with
    /// <c>Batches.InfoAsync</c>.
    /// <para>
    /// Codes worth branching on: <c>payment.bad_amount</c>, <c>payment.below_minimum</c>,
    /// <c>request.unknown_currency</c>, <c>request.missing_field</c> (an item without <c>order_id</c>),
    /// <c>payout.batch_too_large</c>, <c>idempotency.key_reused</c>.
    /// </para>
    /// </summary>
    /// <param name="request">The invoices to create.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<BatchSubmitted> BatchAsync(
        PaymentBatchRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<BatchSubmitted>(Routes.PostV1PaymentBatch, request, options, cancellationToken);

    /// <summary><c>POST /v1/payment/qr</c> — QR image of the invoice's payment URI.</summary>
    /// <param name="lookup">Invoice id, or a lookup by <c>order_id</c>.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<QrCode> QrAsync(
        PaymentLookup lookup,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<QrCode>(
            Routes.PostV1PaymentQr,
            new PaymentQrRequest { Uuid = lookup.Uuid, OrderId = lookup.OrderId },
            options,
            cancellationToken);

    /// <summary><c>POST /v1/payment/services</c> — currencies/networks accepted for deposits, with limits and fees.</summary>
    /// <param name="filter">Page window.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    public PagePromise<ServiceMethod> ServicesAsync(
        PaymentServicesRequest? filter = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PagedPost<ServiceMethod>(Routes.PostV1PaymentServices, filter, options, cancellationToken);

    /// <summary><c>POST /v1/payment/send-email</c> — email the receipt (defaults to the invoice's <c>payer_email</c>).</summary>
    /// <param name="request">Which invoice, and where to send it.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<EmailSent> SendEmailAsync(
        PaymentSendEmailRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<EmailSent>(Routes.PostV1PaymentSendEmail, request, options, cancellationToken);

    /// <summary><c>POST /v1/payment/resend</c> — re-deliver the invoice's last webhook.</summary>
    /// <param name="lookup">Invoice id, or a lookup by <c>order_id</c>.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<OkResult> ResendAsync(
        PaymentLookup lookup,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<OkResult>(
            Routes.PostV1PaymentResend,
            new PaymentResendRequest { Uuid = lookup.Uuid, OrderId = lookup.OrderId },
            options,
            cancellationToken);

    // --- payer-facing (public, unsigned) — for custom checkout pages ---

    /// <summary><c>GET /v1/pay/{id}</c> — the invoice as the payer sees it. No credentials needed.</summary>
    /// <param name="uuid">Invoice id.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PublicPayment> PublicViewAsync(
        string uuid,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PublicPayment>(
            Routes.GetV1PayId,
            null,
            options,
            cancellationToken,
            pathParams: new Dictionary<string, string> { ["id"] = uuid });

    /// <summary>
    /// <c>POST /v1/pay/{id}/select</c> — pick the asset/network on a multi-currency invoice. No
    /// credentials needed.
    /// </summary>
    /// <param name="uuid">Invoice id.</param>
    /// <param name="request">Chosen currency and network.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PublicPayment> SelectAsync(
        string uuid,
        PaySelectRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PublicPayment>(
            Routes.PostV1PayIdSelect,
            request,
            options,
            cancellationToken,
            pathParams: new Dictionary<string, string> { ["id"] = uuid });

    /// <summary><c>GET /v1/pay/{id}/qr</c> — QR for the payer page. No credentials needed.</summary>
    /// <param name="uuid">Invoice id.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<QrCode> PublicQrAsync(
        string uuid,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<QrCode>(
            Routes.GetV1PayIdQr,
            null,
            options,
            cancellationToken,
            pathParams: new Dictionary<string, string> { ["id"] = uuid });
}
