using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Oblodai.Contract;
using Oblodai.Resources;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Conformance;

/// <summary>
/// The shared conformance suite every Oblodai SDK runs (backend <c>tools/sdkgen/conformance</c>).
/// <para>
/// Scenarios are read from <c>$SDKGEN_CONFORMANCE</c>, else from <c>tools/sdkgen/conformance</c> of the
/// backend checkout (<c>$OBLODAI_BACKEND</c>, else <c>../oblodai-backend</c>). Signing vectors are not in
/// the scenario files: each suite names the backend <c>openapi.json</c> and a pointer into its
/// <c>x-oblodai-signing</c>, and the vectors are read from there. Without a suite the tests are skipped
/// — unless <c>OBLODAI_BACKEND</c> or <c>SDKGEN_CONFORMANCE</c> named one, then they fail.
/// </para>
/// <para>
/// Every call scenario goes through the generated method of its <c>operationId</c> (the request built
/// from the scenario's wire-named arguments with <see cref="Model.From{T}"/>) over a scripted
/// <see cref="HttpMessageHandler"/>; retry pauses are recorded by a <see cref="RecordingTimeProvider"/>
/// instead of slept.
/// </para>
/// </summary>
public class ConformanceTests
{
    private static readonly string? Explicit = Environment.GetEnvironmentVariable("SDKGEN_CONFORMANCE");

    /// <summary>The suite directory.</summary>
    public static string Directory => Explicit is { Length: > 0 }
        ? Explicit
        : Path.Combine(Repo.Backend, "tools", "sdkgen", "conformance");

    /// <summary>True when the suite is there to run.</summary>
    public static bool Available
    {
        get
        {
            if (System.IO.Directory.Exists(Directory))
            {
                return true;
            }

            if (Explicit is { Length: > 0 } || Repo.BackendRequired)
            {
                throw new InvalidOperationException($"conformance suite not found at {Directory}");
            }

            return false;
        }
    }

    public static TheoryData<string, int> SigningCases() => Cases("signing");

    public static TheoryData<string, int> WebhookCases() => Cases("webhook");

    public static TheoryData<string, int> WebhookDeliveryCases() => Cases("webhook_delivery");

    public static TheoryData<string> CallScenarios()
    {
        var data = new TheoryData<string>();
        if (!Available)
        {
            data.Add("(skipped)");
            return data;
        }

        foreach (var suite in new[] { "retry", "money", "forward_compat" })
        {
            foreach (var scenario in Suite(suite).GetProperty("scenarios").EnumerateArray())
            {
                data.Add($"{suite}/{scenario.GetProperty("name").GetString()}");
            }
        }

        return data;
    }

    public static TheoryData<string> WebhookBodies()
    {
        var data = new TheoryData<string>();
        if (!Available)
        {
            data.Add("(skipped)");
            return data;
        }

        foreach (var body in Suite("forward_compat").GetProperty("webhooks").EnumerateArray())
        {
            data.Add(body.GetProperty("name").GetString()!);
        }

        return data;
    }

    /// <summary>forward_compat webhooks: the body parses, keeps its raw type, and is known exactly as said.</summary>
    [ConformanceTheory]
    [MemberData(nameof(WebhookBodies))]
    public void WebhookParse(string name)
    {
        var body = Suite("forward_compat").GetProperty("webhooks").EnumerateArray()
            .Single(b => b.GetProperty("name").GetString() == name);
        var expect = body.GetProperty("expect");

        var parsed = WebhookVerifier.Parse(body.GetProperty("body").GetRawText());

        Assert.Equal(expect.GetProperty("type").GetString(), parsed.Type);
        Assert.Equal(expect.GetProperty("known").GetBoolean(), WebhookVerifier.IsKnownEvent(parsed));
    }

