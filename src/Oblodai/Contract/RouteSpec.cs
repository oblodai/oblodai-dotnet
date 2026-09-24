namespace Oblodai.Contract;

/// <summary>Which credential the gateway's gate expects on a route.</summary>
public enum RouteAuth
{
    /// <summary>No credentials: payer-facing and catalogue routes.</summary>
    Public,

    /// <summary>Signed with the merchant's API key — the one pair, on every merchant-facing route.</summary>
    Key,

    /// <summary>Merchant provisioning: unsigned, gated by <c>X-Admin-Token</c> on a self-hosted gateway.</summary>
    Onboard,
}

/// <summary>How a route paginates its result.</summary>
public enum ListKind
{
    /// <summary>Not a list route.</summary>
    None,

    /// <summary><c>{ items, paginate }</c>.</summary>
    Paged,
}

/// <summary>
/// One route of the gateway, exactly as its OpenAPI contract declares it. The registry
/// <c>Oblodai.Resources.Routes</c> is generated from <c>openapi.json</c>, so every route the SDK can
/// call is one the gateway declares, with the same auth gate and idempotency wrapper.
/// </summary>
/// <param name="OperationId">The OpenAPI <c>operationId</c>: the route's stable name.</param>
/// <param name="Method">HTTP method, upper case.</param>
/// <param name="Path">Path template; <c>{name}</c> segments are filled from path parameters.</param>
/// <param name="Auth">Which credential the gateway expects.</param>
/// <param name="Idempotent">Wrapped in the gateway's idempotency cache: a key is generated when the caller supplies none.</param>
/// <param name="Safe">Retry-safe (<c>x-retry-safe</c>): a transport failure may be retried without risking a duplicate side effect.</param>
/// <param name="Bare">Answers outside the JSON envelope (binary documents).</param>
/// <param name="List">How the route paginates.</param>
public sealed record RouteSpec(
    string OperationId,
    string Method,
    string Path,
    RouteAuth Auth,
    bool Idempotent,
    bool Safe,
    bool Bare,
    ListKind List = ListKind.None)
{
    /// <summary>The registry key: <c>"POST /v1/payment"</c>.</summary>
    public string Key => $"{Method} {Path}";

    /// <inheritdoc />
    public override string ToString() => Key;
}
