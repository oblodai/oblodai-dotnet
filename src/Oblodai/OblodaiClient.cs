using System.Runtime.InteropServices;

namespace Oblodai;

/// <summary>
/// The Oblodai API client. One instance per API key; safe to share across requests and threads.
/// <code>
/// using var oblodai = new OblodaiClient(new OblodaiOptions { PublicId = "…", Secret = "…" });
/// var invoice = await oblodai.Payments.CreateAsync(amount: 25m, currency: "USDT", orderId: "order-1");
/// </code>
/// Every method takes its request as named arguments (or as the request model), then
/// <see cref="RequestOptions"/> and a <see cref="CancellationToken"/>. The resource properties
/// (<c>Payments</c>, <c>Payouts</c>, …) are generated, one per resource of the contract
/// (<c>Generated/Client.g.cs</c>).
/// </summary>
public sealed partial class OblodaiClient : IDisposable
{
    /// <summary>Version of this SDK.</summary>
    public const string SdkVersion = "2.0.0";

    private readonly OblodaiOptions _options;

    /// <summary>Build a client from options (each field falls back to its environment variable).</summary>
    /// <param name="options">Credentials and behaviour; the environment supplies whatever is left unset.</param>
    /// <param name="httpClient">
    /// An externally managed <see cref="HttpClient"/> — pass the one an <c>IHttpClientFactory</c> hands
    /// you. Configure it not to follow redirects; the SDK applies its own per-attempt timeouts, so leave
    /// <see cref="HttpClient.Timeout"/> infinite.
    /// </param>
    public OblodaiClient(OblodaiOptions? options = null, HttpClient? httpClient = null)
        : this(options ?? new OblodaiOptions(), httpClient, clock: null)
    {
    }

    private OblodaiClient(OblodaiOptions options, HttpClient? httpClient, SkewCorrectingClock? clock)
    {
        _options = options;
        var resolved = options.Resolve();
        Transport = new OblodaiTransport(
            new TransportOptions
            {
                BaseUrl = resolved.BaseUrl,
                Credentials = resolved.Credentials,
                Timeout = resolved.Timeout ?? TimeSpan.FromSeconds(30),
                Deadline = resolved.Deadline ?? TimeSpan.FromSeconds(90),
                Retry = resolved.Retry ?? RetryOptions.Default,
                Hooks = resolved.Hooks,
                TimeProvider = resolved.TimeProvider ?? TimeProvider.System,
                Clock = clock ?? resolved.Clock,
                Logger = resolved.Logger,
                Headers = resolved.Headers,
                AdminToken = resolved.AdminToken,
                UserAgent = $"oblodai-dotnet/{SdkVersion} ({RuntimeInformation.FrameworkDescription})",
            },
            httpClient);

        CreateResources(Transport);
    }

    /// <summary>The transport, exposed for advanced use (custom routes, tests).</summary>
    public OblodaiTransport Transport { get; }

    /// <summary>
    /// A client with some options changed — <c>client.WithOptions(o =&gt; o with { Timeout = TimeSpan.FromSeconds(5) })</c>.
    /// It shares this client's HTTP connection pool and clock correction; dispose the original, not the copy.
    /// </summary>
    /// <param name="change">Returns the changed options from the current ones.</param>
    public OblodaiClient WithOptions(Func<OblodaiOptions, OblodaiOptions> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var changed = change(_options) ?? throw new ArgumentException("the change returned null", nameof(change));
        return new OblodaiClient(changed, Transport.HttpClient, changed.Clock ?? Transport.Clock);
    }

    /// <summary>
    /// Run one call and return its result together with the raw answer: HTTP status, headers and the
    /// request id — <c>var raw = await client.WithRawResponseAsync(c =&gt; c.Payments.CreateAsync(…));</c>.
    /// An error status still throws, exactly as without it.
    /// </summary>
    /// <typeparam name="T">Result of the call.</typeparam>
    /// <param name="call">One call on this client.</param>
    public Task<RawApiResponse<T>> WithRawResponseAsync<T>(Func<OblodaiClient, Task<T>> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return RawCapture.RunAsync(() => call(this));
    }

    /// <summary>The first page of a list call together with the raw answer it came from.</summary>
    /// <typeparam name="T">Item model.</typeparam>
    /// <param name="call">One list call on this client.</param>
    public Task<RawApiResponse<Page<T>>> WithRawResponseAsync<T>(Func<OblodaiClient, PagePromise<T>> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return RawCapture.RunAsync(() => call(this).FirstPageAsync());
    }

    /// <inheritdoc />
    public void Dispose() => Transport.Dispose();
}
