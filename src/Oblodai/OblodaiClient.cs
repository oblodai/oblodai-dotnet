using System.Runtime.InteropServices;
using Oblodai.Contract;
using Oblodai.Resources;

namespace Oblodai;

/// <summary>
/// The Oblodai API client. One instance per key pair; safe to share across requests and threads.
/// <code>
/// var oblodai = new OblodaiClient(new OblodaiOptions { PublicId = "…", Secret = "…" });
/// var invoice = await oblodai.Payments.CreateAsync(new PaymentRequest
/// {
///     Amount = "25", Currency = "USDT", Network = Network.Tron, OrderId = "order-1",
/// });
/// </code>
/// </summary>
public sealed class OblodaiClient : IDisposable
{
    /// <summary>Version of this SDK.</summary>
    public const string SdkVersion = "1.3.0";

    /// <summary>Build a client from options (each field falls back to its environment variable).</summary>
    /// <param name="options">Credentials and behaviour; the environment supplies whatever is left unset.</param>
    /// <param name="httpClient">
    /// An externally managed <see cref="HttpClient"/> — pass the one an <c>IHttpClientFactory</c> hands
    /// you. Configure it not to follow redirects; the SDK applies its own per-attempt timeouts, so leave
    /// <see cref="HttpClient.Timeout"/> generous.
    /// </param>
    public OblodaiClient(OblodaiOptions? options = null, HttpClient? httpClient = null)
    {
        var resolved = (options ?? new OblodaiOptions()).Resolve();
        Transport = new OblodaiTransport(
            new TransportOptions
            {
                BaseUrl = resolved.BaseUrl,
                Credentials = resolved.Credentials,
                PayoutCredentials = resolved.PayoutCredentials,
                TimeoutMs = resolved.TimeoutMs ?? 30_000,
                DeadlineMs = resolved.DeadlineMs ?? 90_000,
                Retry = resolved.Retry ?? RetryOptions.Default,
                Clock = resolved.Clock,
                Logger = resolved.Logger,
                Headers = resolved.Headers,
                AdminToken = resolved.AdminToken,
                UserAgent = $"oblodai-dotnet/{SdkVersion} (contract {ContractVersion.Hash[..12]}; "
                            + $"{RuntimeInformation.FrameworkDescription})",
            },
            httpClient);

        Payments = new Payments(Transport);
        Refunds = new Refunds(Transport);
        Payouts = new Payouts(Transport);
        PayoutLinks = new PayoutLinks(Transport);
        PaymentLinks = new PaymentLinks(Transport);
        Batches = new Batches(Transport);
        Transfers = new Transfers(Transport);
        Wallets = new Wallets(Transport);
        Webhooks = new Webhooks(Transport);
        Documents = new Documents(Transport);
        Splits = new Splits(Transport);
        Settings = new Settings(Transport);
        Account = new Account(Transport);
        Catalog = new Catalog(Transport);
        Sandbox = new Sandbox(Transport);
        Merchants = new Merchants(Transport);
    }

    /// <summary>Invoices: create, look up, cancel, list, and the payer-facing checkout endpoints.</summary>
    public Payments Payments { get; }

    /// <summary>Refunds and underpayment resolutions.</summary>
    public Refunds Refunds { get; }

    /// <summary>Outgoing transfers to external addresses.</summary>
    public Payouts Payouts { get; }

    /// <summary>Payout links (cheques): funds reserved now, claimed later by whoever holds the token.</summary>
    public PayoutLinks PayoutLinks { get; }

    /// <summary>Reusable payment links (tip jars, price tags).</summary>
    public PaymentLinks PaymentLinks { get; }

    /// <summary>Progress of asynchronous batches.</summary>
    public Batches Batches { get; }

    /// <summary>Internal, instant, fee-free moves between platform balances.</summary>
    public Transfers Transfers { get; }

    /// <summary>Static deposit wallets.</summary>
    public Wallets Wallets { get; }

    /// <summary>Webhook endpoint management and delivery inspection.</summary>
    public Webhooks Webhooks { get; }

    /// <summary>Generated PDF/CSV documents.</summary>
    public Documents Documents { get; }

    /// <summary>Revenue splits.</summary>
    public Splits Splits { get; }

    /// <summary>Merchant-level configuration exposed over the API.</summary>
    public Settings Settings { get; }

    /// <summary>Balances and account-level facts.</summary>
    public Account Account { get; }

    /// <summary>Public reference data — no credentials needed.</summary>
    public Catalog Catalog { get; }

    /// <summary>Developer sandbox: fake money, simulated deposits, webhook inspector.</summary>
    public Sandbox Sandbox { get; }

    /// <summary>Merchant provisioning, for platforms that onboard merchants themselves.</summary>
    public Merchants Merchants { get; }

    /// <summary>The transport, exposed for advanced use (custom routes, tests).</summary>
    public OblodaiTransport Transport { get; }

    /// <inheritdoc />
    public void Dispose() => Transport.Dispose();
}
