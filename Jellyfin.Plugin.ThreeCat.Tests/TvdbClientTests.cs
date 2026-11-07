using System.Globalization;
using System.Net;
using System.Text;
using Jellyfin.Plugin.ThreeCat.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the TVDB client: the session token, the pages of a series, the two orders and the cases where it
/// must answer with nothing instead of failing the synchronization.
/// </summary>
public sealed class TvdbClientTests
{
    private const string Login = """{"status":"success","data":{"token":"token-de-prueba"}}""";

    [Fact]
    public async Task GetEpisodesAsync_ReadsTheAiredOrderAndLeavesTheSpecialsOut()
    {
        var handler = new StubHandler(request => IsLogin(request)
            ? Json(Login)
            : Json(Page(
                null,
                Episode(2, "Dos", 1, 2, "2020-01-08", 2),
                Episode(0, "Especial", 0, 1, "2019-12-01"),
                Episode(1, "Uno", 1, 1, "2020-01-01", 1))));

        var episodes = await Client(handler)
            .GetEpisodesAsync("91001", "clave-aired", null, absolute: false, CancellationToken.None);

        Assert.Equal(2, episodes.Count);
        Assert.Equal((1, 1), (episodes[0].Season, episodes[0].Episode));
        Assert.Equal("Uno", episodes[0].Title);
        Assert.Equal(new DateOnly(2020, 1, 1), episodes[0].AirDate);
        Assert.Equal((1, 2), (episodes[1].Season, episodes[1].Episode));

        // The specials of the aired order are not emissions of 3Cat: they are not even returned.
        Assert.DoesNotContain(episodes, episode => episode.Title == "Especial");

        // The list is sorted by season and episode, not left in the order TVDB published it.
        Assert.Equal(new[] { "Uno", "Dos" }, episodes.Select(episode => episode.Title).ToArray());

        // The key is exchanged once and the token travels in the header of the read.
        Assert.Equal(1, handler.Logins);
        Assert.Equal("Bearer token-de-prueba", handler.Authorizations[1]);
    }

    [Fact]
    public async Task GetEpisodesAsync_ReusesTheTokenOfTheSession()
    {
        var handler = new StubHandler(request => IsLogin(request)
            ? Json(Login)
            : Json(Page(null, Episode(1, "Uno", 1, 1, "2020-01-01", 1))));

        var client = Client(handler);

        await client.GetEpisodesAsync("91002", "clave-reuso", null, absolute: false, CancellationToken.None);
        await client.GetEpisodesAsync("91003", "clave-reuso", null, absolute: false, CancellationToken.None);

        // A token lasts a month: two series of one run must not ask for two.
        Assert.Equal(1, handler.Logins);
        Assert.Equal(2, handler.Reads);
    }

    [Fact]
    public async Task GetEpisodesAsync_ReadsEveryPageOfTheList()
    {
        var handler = new StubHandler(request =>
        {
            if (IsLogin(request))
            {
                return Json(Login);
            }

            return request.RequestUri!.PathAndQuery.Contains("page=0", StringComparison.Ordinal)
                ? Json(Page(
                    "https://api4.thetvdb.com/v4/series/91004/episodes/default?page=1",
                    Episode(1, "Uno", 1, 1, "2020-01-01", 1)))
                : Json(Page(null, Episode(2, "Dos", 1, 2, "2020-01-08", 2)));
        });

        var episodes = await Client(handler)
            .GetEpisodesAsync("91004", "clave-paginas", null, absolute: false, CancellationToken.None);

        // A thousand episodes of a series are more than one page: `links.next` is what says there is another.
        Assert.Equal(new[] { "Uno", "Dos" }, episodes.Select(episode => episode.Title).ToArray());
        Assert.Equal(2, handler.Reads);
    }

