namespace Oblodai;

/// <summary>
/// Request and response hooks: plain callbacks the client calls once per attempt, for observability —
/// metrics, tracing, structured logs. They run synchronously on the calling flow, so keep them
/// cheap; an exception thrown by a hook propagates out of the call.
/// <code>
/// new OblodaiOptions
/// {
///     Hooks = new Hooks
///     {
///         OnRequest = r => logger.LogDebug("{Op} attempt {N}", r.OperationId, r.Attempt),
///         OnResponse = r => metrics.Record(r.Request.OperationId, r.Status, r.Elapsed),
///     },
/// };
/// </code>
/// </summary>
public sealed record Hooks
{
    /// <summary>Called before each attempt is sent.</summary>
    public Action<RequestInfo>? OnRequest { get; init; }

    /// <summary>Called after each attempt: with its HTTP status, or status 0 and the transport error.</summary>
    public Action<ResponseInfo>? OnResponse { get; init; }
}

/// <summary>One attempt about to be sent.</summary>
/// <param name="Method">HTTP method.</param>
/// <param name="Url">Absolute URL, query included.</param>
/// <param name="Headers">The headers as sent, with the signature and the admin token redacted.</param>
/// <param name="Attempt">1 for the first attempt, 2 for the first retry, and so on.</param>
/// <param name="RequestId"><c>X-Request-ID</c> of the call; the same on every attempt.</param>
/// <param name="OperationId">The route's OpenAPI <c>operationId</c>.</param>
public sealed record RequestInfo(
    string Method,
    string Url,
    IReadOnlyDictionary<string, string> Headers,
    int Attempt,
    string RequestId,
    string OperationId);

/// <summary>How one attempt ended: an HTTP response, or <see cref="Status"/> 0 and a transport <see cref="Error"/>.</summary>
/// <param name="Request">The attempt.</param>
/// <param name="Status">HTTP status, or 0 when no response arrived (timeout, network error).</param>
/// <param name="Headers">Response headers (empty when no response arrived).</param>
/// <param name="Elapsed">Time from sending the attempt to this point.</param>
/// <param name="Error">The error this attempt ended with (an error status or a transport failure), else null.</param>
public sealed record ResponseInfo(
    RequestInfo Request,
    int Status,
    IReadOnlyDictionary<string, string> Headers,
    TimeSpan Elapsed,
    OblodaiException? Error);
