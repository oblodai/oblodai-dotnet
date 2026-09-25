using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Oblodai.Contract;

namespace Oblodai.Resources;

/// <summary>
/// Shared plumbing of the generated resource namespaces (<c>Generated/Resources.g.cs</c>): it turns a
/// route plus a body into a transport call, a list route into a lazy <see cref="PagePromise{T}"/> and a
/// bare route into a <see cref="FileResult"/>.
/// </summary>
public abstract class Resource
{
    /// <summary>Bind the resource to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    protected Resource(OblodaiTransport transport) => Transport = transport;

    /// <summary>The transport every call goes through.</summary>
    protected OblodaiTransport Transport { get; }

    /// <summary>
    /// Poll until the status is in <paramref name="terminal"/> — the body of the generated
    /// <c>WaitAsync</c> of long-running operations (<see cref="LongRunning.PollAsync{T}(Func{CancellationToken, Task{T}}, Func{T, string}, IReadOnlySet{string}, TimeProvider, TimeSpan?, TimeSpan?, CancellationToken)"/>
    /// on the client's time source).
    /// </summary>
    /// <typeparam name="T">The poll answer.</typeparam>
    /// <param name="poll">One poll.</param>
    /// <param name="status">The status of an answer.</param>
    /// <param name="terminal">The statuses that end the wait.</param>
    /// <param name="pollInterval">Pause between polls (2 s by default).</param>
    /// <param name="timeout">Longest wait (10 min by default).</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    protected Task<T> PollUntilAsync<T>(
        Func<CancellationToken, Task<T>> poll,
        Func<T, string> status,
        IReadOnlySet<string> terminal,
        TimeSpan? pollInterval,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => LongRunning.PollAsync(poll, status, terminal, Transport.Options.TimeProvider, pollInterval, timeout, cancellationToken);

    /// <summary>Call an envelope route and decode its <c>result</c> — the entry point of generated methods.</summary>
    /// <typeparam name="T">Model of the result payload.</typeparam>
    /// <param name="route">Route to call.</param>
    /// <param name="body">Request body, or null.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <param name="pathParams">Values for <c>{name}</c> path segments.</param>
    /// <param name="query">Query parameters.</param>
    protected Task<T> RequestAsync<T>(
        RouteSpec route,
        object? body,
        RequestOptions? options,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? pathParams = null,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null)
        => Transport.CallAsync<T>(route, CallOptions.From(options, body, pathParams, query), cancellationToken);

    /// <summary>
    /// Call a paged list route lazily. The request's own <c>limit</c>/<c>offset</c> (body, or query
    /// on a GET route) pick the first page; every page repeats the rest of the request.
    /// </summary>
    /// <typeparam name="T">Item model.</typeparam>
    /// <param name="route">Route to call.</param>
    /// <param name="body">Request body (filter), or null.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    /// <param name="pathParams">Values for <c>{name}</c> path segments.</param>
    /// <param name="query">Query parameters.</param>
    protected PagePromise<T> RequestPaged<T>(
        RouteSpec route,
        object? body,
        RequestOptions? options,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? pathParams = null,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null)
    {
        AssertNoIdempotencyKey(route, options);
        var useQuery = route.Method == "GET";
        var filter = ToJsonObject(body);
        var rest = new List<KeyValuePair<string, string?>>();
        var limit = TakeInt(filter, "limit");
        var offset = TakeInt(filter, "offset");
        foreach (var pair in query ?? [])
        {
            if (pair.Key is "limit" or "offset")
            {
                if (pair.Value is not null)
                {
                    var value = ParseInt(pair.Key, pair.Value);
                    if (pair.Key == "limit")
                    {
                        limit = value;
                    }
                    else
                    {
                        offset = value;
                    }
                }

                continue;
            }

            rest.Add(pair);
        }

        return new PagePromise<T>(
            async (pageLimit, pageOffset, token) =>
            {
                object? pageBody = null;
                var pageQuery = new List<KeyValuePair<string, string?>>(rest);
                var window = new[]
                {
                    new KeyValuePair<string, string?>("limit", pageLimit.ToString(CultureInfo.InvariantCulture)),
                    new KeyValuePair<string, string?>("offset", pageOffset.ToString(CultureInfo.InvariantCulture)),
                };
                if (useQuery)
                {
                    pageQuery.AddRange(window);
                    pageBody = body is null ? null : filter;
                }
                else
                {
                    var copy = filter.DeepClone().AsObject();
                    copy["limit"] = pageLimit;
                    copy["offset"] = pageOffset;
                    pageBody = copy;
                }

                var element = await Transport
                    .CallElementAsync(
                        route,
                        CallOptions.From((options ?? new RequestOptions()) with { IdempotencyKey = null }, pageBody, pathParams, pageQuery),
                        token)
                    .ConfigureAwait(false);
                return AsPage<T>(element);
            },
            limit,
            offset,
            cancellationToken);
    }

