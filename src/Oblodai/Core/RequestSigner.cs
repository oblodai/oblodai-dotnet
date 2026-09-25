using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Oblodai.Contract;

namespace Oblodai;

/// <summary>
/// Request signing — the exact recipe the gateway verifies, taken from the contract
/// (<see cref="SigningProtocol"/>, generated from <c>x-oblodai-signing</c>):
/// <code>
/// canonical = the parts of SigningProtocol.Request.CanonicalParts joined by SigningProtocol.Request.Separator
///             (ts, METHOD, request_uri, idempotency_key, body)
/// signature = hex(HMAC-SHA256(secret, canonical))
/// </code>
/// <list type="bullet">
/// <item><description><c>ts</c> is unix seconds; the gateway accepts <see cref="SigningProtocol.SkewSeconds"/> of skew either way.</description></item>
/// <item><description><c>request_uri</c> is path plus raw query (<c>/v1/x?limit=1</c>), never the origin.</description></item>
/// <item><description>The idempotency slot is the empty string when no idempotency key header is sent.</description></item>
/// <item><description><c>body</c> is the byte-exact request body; GETs sign an empty body.</description></item>
/// </list>
/// Pure: no clock, no I/O.
/// </summary>
public static class RequestSigner
{
    /// <summary>Header of the public id: <see cref="SigningProtocol.Request.PublicId"/>.</summary>
    public const string HeaderPublicId = SigningProtocol.Request.PublicId;

    /// <summary>Header of the signature: <see cref="SigningProtocol.Request.Signature"/>.</summary>
    public const string HeaderSignature = SigningProtocol.Request.Signature;

    /// <summary>Header of the timestamp (unix seconds): <see cref="SigningProtocol.Request.Timestamp"/>.</summary>
    public const string HeaderTimestamp = SigningProtocol.Request.Timestamp;

    /// <summary>Header of the idempotency key: <see cref="SigningProtocol.Request.IdempotencyKey"/>.</summary>
    public const string HeaderIdempotencyKey = SigningProtocol.Request.IdempotencyKey;

    /// <summary><c>X-Admin-Token</c>, sent on merchant-provisioning routes only.</summary>
    public const string HeaderAdminToken = "X-Admin-Token";

    /// <summary>Clock skew the gateway tolerates, in seconds: <see cref="SigningProtocol.SkewSeconds"/>.</summary>
    public const int SignatureSkewSeconds = SigningProtocol.SkewSeconds;

    /// <summary>The string that gets signed. Exposed for debugging signature mismatches.</summary>
    /// <param name="ts">Unix timestamp in seconds, as sent in the <see cref="HeaderTimestamp"/> header.</param>
    /// <param name="method">HTTP method (upper-cased here).</param>
    /// <param name="requestUri">Path plus raw query, exactly as the request line carries it.</param>
    /// <param name="idempotencyKey">Value of the <see cref="HeaderIdempotencyKey"/> header; empty when absent.</param>
    /// <param name="body">Request body as sent; empty for GET.</param>
    public static string CanonicalString(long ts, string method, string requestUri, string? idempotencyKey, string body)
        => Encoding.UTF8.GetString(RequestCanonical(ts, method, requestUri, idempotencyKey, Encoding.UTF8.GetBytes(body)));

    /// <summary>Lower-case hex HMAC-SHA256 of the canonical string.</summary>
    /// <param name="secret">API key secret.</param>
    /// <param name="ts">Unix timestamp in seconds.</param>
    /// <param name="method">HTTP method.</param>
    /// <param name="requestUri">Path plus raw query.</param>
    /// <param name="idempotencyKey">Idempotency key, or null/empty when absent.</param>
    /// <param name="body">Request body bytes as text; empty for GET.</param>
    public static string Sign(string secret, long ts, string method, string requestUri, string? idempotencyKey, string body)
        => Sign(secret, ts, method, requestUri, idempotencyKey, Encoding.UTF8.GetBytes(body));

