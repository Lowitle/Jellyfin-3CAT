using System.Net;
using System.Text;
using Jellyfin.Plugin.ThreeCat.Sync;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of <see cref="OpenTvIdsClient"/>: the JSONL parse, the index by <c>programatv_id</c> and the
/// local copy that keeps the plugin working when GitHub is not reachable.
/// </summary>
public sealed class OpenTvIdsClientTests
{
    private const string BolaDeDrac =
        "{\"id\":\"3cat:73476\",\"emitter\":\"3cat\",\"emitterId\":\"73476\",\"kind\":\"anime\","
        + "\"title\":\"Bola de Drac Súper\",\"originalTitle\":\"Dragon Ball Super\","
        + "\"ids\":{\"wikidata\":\"Q19839121\",\"imdb\":\"tt4644488\",\"tmdb\":\"62715\",\"tvdb\":\"295068\"}}";

    private const string MerliSinTvdb =
        "{\"id\":\"3cat:42\",\"emitter\":\"3cat\",\"emitterId\":\"42\",\"kind\":\"series\","
        + "\"title\":\"Merlí\",\"ids\":{\"wikidata\":\"Q1\",\"tmdb\":\"1234\"}}";

    [Fact]
    public void Parse_IndexesTheLinesByEmitterId()
    {
        var records = OpenTvIdsClient.Parse(BolaDeDrac + "\n" + MerliSinTvdb + "\n");

        Assert.Equal(2, records.Count);
        Assert.Equal("Bola de Drac Súper", records["73476"].Title);
        Assert.Equal("62715", records["73476"].Ids?.Tmdb);
        Assert.Equal("tt4644488", records["73476"].Ids?.Imdb);
        Assert.Equal("295068", records["73476"].Ids?.Tvdb);
        Assert.Equal("1234", records["42"].Ids?.Tmdb);
        Assert.Null(records["42"].Ids?.Tvdb);
    }

    [Fact]
    public void Parse_IgnoresCorruptLinesAndLinesWithoutAnEmitterId()
    {
        var jsonl = string.Join(
            '\n',
            BolaDeDrac,
            "{ not json",
            "{\"id\":\"3cat:1\",\"title\":\"Sin emitterId\"}",
            "   ",
            MerliSinTvdb);

        var records = OpenTvIdsClient.Parse(jsonl);

        Assert.Equal(2, records.Count);
        Assert.True(records.ContainsKey("73476"));
        Assert.True(records.ContainsKey("42"));
    }

    [Fact]
    public void Parse_SkipsTheBomAndKeepsTheFirstLineOfADuplicate()
    {
        var first = BolaDeDrac.Replace("62715", "111", StringComparison.Ordinal);
        var second = BolaDeDrac.Replace("62715", "222", StringComparison.Ordinal);

        var records = OpenTvIdsClient.Parse("\uFEFF" + first + "\n" + second);

        Assert.Equal("111", records["73476"].Ids?.Tmdb);
    }

    [Fact]
    public void Parse_ReturnsEmptyForBlankContent()
    {
        Assert.Empty(OpenTvIdsClient.Parse(null));
        Assert.Empty(OpenTvIdsClient.Parse(string.Empty));
        Assert.Empty(OpenTvIdsClient.Parse(" \n \r\n "));
    }

    [Fact]
    public void Find_TrimsTheProgramIdAndReturnsNullWhenThereIsNoEntry()
    {
        var dataset = new OpenTvIdsDataset(OpenTvIdsClient.Parse(BolaDeDrac), null, fromCache: false);

        Assert.Equal("Bola de Drac Súper", dataset.Find(" 73476 ")?.Title);
        Assert.Null(dataset.Find("999"));
        Assert.Null(dataset.Find(null));
    }

