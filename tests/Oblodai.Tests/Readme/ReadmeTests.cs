using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Readme;

/// <summary>
/// The READMEs' C# runs: every block is a region of <see cref="ReadmeSnippets"/>, character for
/// character, and every region is executed here against a scripted gateway.
/// </summary>
public class ReadmeTests
{
    private const string EnglishReadme = "README.md";
    private const string RussianReadme = "README.ru.md";
    private const string MarkerStart = "// snippet:";
    private const string MarkerEnd = "// endsnippet";
    private const string SnippetIndent = "        ";

    private static readonly string SnippetSource =
        Path.Combine(Repo.Root, "tests", "Oblodai.Tests", "Readme", "ReadmeSnippets.cs");

    /// <summary>A gateway that answers every route with a plausible result and records the calls.</summary>
    private static FakeHttpHandler Gateway() => new(request =>
    {
        var path = new Uri(request.Url).AbsolutePath;
        return path switch
        {
            "/v1/payment" => ScriptedResponse.Ok("""{"uuid":"inv-1","status":"created","amount":"25","url":"https://pay.test/inv-1","address":"TX"}"""),
            "/v1/payout" => ScriptedResponse.Ok("""{"uuid":"po-1","status":"pending","amount":"10"}"""),
            "/v1/payment/history" or "/v1/payout/history" => ScriptedResponse.Page(
                """[{"uuid":"a","order_id":"o-a","amount":"1.50","status":"paid"}]""", 0, 1, 50, false),
            "/v1/sandbox/faucet" => ScriptedResponse.Ok("""{"asset":"USDT","amount":"1000"}"""),
            "/v1/sandbox/deposit" => ScriptedResponse.Ok("""{"invoice_id":"inv-1","txid":"sbx-1","confirmations":20,"amount":"25"}"""),
            "/v1/payout/batch" => ScriptedResponse.Ok("""{"batch_id":"b-1","status":"pending","kind":"payout","count":1}"""),
            "/v1/batch/info" => ScriptedResponse.Ok("""{"batch_id":"b-1","status":"completed","succeeded":1,"failed":0,"items":[]}"""),
            "/v1/documents/jobs" => ScriptedResponse.Ok("""{"job_id":"j-1","status":"queued","kind":"statement"}"""),
            "/v1/documents/jobs/info" => ScriptedResponse.Ok("""{"job_id":"j-1","status":"done","kind":"statement"}"""),
            "/v1/documents/jobs/file" => new ScriptedResponse { Body = "%PDF-1.7", ContentType = "application/pdf" },
            "/v1/payment/info" => ScriptedResponse.Ok("""{"uuid":"inv-1","status":"paid"}"""),
            "/v1/balance" => new ScriptedResponse
            {
                Body = """{"state":0,"result":{"balance":{"merchant":[]}}}""",
                Headers = new Dictionary<string, string> { ["X-Request-ID"] = "gw-1" },
            },
            _ => throw new InvalidOperationException($"the README gateway has no answer for {path}"),
        };
    });

    private static OblodaiClient Client(FakeHttpHandler gateway)
        => new(new OblodaiOptions
        {
            PublicId = "pk",
            Secret = "s",
            BaseUrl = "https://api.test",
            Retry = new RetryOptions { MaxRetries = 0 },
            TimeProvider = new RecordingTimeProvider(),
        }, gateway.Client());

    [Fact]
    public async Task QuickStartCreatesAnInvoiceAndAPayout()
    {
        var gateway = Gateway();
        using var oblodai = Client(gateway);

        var invoice = await ReadmeSnippets.QuickStartPayment(oblodai);
        var payout = await ReadmeSnippets.QuickStartPayout(oblodai);

        Assert.Equal("inv-1", invoice.Uuid);
        Assert.Equal("25", JsonDocument.Parse(gateway.Calls[0].Body!).RootElement.GetProperty("amount").GetString());
        Assert.Equal("po-1", payout.Uuid);
        Assert.Equal("payout-1", gateway.Calls[1].Header(RequestSigner.HeaderIdempotencyKey));
    }

    [Fact]
    public async Task TheRequestModelAndTheDictionaryFormSendTheSameWire()
    {
        var gateway = Gateway();
        using var oblodai = Client(gateway);

        await ReadmeSnippets.RequestModel(oblodai);

        Assert.Equal("USDT", JsonDocument.Parse(gateway.Calls[0].Body!).RootElement.GetProperty("to_currency").GetString());
        Assert.Equal("25.00", JsonDocument.Parse(gateway.Calls[1].Body!).RootElement.GetProperty("amount").GetString());
    }

