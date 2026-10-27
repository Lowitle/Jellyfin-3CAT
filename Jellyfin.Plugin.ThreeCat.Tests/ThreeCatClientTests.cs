using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the pure helpers of <see cref="ThreeCatClient"/>.
/// </summary>
public sealed class ThreeCatClientTests
{
    [Theory]
    [InlineData("119987484", "119987484")]
    [InlineData("  119987484  ", "119987484")]
    [InlineData("https://www.3cat.cat/3cat/doraemon-el-gat-cosmic/video/3251892/", "3251892")]
    [InlineData("https://www.3cat.cat/3cat/doraemon-el-gat-cosmic/video/3251892", "3251892")]
    [InlineData("?idint=119987484", "119987484")]
    [InlineData("doraemon", "doraemon")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void NormalizeReference_ExtractsTheNumericId(string? reference, string expected)
    {
        Assert.Equal(expected, ThreeCatClient.NormalizeReference(reference));
    }

    [Fact]
    public void PickFile_PrefersTheActiveFile()
    {
        var media = Media(
            new ThreeCatMediaFile { File = "https://mp4/low.mp4", Active = false },
            new ThreeCatMediaFile { File = "https://mp4/high.mp4", Active = true });

        Assert.Equal("https://mp4/high.mp4", ThreeCatClient.PickFile(media));
    }

    [Fact]
    public void PickFile_FallsBackToTheFirstUsableFile()
    {
        var media = Media(
            new ThreeCatMediaFile { File = string.Empty, Active = false },
            new ThreeCatMediaFile { File = "https://mp4/only.mp4", Active = false });

        Assert.Equal("https://mp4/only.mp4", ThreeCatClient.PickFile(media));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PickFile_ReturnsNullWhen3CatPublishesNoStream(bool withEmptyList)
    {
        var media = withEmptyList
            ? new ThreeCatMedia { Playback = new ThreeCatPlayback { Files = [] } }
            : new ThreeCatMedia();

        Assert.Null(ThreeCatClient.PickFile(media));
        Assert.Null(ThreeCatClient.PickFile(null));
    }

    [Fact]
    public void PickFile_ReturnsNullWhenEveryFileIsBlank()
    {
        var media = Media(
            new ThreeCatMediaFile { File = "  ", Active = true },
            new ThreeCatMediaFile { File = null, Active = true });

        Assert.Null(ThreeCatClient.PickFile(media));
    }

    private static ThreeCatMedia Media(params ThreeCatMediaFile[] files)
        => new() { Playback = new ThreeCatPlayback { Files = [.. files] } };
}