    [Fact]
    public async Task GetEpisodesAsync_WritesTheAbsoluteOrderAsOneSeason()
    {
        var handler = new StubHandler(request => IsLogin(request)
            ? Json(Login)
            : Json(Page(null, Episode(9, "Absoluto", 0, 857, "1997-04-13", 857))));

        var episodes = await Client(handler)
            .GetEpisodesAsync("91005", "clave-absoluta", null, absolute: true, CancellationToken.None);

        // The absolute numbering of TVDB is one season with one episode per work: «S01E857».
        var episode = Assert.Single(episodes);
        Assert.Equal((1, 857), (episode.Season, episode.Episode));
        Assert.Contains("/episodes/absolute", handler.Requests[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetEpisodesAsync_FallsBackToTheAiredOrderWhenTheAbsoluteOneIsEmpty()
    {
        var handler = new StubHandler(request =>
        {
            if (IsLogin(request))
            {
                return Json(Login);
            }

            return request.RequestUri!.PathAndQuery.Contains("/episodes/absolute", StringComparison.Ordinal)
                ? Json(Page(null))
                : Json(Page(null, Episode(9, "Cinco", 2, 1, "2020-02-01", 5)));
        });

        var episodes = await Client(handler)
            .GetEpisodesAsync("91006", "clave-caida", null, absolute: true, CancellationToken.None);

        // The absolute order is curated by hand and some series publish none: the aired order still carries
        // the absolute number of every episode.
        var episode = Assert.Single(episodes);
        Assert.Equal((1, 5), (episode.Season, episode.Episode));
    }

    [Fact]
    public async Task GetEpisodesAsync_WithoutAKey_DoesNotTouchTheNetwork()
    {
        var handler = new StubHandler(_ => Json(Login));

        var episodes = await Client(handler)
            .GetEpisodesAsync("91007", "  ", null, absolute: false, CancellationToken.None);

        Assert.Empty(episodes);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetEpisodesAsync_WithoutAnId_DoesNotTouchTheNetwork()
    {
        var handler = new StubHandler(_ => Json(Login));

        var episodes = await Client(handler)
            .GetEpisodesAsync(null, "clave", null, absolute: false, CancellationToken.None);

        Assert.Empty(episodes);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetEpisodesAsync_ReturnsNothingWhenTvdbRejectsTheKey()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var episodes = await Client(handler)
            .GetEpisodesAsync("91009", "clave-mala", null, absolute: false, CancellationToken.None);

        // A wrong key is a configuration mistake: it is not retried and it never reaches the episodes.
        Assert.Empty(episodes);
        Assert.Equal(0, handler.Reads);
    }

    [Fact]
    public async Task GetEpisodesAsync_LogsInAgainWhenTheSessionHasExpired()
    {
        var logins = 0;
        var reads = 0;

        var handler = new StubHandler(request =>
        {
            if (IsLogin(request))
            {
                logins++;
                return Json(string.Concat(
                    "{\"status\":\"success\",\"data\":{\"token\":\"token-",
                    logins.ToString(CultureInfo.InvariantCulture),
                    "\"}}"));
            }

            reads++;
            return reads == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Json(Page(null, Episode(1, "Uno", 1, 1, "2020-01-01", 1)));
        });

        var episodes = await Client(handler)
            .GetEpisodesAsync("91008", "clave-caducada", null, absolute: false, CancellationToken.None);

        // A 401 on a read is an expired token, not a wrong key: the client logs in again and repeats the page.
        var episode = Assert.Single(episodes);
        Assert.Equal((1, 1), (episode.Season, episode.Episode));
        Assert.Equal(2, logins);
        Assert.Equal("Bearer token-2", handler.Authorizations[^1]);
    }

    private static bool IsLogin(HttpRequestMessage request)
        => request.Method == HttpMethod.Post;

    private static string Page(string? next, params string[] episodes)
        => string.Concat(
            """{"status":"success","data":{"episodes":[""",
            string.Join(",", episodes),
            """]},"links":{"next":""",
            next is null ? "null" : string.Concat("\"", next, "\""),
            "}}");

    private static string Episode(int id, string name, int season, int number, string aired, int? absolute = null)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{{\"id\":{0},\"name\":\"{1}\",\"seasonNumber\":{2},\"number\":{3},\"absoluteNumber\":{4},"
            + "\"aired\":\"{5}\"}}",
            id,
            name,
            season,
            number,
            absolute?.ToString(CultureInfo.InvariantCulture) ?? "null",
            aired);

    private static TvdbClient Client(HttpMessageHandler handler)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri(TvdbClient.BaseAddress, UriKind.Absolute) },
            NullLogger<TvdbClient>.Instance);

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

        public List<string?> Authorizations { get; } = [];

        public int Logins => Requests.Count(request => request.StartsWith("POST ", StringComparison.Ordinal));

        public int Reads => Requests.Count(request => request.StartsWith("GET ", StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(string.Concat(request.Method.Method, " ", request.RequestUri!.PathAndQuery));
            Authorizations.Add(request.Headers.Authorization?.ToString());

            return Task.FromResult(_respond(request));
        }
    }
}
