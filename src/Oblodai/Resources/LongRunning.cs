using Oblodai.Contract;

namespace Oblodai
{
    /// <summary>
    /// Which operations are long-running and how to follow them — a decision of this SDK, not of the
    /// API, so the generator knows nothing of it. A create call listed in <see cref="Operations"/>
    /// answers with an acknowledgement; the matching <c>WaitAsync</c>
    /// (<see cref="Resources.Batches.WaitAsync(BatchSubmitResponse, TimeSpan?, TimeSpan?, CancellationToken)"/>,
    /// <see cref="Resources.Documents.WaitAsync(DocumentJobAccepted, TimeSpan?, TimeSpan?, CancellationToken)"/>)
    /// polls the operation named here until the status is terminal.
    /// </summary>
    public static class LongRunning
    {
        /// <summary>How long to wait between two polls unless told otherwise.</summary>
        public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

        /// <summary>How long a wait lasts at most unless told otherwise.</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

        /// <summary>Create <c>operationId</c> → the <c>operationId</c> that polls it.</summary>
        public static IReadOnlyDictionary<string, string> Operations { get; } = new Dictionary<string, string>
        {
            ["createPaymentBatch"] = "getBatchInfo",
            ["createPayoutBatch"] = "getBatchInfo",
            ["createRefundBatch"] = "getBatchInfo",
            ["createTransferBatch"] = "getBatchInfo",
            ["createDocumentJob"] = "getDocumentJob",
        };

        /// <summary>
        /// Statuses after which a job no longer changes: a batch ends <c>completed</c> or <c>stopped</c>
        /// (<c>on_error=stop</c>), a document job <c>done</c>, <c>failed</c> or <c>expired</c>.
        /// </summary>
        public static IReadOnlySet<string> TerminalStatuses { get; } =
            new HashSet<string>(StringComparer.Ordinal) { "completed", "stopped", "done", "failed", "expired" };

        /// <summary>
        /// Poll until <paramref name="status"/> of the answer is terminal and return that answer — a
        /// terminal failure is returned, not thrown, so a failed job is inspected like a finished one.
        /// </summary>
        /// <typeparam name="T">The poll answer.</typeparam>
        /// <param name="poll">One poll.</param>
        /// <param name="status">The status of an answer.</param>
        /// <param name="time">Time source for the pauses and the timeout.</param>
        /// <param name="pollInterval">Pause between polls; <see cref="DefaultPollInterval"/> when null.</param>
        /// <param name="timeout">Longest wait; <see cref="DefaultTimeout"/> when null.</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <exception cref="TransportException"><c>sdk.wait_timeout</c>: still running after <paramref name="timeout"/>.</exception>
        public static async Task<T> PollAsync<T>(
            Func<CancellationToken, Task<T>> poll,
            Func<T, string> status,
            TimeProvider time,
            TimeSpan? pollInterval,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            var interval = pollInterval ?? DefaultPollInterval;
            var limit = timeout ?? DefaultTimeout;
            if (interval < TimeSpan.Zero || limit <= TimeSpan.Zero)
            {
                throw new ConfigException(
                    SdkErrorCodes.BadConfig, "pollInterval must not be negative and timeout must be positive", "timeout");
            }

            var deadline = time.GetUtcNow() + limit;
            while (true)
            {
                var answer = await poll(cancellationToken).ConfigureAwait(false);
                var current = status(answer);
                if (TerminalStatuses.Contains(current))
                {
                    return answer;
                }

                if (time.GetUtcNow() + interval > deadline)
                {
                    throw new TransportException(
                        SdkErrorCodes.WaitTimeout,
                        $"the operation is still {current} after {limit}; wait again or poll it yourself");
                }

                await Task.Delay(interval, time, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>The route that polls <paramref name="createOperationId"/>, from the generated table.</summary>
        /// <param name="createOperationId">The create call's <c>operationId</c>.</param>
        internal static RouteSpec PollRoute(string createOperationId)
            => Resources.Routes.All[Operations[createOperationId]];
    }
}

namespace Oblodai.Resources
{
    /// <summary>Waiting for a batch to finish.</summary>
    public sealed partial class Batches
    {
        /// <summary>
        /// Poll <see cref="GetInfoAsync(string, long?, long?, RequestOptions?, CancellationToken)"/> until
        /// the batch is <c>completed</c> or <c>stopped</c>, and return the last answer.
        /// </summary>
        /// <param name="submitted">What <c>Create*Async</c> answered.</param>
        /// <param name="pollInterval">Pause between polls (2 s by default).</param>
        /// <param name="timeout">Longest wait (10 min by default); past it <c>sdk.wait_timeout</c> is thrown.</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        public Task<BatchInfoResponse> WaitAsync(
            BatchSubmitResponse submitted,
            TimeSpan? pollInterval = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(submitted);
            return WaitAsync(submitted.BatchId, pollInterval, timeout, cancellationToken);
        }

        /// <summary>Poll a batch by id until it is <c>completed</c> or <c>stopped</c>.</summary>
        /// <param name="batchId">The batch id.</param>
        /// <param name="pollInterval">Pause between polls (2 s by default).</param>
        /// <param name="timeout">Longest wait (10 min by default).</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        public Task<BatchInfoResponse> WaitAsync(
            string batchId,
            TimeSpan? pollInterval = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(batchId);
            return LongRunning.PollAsync(
                token => GetInfoAsync(batchId, cancellationToken: token),
                answer => answer.Status.Value,
                Transport.Options.TimeProvider,
                pollInterval,
                timeout,
                cancellationToken);
        }
    }

