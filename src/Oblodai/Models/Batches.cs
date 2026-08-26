using System.Text.Json;
using System.Text.Json.Serialization;
using Oblodai.Contract;

namespace Oblodai.Models;

/// <summary>
/// <c>/v1/payment/batch</c>, <c>/v1/refund/batch</c>, <c>/v1/payout/batch</c> and
/// <c>/v1/transfer/batch</c> acknowledgement — the work was queued, not done.
/// </summary>
public sealed record BatchSubmitted
{
    /// <summary>Batch id — poll <c>POST /v1/batch/info</c> with it for status and results.</summary>
    [JsonPropertyName("batch_id")]
    public string BatchId { get; init; } = string.Empty;

    /// <summary>Batch kind: <c>payment</c> | <c>refund</c> | <c>payout</c> | <c>transfer</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    /// <summary>Initial status — always <c>pending</c>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>How many items were accepted for processing.</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }
}

/// <summary>One item of a <see cref="BatchInfo"/> listing.</summary>
public sealed record BatchInfoItem
{
    /// <summary>Index of the item in the original array (zero-based).</summary>
    [JsonPropertyName("idx")]
    public int Idx { get; init; }

    /// <summary>
    /// True when <see cref="Status"/> is <c>done</c>, false when it is <c>error</c>; absent while
    /// the item is not processed.
    /// </summary>
    [JsonPropertyName("ok")]
    public bool? Ok { get; init; }

    /// <summary>The item's <c>order_id</c>, if you set one; not always present.</summary>
    [JsonPropertyName("order_id")]
    public string? OrderId { get; init; }

    /// <summary>Item status: <c>pending</c> | <c>processing</c> | <c>done</c> | <c>error</c>.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Result of the successful operation — the same object a single call would return; only when
    /// <see cref="Status"/> is <c>done</c>.
    /// </summary>
    [JsonPropertyName("result")]
    public IReadOnlyDictionary<string, JsonElement>? Result { get; init; }

    /// <summary>Human-readable error message; only when <see cref="Status"/> is <c>error</c>.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>
    /// Machine-readable error code — the same one a single call would have returned, from the
    /// <c>ErrorCodes</c> catalogue. An item the batch never got to reports the reason the batch itself
    /// stopped.
    /// </summary>
    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; init; }

    /// <summary>
    /// HTTP status a single call would have returned; absent if the item never reached the handler.
    /// </summary>
    [JsonPropertyName("http_status")]
    public int? HttpStatus { get; init; }
}

/// <summary><c>/v1/batch/info</c> — progress and per-item outcome of an asynchronous batch.</summary>
public sealed record BatchInfo
{
    /// <summary>Batch id.</summary>
    [JsonPropertyName("batch_id")]
    public string BatchId { get; init; } = string.Empty;

    /// <summary>Batch kind: <c>payment</c> | <c>refund</c> | <c>payout</c> | <c>transfer</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Batch status: <c>pending</c> | <c>processing</c> | <c>completed</c> | <c>stopped</c>. BOTH
    /// <c>completed</c> and <c>stopped</c> are terminal — poll until either, and neither means
    /// "everything succeeded": check <see cref="Succeeded"/> and <see cref="Failed"/>.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>Error handling mode the batch was submitted with: <c>continue</c> | <c>stop</c>.</summary>
    [JsonPropertyName("on_error")]
    public BatchOnError OnError { get; init; }

    /// <summary>Total items in the batch — over the whole batch, independent of pagination.</summary>
    [JsonPropertyName("total")]
    public int Total { get; init; }

    /// <summary>Processed successfully.</summary>
    [JsonPropertyName("succeeded")]
    public int Succeeded { get; init; }

    /// <summary>
    /// Failed with an error; with <c>on_error: stop</c>, items skipped after the halt are counted
    /// here too.
    /// </summary>
    [JsonPropertyName("failed")]
    public int Failed { get; init; }

    /// <summary>Page of items with the result or the error for each one.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<BatchInfoItem> Items { get; init; } = [];

    /// <summary>Batch creation time (RFC 3339).</summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>Time of the last change (RFC 3339).</summary>
    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = string.Empty;
}
