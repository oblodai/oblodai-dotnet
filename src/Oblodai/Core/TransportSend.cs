using System.Net.Http.Headers;
using System.Text;
using Oblodai.Contract;

namespace Oblodai;

/// <summary>
/// The part of the transport that talks to the socket: one attempt, its timeout, the redirect check
/// and the capped read of the body. Split from the lifecycle above so the retry/skew policy and the
/// I/O it drives can each be read in one screen.
/// </summary>
public sealed partial class OblodaiTransport
{
    private async Task PauseAsync(OblodaiException error, int attempt, DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        var ms = RetryPolicy.DelayMs(error, attempt, _options.Retry);
        if (DateTimeOffset.UtcNow.AddMilliseconds(ms) > deadline)
        {
            throw new TransportException(
                SdkErrorCodes.TransportDeadline,
                $"retry would exceed the call deadline; last error: {error.Message}",
                error);
        }

        if (ms <= 0)
        {
            return;
        }

        // A cancellation during the pause is the caller's own: it propagates as itself, so the awaiting
        // code sees the OperationCanceledException it registered the token for.
        await Task.Delay(ms, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(RawResponse Raw, HttpResponseHeaders Headers)> SendAsync(
        RouteSpec route,
        BuiltRequest request,
        CallOptions options,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        var budget = (int)Math.Max(1, Math.Min((deadline - DateTimeOffset.UtcNow).TotalMilliseconds, int.MaxValue));
        var timeoutMs = Math.Min(options.TimeoutMs ?? _options.TimeoutMs, budget);

        using var timeoutSource = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        if (request.Body is not null)
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
        }

        foreach (var (key, value) in request.Headers)
        {
            if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue; // set on the content above
            }

            message.Headers.TryAddWithoutValidation(key, value);
        }

        var sentTo = message.RequestUri;
        try
        {
            // ResponseHeadersRead, so the size cap is applied while the body streams in rather than after
            // an unbounded buffer has already been allocated. The linked token covers the whole read.
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                .ConfigureAwait(false);

            AssertNoRedirectWasFollowed(sentTo, response);

            var bytes = await ReadCappedAsync(response, CapFor(route), linked.Token).ConfigureAwait(false);
            var raw = new RawResponse(
                (int)response.StatusCode,
                bytes,
                response.Content.Headers.ContentType?.ToString(),
                response.Content.Headers.ContentDisposition?.ToString());
            return (raw, response.Headers);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            // The caller's own cancellation, kept as itself: `catch (OperationCanceledException)` and
            // ASP.NET's request-abort handling must see the cancellation they asked for, not an SDK error.
            _ = ex;
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new TransportException(SdkErrorCodes.TransportTimeout, $"request timed out after {timeoutMs} ms", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new TransportException(SdkErrorCodes.TransportNetwork, $"network error: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new TransportException(SdkErrorCodes.TransportNetwork, $"network error: {ex.Message}", ex);
        }
    }

    /// <summary>The cap this route's body may not exceed.</summary>
    private static long CapFor(RouteSpec route)
        => route.Bare ? MaxBareResponseBytes : MaxJsonResponseBytes;

    /// <summary>
    /// The SDK's own client never follows a redirect, but an injected one (an <c>IHttpClientFactory</c>
    /// client, a test double) may — and a followed redirect sends the signed headers, the idempotency key
    /// and the body to a host the caller never named. The final URI is compared with the one sent.
    /// </summary>
    private static void AssertNoRedirectWasFollowed(Uri? sentTo, HttpResponseMessage response)
    {
        var landedOn = response.RequestMessage?.RequestUri;
        if (sentTo is null || landedOn is null || landedOn == sentTo)
        {
            return;
        }

        throw ApiExceptionFactory.Create(
            (int)response.StatusCode,
            new ErrorDetail
            {
                Code = "internal",
                Message = $"unexpected redirect to {landedOn.GetLeftPart(UriPartial.Path)}; the HTTP client followed it "
                          + "and carried the signed headers to another URL — configure it with AllowAutoRedirect = false",
            },
            raw: null,
            synthetic: true);
    }

    /// <summary>Reads at most <paramref name="cap"/> bytes, then refuses instead of growing the buffer.</summary>
    private static async Task<byte[]> ReadCappedAsync(HttpResponseMessage response, long cap, CancellationToken cancellationToken)
    {
        var declared = response.Content.Headers.ContentLength;
        if (declared > cap)
        {
            throw TooLarge(declared.Value, cap);
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream(declared is > 0 and <= 1 << 20 ? (int)declared.Value : 0);
        var chunk = new byte[81_920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > cap)
            {
                throw TooLarge(buffer.Length + read, cap);
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static ContractException TooLarge(long seen, long cap)
        => new(
            SdkErrorCodes.ResponseTooLarge,
            $"response body exceeds the {cap / (1024 * 1024)} MiB the SDK buffers (at least {seen} bytes) — "
            + "fetch it with a narrower filter or a document job",
            httpStatus: 0);
}
