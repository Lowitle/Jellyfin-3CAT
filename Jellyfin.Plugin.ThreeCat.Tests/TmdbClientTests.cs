using System.Net;
using System.Text;
using Jellyfin.Plugin.ThreeCat.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the TMDB client: the episodes it reads, the two forms of the key and the cases where it must
/// answer with nothing instead of failing the synchronization.
/// </summary>
public sealed class TmdbClientTests
{
    [Fact]
    public async Task GetEpisodesAsync_ReadsTheSeasonsAndSortsTheEpisodes()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/season/1", StringComparison.Ordinal)
                ? Json(
                    """
                    {"season_number":1,"episodes":[
                      {"season_number":1,"episode_number":2,"name":"Dos","air_date":"2020-01-08"},
                      {"season_number":1,"episode_number":1,"name":"Uno","air_date":"2020-01-01"}
                    ]}
                    """)
                : Json(
                    """
                    {"id":123,"name":"Serie","seasons":[
                      {"season_number":0,"episode_count":2},
                      {"season_number":1,"episode_count":2}
                    ]}
                    """));

        var episodes = await Client(handler)
            .GetEpisodesAsync("90001", "clave-de-prueba", "es-ES", CancellationToken.None);

        Assert.Equal(2, episodes.Count);
        Assert.Equal(1, episodes[0].Episode);
        Assert.Equal("Uno", episodes[0].Title);
        Assert.Equal(new DateOnly(2020, 1, 1), episodes[0].AirDate);
        Assert.Equal(2, episodes[1].Episode);

        // The specials of the provider are not read: they are not emissions of 3Cat.
        Assert.DoesNotContain(handler.Requests, url => url.Contains("/season/0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetEpisodesAsync_WithoutAKey_DoesNotTouchTheNetwork()
    {
        var handler = new StubHandler(_ => Json("""{"id":1,"seasons":[]}"""));

        var episodes = await Client(handler).GetEpisodesAsync("90002", "  ", null, CancellationToken.None);

        Assert.Empty(episodes);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetEpisodesAsync_WithoutAnId_DoesNotTouchTheNetwork()
    {
        var handler = new StubHandler(_ => Json("""{"id":1,"seasons":[]}"""));

        var episodes = await Client(handler).GetEpisodesAsync(null, "clave", null, CancellationToken.None);

        Assert.Empty(episodes);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetEpisodesAsync_ReturnsNothingWhenTmdbDoesNotServeTheSeries()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var episodes = await Client(handler).GetEpisodesAsync("90003", "clave", null, CancellationToken.None);

        Assert.Empty(episodes);
    }

    [Fact]
    public async Task GetEpisodesAsync_SendsAV3KeyAsTheApiKeyParameter()
    {
        var handler = new StubHandler(_ => Json("""{"id":1,"seasons":[]}"""));

        await Client(handler).GetEpisodesAsync("90004", "0123456789abcdef", null, CancellationToken.None);

        Assert.Contains("api_key=0123456789abcdef", handler.Requests[0], StringComparison.Ordinal);
        Assert.Null(handler.Authorization);
    }

    [Fact]
    public async Task GetEpisodesAsync_SendsAV4TokenAsABearerToken()
    {
        var handler = new StubHandler(_ => Json("""{"id":1,"seasons":[]}"""));

        await Client(handler)
            .GetEpisodesAsync("90005", "eyJhbGciOiJIUzI1NiJ9.cGF5bG9hZA.firma", null, CancellationToken.None);

        Assert.Equal("Bearer eyJhbGciOiJIUzI1NiJ9.cGF5bG9hZA.firma", handler.Authorization);
        Assert.DoesNotContain("api_key", handler.Requests[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetEpisodesAsync_ReadsTheEpisodesOfEverySeason()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/season/1", StringComparison.Ordinal))
            {
                return Json(
                    """{"season_number":1,"episodes":[{"season_number":1,"episode_number":1,"name":"Uno","air_date":"2020-01-01"}]}""");
            }

            if (path.EndsWith("/season/2", StringComparison.Ordinal))
            {
                return Json(
                    """{"season_number":2,"episodes":[{"season_number":2,"episode_number":1,"name":"Dos","air_date":""}]}""");
            }

            return Json(
                """{"id":1,"seasons":[{"season_number":1,"episode_count":1},{"season_number":2,"episode_count":1}]}""");
        });

        var episodes = await Client(handler)
            .GetEpisodesAsync("90006", "clave", "es-ES", CancellationToken.None);

        Assert.Equal(2, episodes.Count);
        Assert.Equal((1, 1), (episodes[0].Season, episodes[0].Episode));
        Assert.Equal((2, 1), (episodes[1].Season, episodes[1].Episode));

        // The empty air date of the second episode is read as no date, not as an error.
        Assert.Null(episodes[1].AirDate);
    }

    private static TmdbClient Client(HttpMessageHandler handler)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri(TmdbClient.BaseAddress, UriKind.Absolute) },
            NullLogger<TmdbClient>.Instance);

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    /// <summary>
    /// HTTP handler that answers from a function and records what the client asked for.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            => _respond = respond;

        public List<string> Requests { get; } = [];

        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            Authorization = request.Headers.Authorization?.ToString();

            return Task.FromResult(_respond(request));
        }
    }
}
