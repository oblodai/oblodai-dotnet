using System.Text.Json;
using System.Text.Json.Nodes;
using Oblodai.Contract;

namespace Oblodai.Resources;

/// <summary>
/// Shared plumbing for the resource namespaces: it turns a route plus a body into a transport call,
/// and a list route into a lazy <see cref="PagePromise{T}"/>.
/// </summary>
public abstract class Resource
{
    /// <summary>Bind the resource to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    protected Resource(OblodaiTransport transport) => Transport = transport;

    /// <summary>The transport every call goes through.</summary>
    protected OblodaiTransport Transport { get; }

    /// <summary>Call an envelope route and decode its result.</summary>
    /// <typeparam name="T">Model of the result payload.</typeparam>
    /// <param name="route">Route to call.</param>
    /// <param name="body">Request body, or null.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <param name="pathParams">Values for <c>{name}</c> path segments.</param>
    /// <param name="query">Query parameters.</param>
    protected Task<T> CallAsync<T>(
        RouteSpec route,
        object? body,
        RequestOptions? options,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? pathParams = null,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null)
        => Transport.CallAsync<T>(route, CallOptionsFrom(options, body, pathParams, query), cancellationToken);

    /// <summary>Call a paged POST list route; the filter's own <c>limit</c>/<c>offset</c> seed the window.</summary>
    /// <typeparam name="T">Item model.</typeparam>
    /// <param name="route">Route to call.</param>
    /// <param name="filter">Filter record, or null.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    protected PagePromise<T> PagedPost<T>(
        RouteSpec route,
        object? filter,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        AssertNoIdempotencyKey(route, options);
        var body = ToJsonObject(filter);
        var limit = TakeInt(body, "limit");
        var offset = TakeInt(body, "offset");
        var pageOptions = options;

        return new PagePromise<T>(
            async (pageLimit, pageOffset, token) =>
            {
                var pageBody = body.DeepClone().AsObject();
                pageBody["limit"] = pageLimit;
                pageBody["offset"] = pageOffset;
                var element = await Transport
                    .CallElementAsync(route, CallOptionsFrom(pageOptions, pageBody, null, null), token)
                    .ConfigureAwait(false);
                return AsPage<T>(element);
            },
            limit,
            offset,
            cancellationToken);
    }

    /// <summary>Call a paged GET list route, passing the window as query parameters.</summary>
    /// <typeparam name="T">Item model.</typeparam>
    /// <param name="route">Route to call.</param>
    /// <param name="page">Requested window.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    /// <param name="extraQuery">Query parameters besides the window.</param>
    protected PagePromise<T> PagedGet<T>(
        RouteSpec route,
        PageParams? page,
        RequestOptions? options,
        CancellationToken cancellationToken,
        IReadOnlyList<KeyValuePair<string, string?>>? extraQuery = null)
    {
        AssertNoIdempotencyKey(route, options);
        var pageOptions = options;
        return new PagePromise<T>(
            async (pageLimit, pageOffset, token) =>
            {
                var query = new List<KeyValuePair<string, string?>>(extraQuery ?? [])
                {
                    new("limit", pageLimit.ToString()),
                    new("offset", pageOffset.ToString()),
                };
                var element = await Transport
                    .CallElementAsync(route, CallOptionsFrom(pageOptions, null, null, query), token)
                    .ConfigureAwait(false);
                return AsPage<T>(element);
            },
            page?.Limit,
            page?.Offset,
            cancellationToken);
    }

    /// <summary>Call a plain list route (<c>{items}</c> without a paginate block).</summary>
    /// <typeparam name="T">Item model.</typeparam>
    /// <param name="route">Route to call.</param>
    /// <param name="body">Request body, or null.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    protected async Task<IReadOnlyList<T>> PlainListAsync<T>(
        RouteSpec route,
        object? body,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        var element = await Transport
            .CallElementAsync(route, CallOptionsFrom(options, body, null, null), cancellationToken)
            .ConfigureAwait(false);
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            throw new ContractException("expected an {items} list result", 200, element.ToString());
        }

        return OblodaiJson.Deserialize<List<T>>(items);
    }

    /// <summary>Call a bare route and return the bytes it answered with.</summary>
    /// <param name="route">Route to call.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <param name="pathParams">Values for <c>{name}</c> path segments.</param>
    /// <param name="query">Query parameters.</param>
    /// <param name="body">Request body for bare POST routes.</param>
    protected async Task<FileResult> FileAsync(
        RouteSpec route,
        RequestOptions? options,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? pathParams = null,
        IReadOnlyList<KeyValuePair<string, string?>>? query = null,
        object? body = null)
    {
        var raw = await Transport
            .CallRawAsync(route, CallOptionsFrom(options, body, pathParams, query), cancellationToken)
            .ConfigureAwait(false);
        return new FileResult(
            raw.Body,
            raw.ContentType ?? "application/octet-stream",
            FilenameFrom(raw.ContentDisposition));
    }

    /// <summary>Build the query list from name/value pairs, dropping the ones that are null.</summary>
    /// <param name="pairs">Candidate parameters.</param>
    protected static List<KeyValuePair<string, string?>> Query(params (string Name, string? Value)[] pairs)
        => pairs.Where(p => p.Value is not null).Select(p => new KeyValuePair<string, string?>(p.Name, p.Value)).ToList();

    /// <summary>
    /// A list route is read-only, so the gateway never deduplicates it by <c>Idempotency-Key</c>. Dropping
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
            $"{route.Method} {route.Path} does not deduplicate by Idempotency-Key; drop IdempotencyKey from this call",
            "IdempotencyKey");
    }

    private static CallOptions CallOptionsFrom(
        RequestOptions? options,
        object? body,
        IReadOnlyDictionary<string, string>? pathParams,
        IReadOnlyList<KeyValuePair<string, string?>>? query)
        => new()
        {
            Body = body,
            PathParams = pathParams,
            Query = query,
            IdempotencyKey = options?.IdempotencyKey,
            TimeoutMs = options?.TimeoutMs,
            DeadlineMs = options?.DeadlineMs,
            Headers = options?.Headers,
        };

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
            return null;
        }

        body.Remove(name);
        return node.GetValue<int>();
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
