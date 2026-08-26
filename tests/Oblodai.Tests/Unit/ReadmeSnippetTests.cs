using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Oblodai.Contract;
using Oblodai.Models;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// The C# the two READMEs print, written once and compiled here. A snippet that does not compile is a
/// bug report from the first person who tries the SDK, and the usual cause is invisible in review: a
/// type that lives in a namespace the snippet's <c>using</c> lines do not import. The regions between a
/// <c>// snippet:</c> and an <c>// endsnippet</c> comment are the source of truth — the README blocks
/// must match them character for character, and <see cref="ReadmeSnippetTests"/> fails when they drift.
/// </summary>
internal static class ReadmeSnippets
{
    /// <summary>README: "Where to get keys" — the client, with the merchant's one API key.</summary>
    internal static OblodaiClient Credentials(string publicId, string secret)
    {
        // snippet:credentials
        using var oblodai = new OblodaiClient(new OblodaiOptions { PublicId = publicId, Secret = secret });
        // endsnippet
        return oblodai;
    }

    /// <summary>README: "Quick start" — an invoice, from a client configured by the environment.</summary>
    internal static async Task QuickStartPayment()
    {
        // snippet:quickstart-payment
        using var oblodai = new OblodaiClient(); // OBLODAI_PUBLIC_ID / OBLODAI_SECRET from the environment

        var invoice = await oblodai.Payments.CreateAsync(new PaymentRequest
        {
            Amount = "25",              // amounts are decimal strings, never floats
            Currency = "USDT",          // what you price in: a fiat (USD, EUR, …) or a crypto asset
            Network = Network.Tron,     // omit to let the payer choose the network on the pay page
            OrderId = "order-1001",     // your reference; the invoice is idempotent per order_id
            UrlCallback = "https://shop.example/oblodai/webhook",
        });

        Console.WriteLine($"{invoice.Url} {invoice.Address} {invoice.Status}"); // status: created
        // endsnippet

        // Pricing in fiat, mentioned in prose next to the snippet.
        await oblodai.Payments.CreateAsync(
            new PaymentRequest { Amount = "25", Currency = "USD", ToCurrency = "USDT" });
    }

    /// <summary>README: "Quick start" — a payout with a caller-supplied idempotency key.</summary>
    internal static async Task QuickStartPayout(OblodaiClient oblodai)
    {
        // snippet:quickstart-payout
        var payout = await oblodai.Payouts.CreateAsync(
            new PayoutRequest
            {
                Address = "TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx",
                Amount = "10",
                Currency = "USDT",
                Network = Network.Tron,
                OrderId = "payout-1",   // your reference; the payout is idempotent per order_id
            },
            new RequestOptions { IdempotencyKey = "payout-1" });

        Console.WriteLine($"{payout.Uuid} {payout.Status}"); // pending → … → confirmed
        // endsnippet
    }

    /// <summary>README: "Sandbox / testing" — faucet, invoice, simulated deposit.</summary>
    internal static async Task SandboxRun(string testPublicId, string testSecret)
    {
        // snippet:sandbox
        using var sandbox = new OblodaiClient(new OblodaiOptions { PublicId = testPublicId, Secret = testSecret });

        await sandbox.Sandbox.FaucetAsync(new SandboxFaucetRequest { Asset = "USDT", Amount = "1000" });

        var invoice = await sandbox.Payments.CreateAsync(new PaymentRequest
        {
            Amount = "25", Currency = "USDT", Network = Network.Tron, OrderId = "sandbox-1",
        });

        // No Amount pays exactly what is due; repeating a Txid adds confirmations instead of paying twice.
        var deposit = await sandbox.Sandbox.DepositAsync(new SandboxDepositRequest { InvoiceId = invoice.Uuid });

        Console.WriteLine($"{deposit.Txid} {deposit.Confirmations}");
        // endsnippet

        // The rehearsal delivery and the wipe, mentioned in prose next to the snippet.
        await sandbox.Webhooks.TestAsync(
            WebhookKind.Payment,
            new TestWebhookPaymentRequest { UrlCallback = "https://shop.example/oblodai/webhook" });
        await sandbox.Sandbox.ResetAsync();
    }

