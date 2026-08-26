using System.Globalization;
using Oblodai.Contract;
using Oblodai.Models;

namespace Oblodai.Resources;

/// <summary>
/// Generated PDF/CSV documents. Every method returns the bytes (<see cref="FileResult"/>); large
/// ranges go through asynchronous jobs (<see cref="CreateJobAsync"/> → <see cref="JobInfoAsync"/> →
/// <see cref="JobFileAsync"/>). <see cref="DownloadAsync"/> is the exception: it needs no credentials,
/// because the <c>document_url</c> carries its own signature.
/// </summary>
public sealed class Documents : Resource
{
    /// <summary>Bind the namespace to a transport.</summary>
    /// <param name="transport">The HTTP engine.</param>
    public Documents(OblodaiTransport transport)
        : base(transport)
    {
    }

    /// <summary>
    /// <c>POST /v1/documents/jobs</c> — queue a large report; poll <see cref="JobInfoAsync"/>, then
    /// <see cref="JobFileAsync"/>.
    /// </summary>
    /// <param name="request">Report kind, period, format and language.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<DocumentJob> CreateJobAsync(
        DocumentsJobsRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<DocumentJob>(Routes.PostV1DocumentsJobs, request, options, cancellationToken);

    /// <summary><c>POST /v1/documents/jobs/info</c>.</summary>
    /// <param name="jobId">Job id from the creation response.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<DocumentJob> JobInfoAsync(
        string jobId,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => CallAsync<DocumentJob>(
            Routes.PostV1DocumentsJobsInfo,
            new DocumentsJobsInfoRequest { JobId = jobId },
            options,
            cancellationToken);

    /// <summary><c>GET /v1/documents/jobs/file</c> — the finished job's bytes.</summary>
    /// <param name="jobId">Job id from the creation response.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<FileResult> JobFileAsync(
        string jobId,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => FileAsync(
            Routes.GetV1DocumentsJobsFile,
            options,
            cancellationToken,
            query: Query(("job_id", jobId)));

    /// <summary><c>GET /v1/documents/statement</c> — account statement for a period (PDF or CSV).</summary>
    /// <param name="query">Period, format and language.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<FileResult> StatementAsync(
        PeriodQuery? query = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => FileAsync(Routes.GetV1DocumentsStatement, options, cancellationToken, query: PeriodParams(query));

    /// <summary><c>GET /v1/documents/balance</c> — balance certificate (PDF).</summary>
    /// <param name="query">Language.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<FileResult> BalanceCertificateAsync(
        DocumentQuery? query = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => FileAsync(Routes.GetV1DocumentsBalance, options, cancellationToken, query: Query(("lang", query?.Lang)));

    /// <summary><c>GET /v1/documents/fees</c> — the fee schedule in force for the merchant (PDF).</summary>
    /// <param name="query">Language.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<FileResult> FeeScheduleAsync(
        DocumentQuery? query = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => FileAsync(Routes.GetV1DocumentsFees, options, cancellationToken, query: Query(("lang", query?.Lang)));

    /// <summary><c>GET /v1/documents/ledger</c> — full ledger export for a period (PDF or CSV).</summary>
    /// <param name="query">Period, format and language.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<FileResult> LedgerAsync(
        PeriodQuery? query = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => FileAsync(Routes.GetV1DocumentsLedger, options, cancellationToken, query: PeriodParams(query));

    /// <summary><c>GET /v1/documents/split</c> — how one payment was split between partners (PDF).</summary>
    /// <param name="paymentUuid">Invoice id, sent as the <c>uuid</c> query parameter.</param>
    /// <param name="query">Language.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<FileResult> SplitReportAsync(
        string paymentUuid,
        DocumentQuery? query = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => FileAsync(
            Routes.GetV1DocumentsSplit,
            options,
            cancellationToken,
            query: Query(("lang", query?.Lang), ("uuid", paymentUuid)));

    /// <summary><c>GET /v1/documents/batch</c> — per-row report of an asynchronous batch.</summary>
    /// <param name="batchId">Batch id, sent as the <c>uuid</c> query parameter.</param>
    /// <param name="query">Format and language.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<FileResult> BatchReportAsync(
        string batchId,
        FormatQuery? query = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => FileAsync(
            Routes.GetV1DocumentsBatch,
            options,
            cancellationToken,
            query: Query(("lang", query?.Lang), ("format", query?.Format), ("uuid", batchId)));

    /// <summary><c>GET /v1/documents/link</c> — payment-link report (its invoices).</summary>
    /// <param name="linkId">Payment link id, sent as the <c>uuid</c> query parameter.</param>
    /// <param name="query">Format and language.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<FileResult> LinkReportAsync(
        string linkId,
        FormatQuery? query = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => FileAsync(
            Routes.GetV1DocumentsLink,
            options,
            cancellationToken,
            query: Query(("lang", query?.Lang), ("format", query?.Format), ("uuid", linkId)));

    /// <summary><c>GET /v1/documents/wallet/statement</c> — static-wallet statement.</summary>
    /// <param name="walletUuid">Wallet id, sent as the <c>uuid</c> query parameter.</param>
    /// <param name="query">Period, format and language.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<FileResult> WalletStatementAsync(
        string walletUuid,
        PeriodQuery? query = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var query2 = PeriodParams(query);
        query2.Add(new KeyValuePair<string, string?>("uuid", walletUuid));
        return FileAsync(Routes.GetV1DocumentsWalletStatement, options, cancellationToken, query: query2);
    }

    /// <summary><c>GET /v1/documents/referrals</c> — referral earnings report.</summary>
    /// <param name="query">Period, format and language.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<FileResult> ReferralsReportAsync(
        PeriodQuery? query = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => FileAsync(Routes.GetV1DocumentsReferrals, options, cancellationToken, query: PeriodParams(query));

    /// <summary>
    /// <c>GET /v1/documents/{kind}/{id}</c> — a public document by its signed link (<c>exp</c> and
    /// <c>sig</c> come from a <c>document_url</c>). No credentials needed; prefer fetching the
    /// <c>document_url</c> directly.
    /// </summary>
    /// <param name="kind">Document kind from the URL, e.g. <c>"invoice"</c>.</param>
    /// <param name="id">Document id from the URL.</param>
    /// <param name="query">The <c>exp</c>/<c>sig</c> pair, and the language.</param>
    /// <param name="options">Per-call options.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    public Task<FileResult> DownloadAsync(
        string kind,
        string id,
        SignedDocumentQuery query,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
        => FileAsync(
            Routes.GetV1DocumentsKindId,
            options,
            cancellationToken,
            pathParams: new Dictionary<string, string> { ["kind"] = kind, ["id"] = id },
            query: Query(
                ("lang", query.Lang),
                ("exp", query.Exp.ToString(CultureInfo.InvariantCulture)),
                ("sig", query.Sig)));

    private static List<KeyValuePair<string, string?>> PeriodParams(PeriodQuery? query)
        => Query(
            ("lang", query?.Lang),
            ("format", query?.Format),
            ("from", query?.From),
            ("to", query?.To));
}