    /// <summary>Lower-case hex HMAC-SHA256 over the exact body bytes.</summary>
    /// <param name="secret">API key secret.</param>
    /// <param name="ts">Unix timestamp in seconds.</param>
    /// <param name="method">HTTP method.</param>
    /// <param name="requestUri">Path plus raw query.</param>
    /// <param name="idempotencyKey">Idempotency key, or null/empty when absent.</param>
    /// <param name="body">Request body bytes.</param>
    public static string Sign(string secret, long ts, string method, string requestUri, string? idempotencyKey, ReadOnlySpan<byte> body)
        => Mac(secret, RequestCanonical(ts, method, requestUri, idempotencyKey, body));

    /// <summary>
    /// Webhook signature: <c>hex(HMAC-SHA256(secret, canonical))</c>, the canonical string being the parts of
    /// <see cref="SigningProtocol.Webhook.CanonicalParts"/> (<c>ts</c>, <c>payload</c>) joined by
    /// <see cref="SigningProtocol.Webhook.Separator"/>. The payload is signed verbatim, so verifiers must
    /// use the raw request bytes, never a re-encoded parse of them.
    /// </summary>
    /// <param name="secret">Endpoint secret.</param>
    /// <param name="ts">Delivery timestamp, unix seconds.</param>
    /// <param name="payload">Raw delivery body.</param>
    public static string SignWebhook(string secret, long ts, ReadOnlySpan<byte> payload)
    {
        using var canonical = new MemoryStream();
        var separator = Encoding.UTF8.GetBytes(SigningProtocol.Webhook.Separator);
        for (var i = 0; i < SigningProtocol.Webhook.CanonicalParts.Count; i++)
        {
            if (i > 0)
            {
                canonical.Write(separator);
            }

            switch (SigningProtocol.Webhook.CanonicalParts[i])
            {
                case "ts":
                    canonical.Write(Encoding.UTF8.GetBytes(ts.ToString(CultureInfo.InvariantCulture)));
                    break;
                case "payload":
                    canonical.Write(payload);
                    break;
                default:
                    throw UnknownPart(SigningProtocol.Webhook.CanonicalParts[i]);
            }
        }

        return Mac(secret, canonical.ToArray());
    }

    /// <summary>Webhook signature over a text payload.</summary>
    /// <param name="secret">Endpoint secret.</param>
    /// <param name="ts">Delivery timestamp, unix seconds.</param>
    /// <param name="payload">Raw delivery body as text.</param>
    public static string SignWebhook(string secret, long ts, string payload)
        => SignWebhook(secret, ts, Encoding.UTF8.GetBytes(payload));

    /// <summary>The request canonical string as bytes: the contract's parts in its order.</summary>
    private static byte[] RequestCanonical(long ts, string method, string requestUri, string? idempotencyKey, ReadOnlySpan<byte> body)
    {
        using var canonical = new MemoryStream();
        var separator = Encoding.UTF8.GetBytes(SigningProtocol.Request.Separator);
        for (var i = 0; i < SigningProtocol.Request.CanonicalParts.Count; i++)
        {
            if (i > 0)
            {
                canonical.Write(separator);
            }

            var part = SigningProtocol.Request.CanonicalParts[i];
            if (part == "body")
            {
                canonical.Write(body);
                continue;
            }

            canonical.Write(Encoding.UTF8.GetBytes(part switch
            {
                "ts" => ts.ToString(CultureInfo.InvariantCulture),
                "METHOD" => method.ToUpperInvariant(),
                "request_uri" => requestUri,
                "idempotency_key" => idempotencyKey ?? string.Empty,
                _ => throw UnknownPart(part),
            }));
        }

        return canonical.ToArray();
    }

    private static string Mac(string secret, byte[] canonical)
        => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), canonical)).ToLowerInvariant();

    /// <summary>A canonical part this runtime does not know: the contract grew, the SDK must follow.</summary>
    private static InvalidOperationException UnknownPart(string part)
        => new($"the contract's canonical string has a part \"{part}\" this SDK does not know; upgrade the SDK");
}
