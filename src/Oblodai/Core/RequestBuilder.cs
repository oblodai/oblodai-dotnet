using System.Text;
using System.Text.Json.Serialization;
using Oblodai.Contract;

namespace Oblodai;

/// <summary>
/// An API key pair. <see cref="Secret"/> never reaches a log: the compiler-generated
/// <c>ToString()</c> is overridden to print <c>[redacted]</c>, and the secret is not serialized.
/// </summary>
/// <param name="PublicId">Public id, sent as <c>X-Public-Id</c>.</param>
/// <param name="Secret">Secret used to sign; redacted in <c>ToString()</c> and never serialized.</param>
public sealed record Credentials(string PublicId, [property: JsonIgnore] string Secret)
{
    /// <summary>Prints the public id and <c>[redacted]</c> in place of the secret.</summary>
    /// <param name="builder">Buffer the record's <c>ToString()</c> writes into.</param>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("PublicId = ").Append(PublicId).Append(", Secret = ").Append(Redaction.Placeholder);
        return true;
    }
}

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

    /// <summary>Sent as <c>X-Request-ID</c>; not signed.</summary>
    public string? RequestId { get; init; }

    /// <summary>Unix seconds; signed into <c>X-Timestamp</c>.</summary>
    public required long Ts { get; init; }

    /// <summary>Value of the <c>User-Agent</c> header.</summary>
    public required string UserAgent { get; init; }

    /// <summary>Caller headers; those colliding with signed or reserved names are dropped.</summary>
    public IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }

    /// <summary>Admin token of a self-hosted gateway; attached to onboarding routes only.</summary>
    public string? AdminToken { get; init; }
}

/// <summary>
/// Builds the outgoing request — URL, headers, body — as a pure function of its inputs, so the
/// signing material (what is signed) and the wire bytes (what is sent) come from one place and
/// cannot disagree.
/// </summary>
public static class RequestBuilder
{
    /// <summary><c>X-Request-ID</c>: ties one call (all its attempts) to the gateway's logs.</summary>
    public const string HeaderRequestId = "X-Request-ID";

    /// <summary>
    /// Headers the SDK owns; a caller-supplied header with one of these names is dropped, compared
    /// case-insensitively. <c>X-Admin-Token</c> is here so it can only be attached by the transport,
    /// on onboarding routes — a caller header must never smuggle it onto a signed route.
    /// </summary>
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        RequestSigner.HeaderPublicId,
        RequestSigner.HeaderSignature,
        RequestSigner.HeaderTimestamp,
        RequestSigner.HeaderIdempotencyKey,
        RequestSigner.HeaderAdminToken,
        HeaderRequestId,
        "Accept",
        "User-Agent",
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
            AssertHeaderIsSendable(key, value);
            if (!ReservedHeaders.Contains(key))
            {
                headers[key] = value;
            }
        }

        headers["Accept"] = "application/json";
        headers["User-Agent"] = input.UserAgent;
        if (!string.IsNullOrEmpty(input.RequestId))
        {
            headers[HeaderRequestId] = input.RequestId!;
        }

        if (route.Auth == RouteAuth.Onboard && !string.IsNullOrEmpty(input.AdminToken))
        {
            headers[RequestSigner.HeaderAdminToken] = input.AdminToken!;
        }

        var hasBody = route.Method != "GET";
        if (hasBody)
        {
            headers["Content-Type"] = "application/json";
        }

        if (!string.IsNullOrEmpty(input.IdempotencyKey))
        {
            headers[RequestSigner.HeaderIdempotencyKey] = input.IdempotencyKey!;
        }

        if (route.Auth == RouteAuth.Key)
        {
            if (input.Credentials is null)
            {
                throw new ConfigException(
                    SdkErrorCodes.MissingCredentials,
                    $"{route.Method} {route.Path} needs the merchant's API key: set PublicId/Secret on "
                    + "OblodaiOptions or the OBLODAI_PUBLIC_ID / OBLODAI_SECRET environment variables");
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
    /// A caller header must be sendable verbatim: visible ASCII with ordinary spaces and tabs. A CR or
    /// LF would let one header value inject another (or a whole request) into the connection, and a
    /// non-ASCII byte is encoded differently by every HTTP stack — both are refused before signing.
    /// </summary>
    /// <param name="name">Header name.</param>
    /// <param name="value">Header value.</param>
    /// <exception cref="ConfigException">The name or value cannot be sent as it stands.</exception>
    public static void AssertHeaderIsSendable(string name, string value)
    {
        if (string.IsNullOrEmpty(name) || name.Any(c => c is < '!' or > '~'))
        {
            throw new ConfigException(
                SdkErrorCodes.BadHeader,
                $"header name \"{name}\" must be non-empty visible ASCII without spaces",
                "Headers");
        }

        foreach (var c in value ?? string.Empty)
        {
            if (c is '\r' or '\n' or > (char)126 || (c < ' ' && c != '\t'))
            {
                throw new ConfigException(
                    SdkErrorCodes.BadHeader,
                    $"header \"{name}\" has a value that cannot be sent verbatim (control or non-ASCII character)",
                    "Headers");
            }
        }
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
