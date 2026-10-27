using System.Net;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Tells which HTTP failures are worth retrying. 3Cat throttles bursts of subtitle and catalog
/// downloads with 429/503 answers, which used to be indistinguishable from a missing resource.
/// </summary>
internal static class HttpRetryPolicy
{
    /// <summary>
    /// Number of attempts made for a request, the first try included.
    /// </summary>
    internal const int MaxAttempts = 3;

    /// <summary>
    /// Tells whether a status code is a transient failure.
    /// </summary>
    /// <param name="status">Status code of the response.</param>
    /// <returns><c>true</c> for timeouts, rate limiting and server errors.</returns>
    internal static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    /// <summary>
    /// Delay before a failed attempt is repeated; it grows linearly (0,5 s, 1 s, 1,5 s…).
    /// </summary>
    /// <param name="attempt">Attempt that just failed (1 for the first try).</param>
    /// <returns>How long to wait before retrying.</returns>
    internal static TimeSpan Delay(int attempt)
        => TimeSpan.FromMilliseconds(500 * Math.Clamp(attempt, 1, MaxAttempts));
}
