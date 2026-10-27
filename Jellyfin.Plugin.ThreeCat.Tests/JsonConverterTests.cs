using System.Text.Json;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the converters that absorb the inconsistencies of the 3Cat JSON feeds.
/// </summary>
public sealed class JsonConverterTests
{
    [Theory]
    [InlineData("\"15/06/2026 08:00:00\"", 8)]
    [InlineData("\"15/06/2026 08:00\"", 8)]
    [InlineData("\"15/06/2026\"", 0)]
    [InlineData("\"2026-06-15T08:00:00\"", 8)]
    public void DateConverter_ReadsTheCatalogFormats(string json, int expectedHour)
    {
        var date = JsonSerializer.Deserialize<DateTimeOffset?>(json, Options());

        Assert.Equal(new DateTime(2026, 6, 15, expectedHour, 0, 0), date!.Value.DateTime);
    }

    [Fact]
    public void DateConverter_PrefersTheIsoValueOfTheMediaEndpoint()
    {
        var date = JsonSerializer.Deserialize<DateTimeOffset?>(
            """{ "text": "15/06/2026 08:00:00", "utc": "2026-06-15T06:00:00+00:00" }""",
            Options());

        Assert.Equal(new DateTimeOffset(2026, 6, 15, 6, 0, 0, TimeSpan.Zero), date);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("\"\"")]
    [InlineData("\"sense data\"")]
    [InlineData("{ \"text\": \"\" }")]
    [InlineData("{}")]
    public void DateConverter_ReturnsNullWhenThereIsNoDate(string json)
    {
        Assert.Null(JsonSerializer.Deserialize<DateTimeOffset?>(json, Options()));
    }

    [Theory]
    [InlineData("\"00:24:07:00\"", 1447)]
    [InlineData("\"00:24:07\"", 1447)]
    [InlineData("\"24:07\"", 1447)]
    [InlineData("\"1:02:03\"", 3723)]
    [InlineData("1447000", 1447)]
    public void DurationConverter_ReadsThePublishedFormats(string json, int expectedSeconds)
    {
        var duration = JsonSerializer.Deserialize<TimeSpan?>(json, Options());

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), duration);
    }

    [Fact]
    public void DurationConverter_UsesTheMillisecondsOfTheMediaEndpoint()
    {
        var duration = JsonSerializer.Deserialize<TimeSpan?>(
            """{ "text": "00:24:07", "milisegons": 1447000 }""",
            Options());

        Assert.Equal(TimeSpan.FromMilliseconds(1447000), duration);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("\"\"")]
    [InlineData("\"00:00:00\"")]
    [InlineData("\"no\"")]
    [InlineData("{}")]
    public void DurationConverter_ReturnsNullWhenThereIsNoDuration(string json)
    {
        Assert.Null(JsonSerializer.Deserialize<TimeSpan?>(json, Options()));
    }

    private static JsonSerializerOptions Options() => new(JsonSerializerDefaults.Web)
    {
        Converters = { new ThreeCatDateConverter(), new ThreeCatDurationConverter() }
    };
}
