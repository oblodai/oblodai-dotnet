using Oblodai.Contract;
using Oblodai.Models;

namespace Oblodai.Resources;

/// <summary>
/// Merchant provisioning — for platforms that onboard merchants themselves. These routes are not
/// HMAC-signed; a self-hosted gateway gates them with its admin token (the <c>AdminToken</c> option).
/// </summary>
public sealed class Merchants : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public Merchants(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary>
    /// <c>POST /v1/merchants</c> — create a merchant and mint its API key (the secret is shown once).
    /// </summary>
    /// <param name="request">Owner email and display name.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<MerchantOnboarded> CreateAsync(
        MerchantsRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<MerchantOnboarded>(Routes.PostV1Merchants, request, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/merchants/{id}/sandbox</c> — the merchant's dev store and its <c>test_</c> key
    /// (idempotent).
    /// </summary>
    /// <param name="merchantId">Merchant id from the onboarding response.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<SandboxStore> CreateSandboxAsync(
        string merchantId,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<SandboxStore>(
            Routes.PostV1MerchantsIdSandbox,
            null,
            options,
            cancellationToken,
            pathParams: new Dictionary<string, string> { ["id"] = merchantId });
}
