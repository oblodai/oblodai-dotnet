namespace Oblodai;

/// <summary>
/// Idempotency keys. On create-type routes the gateway caches the first response per key for the
/// merchant and replays it on retries; a different body under the same key is a 409
/// <c>idempotency.key_reused</c>. The SDK generates a key once per logical call and reuses it on
/// every retry, so a timeout never turns into a double payout.
/// </summary>
public static class Idempotency
{
    /// <summary>Longest key the gateway accepts.</summary>
    public const int MaxKeyLength = 255;

    /// <summary>A fresh v4 UUID key from the platform CSPRNG.</summary>
    public static string NewKey() => Guid.NewGuid().ToString();

    /// <summary>Validate a caller-supplied key before it is signed and sent.</summary>
    /// <param name="key">The key.</param>
    /// <exception cref="ValidationException">The key could not be sent verbatim as a header value.</exception>
    public static void AssertValid(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw Invalid("IdempotencyKey must be a non-empty string");
        }

        if (key.Length > MaxKeyLength)
        {
            throw Invalid($"IdempotencyKey is too long (max {MaxKeyLength} chars)");
        }

        // Header values must be visible ASCII: the key is signed verbatim, so a stray control char or
        // surrounding whitespace would silently change the MAC on one side only.
        foreach (var c in key)
        {
            if (c is < '!' or > '~')
            {
                throw Invalid("IdempotencyKey must be printable ASCII without spaces");
            }
        }
    }

    private static ValidationException Invalid(string message) => new(new ApiErrorInit(
        SdkErrorCodes.BadIdempotencyKey,
        message,
        HttpStatus: 0,
        Retryable: false,
        Field: "IdempotencyKey"));
}
