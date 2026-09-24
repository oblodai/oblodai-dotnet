namespace Oblodai.Tests.Support;

/// <summary>
/// A clock whose timers fire at once: every pause the SDK takes is recorded instead of slept, and
/// virtual time advances by it, so deadlines and waits behave as if the time had passed.
/// </summary>
public sealed class RecordingTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;
    private TimeSpan _elapsed;

    /// <summary>Every pause requested, in order.</summary>
    public List<TimeSpan> Delays { get; } = [];

    /// <summary>The pauses in whole milliseconds.</summary>
    public IReadOnlyList<double> DelaysMs
    {
        get
        {
            lock (_gate)
            {
                return Delays.Select(d => d.TotalMilliseconds).ToList();
            }
        }
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _start + _elapsed;
        }
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            lock (_gate)
            {
                Delays.Add(dueTime);
                _elapsed += dueTime;
            }

            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        return new Fired();
    }

    private sealed class Fired : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
