using Oblodai.Contract;
using Oblodai.Models;

namespace Oblodai.Resources;

/// <summary>
/// Payout links (cheques): funds reserved now, claimed later by whoever holds the token. Payout key
/// (the recipient-facing methods need no credentials).
/// </summary>
public sealed class PayoutLinks : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public PayoutLinks(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary>
    /// <c>POST /v1/payout/link</c> — reserve funds and mint a claim token (<c>claim_token</c>/
    /// <c>claim_url</c> are returned once). Idempotent by <c>reference</c>.
    /// <para>
    /// Codes worth branching on: <c>payout_link.disabled</c>, <c>payout.insufficient_funds</c>
    /// (retryable), <c>payout.funds_maturing</c> (retryable), <c>payout.bad_amount</c>,
    /// <c>payout.bad_address</c>, <c>payout.reference_collision</c> (that <c>reference</c> already
    /// minted a different link), <c>merchant.wrong_key_kind</c>.
    /// </para>
    /// </summary>
    /// <param name="request">Amount, asset and network of the link.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PayoutLink> CreateAsync(
        PayoutLinkRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PayoutLink>(Routes.PostV1PayoutLink, request, options, cancellationToken);

    /// <summary><c>POST /v1/payout/link/info</c>.</summary>
    /// <param name="link">Payout link id, or the link itself.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PayoutLink> InfoAsync(
        LinkRef link,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PayoutLink>(
            Routes.PostV1PayoutLinkInfo,
            new PayoutLinkInfoRequest { LinkId = link.LinkId },
            options,
            cancellationToken);

    /// <summary>Alias of <see cref="InfoAsync"/>.</summary>
    /// <param name="link">Payout link id, or the link itself.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PayoutLink> GetAsync(
        LinkRef link,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => InfoAsync(link, options, cancellationToken);

    /// <summary><c>POST /v1/payout/link/list</c>.</summary>
    /// <param name="filter">Page window.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    public PagePromise<PayoutLink> ListAsync(
        PayoutLinkListRequest? filter = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PagedPost<PayoutLink>(Routes.PostV1PayoutLinkList, filter, options, cancellationToken);

    /// <summary><c>POST /v1/payout/link/cancel</c> — release the reserved funds of an unclaimed link.</summary>
    /// <param name="link">Payout link id, or the link itself.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PayoutLink> CancelAsync(
        LinkRef link,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PayoutLink>(
            Routes.PostV1PayoutLinkCancel,
            new PayoutLinkCancelRequest { LinkId = link.LinkId },
            options,
            cancellationToken);

    /// <summary>
    /// <c>POST /v1/payout/link/batch</c> — SYNCHRONOUS: at most 500 links in one signed call, with
    /// per-element outcomes, so a 200 can still contain failures — check every <c>Items[].Ok</c>.
    /// <c>reference</c> is required on every item.
    /// <para>
    /// Call-level codes worth branching on: <c>payoutlink.batch_too_large</c> (&gt;500),
    /// <c>payoutlink.empty_batch</c>, <c>payout_link.disabled</c>, <c>payout.insufficient_funds</c>
    /// (retryable), <c>merchant.wrong_key_kind</c>.
    /// </para>
    /// </summary>
    /// <param name="request">The links to mint.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<BatchElement<PayoutLink>>> BatchAsync(
        PayoutLinkBatchRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PlainListAsync<BatchElement<PayoutLink>>(
            Routes.PostV1PayoutLinkBatch,
            request,
            options,
            cancellationToken);

    /// <summary><c>POST /v1/payout/link/cheque</c> — printable PDF cheque for a claim token.</summary>
    /// <param name="request">Claim token and document language.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<FileResult> ChequeAsync(
        PayoutLinkChequeRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => FileAsync(Routes.PostV1PayoutLinkCheque, options, cancellationToken, body: request);

    // --- recipient side (public, unsigned) ---

    /// <summary><c>GET /v1/claim/{token}</c> — what the recipient sees before claiming. No credentials needed.</summary>
    /// <param name="token">Claim token from the link.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<ClaimPreview> ClaimPreviewAsync(
        string token,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ClaimPreview>(
            Routes.GetV1ClaimToken,
            null,
            options,
            cancellationToken,
            pathParams: new Dictionary<string, string> { ["token"] = token });

    /// <summary>
    /// <c>POST /v1/claim/{token}</c> — claim to an address (and passcode when the link has one). No
    /// credentials needed.
    /// <para>
    /// Codes worth branching on: <c>request.not_found</c> (unknown or spent token),
    /// <c>payout.bad_address</c>, <c>payout.address_network_mismatch</c>, <c>payout.memo_required</c>,
    /// <c>payout.bad_status</c> (already claimed, cancelled or expired), <c>request.rate_limited</c>
    /// (a wrong passcode, throttled).
    /// </para>
    /// </summary>
    /// <param name="token">Claim token from the link.</param>
    /// <param name="request">Destination address, memo and passcode.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<ClaimResult> ClaimAsync(
        string token,
        ClaimRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ClaimResult>(
            Routes.PostV1ClaimToken,
            request,
            options,
            cancellationToken,
            pathParams: new Dictionary<string, string> { ["token"] = token });
}

