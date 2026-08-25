using Oblodai.Contract;
using Oblodai.Models;

namespace Oblodai.Resources;

/// <summary>
/// Merchant-level configuration exposed over the API. Payment key, except the auto-withdrawal and
/// API-allowlist methods, which need the payout key.
/// </summary>
public sealed class Settings : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public Settings(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary><c>POST /v1/payment/discount/set</c> — payer-facing discount/markup per currency+network.</summary>
    /// <param name="request">Percentage, and the currency/network it applies to.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<DiscountRule> SetDiscountAsync(
        PaymentDiscountSetRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<DiscountRule>(Routes.PostV1PaymentDiscountSet, request, options, cancellationToken);

    /// <summary><c>POST /v1/payment/discount/list</c>.</summary>
    /// <param name="filter">Page window.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    public PagePromise<DiscountRule> ListDiscountsAsync(
        PaymentDiscountListRequest? filter = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PagedPost<DiscountRule>(Routes.PostV1PaymentDiscountList, filter, options, cancellationToken);

    /// <summary><c>POST /v1/payment/accuracy/get</c> — under/overpayment tolerance.</summary>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<AccuracyConfig> GetAccuracyAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<AccuracyConfig>(Routes.PostV1PaymentAccuracyGet, null, options, cancellationToken);

    /// <summary><c>POST /v1/payment/accuracy/set</c>.</summary>
    /// <param name="request">The new tolerance.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<AccuracyConfig> SetAccuracyAsync(
        PaymentAccuracySetRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<AccuracyConfig>(Routes.PostV1PaymentAccuracySet, request, options, cancellationToken);

    /// <summary><c>POST /v1/payment/autorefund/get</c>.</summary>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<AutoRefundConfig> GetAutoRefundAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<AutoRefundConfig>(Routes.PostV1PaymentAutorefundGet, null, options, cancellationToken);

    /// <summary><c>POST /v1/payment/autorefund/set</c> — refund over/underpayments automatically.</summary>
    /// <param name="request">Which imbalances to refund without asking.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<AutoRefundConfig> SetAutoRefundAsync(
        PaymentAutorefundSetRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<AutoRefundConfig>(Routes.PostV1PaymentAutorefundSet, request, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/payment/accepted/list</c> — which currency/network pairs invoices may be paid in.
    /// </summary>
    /// <param name="filter">Page window.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    public PagePromise<AcceptedMethod> ListAcceptedAsync(
        PaymentAcceptedListRequest? filter = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PagedPost<AcceptedMethod>(Routes.PostV1PaymentAcceptedList, filter, options, cancellationToken);

    /// <summary><c>POST /v1/payment/accepted/set</c>.</summary>
    /// <param name="request">The full list of accepted pairs; an empty list accepts everything.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<OkResult> SetAcceptedAsync(
        PaymentAcceptedSetRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<OkResult>(Routes.PostV1PaymentAcceptedSet, request, options, cancellationToken);

    /// <summary><c>POST /v1/payment/fee-config/get</c> — share of the network fee charged to the payer.</summary>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PaymentFeeConfig> GetPaymentFeeConfigAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PaymentFeeConfig>(Routes.PostV1PaymentFeeConfigGet, null, options, cancellationToken);

    /// <summary><c>POST /v1/payment/fee-config/set</c>.</summary>
    /// <param name="request">The new payer share.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<PaymentFeeConfig> SetPaymentFeeConfigAsync(
        PaymentFeeConfigSetRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<PaymentFeeConfig>(Routes.PostV1PaymentFeeConfigSet, request, options, cancellationToken);

    /// <summary><c>POST /v1/auto-withdraw/list</c>. Payout key.</summary>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<AutoWithdrawRule>> ListAutoWithdrawAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PlainListAsync<AutoWithdrawRule>(Routes.PostV1AutoWithdrawList, null, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/auto-withdraw/set</c> — sweep a currency to an address once the balance passes
    /// <c>min_amount</c>. Payout key.
    /// </summary>
    /// <param name="request">Asset, network, destination address and threshold.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<AutoWithdrawRule>> SetAutoWithdrawAsync(
        AutoWithdrawSetRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PlainListAsync<AutoWithdrawRule>(Routes.PostV1AutoWithdrawSet, request, options, cancellationToken);

    /// <summary><c>POST /v1/auto-withdraw/delete</c>. Payout key.</summary>
    /// <param name="currency">Asset whose auto-withdrawal to switch off.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<IReadOnlyList<AutoWithdrawRule>> DeleteAutoWithdrawAsync(
        string currency,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PlainListAsync<AutoWithdrawRule>(
            Routes.PostV1AutoWithdrawDelete,
            new AutoWithdrawDeleteRequest { Currency = currency },
            options,
            cancellationToken);

    /// <summary><c>POST /v1/api-allowlist/list</c> — source IPs allowed to use the API keys. Payout key.</summary>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<ApiAllowlist> ListApiAllowlistAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ApiAllowlist>(Routes.PostV1ApiAllowlistList, null, options, cancellationToken);

    /// <summary><c>POST /v1/api-allowlist/add</c>. Payout key.</summary>
    /// <param name="cidr">IP or subnet in CIDR notation.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<ApiAllowlist> AddApiAllowlistAsync(
        string cidr,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ApiAllowlist>(
            Routes.PostV1ApiAllowlistAdd,
            new ApiAllowlistAddRequest { Cidr = cidr },
            options,
            cancellationToken);

    /// <summary><c>POST /v1/api-allowlist/remove</c>. Payout key.</summary>
    /// <param name="cidr">IP or subnet in CIDR notation.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<ApiAllowlist> RemoveApiAllowlistAsync(
        string cidr,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ApiAllowlist>(
            Routes.PostV1ApiAllowlistRemove,
            new ApiAllowlistRemoveRequest { Cidr = cidr },
            options,
            cancellationToken);

    /// <summary>
    /// <c>POST /v1/api-allowlist/enable</c> — switch enforcement on or off (the list is kept). Payout key.
    /// </summary>
    /// <param name="enabled">True accepts API calls only from listed addresses.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<ApiAllowlist> EnableApiAllowlistAsync(
        bool enabled,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ApiAllowlist>(
            Routes.PostV1ApiAllowlistEnable,
            new ApiAllowlistEnableRequest { Enabled = enabled },
            options,
            cancellationToken);
}
