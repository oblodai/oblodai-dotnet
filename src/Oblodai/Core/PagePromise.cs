using System.Runtime.CompilerServices;

namespace Oblodai;

/// <summary>
/// What a list method returns: <c>await</c> it for the first page, or <c>await foreach</c> it to walk
/// every item across pages. Nothing is requested until it is consumed, and the first page is fetched
/// once however many ways it is consumed — so an unconsumed list never touches the network and an
/// unconsumed failure never surfaces as an unobserved exception.
/// </summary>
/// <typeparam name="T">Item model.</typeparam>
public sealed class PagePromise<T> : IAsyncEnumerable<T>
{
    /// <summary>Page size used when the caller does not ask for one.</summary>
    public const int DefaultPageLimit = 50;

    private readonly Func<int, int, CancellationToken, Task<Page<T>>> _fetchPage;
    private readonly int _limit;
    private readonly int _offset;
    private readonly CancellationToken _cancellationToken;
    private Task<Page<T>>? _firstPage;

    /// <summary>Build a lazy list handle.</summary>
    /// <param name="fetchPage">Fetches one page: (limit, offset, cancellation token).</param>
    /// <param name="limit">Page size; <see cref="DefaultPageLimit"/> when null.</param>
    /// <param name="offset">Starting offset.</param>
    /// <param name="cancellationToken">Cancels every page fetch.</param>
    public PagePromise(
        Func<int, int, CancellationToken, Task<Page<T>>> fetchPage,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default)
    {
        _fetchPage = fetchPage;
        _limit = limit ?? DefaultPageLimit;
        _offset = offset ?? 0;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Fetch (once) and return the first page.</summary>
    public Task<Page<T>> FirstPageAsync() => _firstPage ??= _fetchPage(_limit, _offset, _cancellationToken);

    /// <summary>Makes <c>await client.Payments.History(…)</c> yield the first page.</summary>
    public TaskAwaiter<Page<T>> GetAwaiter() => FirstPageAsync().GetAwaiter();

    /// <summary>Collect every item into a list, optionally capped.</summary>
    /// <param name="maxItems">Stop after this many items.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    public async Task<List<T>> AllAsync(int? maxItems = null, CancellationToken cancellationToken = default)
    {
        var output = new List<T>();
        var limit = maxItems ?? int.MaxValue;
        var enumerator = GetAsyncEnumerator(cancellationToken);
        await using (enumerator.ConfigureAwait(false))
        {
            // The cap is checked before pulling, so a satisfied cap never costs one more page request.
            while (output.Count < limit && await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                output.Add(enumerator.Current);
            }
        }

        return output;
    }

    /// <inheritdoc />
    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, cancellationToken);
        var token = linked.Token;
        var offset = _offset;
        var pending = _firstPage; // reuse the first page when it was already requested

        while (true)
        {
            var page = await (pending ?? _fetchPage(_limit, offset, token)).ConfigureAwait(false);
            pending = null;

            foreach (var item in page.Items)
            {
                yield return item;
            }

            offset += page.Items.Count;
            if (page.Items.Count == 0 || !page.Paginate.HasPages)
            {
                yield break;
            }
        }
    }
}
