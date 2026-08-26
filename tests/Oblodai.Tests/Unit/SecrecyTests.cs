using System.Text.Json;
using Oblodai.Models;
using Oblodai.Resources;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// A secret must survive being printed. Records print every member by default and
/// <see cref="JsonSerializer"/> writes every property, so the two ways an object most often reaches a
/// log — <c>logger.LogInformation("{Options}", options)</c> and a structured logger serializing its
/// fields — are exactly the two that would leak an API key, a webhook secret or a claim token.
/// </summary>
public class SecrecyTests
{
    private const string Secret = "sk_live_do_not_print_me";

    public static TheoryData<object> SecretBearers() =>
    [
        new OblodaiOptions { PublicId = "pk", Secret = Secret, PayoutSecret = Secret, AdminToken = Secret },
        new OblodaiOptions { PublicId = "pk", Secret = Secret }.Resolve(_ => null),
        new Credentials("pk", Secret),
        new TransportOptions { BaseUrl = "https://api.test", UserAgent = "ua", AdminToken = Secret },
        new WebhookVerifyOptions { Secret = Secret, PreviousSecret = Secret },
        new WebhookEndpoint { EndpointId = "e", Url = "https://x.test", Secret = Secret },
        new WebhookSecretRotated { EndpointId = "e", Url = "https://x.test", Secret = Secret },
        new ApiKeyPair { PublicId = "pk", Secret = Secret, Kind = "api" },
        new PayoutLink { LinkId = "l", ClaimToken = Secret, ClaimUrl = $"https://x.test/c/{Secret}", Passcode = Secret },
    ];

    [Theory]
    [MemberData(nameof(SecretBearers))]
    public void NeitherToStringNorJsonEverPrintsASecret(object bearer)
    {
        Assert.DoesNotContain(Secret, bearer.ToString());
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(bearer, bearer.GetType(), OblodaiJson.Options));
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(bearer, bearer.GetType()));
    }

    [Fact]
    public void TheRedactedFormStillNamesTheFieldsSoAConfigurationBugIsStillDebuggable()
    {
        var text = new OblodaiOptions { PublicId = "pk_test", Secret = Secret, BaseUrl = "https://api.test" }.ToString();

        Assert.Contains("PublicId = pk_test", text);
        Assert.Contains("BaseUrl = https://api.test", text);
        Assert.Contains($"Secret = {Redaction.Placeholder}", text);

        // An absent secret prints as absent, so "[redacted]" never implies a value that is not there.
        Assert.Contains("PayoutSecret = null", text);
    }

    [Fact]
    public void ThePropertyStillHandsTheSecretOverToWhoeverAsksForIt()
    {
        var rotated = new WebhookSecretRotated { Secret = Secret };
        var link = new PayoutLink { ClaimToken = Secret, ClaimUrl = "https://x.test/c/tok" };

        Assert.Equal(Secret, rotated.Secret);
        Assert.Equal(Secret, link.ClaimToken);

        // And there is one explicit, un-loggable way to serialize the real values, for the code that
        // stores a secret or mails a claim URL.
        Assert.Contains(Secret, OblodaiJson.SerializeWithSecrets(rotated));
        Assert.Contains("https://x.test/c/tok", OblodaiJson.SerializeWithSecrets(link));

        // Revealing is scoped to the one call: the default path is redacted again straight after.
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(rotated, OblodaiJson.Options));
    }

    [Fact]
    public void ASecretReadFromTheWireIsStillReadable()
    {
        var body = $$"""{"endpoint_id":"e","url":"https://x.test","secret":"{{Secret}}"}""";

        var endpoint = JsonSerializer.Deserialize<WebhookEndpoint>(body, OblodaiJson.Options)!;

        Assert.Equal(Secret, endpoint.Secret);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(endpoint, OblodaiJson.Options));
    }

    [Fact]
    public async Task TheLoggerNeverSeesASecretBecauseRedactionHappensBeforeTheCall()
    {
        var logger = new CapturingLogger();
        var handler = new FakeHttpHandler(ScriptedResponse.Ok("""{"balance":{"merchant":[]}}"""));
        using var client = new OblodaiClient(
            new OblodaiOptions
            {
                PublicId = "pk",
                Secret = Secret,
                BaseUrl = "https://api.test",
                Logger = logger,
                Headers = new Dictionary<string, string> { ["X-Trace"] = "t1" },
            },
            handler.Client());

        await client.Account.BalanceAsync();

        Assert.NotEmpty(logger.Lines);
        Assert.DoesNotContain(logger.Lines, line => line.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public void AnInjectedClientWithAFiniteTimeoutIsCalledOutWithBothNumbers()
    {
        var logger = new CapturingLogger();
        var handler = new FakeHttpHandler(ScriptedResponse.Ok());

        using var client = new OblodaiClient(
            new OblodaiOptions
            {
                PublicId = "pk",
                Secret = "s",
                BaseUrl = "https://api.test",
                TimeoutMs = 30_000,
                Logger = logger,
            },
            handler.Client(TimeSpan.FromSeconds(5)));

        var warning = Assert.Single(logger.Lines, l => l.StartsWith("Warn ", StringComparison.Ordinal));
        Assert.Contains("finite Timeout", warning);
        Assert.Contains("httpClientTimeoutMs=5000", warning);
        Assert.Contains("sdkTimeoutMs=30000", warning);
    }

    [Fact]
    public void AClientThatOwnsItsHttpClientHasNothingToWarnAbout()
    {
        var logger = new CapturingLogger();
        using var client = new OblodaiClient(new OblodaiOptions
        {
            PublicId = "pk",
            Secret = "s",
            BaseUrl = "https://api.test",
            Logger = logger,
        });

        Assert.DoesNotContain(logger.Lines, l => l.Contains("Timeout", StringComparison.Ordinal));
    }

    private sealed class CapturingLogger : IOblodaiLogger
    {
        public List<string> Lines { get; } = [];

        public void Log(OblodaiLogLevel level, string message, IReadOnlyDictionary<string, object?>? fields = null)
        {
            var rendered = fields is null
                ? string.Empty
                : string.Join(" ", LogRedaction.Redact(fields).Select(kv => $"{kv.Key}={kv.Value}"));
            Lines.Add($"{level} {message} {rendered}");
        }
    }
}