    [ConformanceFact]
    public void TheContractDeclaresTheSigningRecipeThisSdkImplements()
    {
        var (signing, _, _) = Source("signing", Suite("signing").GetProperty("checks")[0].GetProperty("name").GetString()!);

        Assert.Equal("ts\\nMETHOD\\nrequest_uri\\nidempotency_key\\nbody", signing.GetProperty("canonical").GetString());
        Assert.Equal(RequestSigner.SignatureSkewSeconds, signing.GetProperty("skew_seconds").GetInt32());
        Assert.Equal(Idempotency.MaxKeyLength, signing.GetProperty("max_idempotency_key_length").GetInt32());
        Assert.Equal(
            new[] { RequestSigner.HeaderPublicId, RequestSigner.HeaderSignature, RequestSigner.HeaderTimestamp, RequestSigner.HeaderIdempotencyKey },
            signing.GetProperty("headers").EnumerateArray().Select(h => h.GetString()));
    }

    [ConformanceTheory]
    [MemberData(nameof(SigningCases))]
    public void RequestSigning(string check, int vectorIndex)
    {
        var (_, vectors, checkElement) = Source("signing", check);
        var vector = vectors[vectorIndex];
        var key = vector.GetProperty("idempotency_key").GetString();
        var ts = vector.GetProperty("ts").GetInt64();
        var method = vector.GetProperty("method").GetString()!;
        var uri = vector.GetProperty("request_uri").GetString()!;
        var body = vector.GetProperty("body").GetString()!;

        switch (checkElement.GetProperty("kind").GetString())
        {
            case "request_canonical":
                Assert.Equal(vector.GetProperty("canonical").GetString(), RequestSigner.CanonicalString(ts, method, uri, key, body));
                break;
            case "request_signature":
                Assert.Equal(
                    vector.GetProperty("signature").GetString(),
                    RequestSigner.Sign(vector.GetProperty("secret").GetString()!, ts, method, uri, key, body));
                break;
            default:
                Assert.Fail($"unknown check kind in {check}");
                break;
        }
    }

    [ConformanceTheory]
    [MemberData(nameof(WebhookCases))]
    public void Webhook(string check, int vectorIndex)
    {
        var (signing, vectors, checkElement) = Source("webhook", check);
        var vector = vectors[vectorIndex];
        var secret = vector.GetProperty("secret").GetString()!;
        var ts = vector.GetProperty("ts").GetInt64();
        var payload = vector.GetProperty("payload").GetString()!;
        var signature = vector.GetProperty("signature").GetString()!;

        if (checkElement.GetProperty("kind").GetString() == "webhook_signature")
        {
            Assert.Equal(signature, RequestSigner.SignWebhook(secret, ts, payload));
            return;
        }

        Assert.Equal("webhook_verify", checkElement.GetProperty("kind").GetString());
        var skew = signing.GetProperty("skew_seconds").GetInt32();
        var from = checkElement.GetProperty("now_from_ts");
        long offset = from.ValueKind == JsonValueKind.Number
            ? from.GetInt64()
            : from.GetString() switch
            {
                "skew" => skew,
                "skew+1" => skew + 1,
                var other => throw new InvalidOperationException($"now_from_ts {other}"),
            };
        switch (checkElement.GetProperty("mutate").GetString())
        {
            case "payload":
                payload += " ";
                break;
            case "signature":
                signature = (signature[0] != '0' ? "0" : "1") + signature[1..];
                break;
        }

        var headers = new Dictionary<string, string>
        {
            [WebhookVerifier.HeaderTimestamp] = ts.ToString(),
            [WebhookVerifier.HeaderSignature] = signature,
        };
        var options = new WebhookVerifyOptions { Secret = secret, ToleranceSeconds = skew, Now = () => ts + offset };
        var expect = checkElement.GetProperty("expect").GetString();
        var raw = Encoding.UTF8.GetBytes(payload);
        if (expect == "ok")
        {
            // The vectors sign bare payloads, not whole events: verification gets past the MAC and the
            // freshness window and only then may refuse to parse — that refusal still is a pass.
            try
            {
                WebhookVerifier.Verify(raw, headers, options);
            }
            catch (WebhookPayloadException)
            {
            }

            return;
        }

        var error = Assert.Throws<SignatureException>(() => WebhookVerifier.Verify(raw, headers, options));
        Assert.Equal("webhook." + expect, error.Code);
    }

