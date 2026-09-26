using Oblodai.Contract;
using Oblodai.Tests.Support;
using Xunit;

namespace Oblodai.Tests.Unit;

/// <summary>
/// What the decoder does with an error envelope that is not shaped the way the documentation says. A
/// gateway behind a proxy, a partial deploy or a plain bug can all produce one, and the answer must
/// still classify as the HTTP failure it is — a decoding crash turns a retryable 503 into an unhandled
/// exception in the caller's request path.
/// </summary>
public class EnvelopeHardeningTests
{
    public static TheoryData<string> DeviantEnvelopes() =>
    [
        """{"error":{"code":404,"message":"gone"}}""",
        """{"error":{"code":"payment.not_found","message":["gone"]}}""",
        """{"error":{"code":"payment.not_found","retryable":"yes"}}""",
        """{"error":{"code":"payment.not_found","retry_after":1e30}}""",
        """{"error":{"code":"payment.not_found","field":{"name":"amount"}}}""",
        """{"error":{"code":"payment.not_found","request_id":42}}""",
        """{"error":{"code":null}}""",
        """{"error":{}}""",
        """{"error":{"code":"payment.not_found","retry_after":{"seconds":5}}}""",
        """{"error":{"code":"payment.not_found","retry_after":-9007199254740993}}""",
    ];

    [Theory]
    [MemberData(nameof(DeviantEnvelopes))]
    public void ADeviantErrorObjectStillClassifiesAsTheHttpFailure(string body)
    {
        var decoded = EnvelopeDecoder.Decode(503, body);

        Assert.False(decoded.Ok);
        var error = decoded.Error!;
        Assert.Equal(503, error.HttpStatus);
        Assert.True(error.Retryable, "a 503 must stay retryable however malformed its envelope is");
        Assert.True(error.RetryAfter is null or >= 0);
    }

    [Fact]
    public void FieldsOfTheWrongTypeAreTreatedAsAbsentNotAsAReasonToThrow()
    {
        var decoded = EnvelopeDecoder.Decode(
            400, """{"error":{"code":"payment.bad_amount","message":7,"field":[1],"retryable":"no","request_id":42}}""");

        var error = decoded.Error!;
        Assert.Equal("payment.bad_amount", error.Code);
        Assert.Equal("request failed with HTTP 400 (payment.bad_amount)", error.Description);
        Assert.Equal("[payment.bad_amount] request failed with HTTP 400 (payment.bad_amount)", error.Message);
        Assert.Null(error.Field);
        Assert.Null(error.RequestId);
        Assert.False(error.Retryable); // 400 is not in the transient set
    }

    [Fact]
    public void DetailsKeepOnlyStringValues()
    {
        var denied = EnvelopeDecoder.Decode(
            403,
            """{"error":{"code":"cli.permission_denied","retryable":false,"details":{"required_role":"finance","role":"viewer","n":3,"x":null}}}""").Error!;
        Assert.Equal(
            new Dictionary<string, string> { ["required_role"] = "finance", ["role"] = "viewer" },
            denied.Details);
        Assert.Same(denied.Details, denied.ToLogRecord()["details"]);

        var list = EnvelopeDecoder.Decode(
            403, """{"error":{"code":"cli.permission_denied","details":["finance"]}}""").Error!;
        Assert.Null(list.Details);
        Assert.Null(EnvelopeDecoder.Decode(403, """{"error":{"code":"cli.permission_denied"}}""").Error!.Details);
    }

    [Fact]
    public void AnErrorObjectWithoutAUsableCodeKeepsTheRequestIdItDidCarry()
    {
        var decoded = EnvelopeDecoder.Decode(500, """{"error":{"code":"","request_id":"req-9"}}""");

        var error = decoded.Error!;
        Assert.True(error.Synthetic);
        Assert.Equal("req-9", error.RequestId);
        Assert.Equal("internal", error.Code);
    }

    [Theory]
    [InlineData("""{"error":{"code":"request.rate_limited","retry_after":7}}""", 7)]
    [InlineData("""{"error":{"code":"request.rate_limited","retry_after":"7"}}""", 7)]
    [InlineData("""{"error":{"code":"request.rate_limited","retry_after":6.2}}""", 7)]
    [InlineData("""{"error":{"code":"request.rate_limited","retry_after":-5}}""", 0)]
    [InlineData("""{"error":{"code":"request.rate_limited","retry_after":2147483647}}""", 86_400)]
    public void RetryAfterIsReadWidelyAndClamped(string body, int expected)
    {
        var error = EnvelopeDecoder.Decode(429, body).Error!;
        Assert.Equal(expected, error.RetryAfter);
    }

    [Theory]
    [InlineData("120", 120)]
    [InlineData("  120  ", 120)]
    [InlineData("999999999999", 86_400)]
    [InlineData("-1", 0)]
    [InlineData("not-a-date", null)]
    [InlineData("", null)]
    public void TheRetryAfterHeaderNeverOverflowsAndNeverGoesNegative(string header, int? expected)
        => Assert.Equal(expected, EnvelopeDecoder.ParseRetryAfter(header, DateTimeOffset.UnixEpoch));

    [Fact]
    public void AFarFutureHttpDateClampsInsteadOfWrappingToANegativePause()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // (int)Math.Ceiling of this delta is negative; a negative pause means no pause at all.
        var seconds = EnvelopeDecoder.ParseRetryAfter("Fri, 31 Dec 9999 23:59:59 GMT", now);

        Assert.Equal(EnvelopeDecoder.MaxRetryAfterSeconds, seconds);
    }

    [Fact]
    public void ARetryAfterInSecondsCannotOverflowIntoANegativeDelayInMilliseconds()
    {
        var error = new RateLimitException(new ApiErrorInit(
            "request.rate_limited", "slow down", 429, Retryable: true, RetryAfter: int.MaxValue));

        var delay = RetryPolicy.DelayMs(error, 0, new RetryOptions { MaxRetryAfterMs = 30_000 });

        Assert.Equal(30_000, delay);
    }

    [Fact]
    public void TheBodyItselfNeverReachesTheMessageOfTheError()
    {
        const string secretish = "card 4111111111111111 belonging to Ada Lovelace";
        var decoded = EnvelopeDecoder.Decode(502, $"<html>{secretish}</html>");

        var error = decoded.Error!;
        Assert.DoesNotContain("4111111111111111", error.Message);
        Assert.DoesNotContain("Ada", error.Message);
        Assert.DoesNotContain(secretish, error.ToString());
        Assert.DoesNotContain(secretish, error.ToJson());
        Assert.Contains("markup", error.Message);
    }

    [Fact]
    public void AnErrorPrintsItsCodeAndKeepsTheStackAndInnerException()
    {
        var inner = new InvalidOperationException("socket closed");
        Exception thrown;
        try
        {
            throw new TransportException(SdkErrorCodes.TransportNetwork, "network error", inner);
        }
        catch (TransportException error)
        {
            thrown = error;
        }

        var text = thrown.ToString();
        Assert.Contains(SdkErrorCodes.TransportNetwork, text);
        Assert.Contains("socket closed", text);
        Assert.Contains(nameof(AnErrorPrintsItsCodeAndKeepsTheStackAndInnerException), text);
    }
}
