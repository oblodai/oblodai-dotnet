using System.Text;
using Oblodai.Contract;

namespace Oblodai;

/// <summary>An API key pair.</summary>
/// <param name="PublicId">Public id, sent as <c>X-Public-Id</c>.</param>
/// <param name="Secret">Secret used to sign.</param>
public sealed record Credentials(string PublicId, string Secret);

/// <summary>The outgoing request, fully materialized.</summary>
/// <param name="Url">Absolute URL to send to.</param>
/// <param name="Method">HTTP method.</param>
/// <param name="Headers">Headers, including the signature.</param>
/// <param name="Body">Body text, or null for GET.</param>
/// <param name="RequestUri">What was signed: path plus raw query.</param>
public sealed record BuiltRequest(
    string Url,
    string Method,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    string RequestUri);

/// <summary>Everything the builder needs; it touches neither the network nor the clock.</summary>
public sealed record BuildInput
{
    /// <summary>Gateway origin, optionally with a path prefix.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>The route being called.</summary>
    public required RouteSpec Route { get; init; }

    /// <summary>Values for the <c>{name}</c> segments of the route path.</summary>
    public IReadOnlyDictionary<string, string>? PathParams { get; init; }

    /// <summary>Query parameters in the order they should appear.</summary>
    public IReadOnlyList<KeyValuePair<string, string?>>? Query { get; init; }

    /// <summary>Already-serialized body; empty string for GET.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>Key pair to sign with, when the route is signed.</summary>
    public Credentials? Credentials { get; init; }

    /// <summary>Idempotency key to send and sign, when there is one.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Unix seconds; signed into <c>X-Timestamp</c>.</summary>
    public required long Ts { get; init; }

    /// <summary>Value of the <c>User-Agent</c> header.</summary>
    public required string UserAgent { get; init; }

    /// <summary>Caller headers; those colliding with signed or reserved names are dropped.</summary>
    public IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }
}

/// <summary>
/// Builds the outgoing request — URL, headers, body — as a pure function of its inputs, so the
/// signing material (what is signed) and the wire bytes (what is sent) come from one place and
/// cannot disagree.
/// </summary>
public static class RequestBuilder
{
    /// <summary>Headers the SDK owns; a caller-supplied header with one of these names is dropped.</summary>
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        RequestSigner.HeaderPublicId,
        RequestSigner.HeaderSignature,
        RequestSigner.HeaderTimestamp,
        RequestSigner.HeaderIdempotencyKey,
        "Content-Type",
        "Content-Length",
        "Host",
    };

    /// <summary>Materialize the request.</summary>
    /// <param name="input">What to build.</param>
    /// <exception cref="ConfigException">Missing credentials, or a path parameter that would rewrite the URL.</exception>
    public static BuiltRequest Build(BuildInput input)
    {
        var route = input.Route;
        var origin = new Uri(input.BaseUrl, UriKind.Absolute);
        var prefix = origin.AbsolutePath.TrimEnd('/');
        var path = prefix + FillPath(route.Path, input.PathParams);

        var query = new StringBuilder();
        foreach (var (key, value) in input.Query ?? [])
        {
            if (value is null)
            {
                continue;
            }

            query.Append(query.Length == 0 ? '?' : '&')
                .Append(Uri.EscapeDataString(key))
                .Append('=')
                .Append(Uri.EscapeDataString(value));
        }

        var requestUri = path + query;
        var url = $"{origin.Scheme}://{origin.Authority}{requestUri}";

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in input.ExtraHeaders ?? new Dictionary<string, string>())
        {
            if (!ReservedHeaders.Contains(key))
            {
                headers[key] = value;
            }
        }

        headers["Accept"] = "application/json";
        headers["User-Agent"] = input.UserAgent;

        var hasBody = route.Method != "GET";
        if (hasBody)
        {
            headers["Content-Type"] = "application/json";
        }

        if (!string.IsNullOrEmpty(input.IdempotencyKey))
        {
            headers[RequestSigner.HeaderIdempotencyKey] = input.IdempotencyKey!;
        }

        if (route.Auth is not (RouteAuth.Public or RouteAuth.Onboard))
        {
            if (input.Credentials is null)
            {
                var kind = route.Auth == RouteAuth.Any ? "merchant" : route.Auth.ToString().ToLowerInvariant();
                throw new ConfigException(
                    SdkErrorCodes.MissingCredentials,
                    $"{route.Method} {route.Path} needs a {kind} API key: set PublicId/Secret on OblodaiOptions "
                    + "or the OBLODAI_PUBLIC_ID / OBLODAI_SECRET environment variables");
            }

            headers[RequestSigner.HeaderPublicId] = input.Credentials.PublicId;
            headers[RequestSigner.HeaderTimestamp] = input.Ts.ToString();
            headers[RequestSigner.HeaderSignature] = RequestSigner.Sign(
                input.Credentials.Secret,
                input.Ts,
                route.Method,
                requestUri,
                input.IdempotencyKey,
                hasBody ? input.Body : string.Empty);
        }

        return new BuiltRequest(url, route.Method, headers, hasBody ? input.Body : null, requestUri);
    }

    /// <summary>
    /// Substitute <c>{name}</c> segments; every placeholder must be supplied, and values are
    /// percent-encoded and rejected when they could escape their segment.
    /// </summary>
    /// <param name="template">Route path template.</param>
    /// <param name="pathParams">Values by placeholder name.</param>
    public static string FillPath(string template, IReadOnlyDictionary<string, string>? pathParams)
    {
        if (!template.Contains('{'))
        {
            return template;
        }

        var output = new StringBuilder(template.Length);
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] != '{')
            {
                output.Append(template[i]);
                continue;
            }

            var end = template.IndexOf('}', i);
            if (end < 0)
            {
                output.Append(template[i]);
                continue;
            }

            var name = template[(i + 1)..end];
            i = end;
            var value = pathParams is not null && pathParams.TryGetValue(name, out var v) ? v : null;
            if (string.IsNullOrEmpty(value) || value == "." || value == ".." || value.Contains('/'))
            {
                throw new ConfigException(
                    SdkErrorCodes.BadPathParam,
                    $"path parameter \"{name}\" for {template} must be a non-empty single segment (got \"{value}\")",
                    name);
            }

            output.Append(Uri.EscapeDataString(value));
        }

        return output.ToString();
    }
}