    /// <summary>README: "Lists" — one page, every item, and a collected list.</summary>
    internal static async Task Lists(OblodaiClient oblodai)
    {
        // snippet:lists
        Page<Payment> page = await oblodai.Payments.HistoryAsync(new PaymentHistoryRequest { Limit = 50 });
        Console.WriteLine($"{page.Items.Count} of {page.Paginate.Total}, more: {page.Paginate.HasPages}");

        await foreach (var payout in oblodai.Payouts.HistoryAsync(new PayoutHistoryRequest { Status = PayoutStatus.Confirmed }))
        {
            Console.WriteLine(payout.Uuid);
        }

        List<Payout> refunds = await oblodai.Payouts.HistoryAsync(new PayoutHistoryRequest { Kind = "refund" }).AllAsync(1000);
        // endsnippet
        Console.WriteLine(refunds.Count);
    }

    /// <summary>README: "Webhooks" — verify over the raw bytes, then dispatch on the event.</summary>
    internal static void WebhookReceiver(byte[] rawBody, Func<string, string?> header)
    {
        // snippet:webhooks
        var info = WebhookVerifier.VerifyDelivery(rawBody, header, new WebhookVerifyOptions
        {
            Secret = Environment.GetEnvironmentVariable("OBLODAI_WEBHOOK_SECRET")!,
        });

        if (info.IsTest) // a rehearsal delivery: signed like a live one, but no money moved
        {
            return;
        }

        switch (info.Event)
        {
            case PaymentEvent { Status.Value: "paid" } paid: MarkOrderPaid(paid.OrderId, info.Id); break;
            case PayoutEvent payout: Track(payout.Uuid, payout.Status); break;
            case WalletEvent deposit: Credit(deposit.Address, deposit.PaymentAmount); break;
        }
        // endsnippet
    }

    /// <summary>README: "Errors" — branch on the code, let the SDK's own retries stand.</summary>
    internal static async Task Errors(OblodaiClient oblodai, PayoutRequest request)
    {
        // snippet:errors
        try
        {
            var payout = await oblodai.Payouts.CreateAsync(request);
            Console.WriteLine($"{payout.Uuid} {payout.Status}");
        }
        catch (OblodaiException error)
            when (error.Code is ErrorCodes.PayoutInsufficientFunds or ErrorCodes.PayoutFundsMaturing)
        {
            ScheduleRetry(error.RetryAfter ?? 60); // retryable — the balance may still arrive
        }
        catch (OblodaiException error)
        {
            Log(error.Code, error.RequestId); // the SDK already retried whatever was safe to retry
        }
        // endsnippet
    }

    /// <summary>README: "Configuration" — the client behind an <c>IHttpClientFactory</c>.</summary>
    internal static void DependencyInjection(IServiceCollection services, string publicId, string secret)
    {
        // snippet:dependency-injection
        services.AddHttpClient("oblodai").ConfigurePrimaryHttpMessageHandler(
            () => new SocketsHttpHandler { AllowAutoRedirect = false });

        services.AddSingleton(sp => new OblodaiClient(
            new OblodaiOptions { PublicId = publicId, Secret = secret },
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("oblodai")));
        // endsnippet
    }

    /// <summary>What the last README snippet calls once an order is settled.</summary>
    internal static string? LastOrderPaid { get; private set; }

    /// <summary>What the error snippet scheduled.</summary>
    internal static int LastRetryDelay { get; private set; }

    private static void MarkOrderPaid(string? orderId, string? deliveryId)
        => LastOrderPaid = $"{orderId} {deliveryId}";

    private static void Track(string uuid, PayoutStatus status) => LastOrderPaid = $"{uuid} {status}";

    private static void Credit(string address, string amount) => LastOrderPaid = $"{address} {amount}";

    private static void ScheduleRetry(int seconds) => LastRetryDelay = seconds;

    private static void Log(string code, string? requestId) => LastOrderPaid = $"{code} {requestId}";
}

