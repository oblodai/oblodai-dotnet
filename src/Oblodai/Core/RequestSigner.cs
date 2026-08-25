using System.Security.Cryptography;
using System.Text;

namespace Oblodai;

/// <summary>
/// Request signing — the exact recipe the gateway verifies:
/// <code>
/// canonical = ts "\n" METHOD "\n" requestUri "\n" idempotencyKey "\n" body
/// signature = hex(HMAC-SHA256(secret, canonical))
/// </code>
/// <list type="bullet">
/// <item><description><c>ts</c> is unix seconds; the gateway accepts ±300 s of skew.</description></item>
/// <item><description><c>requestUri</c> is path plus raw query (<c>/v1/x?limit=1</c>), never the origin.</description></item>
/// <item><description>The idempotency slot is the empty string when no <c>Idempotency-Key</c> header is sent.</description></item>
/// <item><description><c>body</c> is the byte-exact request body; GETs sign an empty body.</description></item>
/// </list>
/// Pure: no clock, no I/O.
/// </summary>
public static class RequestSigner
{
    /// <summary><c>X-Public-Id</c>.</summary>
    public const string HeaderPublicId = "X-Public-Id";

    /// <summary><c>X-Signature</c>.</summary>
    public const string HeaderSignature = "X-Signature";

    /// <summary><c>X-Timestamp</c> (unix seconds).</summary>
    public const string HeaderTimestamp = "X-Timestamp";

    /// <summary><c>Idempotency-Key</c>.</summary>
    public const string HeaderIdempotencyKey = "Idempotency-Key";

    /// <summary><c>X-Admin-Token</c>, sent on merchant-provisioning routes only.</summary>
    public const string HeaderAdminToken = "X-Admin-Token";

    /// <summary>Clock skew the gateway tolerates, in seconds.</summary>
    public const int SignatureSkewSeconds = 300;

    /// <summary>The string that gets signed. Exposed for debugging signature mismatches.</summary>
    /// <param name="ts">Unix timestamp in seconds, as sent in <c>X-Timestamp</c>.</param>
    /// <param name="method">HTTP method (upper-cased here).</param>
    /// <param name="requestUri">Path plus raw query, exactly as the request line carries it.</param>
    /// <param name="idempotencyKey">Value of the <c>Idempotency-Key</c> header; empty when absent.</param>
    /// <param name="body">Request body as sent; empty for GET.</param>
    public static string CanonicalString(long ts, string method, string requestUri, string? idempotencyKey, string body)
        => $"{ts}\n{method.ToUpperInvariant()}\n{requestUri}\n{idempotencyKey ?? string.Empty}\n{body}";

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
    {
        var prefix = Encoding.UTF8.GetBytes(
            $"{ts}\n{method.ToUpperInvariant()}\n{requestUri}\n{idempotencyKey ?? string.Empty}\n");
        var buffer = new byte[prefix.Length + body.Length];
        prefix.CopyTo(buffer, 0);
        body.CopyTo(buffer.AsSpan(prefix.Length));
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), buffer);
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    /// <summary>
    /// Webhook signature: <c>hex(HMAC-SHA256(secret, "&lt;unix ts&gt;." + payload))</c>. The payload is
    /// signed verbatim, so verifiers must use the raw request bytes, never a re-encoded parse of them.
    /// </summary>
    /// <param name="secret">Endpoint secret.</param>
    /// <param name="ts">Delivery timestamp, unix seconds.</param>
    /// <param name="payload">Raw delivery body.</param>
    public static string SignWebhook(string secret, long ts, ReadOnlySpan<byte> payload)
    {
        var prefix = Encoding.UTF8.GetBytes($"{ts}.");
        var buffer = new byte[prefix.Length + payload.Length];
        prefix.CopyTo(buffer, 0);
        payload.CopyTo(buffer.AsSpan(prefix.Length));
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), buffer);
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    /// <summary>Webhook signature over a text payload.</summary>
    /// <param name="secret">Endpoint secret.</param>
    /// <param name="ts">Delivery timestamp, unix seconds.</param>
    /// <param name="payload">Raw delivery body as text.</param>
    public static string SignWebhook(string secret, long ts, string payload)
        => SignWebhook(secret, ts, Encoding.UTF8.GetBytes(payload));
}
