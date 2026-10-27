using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the projection of the 3Cat program search into the catalog shown by the configuration page.
/// </summary>
public sealed class ThreeCatCatalogTests
{
    [Fact]
    public void ToCatalogProgram_MapsTheTelevisionProgram()
    {
        var program = new ThreeCatSearchProgram
        {
            Id = "119987484",
            Title = " Doraemon, el gat còsmic ",
            Slug = "doraemon-el-gat-cosmic",
            Typology = "PTVC_PROGRAMA",
            Published = "23/10/2026 08:00:00",
            Channels =
            [
                new ThreeCatLabel { Description = "SX3", Main = false },
                new ThreeCatLabel { Description = "TV3", Main = true }
            ],
            Languages = [new ThreeCatLabel { Description = "Català", Main = true }],
            AgeRatings = [new ThreeCatLabel { Description = "Tots els públics", Main = true }]
        };

        var entry = ThreeCatClient.ToCatalogProgram(program);

        Assert.Equal("119987484", entry.Id);
        Assert.Equal("Doraemon, el gat còsmic", entry.Title);
        Assert.Equal("https://www.3cat.cat/3cat/doraemon-el-gat-cosmic/", entry.Url);
        Assert.Equal("TV", entry.Kind);
        Assert.Equal("TV3", entry.Channel);
        Assert.Equal(["Català"], entry.Languages);
        Assert.Equal("Tots els públics", entry.AgeRating);
        Assert.Equal("2026-10-23", entry.Published);
        Assert.True(entry.HasVideo);
    }

    [Fact]
    public void ToCatalogProgram_FlagsTheWebAndRadioPrograms()
    {
        var entry = ThreeCatClient.ToCatalogProgram(new ThreeCatSearchProgram
        {
            Id = "2236",
            Title = "Divergents",
            Slug = "divergents",
            Typology = "WCR_PROGRAMA",
            Channels = [new ThreeCatLabel { Description = "Catalunya Ràdio", Main = true }]
        });

        Assert.Equal("Web/radio", entry.Kind);
        Assert.Equal("Catalunya Ràdio", entry.Channel);
        Assert.False(entry.HasVideo);
        Assert.Empty(entry.Languages);
        Assert.Null(entry.Published);
    }

    [Fact]
    public void ToCatalogProgram_WithoutTitleOrSlug_FallsBackToTheId()
    {
        var entry = ThreeCatClient.ToCatalogProgram(new ThreeCatSearchProgram { Id = "77136" });

        Assert.Equal("77136", entry.Title);
        Assert.Null(entry.Url);
        Assert.Equal("Web/radio", entry.Kind);
        Assert.Null(entry.Channel);
        Assert.Null(entry.AgeRating);
    }

    [Theory]
    [InlineData("23/10/2026 08:00:00", "2026-10-23")]
    [InlineData("23/10/2026", "2026-10-23")]
    [InlineData("2026-10-23 08:00:00", "2026-10-23")]
    [InlineData("2026-10-23", "2026-10-23")]
    [InlineData(" 23/10/2026 ", "2026-10-23")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("sin fecha", null)]
    public void NormalizeProgramDate_ReturnsAnIsoDateOrNull(string? value, string? expected)
    {
        Assert.Equal(expected, ThreeCatClient.NormalizeProgramDate(value));
    }
}
