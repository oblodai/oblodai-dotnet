using Oblodai.Contract;
using Oblodai.Models;

namespace Oblodai.Resources;

/// <summary>Progress of asynchronous batches (payment, refund, payout, transfer, payout-link).</summary>
public sealed class Batches : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public Batches(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary>
    /// <c>POST /v1/batch/info</c> — status, counters and per-row outcomes. Accepts either key kind; the
    /// gateway requires the kind that created the batch, so a payout batch is retried with the payout
    /// key when one is configured.
    /// </summary>
    /// <param name="request">Batch id and window over its rows.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public async Task<BatchInfo> InfoAsync(
        BatchInfoRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await CallAsync<BatchInfo>(Routes.PostV1BatchInfo, request, options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PermissionException error)
            when (error.Code == ErrorCodes.MerchantWrongKeyKind && options?.PreferPayoutKey != true)
        {
            var retry = (options ?? new RequestOptions()) with { PreferPayoutKey = true };
            return await CallAsync<BatchInfo>(Routes.PostV1BatchInfo, request, retry, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

/// <summary>Internal, instant, fee-free moves between platform balances. Payout key.</summary>
public sealed class Transfers : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public Transfers(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary>
    /// <c>POST /v1/transfer/to-personal</c> — business balance → the owner's personal wallet (needs an
    /// owner link).
    /// <para>
    /// Codes worth branching on: <c>transfer.bad_amount</c>, <c>merchant.no_owner</c>,
    /// <c>merchant.no_personal_wallet</c>, <c>payout.insufficient_funds</c> (retryable),
    /// <c>payout.funds_maturing</c> (retryable), <c>merchant.wrong_key_kind</c>.
    /// </para>
    /// </summary>
    /// <param name="request">Amount, asset and idempotent <c>order_id</c>.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<TransferToPersonal> ToPersonalAsync(
        TransferToPersonalRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<TransferToPersonal>(Routes.PostV1TransferToPersonal, request, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/transfer/to-user</c> — business balance → another platform user's personal wallet.
    /// <c>amount</c> and <c>currency</c> are required.
    /// <para>
    /// Codes worth branching on: <c>transfer.bad_amount</c>, <c>transfer.no_recipient</c>,
    /// <c>transfer.recipient_not_found</c>, <c>transfer.bad_recipient</c> (the recipient is yourself),
    /// <c>payout.insufficient_funds</c> (retryable), <c>merchant.wrong_key_kind</c>.
    /// </para>
    /// </summary>
    /// <param name="request">Recipient user id, amount and asset.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<TransferToUser> ToUserAsync(
        TransferToUserRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<TransferToUser>(Routes.PostV1TransferToUser, request, options, cancellationToken);

    /// <summary>
    /// <c>POST /v1/transfer/batch</c> — ASYNCHRONOUS batch of <see cref="ToUserAsync"/> transfers; poll
    /// <c>Batches.InfoAsync</c>. <c>order_id</c> is required on every item.
    /// <para>
    /// Codes worth branching on: <c>payout.batch_too_large</c>, <c>payout.empty_batch</c>,
    /// <c>request.missing_field</c> (an item without <c>order_id</c>/<c>amount</c>/<c>currency</c>),
    /// <c>transfer.recipient_not_found</c>, <c>merchant.wrong_key_kind</c>, <c>idempotency.key_reused</c>.
    /// </para>
    /// </summary>
    /// <param name="request">The transfers to submit.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<BatchSubmitted> BatchAsync(
        TransferBatchRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<BatchSubmitted>(Routes.PostV1TransferBatch, request, options, cancellationToken);
}
