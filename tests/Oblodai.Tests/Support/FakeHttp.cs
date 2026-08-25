using System.Net;
using System.Text;

namespace Oblodai.Tests.Support;

/// <summary>One request as the fake transport saw it.</summary>
public sealed record RecordedRequest(string Url, string Method, IReadOnlyDictionary<string, string> Headers, string? Body)
{
    public string Header(string name) => Headers.TryGetValue(name.ToLowerInvariant(), out var v) ? v : string.Empty;

    public bool HasHeader(string name) => Headers.ContainsKey(name.ToLowerInvariant());

    public Uri Uri => new(Url);
}

/// <summary>A scripted answer: a status and body, a throw, or a delay.</summary>
public sealed record ScriptedResponse
{
    public int Status { get; init; } = 200;

    public string Body { get; init; } = """{"state":0,"result":{}}""";

    public string ContentType { get; init; } = "application/json";

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>Throw this instead of answering (simulates a socket failure).</summary>
    public Exception? Throws { get; init; }

    /// <summary>Wait this long before answering (drives the timeout path).</summary>
    public int DelayMs { get; init; }

    /// <summary>A success envelope wrapping <paramref name="resultJson"/>.</summary>
    public static ScriptedResponse Ok(string resultJson) => new() { Body = "{\"state\":0,\"result\":" + resultJson + "}" };

    /// <summary>An empty success envelope.</summary>
    public static ScriptedResponse Ok() => Ok("{}");

    /// <summary>A page envelope.</summary>
    public static ScriptedResponse Page(string itemsJson, int offset, int total, int perPage, bool hasPages)
        => Ok("{\"items\":" + itemsJson + ",\"paginate\":{\"total\":" + total + ",\"per_page\":" + perPage
              + ",\"offset\":" + offset + ",\"has_pages\":" + (hasPages ? "true" : "false") + "}}");

    /// <summary>An error envelope.</summary>
    public static ScriptedResponse Error(int status, string errorJson, IReadOnlyDictionary<string, string>? headers = null)
        => new()
        {
            Status = status,
            Body = "{\"error\":" + errorJson + "}",
            Headers = headers ?? new Dictionary<string, string>(),
        };

    /// <summary>A body that is not an Oblodai envelope at all (a proxy or load balancer answering).</summary>
    public static ScriptedResponse Html(int status, IReadOnlyDictionary<string, string>? headers = null)
        => new()
        {
            Status = status,
            Body = "<html>upstream error</html>",
            ContentType = "text/html",
            Headers = headers ?? new Dictionary<string, string>(),
        };
}

/// <summary>An <see cref="HttpMessageHandler"/> that replays scripted responses in order and records every request.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<ScriptedResponse> _script;

    public FakeHttpHandler(params ScriptedResponse[] script) => _script = new Queue<ScriptedResponse>(script);

    public List<RecordedRequest> Calls { get; } = [];

    /// <summary>A client wired to this handler.</summary>
    public HttpClient Client() => new(this) { Timeout = Timeout.InfiniteTimeSpan };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, values) in request.Headers)
        {
            headers[key.ToLowerInvariant()] = string.Join(",", values);
        }

        string? body = null;
        if (request.Content is not null)
        {
            foreach (var (key, values) in request.Content.Headers)
            {
                headers[key.ToLowerInvariant()] = string.Join(",", values);
            }

            body = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        Calls.Add(new RecordedRequest(request.RequestUri!.ToString(), request.Method.Method, headers, body));

        if (!_script.TryDequeue(out var next))
        {
            throw new InvalidOperationException($"FakeHttpHandler: no scripted response for {request.Method} {request.RequestUri}");
        }

        if (next.DelayMs > 0)
        {
            await Task.Delay(next.DelayMs, cancellationToken);
        }

        if (next.Throws is not null)
        {
            throw next.Throws;
        }

        var response = new HttpResponseMessage((HttpStatusCode)next.Status)
        {
            Content = new StringContent(next.Body, Encoding.UTF8, next.ContentType),
        };

        foreach (var (key, value) in next.Headers)
        {
            if (!response.Headers.TryAddWithoutValidation(key, value))
            {
                response.Content.Headers.TryAddWithoutValidation(key, value);
            }
        }

        return response;
    }
}
