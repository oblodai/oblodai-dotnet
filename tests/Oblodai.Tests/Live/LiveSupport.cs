using Oblodai.Contract;
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
/// A freshly provisioned merchant with a sandbox key on the gateway under test. Onboarding goes through
/// the SDK itself (`POST /v1/merchants` then `/sandbox`), so the unsigned provisioning routes are
/// exercised too.
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

    /// <summary>The merchant this run provisioned.</summary>
    public string MerchantId { get; private set; } = string.Empty;

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

        var merchant = await _anonymous.Merchants.CreateAsync(new MerchantsRequest
        {
            Email = $"{Unique("sdk-dotnet")}@example.com",
            Name = "SDK dotnet live",
        });
        MerchantId = merchant.MerchantId;

        var sandbox = await _anonymous.Merchants.CreateSandboxAsync(merchant.MerchantId);
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

    /// <summary>
    /// Runs a call that may be refused for business reasons on this stand (a feature that is off, an
    /// object in the wrong state). A shape problem — our body rejected, or an answer we cannot decode —
    /// still fails the test.
    /// </summary>
    public static async Task<T?> AcceptRefusalAsync<T>(Task<T> call)
        where T : class
    {
        try
        {
            return await call;
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (ContractException)
        {
            throw;
        }
        catch (ApiException)
        {
            return null;
        }
    }
}
