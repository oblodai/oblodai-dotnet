using System.Text;
using System.Text.Json;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// The signing recipe, checked against the vectors the gateway's own test suite exported. If any of
/// these fail, every signed call would come back 401 — this is the first test to look at.
/// </summary>
public class SigningTests
{
    public static TheoryData<string> SigningVectorNames()
    {
        var data = new TheoryData<string>();
        foreach (var vector in Fixtures.Contract.GetProperty("signing_vectors").EnumerateArray())
        {
            data.Add(vector.GetProperty("name").GetString()!);
        }

        return data;
    }

    public static TheoryData<int> WebhookVectorIndexes()
    {
        var data = new TheoryData<int>();
        var count = Fixtures.Contract.GetProperty("webhook_vectors").GetArrayLength();
        for (var i = 0; i < count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SigningVectorNames))]
    public void MatchesTheGatewaySigningVectors(string name)
    {
        var vector = Fixtures.Contract.GetProperty("signing_vectors").EnumerateArray()
            .First(v => v.GetProperty("name").GetString() == name);

        var ts = vector.GetProperty("ts").GetInt64();
        var method = vector.GetProperty("method").GetString()!;
        var requestUri = vector.GetProperty("request_uri").GetString()!;
        var idempotencyKey = vector.GetProperty("idempotency_key").GetString()!;
        var body = vector.GetProperty("body").GetString()!;

        Assert.Equal(
            vector.GetProperty("canonical").GetString(),
            RequestSigner.CanonicalString(ts, method, requestUri, idempotencyKey, body));
        Assert.Equal(
            vector.GetProperty("signature").GetString(),
            RequestSigner.Sign(vector.GetProperty("secret").GetString()!, ts, method, requestUri, idempotencyKey, body));
    }

    [Fact]
    public void TheIdempotencySlotIsEmptyNotAbsentWhenNoKeyIsSent()
    {
        var withEmpty = RequestSigner.Sign("s", 1, "POST", "/v1/x", string.Empty, "{}");
        var withNull = RequestSigner.Sign("s", 1, "POST", "/v1/x", null, "{}");

        Assert.Equal(withEmpty, withNull);
        Assert.Equal("1\nPOST\n/v1/x\n\n{}", RequestSigner.CanonicalString(1, "POST", "/v1/x", null, "{}"));
    }

    [Fact]
    public void SignsTheBodyBytesSoTextAndBytesAgree()
    {
        const string body = """{"additional_data":"café 日本語 🚀"}""";
        Assert.Equal(
            RequestSigner.Sign("s", 5, "POST", "/v1/payment", null, body),
            RequestSigner.Sign("s", 5, "POST", "/v1/payment", null, Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public void UpperCasesTheMethodAsTheGatewayDoes()
        => Assert.Equal(
            RequestSigner.Sign("s", 5, "POST", "/v1/payment", null, "{}"),
            RequestSigner.Sign("s", 5, "post", "/v1/payment", null, "{}"));

    [Theory]
    [MemberData(nameof(WebhookVectorIndexes))]
    public void MatchesTheGatewayWebhookVectors(int index)
    {
        var vector = Fixtures.Contract.GetProperty("webhook_vectors")[index];
        Assert.Equal(
            vector.GetProperty("signature").GetString(),
            RequestSigner.SignWebhook(
                vector.GetProperty("secret").GetString()!,
                vector.GetProperty("ts").GetInt64(),
                vector.GetProperty("payload").GetString()!));
    }

    [Fact]
    public void TheSnapshotDeclaresTheRecipeThisSdkImplements()
    {
        var signing = Fixtures.Contract.GetProperty("signing");
        Assert.Equal("ts\\nMETHOD\\nrequest_uri\\nidempotency_key\\nbody", signing.GetProperty("canonical").GetString());
        Assert.Equal(RequestSigner.SignatureSkewSeconds, signing.GetProperty("skew_seconds").GetInt32());
        Assert.Equal(Idempotency.MaxKeyLength, signing.GetProperty("max_idempotency_key_length").GetInt32());

        var headers = signing.GetProperty("headers").EnumerateArray().Select(h => h.GetString()).ToList();
        Assert.Contains(RequestSigner.HeaderPublicId, headers);
        Assert.Contains(RequestSigner.HeaderSignature, headers);
        Assert.Contains(RequestSigner.HeaderTimestamp, headers);
        Assert.Contains(RequestSigner.HeaderIdempotencyKey, headers);
    }
}
