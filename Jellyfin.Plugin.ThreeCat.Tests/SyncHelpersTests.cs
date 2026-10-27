using Jellyfin.Plugin.ThreeCat.Sync;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the text helpers shared by the 3Cat synchronization steps.
/// </summary>
public sealed class SyncHelpersTests
{
    [Theory]
    [InlineData("Doraemon, el gat còsmic - El gat còsmic", "Doraemon, el gat còsmic", "El gat còsmic")]
    [InlineData("Doraemon: El gat còsmic", "Doraemon", "El gat còsmic")]
    [InlineData("DORAEMON | El gat còsmic", "Doraemon", "El gat còsmic")]
    [InlineData("Doraemon – El gat còsmic", "Doraemon", "El gat còsmic")]
    [InlineData("Doraemon", "Doraemon", "Doraemon")]
    [InlineData("El gat còsmic", "Doraemon", "El gat còsmic")]
    [InlineData("Doraemon - ", "Doraemon", "Doraemon - ")]
    public void StripSeriesPrefix_RemovesTheRepeatedSeriesName(string title, string series, string expected)
    {
        Assert.Equal(expected, ThreeCatSyncService.StripSeriesPrefix(title, series));
    }

    [Theory]
    [InlineData(" CA ", "ca")]
    [InlineData("Ca-ES", "ca-es")]
    [InlineData("es", "es")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void NormalizeLanguage_ReturnsTheLowerCaseTagOrNull(string? value, string? expected)
    {
        Assert.Equal(expected, ThreeCatSyncService.NormalizeLanguage(value));
    }

    [Theory]
    [InlineData("MASTER", long.MaxValue)]
    [InlineData("master", long.MaxValue)]
    [InlineData("1920x1080", 2073600L)]
    [InlineData("288x422", 121536L)]
    [InlineData("", 0L)]
    [InlineData(null, 0L)]
    [InlineData("original", 0L)]
    [InlineData("1920x", 0L)]
    public void SizeRank_RanksTheSizeTag(string? size, long expected)
    {
        Assert.Equal(expected, ThreeCatSyncService.SizeRank(size));
    }
}
