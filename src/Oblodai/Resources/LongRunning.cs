using Oblodai.Contract;

namespace Oblodai;

/// <summary>
/// Following long-running operations. Which operations are long-running, what polls them and which
/// statuses end them is the contract's <c>x-sdk-poll</c>, generated into <see cref="ApiFacts.Polls"/>;
/// the generated <c>WaitAsync</c> of the resource that polls (<c>Batches.WaitAsync</c>,
/// <c>Documents.WaitAsync</c>, …) runs <see cref="PollAsync{T}(Func{CancellationToken, Task{T}}, Func{T, string}, IReadOnlySet{string}, TimeProvider, TimeSpan?, TimeSpan?, CancellationToken)"/>
/// until the status is terminal.
/// </summary>
public static class LongRunning
{
    /// <summary>How long to wait between two polls unless told otherwise.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>How long a wait lasts at most unless told otherwise.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Create <c>operationId</c> → the <c>operationId</c> that polls it (from <see cref="ApiFacts.Polls"/>).</summary>
    public static IReadOnlyDictionary<string, string> Operations { get; } =
        ApiFacts.Polls.ToDictionary(p => p.Key, p => p.Value.Operation, StringComparer.Ordinal);

    /// <summary>
    /// Every status after which some long-running job no longer changes — the union of the terminal
    /// statuses of <see cref="ApiFacts.Polls"/> (a batch ends <c>completed</c> or <c>stopped</c>, a
    /// document job <c>done</c>, <c>failed</c> or <c>expired</c>).
    /// </summary>
    public static IReadOnlySet<string> TerminalStatuses { get; } =
        new HashSet<string>(ApiFacts.Polls.Values.SelectMany(p => p.Terminal), StringComparer.Ordinal);

    /// <summary>
    /// Poll until the status of the answer is one of <see cref="TerminalStatuses"/> and return that
    /// answer — a terminal failure is returned, not thrown, so a failed job is inspected like a
    /// finished one.
    /// </summary>
    /// <typeparam name="T">The poll answer.</typeparam>
    /// <param name="poll">One poll.</param>
    /// <param name="status">The status of an answer.</param>
    /// <param name="time">Time source for the pauses and the timeout.</param>
    /// <param name="pollInterval">Pause between polls; <see cref="DefaultPollInterval"/> when null.</param>
    /// <param name="timeout">Longest wait; <see cref="DefaultTimeout"/> when null.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <exception cref="TransportException"><c>sdk.wait_timeout</c>: still running after <paramref name="timeout"/>.</exception>
    public static Task<T> PollAsync<T>(
        Func<CancellationToken, Task<T>> poll,
        Func<T, string> status,
        TimeProvider time,
        TimeSpan? pollInterval,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => PollAsync(poll, status, TerminalStatuses, time, pollInterval, timeout, cancellationToken);

    /// <summary>
    /// Poll until the status of the answer is in <paramref name="terminal"/> and return that answer —
    /// a terminal failure is returned, not thrown.
    /// </summary>
    /// <typeparam name="T">The poll answer.</typeparam>
    /// <param name="poll">One poll.</param>
    /// <param name="status">The status of an answer.</param>
    /// <param name="terminal">The statuses that end the wait (<see cref="ApiFacts.Poll.Terminal"/>).</param>
    /// <param name="time">Time source for the pauses and the timeout.</param>
    /// <param name="pollInterval">Pause between polls; <see cref="DefaultPollInterval"/> when null.</param>
    /// <param name="timeout">Longest wait; <see cref="DefaultTimeout"/> when null.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <exception cref="TransportException"><c>sdk.wait_timeout</c>: still running after <paramref name="timeout"/>.</exception>
    public static async Task<T> PollAsync<T>(
        Func<CancellationToken, Task<T>> poll,
        Func<T, string> status,
        IReadOnlySet<string> terminal,
        TimeProvider time,
        TimeSpan? pollInterval,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(poll);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(time);
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
            if (terminal.Contains(current))
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
}