    /// <summary>Call a bare route and return the bytes it answered with.</summary>
    /// <param name="route">Route to call.</param>
    /// <param name="body">Request body for bare POST routes, or null.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <param name="pathParams">Values for <c>{name}</c> path segments.</param>
    /// <param name="query">Query parameters.</param>
    protected async Task<FileResult> RequestFileAsync(
        RouteSpec route,
        object? body,
        RequestOptions? options,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? pathParams = null,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null)
    {
        var raw = await Transport
            .CallRawAsync(route, CallOptions.From(options, body, pathParams, query), cancellationToken)
            .ConfigureAwait(false);
        return new FileResult(
            raw.Body,
            raw.ContentType ?? "application/octet-stream",
            FilenameFrom(raw.ContentDisposition));
    }

    /// <summary>
    /// A list route is read-only, so the gateway never deduplicates it by idempotency key (<see cref="SigningProtocol.Request.IdempotencyKey"/>). Dropping
    /// the caller's key quietly would leave them believing a lost page request is safe to repeat under
    /// the same key; the same refusal the transport raises on any other non-deduplicated route is raised
    /// here instead, at the call site rather than on the first page fetch.
    /// </summary>
    /// <param name="route">The list route.</param>
    /// <param name="options">Per-call options.</param>
    /// <exception cref="ConfigException">The caller supplied an idempotency key.</exception>
    private static void AssertNoIdempotencyKey(RouteSpec route, RequestOptions? options)
    {
        if (options?.IdempotencyKey is null || route.Idempotent)
        {
            return;
        }

        throw new ConfigException(
            SdkErrorCodes.IdempotencyUnsupported,
            $"{route.Method} {route.Path} does not deduplicate by {SigningProtocol.Request.IdempotencyKey}; drop IdempotencyKey from this call",
            "IdempotencyKey");
    }

    private static Page<T> AsPage<T>(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array
            || !element.TryGetProperty("paginate", out var paginate) || paginate.ValueKind != JsonValueKind.Object)
        {
            throw new ContractException("expected an {items, paginate} list result", 200, element.ToString());
        }

        return OblodaiJson.Deserialize<Page<T>>(element);
    }

    private static JsonObject ToJsonObject(object? value)
    {
        if (value is null)
        {
            return [];
        }

        return JsonSerializer.SerializeToNode(value, value.GetType(), OblodaiJson.Options) as JsonObject ?? [];
    }

    private static int? TakeInt(JsonObject body, string name)
    {
        if (!body.TryGetPropertyValue(name, out var node) || node is null)
        {
            body.Remove(name);
            return null;
        }

        body.Remove(name);
        return ParseInt(name, node.ToJsonString());
    }

    private static int ParseInt(string name, string text)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new ConfigException(SdkErrorCodes.BadConfig, $"{name} must be a non-negative integer (got {text})", name);
        }

        return value;
    }

    private static string? FilenameFrom(string? contentDisposition)
    {
        if (string.IsNullOrEmpty(contentDisposition))
        {
            return null;
        }

        foreach (var part in contentDisposition.Split(';'))
        {
            var piece = part.Trim();
            if (piece.StartsWith("filename*=UTF-8''", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(piece["filename*=UTF-8''".Length..]);
            }

            if (piece.StartsWith("filename=", StringComparison.OrdinalIgnoreCase))
            {
                return piece["filename=".Length..].Trim('"');
            }
        }

        return null;
    }
}