/// <summary>
/// Reusable payment links (tip jars, price tags): each checkout spawns an invoice. Payment key (the
/// payer-facing methods need no credentials).
/// </summary>
public sealed class PaymentLinks : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public PaymentLinks(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary>
    /// <c>POST /v1/payment/link</c> — mint a reusable payment page.
    /// <para>
    /// Codes worth branching on: <c>invoice.bad_price</c>, <c>payment.bad_amount</c>,
    /// <c>request.unknown_currency</c>, <c>payment.unsupported_network</c>,
    /// <c>payment.below_minimum</c>, <c>idempotency.key_reused</c>.
    /// </para>
    /// </summary>
    /// <param name="request">Pricing mode, asset and page settings of the link.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PaymentLinkCreated> CreateAsync(
        PaymentLinkRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PaymentLinkCreated>(Routes.PostV1PaymentLink, request, options, cancellationToken);

    /// <summary><c>POST /v1/payment/link/info</c> — the link plus a page of the invoices it spawned (<c>payments</c>).</summary>
    /// <param name="link">Payment link id, or the link itself.</param>
    /// <param name="page">Window over the link's invoices.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PaymentLink> InfoAsync(
        LinkRef link,
        PageParams? page = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PaymentLink>(
            Routes.PostV1PaymentLinkInfo,
            new PaymentLinkInfoRequest { LinkId = link.LinkId, Limit = page?.Limit, Offset = page?.Offset },
            options,
            cancellationToken);

    /// <summary>Alias of <see cref="InfoAsync"/>.</summary>
    /// <param name="link">Payment link id, or the link itself.</param>
    /// <param name="page">Window over the link's invoices.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PaymentLink> GetAsync(
        LinkRef link,
        PageParams? page = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => InfoAsync(link, page, options, cancellationToken);

    /// <summary><c>POST /v1/payment/link/list</c>.</summary>
    /// <param name="filter">Page window.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    public PagePromise<PaymentLink> ListAsync(
        PaymentLinkListRequest? filter = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PagedPost<PaymentLink>(Routes.PostV1PaymentLinkList, filter, options, cancellationToken);

    /// <summary><c>POST /v1/payment/link/toggle</c> — enable or disable a link.</summary>
    /// <param name="link">Payment link id, or the link itself.</param>
    /// <param name="active">True accepts payments; false shows the link as inactive.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PaymentLinkToggled> ToggleAsync(
        LinkRef link,
        bool active,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PaymentLinkToggled>(
            Routes.PostV1PaymentLinkToggle,
            new PaymentLinkToggleRequest { LinkId = link.LinkId, Active = active },
            options,
            cancellationToken);

    // --- payer side (public, unsigned) ---

    /// <summary><c>GET /v1/link/{id}</c> — the link as the payer sees it. No credentials needed.</summary>
    /// <param name="linkId">Payment link id.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PublicPaymentLink> PublicViewAsync(
        string linkId,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PublicPaymentLink>(
            Routes.GetV1LinkId,
            null,
            options,
            cancellationToken,
            pathParams: new Dictionary<string, string> { ["id"] = linkId });

    /// <summary>
    /// <c>POST /v1/link/{id}/checkout</c> — spawn an invoice from the link (rate-capped per IP). No
    /// credentials needed.
    /// </summary>
    /// <param name="linkId">Payment link id.</param>
    /// <param name="request">Amount, asset and buyer details, where the link leaves them open.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PublicPayment> CheckoutAsync(
        string linkId,
        LinkCheckoutRequest? request = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PublicPayment>(
            Routes.PostV1LinkIdCheckout,
            request ?? new LinkCheckoutRequest(),
            options,
            cancellationToken,
            pathParams: new Dictionary<string, string> { ["id"] = linkId });
}
