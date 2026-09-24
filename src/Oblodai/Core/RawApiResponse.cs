namespace Oblodai;

/// <summary>
/// A successful call together with the answer it came from: status, headers and the request id —
/// what <see cref="OblodaiClient.WithRawResponseAsync{T}(Func{OblodaiClient, Task{T}})"/> returns.
/// An error status still throws, exactly as without it.
/// </summary>
/// <typeparam name="T">What the method returns without the raw response.</typeparam>
/// <param name="Value">The parsed result, as the method returns it.</param>
/// <param name="Response">The last successful HTTP answer of the call (for a list, its first page).</param>
public sealed record RawApiResponse<T>(T Value, RawResponse Response)
{
    /// <summary>HTTP status.</summary>
    public int Status => Response.Status;

    /// <summary>Response headers, case-insensitive.</summary>
    public IReadOnlyDictionary<string, string> Headers => Response.Headers;

    /// <summary>The response's <c>X-Request-ID</c>, else the one the SDK sent with the call.</summary>
    public string RequestId => Response.RequestId;

    /// <summary>One header, looked up case-insensitively.</summary>
    /// <param name="name">Header name.</param>
    public string? Header(string name) => Response.Headers.TryGetValue(name, out var value) ? value : null;

    /// <inheritdoc />
    public override string ToString() => $"RawApiResponse {{ Status = {Status}, RequestId = {RequestId} }}";
}

/// <summary>
/// Carries the raw answers of the calls made inside one
/// <see cref="OblodaiClient.WithRawResponseAsync{T}(Func{OblodaiClient, Task{T}})"/> scope. The box is
/// installed in an <see cref="AsyncLocal{T}"/> before the call starts, so the transport — running in the
/// same asynchronous flow — can fill it, while concurrent calls outside the scope never see it.
/// </summary>
internal static class RawCapture
{
    private static readonly AsyncLocal<Box?> Current = new();

    /// <summary>Remember a successful answer for the scope this flow runs in, if any.</summary>
    /// <param name="raw">The answer.</param>
    public static void Record(RawResponse raw)
    {
        if (Current.Value is { } box)
        {
            lock (box)
            {
                // The first answer wins: for a list it is the page the value was built from.
                box.Response ??= raw;
            }
        }
    }

    /// <summary>Run <paramref name="call"/> with a capture box installed and return what it caught.</summary>
    /// <typeparam name="T">Result of the call.</typeparam>
    /// <param name="call">The call.</param>
    public static async Task<RawApiResponse<T>> RunAsync<T>(Func<Task<T>> call)
    {
        var box = new Box();
        var previous = Current.Value;
        Current.Value = box;
        try
        {
            var value = await call().ConfigureAwait(false);
            var response = box.Response
                ?? throw new InvalidOperationException(
                    "no request was sent inside WithRawResponseAsync; call one client method there");
            return new RawApiResponse<T>(value, response);
        }
        finally
        {
            Current.Value = previous;
        }
    }

    private sealed class Box
    {
        public RawResponse? Response { get; set; }
    }
}
