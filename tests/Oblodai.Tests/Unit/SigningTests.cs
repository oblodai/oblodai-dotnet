using System.Text;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// Properties of the signing recipe beyond the gateway's vectors (those are checked by the conformance
/// suite, straight from <c>x-oblodai-signing</c> of the backend's <c>openapi.json</c>).
/// </summary>
public class SigningTests
{
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
}