    /// <summary>Waiting for a document job and downloading its file.</summary>
    public sealed partial class Documents
    {
        /// <summary>
        /// Poll <see cref="GetJobAsync(string, RequestOptions?, CancellationToken)"/> until the job is
        /// <c>done</c>, <c>failed</c> or <c>expired</c>, and return the last answer.
        /// </summary>
        /// <param name="accepted">What <see cref="CreateJobAsync(DocumentJobRequest, RequestOptions?, CancellationToken)"/> answered.</param>
        /// <param name="pollInterval">Pause between polls (2 s by default).</param>
        /// <param name="timeout">Longest wait (10 min by default); past it <c>sdk.wait_timeout</c> is thrown.</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        public Task<DocumentJobView> WaitAsync(
            DocumentJobAccepted accepted,
            TimeSpan? pollInterval = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(accepted);
            return WaitAsync(accepted.JobId, pollInterval, timeout, cancellationToken);
        }

        /// <summary>Poll a document job by id until it is <c>done</c>, <c>failed</c> or <c>expired</c>.</summary>
        /// <param name="jobId">The job id.</param>
        /// <param name="pollInterval">Pause between polls (2 s by default).</param>
        /// <param name="timeout">Longest wait (10 min by default).</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        public Task<DocumentJobView> WaitAsync(
            string jobId,
            TimeSpan? pollInterval = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(jobId);
            return LongRunning.PollAsync(
                token => GetJobAsync(jobId, cancellationToken: token),
                answer => answer.Status.Value,
                Transport.Options.TimeProvider,
                pollInterval,
                timeout,
                cancellationToken);
        }

        /// <summary>Download the file of a finished (<c>done</c>) job.</summary>
        /// <param name="job">The job as <see cref="WaitAsync(string, TimeSpan?, TimeSpan?, CancellationToken)"/> returned it.</param>
        /// <param name="options">Per-call options.</param>
        /// <param name="cancellationToken">Cancels the download.</param>
        /// <exception cref="ConfigException"><c>sdk.job_not_done</c>: the job has no file (yet).</exception>
        public Task<FileResult> DownloadAsync(
            DocumentJobView job,
            RequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(job);
            if (job.Status != DocumentJobStatus.Done)
            {
                throw new ConfigException(
                    SdkErrorCodes.JobNotDone, $"document job {job.JobId} is {job.Status}, not done; it has no file", "job");
            }

            return DownloadJobFileAsync(job.JobId, options, cancellationToken);
        }
    }
}
