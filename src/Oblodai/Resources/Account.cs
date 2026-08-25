using Oblodai.Contract;
using Oblodai.Models;

namespace Oblodai.Resources;

/// <summary>Balances and account-level facts. Payment key.</summary>
public sealed class Account : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public Account(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary><c>POST /v1/balance</c> — available balance per currency.</summary>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Balance> BalanceAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Balance>(Routes.PostV1Balance, null, options, cancellationToken);

    /// <summary><c>POST /v1/referral/info</c> — referral code, link and earnings.</summary>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<ReferralInfo> ReferralAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<ReferralInfo>(Routes.PostV1ReferralInfo, null, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/vrcs</c> — read (no argument) or set volatility-risk conversion (auto-convert
    /// volatile deposits to USDT).
    /// </summary>
    /// <param name="enabled">Leave null to read the current state; pass a value to set it.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<VrcsStatus> VrcsAsync(
        bool? enabled = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<VrcsStatus>(
            Routes.PostV1Vrcs,
            enabled is null ? null : new VrcsRequest { Enabled = enabled },
            options,
            cancellationToken);
}

/// <summary>Public reference data — no credentials needed.</summary>
public sealed class Catalog : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public Catalog(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary><c>GET /v1/currencies</c> — every asset, its networks and live availability.</summary>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Currencies> CurrenciesAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Currencies>(Routes.GetV1Currencies, null, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/exchange-rate/list</c> — current rates, optionally filtered by <c>currency_from</c>/
    /// <c>currency_to</c>.
    /// </summary>
    /// <param name="filter">Currency filter and page window.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    public PagePromise<ExchangeRate> ExchangeRatesAsync(
        ExchangeRateListRequest? filter = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PagedPost<ExchangeRate>(Routes.PostV1ExchangeRateList, filter, options, cancellationToken);
}