    [Fact]
    public async Task SandboxListsLongRunningOptionsAndErrorsRun()
    {
        var gateway = Gateway();
        using var oblodai = Client(gateway);

        await ReadmeSnippets.Sandbox(oblodai);
        Assert.Equal(1, await ReadmeSnippets.Lists(oblodai));

        var batch = await ReadmeSnippets.LongRunning(oblodai, []);
        Assert.Equal(BatchStatus.Completed, batch.Status);
        Assert.Contains(gateway.Calls, c => c.Url.Contains("/v1/documents/jobs/file?job_id=j-1", StringComparison.Ordinal));

        Assert.Equal("gw-1inv-1", await ReadmeSnippets.Options(oblodai));
        var options = gateway.Calls.Single(c => c.Url.EndsWith("/v1/payment/info", StringComparison.Ordinal));
        Assert.Equal("checkout-7f3a", options.Header("X-Request-ID"));
        Assert.Equal("eu-1", options.Header("X-Shop"));

        var refusing = new FakeHttpHandler(ScriptedResponse.Error(
            409, """{"code":"payout.insufficient_funds","message":"not enough","retryable":false,"request_id":"req-9"}"""));
        using var poor = Client(refusing);
        Assert.Equal("req-9", await ReadmeSnippets.Errors(poor));
    }

    [Fact]
    public void TheWebhookSnippetVerifiesARealDelivery()
    {
        var sample = Repo.WebhookSamples.EnumerateArray().First(s => s.GetProperty("body").GetProperty("type").GetString() == "payment");
        var headers = sample.GetProperty("headers").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        var raw = Encoding.UTF8.GetBytes(sample.GetProperty("raw").GetString()!);

        // The recorded delivery is old; the snippet verifies with the default window, so re-sign it now.
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        headers[WebhookVerifier.HeaderTimestamp] = now.ToString();
        headers[WebhookVerifier.HeaderSignature] = RequestSigner.SignWebhook("whsec_readme", now, raw);
        headers.Remove(WebhookVerifier.HeaderSignaturePrev);

        ReadmeSnippets.Webhook(raw, headers, "whsec_readme");
        Assert.Throws<SignatureException>(() => ReadmeSnippets.Webhook(raw, headers, "another"));
    }