    [ConformanceFact]
    public void EveryEventOfThisReleaseHasADelivery()
    {
        var (_, deliveries, _) = Source("webhook_delivery", Suite("webhook_delivery").GetProperty("checks")[0].GetProperty("name").GetString()!);
        Assert.Equal(
            ApiFacts.WebhookEvents.Keys.OrderBy(k => k, StringComparer.Ordinal),
            deliveries.Select(d => d.GetProperty("event").GetString()!).OrderBy(k => k, StringComparer.Ordinal));
    }

    /// <summary>
    /// A real delivery of every event of the contract verifies (with the current secret and, as a receiver
    /// that has not swapped yet, the previous one), parses into its kind's model and exposes every delivery
    /// header of the spec.
    /// </summary>
    [ConformanceTheory]
    [MemberData(nameof(WebhookDeliveryCases))]
    public void WebhookDelivery(string check, int vectorIndex)
    {
        var (_, deliveries, checkElement) = Source("webhook_delivery", check);
        var d = deliveries[vectorIndex];
        Assert.Equal("webhook_delivery", checkElement.GetProperty("kind").GetString());
        var secret = checkElement.GetProperty("key").GetString() switch
        {
            "current" => d.GetProperty("secret").GetString()!,
            "previous" => d.GetProperty("previous_secret").GetString()!,
            var other => throw new InvalidOperationException($"key {other}"),
        };
        var headers = d.GetProperty("headers").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        var ts = d.GetProperty("ts").GetInt64();
        var delivery = WebhookVerifier.VerifyDelivery(
            Encoding.UTF8.GetBytes(d.GetProperty("payload").GetString()!),
            headers,
            new WebhookVerifyOptions { Secret = secret, Now = () => ts });

        var kind = d.GetProperty("kind").GetString()!;
        Assert.True(WebhookVerifier.IsKnownEvent(delivery.Event), kind);
        Assert.Equal(kind, delivery.Event.Type);
        Assert.IsType(ApiFacts.WebhookModels[kind], delivery.Event);
        foreach (var field in Suite("webhook_delivery").GetProperty("headers").EnumerateObject())
        {
            var want = headers[field.Name];
            string? got = field.Value.GetString() switch
            {
                "" => want,
                "id" => delivery.Id,
                "event_id" => delivery.EventId,
                "event_type" => delivery.EventType,
                "event_time" => delivery.EventTime?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "sent_at" => delivery.SentAt.ToString(System.Globalization.CultureInfo.InvariantCulture),
                var other => throw new InvalidOperationException($"the delivery info has no field {other} for {field.Name}"),
            };
            Assert.True(want == got, $"{field.Value.GetString()} = {got}, {field.Name} = {want}");
        }
    }

    [ConformanceTheory]
    [MemberData(nameof(CallScenarios))]
    public async Task Call(string id)
    {
        var slash = id.IndexOf('/');
        var scenario = Suite(id[..slash]).GetProperty("scenarios").EnumerateArray()
            .Single(s => s.GetProperty("name").GetString() == id[(slash + 1)..]);
        var call = scenario.GetProperty("call");
        var script = new Script(scenario.GetProperty("responses"));
        var time = new RecordingTimeProvider();
        using var client = new OblodaiClient(
            new OblodaiOptions { PublicId = "pk_test_conformance", Secret = "conformance-secret", BaseUrl = "https://api.test", TimeProvider = time },
            new HttpClient(script) { Timeout = Timeout.InfiniteTimeSpan });

        object? result = null;
        OblodaiException? error = null;
        try
        {
            result = await Invoke(client, call.GetProperty("operation").GetString()!, call.GetProperty("args"));
        }
        catch (OblodaiException caught)
        {
            error = caught;
        }

        var expect = scenario.GetProperty("expect");
        Assert.Equal(expect.GetProperty("requests").GetInt32(), script.Requests.Count);
        var keys = script.Requests.Select(r => r.Headers.TryGetValues("Idempotency-Key", out var v) ? v.Single() : null).ToList();
        if (expect.TryGetProperty("idempotency_key", out var keyRule))
        {
            if (keyRule.GetString() == "absent")
            {
                Assert.All(keys, Assert.Null);
            }
            else
            {
                Assert.All(keys, k => Assert.False(string.IsNullOrEmpty(k)));
            }
        }

        if (expect.TryGetProperty("same_idempotency_key", out var same) && same.GetBoolean())
        {
            Assert.False(string.IsNullOrEmpty(keys[0]));
            Assert.Single(keys.Distinct());
        }

        if (expect.TryGetProperty("delays_ms", out var delays))
        {
            Assert.Equal(delays.EnumerateArray().Select(d => d.GetDouble()), time.DelaysMs);
        }

        if (expect.TryGetProperty("request_body_field", out var bodyFields))
        {
            using var sent = JsonDocument.Parse(script.Bodies[^1]!);
            foreach (var field in bodyFields.EnumerateObject())
            {
                Assert.Equal(field.Value.ToString(), sent.RootElement.GetProperty(field.Name).ToString());
            }
        }

        if (expect.TryGetProperty("error_code", out var code))
        {
            Assert.NotNull(error);
            Assert.Equal(code.GetString(), error!.Code);
            return;
        }

        if (error is not null)
        {
            throw error;
        }

        if (expect.TryGetProperty("result_field", out var resultFields))
        {
            foreach (var field in resultFields.EnumerateObject())
            {
                Assert.Equal(field.Value.ToString(), Plain(WireProperty(result!, field.Name)));
            }
        }
    }

