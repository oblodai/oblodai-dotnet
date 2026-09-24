using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Oblodai.Tests.Live;

/// <summary>A fact that only runs when <c>OBLODAI_LIVE_URL</c> points at a running gateway.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (string.IsNullOrEmpty(LiveEnvironment.BaseUrl))
        {
            Skip = "set OBLODAI_LIVE_URL to run the live tier (e.g. http://127.0.0.1:8095)";
        }
    }
}

/// <summary>Runs the steps of a live journey in the order the money moves.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StepAttribute : Attribute
{
    public StepAttribute(int order) => Order = order;

    public int Order { get; }
}

/// <summary>Orders test cases by their <see cref="StepAttribute"/>.</summary>
public sealed class StepOrderer : ITestCaseOrderer
{
    /// <summary>Type name for <see cref="TestCaseOrdererAttribute"/>.</summary>
    public const string TypeName = "Oblodai.Tests.Live.StepOrderer";

    /// <summary>Assembly name for <see cref="TestCaseOrdererAttribute"/>.</summary>
    public const string AssemblyName = "Oblodai.Tests";

    /// <inheritdoc />
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
        => testCases.OrderBy(testCase => OrderOf(testCase)).ThenBy(t => t.TestMethod.Method.Name, StringComparer.Ordinal);

    private static int OrderOf(ITestCase testCase)
        => testCase.TestMethod.Method
            .GetCustomAttributes(typeof(StepAttribute))
            .FirstOrDefault()
            ?.GetNamedArgument<int>(nameof(StepAttribute.Order)) ?? 0;
}

/// <summary>
/// A freshly provisioned merchant with a sandbox key on the gateway under test. Onboarding is open on a
/// dev stand: <c>POST /v1/merchants</c> (not part of the merchant API, so plain HTTP here) and then the
/// SDK's own <c>Sandbox.OnboardStoreAsync</c>, which mints the sandbox key pair.
/// </summary>
public class LiveEnvironment : IAsyncLifetime
{
    /// <summary>The gateway under test, from <c>OBLODAI_LIVE_URL</c>.</summary>
    public static readonly string? BaseUrl = Environment.GetEnvironmentVariable("OBLODAI_LIVE_URL");

    /// <summary>A reachable webhook receiver, when the stand has one.</summary>
    public static readonly string? HookUrl = Environment.GetEnvironmentVariable("OBLODAI_LIVE_HOOK_URL");

    /// <summary>An address that is valid on Tron, used for every payout in the journey.</summary>
    public const string Address = "TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx";

    private OblodaiClient? _merchant;
    private OblodaiClient? _anonymous;

    /// <summary>The merchant client, signed with the sandbox key.</summary>
    public OblodaiClient Merchant => _merchant ?? throw new InvalidOperationException("live environment not initialised");

    /// <summary>A client with no credentials, for the payer-facing routes.</summary>
    public OblodaiClient Anonymous => _anonymous ?? throw new InvalidOperationException("live environment not initialised");

    /// <summary>Something unique per run, for order ids and references.</summary>
    public static string Unique(string prefix) => $"{prefix}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Random.Shared.Next(1000, 9999)}";

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(BaseUrl))
        {
            return;
        }

        var options = new OblodaiOptions { BaseUrl = BaseUrl, AllowInsecureBaseUrl = true };
        _anonymous = new OblodaiClient(options);

        using var http = new HttpClient();
        var created = await http.PostAsync(
            $"{BaseUrl.TrimEnd('/')}/v1/merchants",
            new StringContent(
                $"{{\"email\":\"{Unique("sdk-dotnet")}@example.com\",\"name\":\"SDK dotnet live\"}}",
                System.Text.Encoding.UTF8,
                "application/json"));
        using var merchant = System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var merchantId = merchant.RootElement.GetProperty("result").GetProperty("merchant_id").GetString()!;

        var sandbox = await _anonymous.Sandbox.OnboardStoreAsync(merchantId);
        _merchant = new OblodaiClient(options with
        {
            PublicId = sandbox.ApiKey.PublicId,
            Secret = sandbox.ApiKey.Secret,
        });
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _merchant?.Dispose();
        _anonymous?.Dispose();
        return Task.CompletedTask;
    }
}
