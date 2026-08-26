using System.Text.Json.Serialization;
using Oblodai.Contract;

namespace Oblodai.Models;

/// <summary>
/// Element of every batch listing (<c>/v1/payout/mass</c>, <c>/v1/payout/link/batch</c>,
/// <c>/v1/batch/info</c>): either a <see cref="Result"/> or the error the item failed with.
/// </summary>
/// <typeparam name="T">The model a successful item carries.</typeparam>
public sealed record BatchElement<T>
{
    /// <summary>Index of the item in the array you submitted (zero-based).</summary>
    [JsonPropertyName("idx")]
    public int Idx { get; init; }

    /// <summary>True when the item succeeded, false when it failed; absent while it is unprocessed.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    /// <summary>The item's <c>order_id</c>, if you set one; not always present.</summary>
    [JsonPropertyName("order_id")]
    public string? OrderId { get; init; }

    /// <summary>Result of the successful operation — the same object a single call would return.</summary>
    [JsonPropertyName("result")]
    public T? Result { get; init; }

    /// <summary>Human-readable error message; only on a failed item.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>
    /// Machine-readable error code — the same one a single call would have returned
    /// (<c>payment.below_minimum</c>, <c>payout.address_network_mismatch</c>, …), from the
    /// <c>ErrorCodes</c> catalogue.
    /// </summary>
    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; init; }

    /// <summary>
    /// HTTP status a single call would have returned (400, 409, …); absent when the item never
    /// reached the handler.
    /// </summary>
    [JsonPropertyName("http_status")]
    public int? HttpStatus { get; init; }
}

/// <summary>How a fee was settled on a priced result.</summary>
public sealed record FeeInfo
{
    /// <summary>The fee itself. Decimal string.</summary>
    [JsonPropertyName("commission")]
    public string Commission { get; init; } = string.Empty;

    /// <summary>Who paid it: <c>gateway</c>, <c>merchant</c> or <c>recipient</c>.</summary>
    [JsonPropertyName("fee_bearer")]
    public FeeBearerResult FeeBearer { get; init; }

    /// <summary>Pricing mode the fee was computed with (<c>percent</c>, <c>fixed</c>, …).</summary>
    [JsonPropertyName("fee_type")]
    public string FeeType { get; init; } = string.Empty;
}

/// <summary>The bare acknowledgement routes that have nothing else to report answer with.</summary>
public sealed record OkResult
{
    /// <summary>True — the call took effect.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }
}
