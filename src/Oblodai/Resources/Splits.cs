using Oblodai.Contract;
using Oblodai.Models;

namespace Oblodai.Resources;

/// <summary>Revenue splits: a percentage of every payment forwarded to a partner. Payout key.</summary>
public sealed class Splits : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public Splits(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary>
    /// <c>POST /v1/split/rule</c> — to an external address (<c>address</c>+<c>network</c>) or a platform
    /// merchant (<c>merchant_id</c>).
    /// </summary>
    /// <param name="request">Share and recipient of the rule.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<SplitRule> CreateRuleAsync(
        SplitRuleRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<SplitRule>(Routes.PostV1SplitRule, request, options, cancellationToken);

    /// <summary><c>POST /v1/split/rule/list</c>.</summary>
    /// <param name="filter">Page window.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    public PagePromise<SplitRule> ListRulesAsync(
        SplitRuleListRequest? filter = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => PagedPost<SplitRule>(Routes.PostV1SplitRuleList, filter, options, cancellationToken);

    /// <summary><c>POST /v1/split/rule/delete</c>.</summary>
    /// <param name="ruleId">Rule identifier.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<OkResult> DeleteRuleAsync(
        string ruleId,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<OkResult>(
            Routes.PostV1SplitRuleDelete,
            new SplitRuleDeleteRequest { RuleId = ruleId },
            options,
            cancellationToken);

    /// <summary><c>POST /v1/split/config/get</c>.</summary>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<SplitConfig> GetConfigAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<SplitConfig>(Routes.PostV1SplitConfigGet, null, options, cancellationToken);

    /// <summary><c>POST /v1/split/config/set</c> — how long split shares are held back for refunds.</summary>
    /// <param name="request">The new hold period.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<SplitConfig> SetConfigAsync(
        SplitConfigSetRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<SplitConfig>(Routes.PostV1SplitConfigSet, request, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/split/recipient/optin/get</c> — whether this merchant accepts being a split recipient.
    /// </summary>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<SplitOptIn> GetOptInAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<SplitOptIn>(Routes.PostV1SplitRecipientOptinGet, null, options, cancellationToken);

    /// <summary><c>POST /v1/split/recipient/optin</c>.</summary>
    /// <param name="enabled">True lets other merchants route split shares to your balance.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<SplitOptIn> SetOptInAsync(
        bool enabled,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<SplitOptIn>(
            Routes.PostV1SplitRecipientOptin,
            new SplitRecipientOptinRequest { Enabled = enabled },
            options,
            cancellationToken);
}