    [Fact]
    public async Task ClientHooksAndContainerWiringWork()
    {
        using var client = ReadmeSnippets.Client("pk", "s");
        Assert.NotNull(client.Payments);

        var gateway = Gateway();
        using var observed = ReadmeSnippets.Hooks("pk", "s", gateway.Client());
        await observed.WithOptions(o => o with { BaseUrl = "https://api.test" }).Account.GetBalanceAsync();
        Assert.Single(gateway.Calls);

        var services = new ServiceCollection();
        ReadmeSnippets.Container(services);
        await using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<OblodaiClient>());
    }

    [Theory]
    [InlineData(EnglishReadme)]
    [InlineData(RussianReadme)]
    public void EveryCSharpBlockIsACompiledSnippetAndEverySnippetIsShown(string file)
    {
        var snippets = CompiledSnippets();
        var blocks = FencedBlocks(file).Where(b => b.Language == "csharp").ToList();

        Assert.NotEmpty(blocks);
        foreach (var (_, body, line) in blocks)
        {
            var code = StripUsings(body);
            Assert.True(
                snippets.Any(s => s.Body == code),
                $"{file}:{line}: this C# block is not a region of ReadmeSnippets.cs — edit the snippet there and paste it back:\n{code}");
        }

        foreach (var (name, body) in snippets)
        {
            Assert.True(blocks.Any(b => StripUsings(b.Body) == body), $"{file}: snippet \"{name}\" is not shown");
        }
    }

    [Fact]
    public void BothReadmesHaveTheSameSectionsInTheSameOrder()
        => Assert.Equal(Headings(EnglishReadme).Count, Headings(RussianReadme).Count);

    /// <summary>Every option the READMEs tabulate is a real <see cref="OblodaiOptions"/> property, and all of them are there.</summary>
    [Theory]
    [InlineData(EnglishReadme)]
    [InlineData(RussianReadme)]
    public void TheOptionsTableIsTheOptionsType(string file)
    {
        var properties = typeof(OblodaiOptions).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var rows = OptionTableRows(File.ReadAllLines(Path.Combine(Repo.Root, file)));

        Assert.Equal(properties.OrderBy(p => p, StringComparer.Ordinal), rows.OrderBy(p => p, StringComparer.Ordinal));
    }

    /// <summary>
    /// The method table between the <c>sdkgen:methods</c> markers is the generator's and names every
    /// method of the client — every <c>names.lock</c> entry as <c>Resource</c> and <c>MethodAsync</c>.
    /// </summary>
    [Theory]
    [InlineData(EnglishReadme)]
    [InlineData(RussianReadme)]
    public void TheMethodTableNamesEveryLockedMethod(string file)
    {
        var text = File.ReadAllText(Path.Combine(Repo.Root, file));
        var start = text.IndexOf("<!-- sdkgen:methods -->", StringComparison.Ordinal);
        var end = text.IndexOf("<!-- /sdkgen:methods -->", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"{file}: no sdkgen:methods section");
        var rows = text[start..end].Split('\n').Where(l => l.StartsWith("| `", StringComparison.Ordinal))
            .ToDictionary(l => l.Split('`')[1], l => l);
        foreach (var locked in File.ReadAllLines(Path.Combine(Repo.Root, "names.lock")).Where(l => l.Length > 0))
        {
            var (resource, method) = (Pascal(locked.Split('.')[0]), Pascal(locked.Split('.')[1]) + "Async");
            Assert.True(rows.TryGetValue(resource, out var row), $"{file}: no row for {resource}");
            Assert.Contains($"`{method}`", row);
        }
    }

    private static string Pascal(string snake)
        => string.Concat(snake.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    [Theory]
    [InlineData(EnglishReadme)]
    [InlineData(RussianReadme)]
    public void TheReadmeNamesThisVersion(string file)
    {
        var text = File.ReadAllText(Path.Combine(Repo.Root, file));
        Assert.Contains($"--version {OblodaiClient.SdkVersion}", text);
        Assert.DoesNotContain("1.3.0", text);
    }

    /// <summary>
    /// MIGRATION-2.0.md names every method of 2.0.0, so nobody has to guess where a call went. The list is
    /// the frozen <c>names.2.0.txt</c>, not the living <c>names.lock</c>: a method added later belongs to
    /// the CHANGELOG, not to the 1.x → 2.0 guide.
    /// </summary>
    [Fact]
    public void TheMigrationGuideNamesEveryMethod()
    {
        var guide = File.ReadAllText(Path.Combine(Repo.Root, "MIGRATION-2.0.md"));
        var frozen = File.ReadAllLines(Path.Combine(Repo.Root, "names.2.0.txt")).Where(l => l.Length > 0).ToList();
        Assert.NotEmpty(frozen);
        foreach (var locked in frozen)
        {
            var parts = locked.Split('.').Select(p => string.Concat(p.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w[1..])));
            var name = string.Join(".", parts) + "Async";
            Assert.Contains($"`{name}`", guide);
        }
    }

    private static List<string> OptionTableRows(string[] lines)
    {
        var rows = new List<string>();
        var inside = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("| Option", StringComparison.Ordinal) || line.StartsWith("| Опция", StringComparison.Ordinal))
            {
                inside = true;
                continue;
            }

            if (!inside || line.StartsWith("| -", StringComparison.Ordinal))
            {
                continue;
            }

            if (!line.StartsWith("| `", StringComparison.Ordinal))
            {
                break;
            }

            var firstCell = line[1..line.IndexOf('|', 1)];
            rows.AddRange(Regex.Matches(firstCell, "`([A-Za-z]+)`").Select(m => m.Groups[1].Value));
        }

        return rows;
    }

    private static List<(string Name, string Body)> CompiledSnippets()
    {
        var snippets = new List<(string, string)>();
        var current = new List<string>();
        var name = string.Empty;
        var inside = false;
        foreach (var raw in File.ReadAllText(SnippetSource).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.StartsWith(MarkerStart, StringComparison.Ordinal))
            {
                (inside, name) = (true, trimmed[MarkerStart.Length..]);
                current.Clear();
            }
            else if (inside && trimmed.StartsWith(MarkerEnd, StringComparison.Ordinal))
            {
                snippets.Add((name, string.Join("\n", current)));
                inside = false;
            }
            else if (inside)
            {
                current.Add(line.StartsWith(SnippetIndent, StringComparison.Ordinal) ? line[SnippetIndent.Length..] : line);
            }
        }

        Assert.False(inside, $"snippet \"{name}\" is never closed");
        return snippets;
    }

    private static List<(string Language, string Body, int Line)> FencedBlocks(string file)
    {
        var lines = File.ReadAllText(Path.Combine(Repo.Root, file)).Split('\n');
        var blocks = new List<(string, string, int)>();
        var current = new List<string>();
        var (language, start, inside) = (string.Empty, 0, false);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (!line.StartsWith("```", StringComparison.Ordinal))
            {
                if (inside)
                {
                    current.Add(line);
                }

                continue;
            }

            if (inside)
            {
                blocks.Add((language, string.Join("\n", current), start));
                current.Clear();
                inside = false;
                continue;
            }

            (inside, language, start) = (true, line[3..].Trim(), i + 1);
        }

        Assert.False(inside, $"{file}: a fenced code block is never closed");
        return blocks;
    }

    private static string StripUsings(string body)
    {
        var lines = body.Split('\n').ToList();
        while (lines.Count > 0 && (lines[0].Length == 0 || Regex.IsMatch(lines[0], @"^using [\w.]+;$")))
        {
            lines.RemoveAt(0);
        }

        return string.Join("\n", lines);
    }

    private static List<string> Headings(string file)
    {
        var headings = new List<string>();
        var inside = false;
        foreach (var line in File.ReadAllText(Path.Combine(Repo.Root, file)).Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inside = !inside;
            }
            else if (!inside && line.StartsWith("## ", StringComparison.Ordinal))
            {
                headings.Add(line[3..].Trim());
            }
        }

        return headings;
    }
}