/// <summary>
/// The two READMEs must print the code <see cref="ReadmeSnippets"/> compiles — and print the same code
/// as each other, because a translation translates prose, never identifiers.
/// </summary>
public class ReadmeSnippetTests
{
    private const string EnglishReadme = "README.md";
    private const string RussianReadme = "README.ru.md";
    private const string MarkerStart = "// snippet:";
    private const string MarkerEnd = "// endsnippet";
    private const string SnippetIndent = "        ";

    private static readonly string SnippetSource =
        Path.Combine(Fixtures.RepoRoot, "tests", "Oblodai.Tests", "Unit", "ReadmeSnippetTests.cs");

    [Fact]
    public void EveryCSharpBlockInBothReadmesIsCodeThisSuiteCompiles()
    {
        var snippets = CompiledSnippets();
        Assert.NotEmpty(snippets);

        foreach (var readme in new[] { EnglishReadme, RussianReadme })
        {
            var blocks = FencedBlocks(readme).Where(b => b.Language == "csharp").ToList();
            Assert.True(
                blocks.Count == snippets.Count,
                $"{readme}: {blocks.Count} C# blocks, but the suite marks {snippets.Count} snippets");

            for (var i = 0; i < blocks.Count; i++)
            {
                Assert.True(
                    StripUsings(blocks[i].Body) == snippets[i].Body,
                    $"{readme}:{blocks[i].Line}: the block does not match snippet \"{snippets[i].Name}\"\n"
                    + $"--- README ---\n{StripUsings(blocks[i].Body)}\n--- compiled ---\n{snippets[i].Body}");
            }
        }
    }

    [Fact]
    public void TheRussianReadmeMirrorsTheEnglishOne()
    {
        var english = FencedBlocks(EnglishReadme);
        var russian = FencedBlocks(RussianReadme);

        Assert.True(
            english.Count == russian.Count,
            $"{EnglishReadme} has {english.Count} code blocks, {RussianReadme} has {russian.Count}");

        for (var i = 0; i < english.Count; i++)
        {
            Assert.True(
                english[i].Language == russian[i].Language && english[i].Body == russian[i].Body,
                $"code block {i + 1} differs between the READMEs (a translation must not translate code)\n"
                + $"--- {EnglishReadme}:{english[i].Line} ---\n{english[i].Body}\n"
                + $"--- {RussianReadme}:{russian[i].Line} ---\n{russian[i].Body}");
        }

        Assert.True(
            Headings(EnglishReadme).Count == Headings(RussianReadme).Count,
            $"{RussianReadme} has {Headings(RussianReadme).Count} H2 sections, {EnglishReadme} has "
            + $"{Headings(EnglishReadme).Count}: the translation must carry every section");
    }

    /// <summary>
    /// The snippet the README prints builds a working client from ONE pair — money-in and money-out
    /// namespaces alike. There is no second credential to pass, and no per-call choice to make.
    /// </summary>
    [Fact]
    public void TheCredentialsSnippetBuildsAWorkingClientFromOnePair()
    {
        var client = ReadmeSnippets.Credentials("oblodai_86b491a9cce7aa8b7ef0", "oblodai_live_s1");
        Assert.NotNull(client.Payments);
        Assert.NotNull(client.Payouts);
    }