    [Fact]
    public async Task LoadAsync_ReadsTheCachedCopyWithoutTouchingTheNetwork()
    {
        using var folder = new TempFolder();
        WriteCache(folder.Root, BolaDeDrac + "\n" + MerliSinTvdb);

        var handler = new StubHandler(_ => throw new InvalidOperationException("no debería descargar"));
        var client = CreateClient(folder.Root, handler);

        var dataset = await client.LoadAsync(null, forceRefresh: false, CancellationToken.None);

        Assert.Equal(2, dataset.Count);
        Assert.True(dataset.FromCache);
        Assert.NotNull(dataset.Updated);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task LoadAsync_FallsBackToThePreviousCopyWhenTheDownloadFails()
    {
        using var folder = new TempFolder();
        WriteCache(folder.Root, BolaDeDrac);

        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(folder.Root, handler);

        var dataset = await client.LoadAsync(
            "https://example.invalid/series.jsonl",
            forceRefresh: true,
            CancellationToken.None);

        Assert.Equal(1, dataset.Count);
        Assert.True(dataset.FromCache);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task LoadAsync_ReturnsAnEmptyDatasetWhenThereIsNoCopyAndTheDownloadFails()
    {
        using var folder = new TempFolder();

        var handler = new StubHandler(_ => throw new HttpRequestException("sin red"));
        var client = CreateClient(folder.Root, handler);

        var dataset = await client.LoadAsync(null, forceRefresh: true, CancellationToken.None);

        Assert.Equal(0, dataset.Count);
        Assert.False(dataset.FromCache);
        Assert.Null(dataset.Updated);
    }

    [Fact]
    public async Task LoadAsync_WritesTheCacheAndTheNextReadUsesIt()
    {
        using var folder = new TempFolder();

        var download = new StubHandler(_ => Json(BolaDeDrac + "\n" + MerliSinTvdb));
        var client = CreateClient(folder.Root, download);

        var dataset = await client.LoadAsync(
            "https://example.invalid/series.jsonl",
            forceRefresh: true,
            CancellationToken.None);

        Assert.Equal(2, dataset.Count);
        Assert.False(dataset.FromCache);
        Assert.True(File.Exists(Path.Combine(folder.Root, "3cat", OpenTvIdsClient.CacheFileName)));

        var offline = CreateClient(
            folder.Root,
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var cached = await offline.LoadAsync(null, forceRefresh: false, CancellationToken.None);

        Assert.Equal(2, cached.Count);
        Assert.True(cached.FromCache);
    }

    private static OpenTvIdsClient CreateClient(string dataPath, HttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            new TestApplicationPaths(dataPath),
            NullLogger<OpenTvIdsClient>.Instance);

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static void WriteCache(string dataPath, string body)
    {
        var folder = Path.Combine(dataPath, "3cat");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, OpenTvIdsClient.CacheFileName), body);
    }

    /// <summary>
    /// Minimal <see cref="IApplicationPaths"/> that points every path at a temporary folder.
    /// </summary>
    private sealed class TestApplicationPaths : IApplicationPaths
    {
        internal TestApplicationPaths(string dataPath) => DataPath = dataPath;

        public string ProgramDataPath => DataPath;

        public string WebPath => DataPath;

        public string ProgramSystemPath => DataPath;

        public string DataPath { get; }

        public string ImageCachePath => DataPath;

        public string PluginsPath => DataPath;

        public string PluginConfigurationsPath => DataPath;

        public string LogDirectoryPath => DataPath;

        public string ConfigurationDirectoryPath => DataPath;

        public string SystemConfigurationFilePath => Path.Combine(DataPath, "system.xml");

        public string CachePath => DataPath;

        public string TempDirectory => DataPath;

        public string VirtualDataPath => DataPath;

        public string TrickplayPath => DataPath;

        public string BackupPath => DataPath;

        public void MakeSanityCheckOrThrow()
        {
        }

        public void CreateAndCheckMarker(string path, string markerName, bool recursive)
        {
        }
    }

    /// <summary>
    /// HTTP handler that answers with whatever the test asks for and counts the requests.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        internal StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        internal int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_respond(request));
        }
    }
}
