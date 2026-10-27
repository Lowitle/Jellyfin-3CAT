using System.Text.Json;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests that the models bind the <c>media.jsp</c> answer as 3Cat publishes it.
/// </summary>
public sealed class MediaModelTests
{
    private const string Payload = """
        {
          "informacio": {
            "id": 3251892,
            "programa_id": "119987484",
            "programa": "Doraemon, el gat còsmic",
            "capitol": "884",
            "titol": "El gat còsmic",
            "descripcio": "<p>Sinopsi de l'episodi</p>",
            "durada": { "text": "00:24:07", "milisegons": 1447000 },
            "data_emissio": { "text": "15/06/2026 08:00:00", "utc": "2026-06-15T06:00:00+00:00" },
            "data_caducitat": false,
            "estat": { "actiu": true },
            "drets": { "text": "Emissió no disponible fora de l'Estat Espanyol" }
          },
          "media": {
            "geo": "ESPANYA",
            "format": "MP4",
            "url": { "file": "https://mp4.3cat.cat/video/3251892.mp4", "label": "720p", "active": true }
          },
          "subtitols": { "text": "Català (SPS)", "iso": "ca", "url": "https://subs/ca.vtt", "format": "vtt" },
          "imatges": [
            { "url": "https://img.3cat.cat/670.jpg", "amplada": "670", "alcada": 378 },
            { "url": "https://img.3cat.cat/1920.jpg", "amplada": 1920, "alcada": "1080" }
          ]
        }
        """;

    [Fact]
    public void Deserialize_ReadsTheMetadataOfTheVideo()
    {
        var media = Deserialize(Payload);
        var info = media.Info!;

        Assert.Equal("3251892", info.Id);
        Assert.Equal("119987484", info.ProgramId);
        Assert.Equal("Doraemon, el gat còsmic", info.Program);
        Assert.Equal(884, info.Chapter);
        Assert.Equal("El gat còsmic", info.Title);
        Assert.Equal("<p>Sinopsi de l'episodi</p>", info.Description);
        Assert.Equal(TimeSpan.FromMilliseconds(1447000), info.Duration);
        Assert.Equal(new DateTimeOffset(2026, 6, 15, 6, 0, 0, TimeSpan.Zero), info.Broadcast);
        Assert.Null(info.Expires);
        Assert.True(info.Status!.Active);
        Assert.Equal("Emissió no disponible fora de l'Estat Espanyol", info.Rights!.Text);
    }

    [Fact]
    public void Deserialize_WrapsTheSingleStreamInAList()
    {
        var media = Deserialize(Payload);

        var file = Assert.Single(media.Playback!.Files!);
        Assert.Equal("720p", file.Label);
        Assert.True(file.Active);
        Assert.Equal("https://mp4.3cat.cat/video/3251892.mp4", ThreeCatClient.PickFile(media));
        Assert.Equal("ESPANYA", media.Playback.Geo);
    }

    [Fact]
    public void Deserialize_WrapsTheSingleSubtitleAndReadsTheImageSizes()
    {
        var media = Deserialize(Payload);

        var subtitle = Assert.Single(media.Subtitles!);
        Assert.Equal("ca", subtitle.Iso);
        Assert.Equal("https://subs/ca.vtt", subtitle.Url);

        Assert.Equal(2, media.Images!.Count);
        Assert.Equal(670, media.Images[0].Width);
        Assert.Equal(378, media.Images[0].Height);
        Assert.Equal(1920, media.Images[1].Width);
        Assert.Equal(1080, media.Images[1].Height);
    }

    [Fact]
    public void Deserialize_AcceptsArraysWhere3CatSendsSeveralItems()
    {
        var media = Deserialize(
            """
            {
              "media": { "url": [
                { "file": "https://mp4/low.mp4", "label": "288p", "active": false },
                { "file": "https://mp4/high.mp4", "label": "720p", "active": true }
              ] },
              "subtitols": [
                { "iso": "ca", "url": "https://subs/ca.vtt" },
                { "iso": "es", "url": "https://subs/es.vtt" }
              ]
            }
            """);

        Assert.Equal(2, media.Playback!.Files!.Count);
        Assert.Equal("https://mp4/high.mp4", ThreeCatClient.PickFile(media));
        Assert.Equal(["ca", "es"], media.Subtitles!.Select(subtitle => subtitle.Iso));
    }

    [Fact]
    public void Deserialize_EmptyAnswerLeavesTheNodesNull()
    {
        var media = Deserialize("{}");

        Assert.Null(media.Info);
        Assert.Null(media.Playback);
        Assert.Null(media.Subtitles);
        Assert.Null(ThreeCatClient.PickFile(media));
    }

    private static ThreeCatMedia Deserialize(string json)
        => JsonSerializer.Deserialize<ThreeCatMedia>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}
