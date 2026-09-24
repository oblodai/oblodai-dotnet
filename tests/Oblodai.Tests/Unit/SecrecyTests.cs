using System.Text.Json;
using Oblodai.Resources;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// A secret must survive being printed. Records print every member by default, so
/// <c>logger.LogInformation("{Options}", options)</c> would leak an API key, a webhook secret or a
/// claim token; options are also kept out of JSON.
/// </summary>
public class SecrecyTests
{
    private const string Secret = "sk_live_do_not_print_me";

    public static TheoryData<object> SecretBearers() =>
    [
        new OblodaiOptions { PublicId = "pk", Secret = Secret, AdminToken = Secret },
        new OblodaiOptions { PublicId = "pk", Secret = Secret }.Resolve(_ => null),
        new Credentials("pk", Secret),
        new TransportOptions { BaseUrl = "https://api.test", UserAgent = "ua", AdminToken = Secret },
        new WebhookVerifyOptions { Secret = Secret, PreviousSecret = Secret },
    ];

    /// <summary>Generated models whose wire fields carry a secret: they print redacted.</summary>
    public static TheoryData<Model> SecretModels() =>
    [
        Parse<RegisterWebhookResult>($$"""{"endpoint_id":"e","url":"https://x.test","secret":"{{Secret}}"}"""),
        Parse<RotateWebhookSecretResult>($$"""{"endpoint_id":"e","secret":"{{Secret}}"}"""),
        Parse<OnboardKey>($$"""{"public_id":"pk","secret":"{{Secret}}"}"""),
        Parse<PayoutLinkCreated>(
            $$"""{"link_id":"l","claim_token":"{{Secret}}","claim_url":"https://x.test/c/{{Secret}}","passcode":"{{Secret}}"}"""),
    ];

    [Theory]
    [MemberData(nameof(SecretBearers))]
    public void NeitherToStringNorJsonEverPrintsASecret(object bearer)
    {
        Assert.DoesNotContain(Secret, bearer.ToString());
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(bearer, bearer.GetType(), OblodaiJson.Options));
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(bearer, bearer.GetType()));
    }

    [Theory]
    [MemberData(nameof(SecretModels))]
    public void AModelPrintsItsSecretsRedactedButStillHandsThemOver(Model model)
    {
        var text = model.ToString();

        Assert.DoesNotContain(Secret, text);
        Assert.Contains(Redaction.Placeholder, text);
        Assert.StartsWith(model.GetType().Name + " { ", text);

        // The property itself is the value: the code that stores a secret reads it from there.
        Assert.Contains(Secret, JsonSerializer.Serialize(model, model.GetType(), OblodaiJson.Options));
    }

    [Fact]
    public void TheRedactedFormStillNamesTheFieldsSoAConfigurationBugIsStillDebuggable()
    {
        var text = new OblodaiOptions { PublicId = "pk_test", Secret = Secret, BaseUrl = "https://api.test" }.ToString();

        Assert.Contains("PublicId = pk_test", text);
        Assert.Contains("BaseUrl = https://api.test", text);
        Assert.Contains($"Secret = {Redaction.Placeholder}", text);

        // An absent secret prints as absent, so "[redacted]" never implies a value that is not there.
        Assert.Contains("AdminToken = null", text);
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

        await client.Account.GetBalanceAsync();

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
                Timeout = TimeSpan.FromSeconds(30),
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

    private static T Parse<T>(string json)
        => JsonSerializer.Deserialize<T>(json, OblodaiJson.Options)!;

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