    // ---- calling a generated method by operationId --------------------------------------------------

    private static readonly Lazy<IReadOnlyDictionary<string, (string Class, string Method)>> Operations = new(ReadOperations);

    /// <summary>operationId → (resource class, method), read from the generated source.</summary>
    private static IReadOnlyDictionary<string, (string Class, string Method)> ReadOperations()
    {
        var source = File.ReadAllText(Path.Combine(Repo.Root, "src", "Oblodai", "Generated", "Resources.g.cs"));
        var output = new Dictionary<string, (string, string)>();
        string? current = null;
        string? method = null;
        foreach (var line in source.Split('\n'))
        {
            if (Regex.Match(line, @"^public sealed partial class (\w+)") is { Success: true } cls)
            {
                current = cls.Groups[1].Value;
            }
            else if (Regex.Match(line, @"^    public [\w<>?, ]+ (\w+Async)\($") is { Success: true } m)
            {
                method = m.Groups[1].Value;
            }
            else if (Regex.Match(line, @"^\s+Routes\.(\w+),$") is { Success: true } r && current is not null && method is not null)
            {
                var route = (RouteSpec)typeof(Routes).GetField(r.Groups[1].Value)!.GetValue(null)!;
                output[route.OperationId] = (current, method);
            }
        }

        return output;
    }

    private static async Task<object?> Invoke(OblodaiClient client, string operationId, JsonElement args)
    {
        var (className, methodName) = Operations.Value[operationId];
        var resource = typeof(OblodaiClient).GetProperties()
            .Select(p => p.GetValue(client))
            .Single(v => v?.GetType().Name == className)!;
        var overloads = resource.GetType().GetMethods().Where(m => m.Name == methodName).ToList();
        var method = overloads.FirstOrDefault(m => m.GetParameters().Any(p => p.Name == "request"))
                     ?? overloads.OrderBy(m => m.GetParameters().Length).First();

        var values = (Dictionary<string, object?>)Plain(args)!;
        try
        {
            var arguments = new List<object?>();
            foreach (var parameter in method.GetParameters())
            {
                arguments.Add(parameter.Name switch
                {
                    // The request is built from the wire-named arguments: a double where the model has
                    // money is refused here, before anything is sent (sdk.float_amount).
                    "request" => typeof(Model).GetMethod(nameof(Model.From))!.MakeGenericMethod(parameter.ParameterType)
                        .Invoke(null, [values]),
                    "options" => null,
                    "cancellationToken" => CancellationToken.None,
                    _ => values.GetValueOrDefault(Snake(parameter.Name!)),
                });
            }

            var returned = method.Invoke(resource, arguments.ToArray());
            if (returned is Task task)
            {
                await task;
                return task.GetType().GetProperty("Result")!.GetValue(task);
            }

            return returned;
        }
        catch (TargetInvocationException wrapped) when (wrapped.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(wrapped.InnerException).Throw();
            throw;
        }
    }

