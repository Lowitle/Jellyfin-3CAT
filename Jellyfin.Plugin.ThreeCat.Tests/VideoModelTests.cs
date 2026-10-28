using System.Text.Json;
using Jellyfin.Plugin.ThreeCat.Library;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests that the catalog model binds the audience and the channels 3Cat publishes for every video, which
/// is what tells a children's programme from the rest without reading anything else.
/// </summary>
public sealed class VideoModelTests
{
    private const string Payload = """
        {
          "id": 4534331,
          "titol": "No t'emportis les meves coses! / Una princesa per en Nobita!",
          "targets": {
            "target": [ { "id": "PUT_INF", "desc": "Infantil", "main": true } ]
          },
          "canals": {
            "canal": [
              { "id": "PUC_S3_X3", "desc": "X3", "main": false },
              { "id": "PUC_S3_S3", "desc": "S3", "main": false },
              { "id": "PUC_S3", "desc": "Super3", "main": true }
            ]
          }
        }
        """;

    [Fact]
    public void Deserialize_ReadsTheAudienceAndTheChannelsOfTheVideo()
    {
        var video = Deserialize(Payload);

        Assert.Equal("PUT_INF", Assert.Single(video.Targets!.Targets!).Id);
        Assert.Equal(
            ["PUC_S3_X3", "PUC_S3_S3", "PUC_S3"],
            video.Channels!.Channels!.Select(channel => channel.Id));
    }

    [Fact]
    public void Deserialize_ClassifiesAChildrensProgramWithWhatTheVideoPublishes()
    {
        var video = Deserialize(Payload);

        Assert.True(ProgramClassifier.IsChildren(
            video.Targets!.Targets!.Select(target => target.Id),
            video.Channels!.Channels!.Select(channel => channel.Id)));
    }

    [Fact]
    public void Deserialize_WrapsASingleAudienceAndASingleChannel()
    {
        // 3Cat publishes a bare object instead of a list when the video holds a single value.
        var video = Deserialize("""
            {
              "id": 49176,
              "targets": { "target": { "id": "PUT_JUV", "desc": "Juvenil", "main": true } },
              "canals": { "canal": { "id": "PUC_TV3", "desc": "TV3", "main": true } }
            }
            """);

        Assert.Equal("PUT_JUV", Assert.Single(video.Targets!.Targets!).Id);
        Assert.Equal("PUC_TV3", Assert.Single(video.Channels!.Channels!).Id);
        Assert.False(ProgramClassifier.IsChildren(
            video.Targets!.Targets!.Select(target => target.Id),
            video.Channels!.Channels!.Select(channel => channel.Id)));
    }

    [Fact]
    public void Deserialize_WithoutAudienceNorChannels_WritesTheOtherFields()
    {
        var video = Deserialize("""{ "id": 3251892, "titol": "El gat còsmic" }""");

        Assert.Equal("3251892", video.Id);
        Assert.Equal("El gat còsmic", video.Title);
        Assert.Null(video.Targets);
        Assert.Null(video.Channels);
        Assert.False(ProgramClassifier.IsChildren(null, null));
    }

    [Fact]
    public void Deserialize_ReadsTheAudienceAndTheChannelsAsTheCatalogPublishesThem()
    {
        // This is the shape of api.3cat.cat/videos: a bare array of labelled values, not an object with
        // a wrapper key. It is the payload that left every program without videos.
        var video = Deserialize("""
            {
              "id": 4534331,
              "targets": [ { "id": "PUT_INF", "desc": "Infantil", "main": true } ],
              "canals": [
                { "id": "PUC_S3_S3", "desc": "S3", "main": false },
                { "id": "PUC_S3", "desc": "Super3", "main": true }
              ]
            }
            """);

        Assert.Equal("PUT_INF", Assert.Single(video.Targets!.Targets!).Id);
        Assert.Equal(["PUC_S3_S3", "PUC_S3"], video.Channels!.Channels!.Select(channel => channel.Id));
        Assert.True(ProgramClassifier.IsChildren(
            video.Targets!.Targets!.Select(target => target.Id),
            video.Channels!.Channels!.Select(channel => channel.Id)));
    }

    [Fact]
    public void Deserialize_ReadsAnEmptyAudienceAsAnEmptyList()
    {
        var video = Deserialize("""{ "id": 1, "targets": [], "canals": [] }""");

        Assert.Empty(video.Targets!.Targets!);
        Assert.Empty(video.Channels!.Channels!);
        Assert.False(ProgramClassifier.IsChildren(
            video.Targets!.Targets!.Select(target => target.Id),
            video.Channels!.Channels!.Select(channel => channel.Id)));
    }

    [Fact]
    public void Deserialize_ReadsAnEmptyTextAsNoAudience()
    {
        var video = Deserialize("""{ "id": 1, "targets": "", "canals": "" }""");

        Assert.Null(video.Targets);
        Assert.Null(video.Channels);
    }

    private static ThreeCatVideo Deserialize(string json)
        => JsonSerializer.Deserialize<ThreeCatVideo>(json, ThreeCatClient.JsonOptions)!;
}
