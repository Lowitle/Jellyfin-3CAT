using System.Net;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the retry rules used when 3Cat throttles or fails a download.
/// </summary>
public sealed class HttpRetryPolicyTests
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void IsTransient_RetriesThrottlingAndServerErrors(HttpStatusCode status)
        => Assert.True(HttpRetryPolicy.IsTransient(status));

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.PartialContent)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public void IsTransient_DoesNotRetryDefinitiveAnswers(HttpStatusCode status)
        => Assert.False(HttpRetryPolicy.IsTransient(status));

    [Fact]
    public void Delay_GrowsWithTheAttemptAndStaysBounded()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), HttpRetryPolicy.Delay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(1000), HttpRetryPolicy.Delay(2));
        Assert.Equal(HttpRetryPolicy.Delay(HttpRetryPolicy.MaxAttempts), HttpRetryPolicy.Delay(10));
        Assert.Equal(HttpRetryPolicy.Delay(1), HttpRetryPolicy.Delay(0));
    }

    [Fact]
    public void MaxAttempts_AllowsAMeaningfulRetry()
        => Assert.InRange(HttpRetryPolicy.MaxAttempts, 2, 5);
}
