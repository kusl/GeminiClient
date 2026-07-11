// GeminiClient.Tests/GeminiErrorParserTests.cs
using System.Net;
using GeminiClient;
using Xunit;

namespace GeminiClient.Tests;

public class GeminiErrorParserTests
{
    [Theory]
    [InlineData("42s", 42)]
    [InlineData("0s", 0)]
    [InlineData("7", 7)]
    public void ParseDuration_ParsesWholeSeconds(string value, double expectedSeconds)
    {
        TimeSpan? result = GeminiErrorParser.ParseDuration(value);
        Assert.NotNull(result);
        Assert.Equal(expectedSeconds, result!.Value.TotalSeconds, precision: 3);
    }

    [Fact]
    public void ParseDuration_ParsesFractionalSeconds()
    {
        TimeSpan? result = GeminiErrorParser.ParseDuration("42.4s");
        Assert.NotNull(result);
        Assert.Equal(42.4, result!.Value.TotalSeconds, precision: 1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-duration")]
    public void ParseDuration_ReturnsNullForInvalidInput(string? value)
    {
        Assert.Null(GeminiErrorParser.ParseDuration(value));
    }

    [Theory]
    [InlineData("You exceeded your quota. limit: 0. Upgrade.", true)]
    [InlineData("LIMIT: 0 today", true)]
    [InlineData("Rate limited, retry soon", false)]
    public void IsQuotaZero_DetectsZeroTierLimit(string message, bool expected)
    {
        Assert.Equal(expected, GeminiErrorParser.IsQuotaZero(message));
    }

    [Fact]
    public void FromHttpResponse_RateLimitedWithRetryInfo_IsTransientAndCarriesRetryAfter()
    {
        const string body =
            """{"error":{"code":429,"message":"Resource exhausted. Please retry in 42.4s.","status":"RESOURCE_EXHAUSTED","details":[{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"42s"}]}}""";

        GeminiApiException ex = GeminiErrorParser.FromHttpResponse(HttpStatusCode.TooManyRequests, body);

        Assert.Equal(GeminiErrorKind.RateLimited, ex.Kind);
        Assert.True(ex.IsTransient);
        Assert.False(ex.IsQuotaZero);
        Assert.NotNull(ex.RetryAfter);
        Assert.Equal(42, ex.RetryAfter!.Value.TotalSeconds, precision: 0);
        Assert.Equal("RESOURCE_EXHAUSTED", ex.ApiStatus);
    }

    [Fact]
    public void FromHttpResponse_QuotaZero_IsNotTransient()
    {
        const string body =
            """{"error":{"code":429,"message":"Quota exceeded. limit: 0.","status":"RESOURCE_EXHAUSTED"}}""";

        GeminiApiException ex = GeminiErrorParser.FromHttpResponse(HttpStatusCode.TooManyRequests, body);

        Assert.Equal(GeminiErrorKind.RateLimited, ex.Kind);
        Assert.True(ex.IsQuotaZero);
        Assert.False(ex.IsTransient); // retrying a zero-tier limit is pointless
    }

    [Fact]
    public void FromHttpResponse_ServiceUnavailable_IsTransient()
    {
        const string body = """{"error":{"code":503,"message":"overloaded","status":"UNAVAILABLE"}}""";
        GeminiApiException ex = GeminiErrorParser.FromHttpResponse(HttpStatusCode.ServiceUnavailable, body);

        Assert.Equal(GeminiErrorKind.ServiceUnavailable, ex.Kind);
        Assert.True(ex.IsTransient);
    }

    [Theory]
    [InlineData(400, GeminiErrorKind.BadRequest, false)]
    [InlineData(401, GeminiErrorKind.Unauthorized, false)]
    [InlineData(403, GeminiErrorKind.Unauthorized, false)]
    [InlineData(500, GeminiErrorKind.ServerError, true)]
    public void FromHttpResponse_MapsStatusCodesToKinds(int status, GeminiErrorKind expectedKind, bool expectedTransient)
    {
        GeminiApiException ex = GeminiErrorParser.FromHttpResponse((HttpStatusCode)status, body: null);
        Assert.Equal(expectedKind, ex.Kind);
        Assert.Equal(expectedTransient, ex.IsTransient);
    }
}