    [Fact]
    public async Task ThePayoutSnippetSendsTheKeyItPrints()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Ok(Fixtures.ResultJson("POST /v1/payout")));
        using var oblodai = new OblodaiClient(
            new OblodaiOptions { PublicId = "oblodai_pay", Secret = "s", BaseUrl = "https://api.test" },
            handler.Client());

        await ReadmeSnippets.QuickStartPayout(oblodai);

        Assert.Equal("payout-1", handler.Calls.Single().Header("Idempotency-Key"));
    }

    [Fact]
    public async Task TheListSnippetsWalkTheirPages()
    {
        var payouts = Fixtures.ResultJson("POST /v1/payout/history");
        var handler = new FakeHttpHandler(
            ScriptedResponse.Ok(Fixtures.ResultJson("POST /v1/payment/history")),
            ScriptedResponse.Ok(payouts),
            ScriptedResponse.Ok(payouts));
        using var oblodai = new OblodaiClient(
            new OblodaiOptions { PublicId = "oblodai_pay", Secret = "s", BaseUrl = "https://api.test" },
            handler.Client());

        await ReadmeSnippets.Lists(oblodai);

        Assert.Equal(3, handler.Calls.Count);
    }

    [Fact]
    public async Task TheErrorSnippetCatchesWhatItSaysItCatches()
    {
        var handler = new FakeHttpHandler(ScriptedResponse.Error(
            409,
            """{"code":"payout.insufficient_funds","message":"top up","retryable":true,"retry_after":60}"""));
        using var oblodai = new OblodaiClient(
            new OblodaiOptions
            {
                PublicId = "oblodai_pay",
                Secret = "s",
                BaseUrl = "https://api.test",
                Retry = new RetryOptions { MaxRetries = 0 },
            },
            handler.Client());

        await ReadmeSnippets.Errors(
            oblodai,
            new PayoutRequest
            {
                Amount = "1",
                Currency = "USDT",
                Address = "TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx",
                OrderId = "o",
            });

        Assert.Equal(60, ReadmeSnippets.LastRetryDelay);
    }

    [Fact]
    public void TheDependencyInjectionSnippetResolvesAClient()
    {
        var services = new ServiceCollection();

        ReadmeSnippets.DependencyInjection(services, "oblodai_pay", "s");

        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<OblodaiClient>();
        Assert.NotNull(client.Payments);
    }

    [Fact]
    public void TheWebhookSnippetRunsOnARealSignedDelivery()
    {
        var sample = Fixtures.WebhookSamples.EnumerateArray().First();
        var rawBody = System.Text.Encoding.UTF8.GetBytes(sample.GetProperty("raw").GetString()!);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in sample.GetProperty("headers").EnumerateObject())
        {
            headers[header.Name] = header.Value.GetString()!;
        }

        var secret = Fixtures.For("POST /v1/webhooks/rotate-secret").Result.GetProperty("secret").GetString()!;

        // The snippet itself is compiled, not run: it reads the secret from the environment and honours
        // the freshness window, and the recorded deliveries are far older than any window worth having.
        // What runs here is the same door with the check switched off, on the same real signed body.
        var info = WebhookVerifier.VerifyDelivery(
            rawBody,
            name => headers.GetValueOrDefault(name),
            new WebhookVerifyOptions { Secret = secret, ToleranceSeconds = 0 });

        Assert.True(WebhookVerifier.IsKnownEvent(info.Event));
        Assert.True(info.Event is PaymentEvent or PayoutEvent or WalletEvent);
    }

    /// <summary>
    /// The environment vocabulary the two READMEs and <c>.env.example</c> print is exactly the one
    /// <see cref="OblodaiOptions.Resolve"/> reads. A doc naming a variable the SDK stopped reading —
    /// <c>OBLODAI_PAYOUT_PUBLIC_ID</c> and its secret, from the two-key era — sends a reader to
    /// configure a container that then signs nothing.
    /// </summary>
    [Theory]
    [InlineData(EnglishReadme)]
    [InlineData(RussianReadme)]
    [InlineData(".env.example")]
    public void TheDocumentedEnvironmentVariablesAreTheOnesTheSdkReads(string file)
    {
        var text = File.ReadAllText(Path.Combine(Fixtures.RepoRoot, file));
        var named = Regex.Matches(text, @"OBLODAI_[A-Z_]+")
            .Select(m => m.Value)
            .Where(name => name is not ("OBLODAI_LIVE_URL" or "OBLODAI_WEBHOOK_SECRET"
                or "OBLODAI_WEBHOOK_PREVIOUS_SECRET"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new[]
            {
                "OBLODAI_ADMIN_TOKEN",
                "OBLODAI_ALLOW_INSECURE",
                "OBLODAI_BASE_URL",
                "OBLODAI_LOG",
                "OBLODAI_PUBLIC_ID",
                "OBLODAI_SECRET",
            },
            named.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Every option the READMEs tabulate is a real <see cref="OblodaiOptions"/> property. The payout
    /// credential pair and <c>PreferPayoutKey</c> are gone from the type, so a table row still offering
    /// them would be a setting a reader cannot set.
    /// </summary>
    [Theory]
    [InlineData(EnglishReadme)]
    [InlineData(RussianReadme)]
    public void TheOptionsTheReadmesTabulateAreRealProperties(string file)
    {
        var properties = typeof(OblodaiOptions).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var rows = OptionTableRows(File.ReadAllLines(Path.Combine(Fixtures.RepoRoot, file)));

        Assert.NotEmpty(rows);
        foreach (var name in rows)
        {
            Assert.True(
                properties.Contains(name),
                $"{file}: the options table names \"{name}\", which is not a property of OblodaiOptions");
        }

        // And the reverse: every option the type offers is documented, so a table cannot go stale by
        // omission either. The logger and the clock are wiring, not configuration a reader copies.
        Assert.Empty(properties.Except(rows, StringComparer.Ordinal));

        Assert.DoesNotContain("PreferPayoutKey", File.ReadAllText(Path.Combine(Fixtures.RepoRoot, file)));
    }

    [Fact]
    public void TheMoneyHelpersTheReadmeNamesAreExact()
    {
        Assert.Equal("0.3", Money.Add("0.1", "0.2"));
        Assert.Equal("0.1", Money.Subtract("0.3", "0.2"));
        Assert.Equal(-1, Money.Compare("0.1", "0.2"));
        Assert.True(Money.AreEqual("25", "25.000000"));
        Assert.True(Money.IsZero("0.000000"));
    }

    /// <summary>
    /// The rows of the Configuration section's option table — the one whose header names the option
    /// column — and nothing from the other tables the READMEs print.
    /// </summary>
    /// <param name="lines">The README, line by line.</param>
    private static List<string> OptionTableRows(string[] lines)
    {
        var rows = new List<string>();
        var inside = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("| Option", StringComparison.Ordinal)
                || line.StartsWith("| \u041e\u043f\u0446\u0438\u044f", StringComparison.Ordinal))
            {
                inside = true;
                continue;
            }

            if (!inside)
            {
                continue;
            }

            if (!line.StartsWith("| `", StringComparison.Ordinal))
            {
                if (line.StartsWith("| -", StringComparison.Ordinal))
                {
                    continue;
                }

                break;
            }

            var firstCell = line[1..line.IndexOf('|', 1)];
            rows.AddRange(Regex.Matches(firstCell, "`([A-Za-z]+)`").Select(m => m.Groups[1].Value));
        }

        return rows;
    }

    /// <summary>The marked regions of this file, dedented by the indentation a method body adds.</summary>
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
                current.Add(line.StartsWith(SnippetIndent, StringComparison.Ordinal)
                    ? line[SnippetIndent.Length..]
                    : line);
            }
        }

        Assert.False(inside, $"snippet \"{name}\" is never closed with \"{MarkerEnd}\"");
        return snippets;
    }

    /// <summary>The fenced code blocks of a Markdown file, in order.</summary>
    private static List<(string Language, string Body, int Line)> FencedBlocks(string file)
    {
        var lines = File.ReadAllText(Path.Combine(Fixtures.RepoRoot, file)).Split('\n');
        var blocks = new List<(string, string, int)>();
        var current = new List<string>();
        var language = string.Empty;
        var start = 0;
        var inside = false;

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

    /// <summary>
    /// Drops the <c>using</c> directives a README block carries for the reader: a compiled snippet lives
    /// inside a method body, where they cannot appear.
    /// </summary>
    private static string StripUsings(string body)
    {
        var lines = body.Split('\n').ToList();
        while (lines.Count > 0
               && (lines[0].Length == 0 || Regex.IsMatch(lines[0], @"^using [\w.]+;$")))
        {
            lines.RemoveAt(0);
        }

        return string.Join("\n", lines);
    }

    /// <summary>The H2 headings of a Markdown file, ignoring anything inside a fenced block.</summary>
    private static List<string> Headings(string file)
    {
        var headings = new List<string>();
        var inside = false;
        foreach (var line in File.ReadAllText(Path.Combine(Fixtures.RepoRoot, file)).Split('\n'))
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