    /// <summary>A JSON value as the plain CLR value a caller would hold: a fraction is a double.</summary>
    private static object? Plain(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => Plain(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().Select(Plain).ToList(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static string Snake(string camel)
        => Regex.Replace(camel.TrimStart('@'), "([A-Z])", "_$1").ToLowerInvariant();

    private static object? WireProperty(object model, string wireName)
        => model.GetType().GetProperties()
            .Single(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == wireName)
            .GetValue(model);

    private static string? Plain(object? value) => value switch
    {
        null => null,
        decimal d => Money.Format(d),
        bool b => b ? "True" : "False",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    // ---- suites --------------------------------------------------------------------------------------

    private static JsonElement Suite(string name)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory, name + ".json"))).RootElement;

    private static TheoryData<string, int> Cases(string name)
    {
        var data = new TheoryData<string, int>();
        if (!Available)
        {
            data.Add("(skipped)", 0);
            return data;
        }

        foreach (var check in Suite(name).GetProperty("checks").EnumerateArray())
        {
            var (_, vectors, _) = Source(name, check.GetProperty("name").GetString()!);
            for (var i = 0; i < vectors.Count; i++)
            {
                data.Add(check.GetProperty("name").GetString()!, i);
            }
        }

        return data;
    }

    private static (JsonElement Signing, IReadOnlyList<JsonElement> Vectors, JsonElement Check) Source(string suiteName, string check)
    {
        var suite = Suite(suiteName);
        var source = suite.GetProperty("source");
        var spec = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory, source.GetProperty("spec").GetString()!))).RootElement;
        var cursor = spec;
        foreach (var part in source.GetProperty("pointer").GetString()!.TrimStart('/').Split('/'))
        {
            cursor = cursor.GetProperty(part.Replace("~1", "/").Replace("~0", "~"));
        }

        var checkElement = suite.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == check);
        return (spec.GetProperty("x-oblodai-signing"), cursor.EnumerateArray().ToList(), checkElement);
    }

    /// <summary>Replays a scenario's responses in order and records what the SDK sent.</summary>
    private sealed class Script : HttpMessageHandler
    {
        private readonly Queue<JsonElement> _responses;

        public Script(JsonElement responses) => _responses = new Queue<JsonElement>(responses.EnumerateArray());

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            if (!_responses.TryDequeue(out var next))
            {
                throw new InvalidOperationException($"unscripted request {request.Method} {request.RequestUri}");
            }

            if (next.TryGetProperty("transport_error", out var transport))
            {
                Assert.Equal("timeout", transport.GetString());
                throw new TaskCanceledException("scripted timeout", new TimeoutException());
            }

            var response = new HttpResponseMessage((HttpStatusCode)next.GetProperty("status").GetInt32())
            {
                RequestMessage = request,
                Content = next.TryGetProperty("json", out var json)
                    ? new StringContent(json.GetRawText(), Encoding.UTF8, "application/json")
                    : new StringContent("<html>proxy</html>", Encoding.UTF8, "text/html"),
            };
            if (next.TryGetProperty("headers", out var headers))
            {
                foreach (var header in headers.EnumerateObject())
                {
                    response.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString());
                }
            }

            return response;
        }
    }
}

/// <summary>A theory that is skipped when the conformance suite is not there to run.</summary>
public sealed class ConformanceTheoryAttribute : TheoryAttribute
{
    public ConformanceTheoryAttribute()
    {
        if (!ConformanceTests.Available)
        {
            Skip = $"conformance suite not found at {ConformanceTests.Directory}; set OBLODAI_BACKEND or SDKGEN_CONFORMANCE";
        }
    }
}

/// <summary>A fact that is skipped when the conformance suite is not there to run.</summary>
public sealed class ConformanceFactAttribute : FactAttribute
{
    public ConformanceFactAttribute()
    {
        if (!ConformanceTests.Available)
        {
            Skip = $"conformance suite not found at {ConformanceTests.Directory}; set OBLODAI_BACKEND or SDKGEN_CONFORMANCE";
        }
    }
}
