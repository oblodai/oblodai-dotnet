using Oblodai.Contract;
using Oblodai.Models;

namespace Oblodai.Resources;

/// <summary>
/// Static deposit wallets: one permanent address per customer, deposits reported as <c>wallet.paid</c>.
/// Payment key, except the blocked-deposit refund, which needs the payout key.
/// </summary>
public sealed class Wallets : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public Wallets(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary><c>POST /v1/wallet</c> — idempotent by <c>order_id</c>.</summary>
    /// <param name="request">Asset, network and your customer reference.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Wallet> CreateAsync(
        WalletRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Wallet>(Routes.PostV1Wallet, request, options, cancellationToken);

    /// <summary><c>POST /v1/wallet/qr</c>.</summary>
    /// <param name="address">Address to render into a QR code.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<WalletQr> QrAsync(
        string address,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<WalletQr>(
            Routes.PostV1WalletQr,
            new WalletQrRequest { Address = address },
            options,
            cancellationToken);

    /// <summary>
    /// <c>POST /v1/wallet/block</c> — stop crediting an address; later deposits wait for a refund
    /// decision.
    /// </summary>
    /// <param name="request">Which address to block, and why.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<WalletBlocked> BlockAsync(
        WalletBlockRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<WalletBlocked>(Routes.PostV1WalletBlock, request, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/wallet/blocked-address-refund</c> — send funds that landed on a blocked address back.
    /// Payout key.
    /// </summary>
    /// <param name="request">Which deposit to return, and where.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<Payout> RefundBlockedDepositAsync(
        WalletBlockedAddressRefundRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<Payout>(Routes.PostV1WalletBlockedAddressRefund, request, options, cancellationToken);
}
