using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the WebVTT to SubRip conversion applied to the 3Cat subtitles.
/// </summary>
public class VttToSrtTests
{
    private const string Sample = """
        WEBVTT
        X-TIMESTAMP-MAP=MPEGTS:900000,LOCAL:00:00:00.000

        NOTE Este bloc s'ignora

        1
        00:00:01.000 --> 00:00:03.500 align:start position:0%
        <c>Hola</c> <00:00:02.000>món

        00:00:04.5 --> 00:00:06.000
        Segona l&iacute;nia
        amb <b>negreta</b> i <i>cursiva</i>

        """;

    [Fact]
    public void Convert_RealWorldDocument_ReturnsCuesInSubRipFormat()
    {
        var expected = string.Join(
            '\n',
            "1",
            "00:00:01,000 --> 00:00:03,500",
            "Hola món",
            string.Empty,
            "2",
            "00:00:04,500 --> 00:00:06,000",
            "Segona línia",
            "amb negreta i cursiva",
            string.Empty,
            string.Empty);

        Assert.Equal(expected, VttToSrt.Convert(Sample));
    }

    [Theory]
    [InlineData("WEBVTT\n\n")]
    [InlineData("WEBVTT\n\nNOTE només un comentari\n\n")]
    [InlineData("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\n\n")]
    public void Convert_DocumentWithoutText_ReturnsEmpty(string vtt)
    {
        Assert.Equal(string.Empty, VttToSrt.Convert(vtt));
    }

    [Theory]
    [InlineData("00:05.5 --> 00:07.25", "00:00:05,500 --> 00:00:07,250")]
    [InlineData("01:02:03.25 --> 01:02:04.5", "01:02:03,250 --> 01:02:04,500")]
    [InlineData("00:00:01.000 --> 00:00:02.000", "00:00:01,000 --> 00:00:02,000")]
    public void Convert_NormalizesTimestamps(string timing, string expectedTiming)
    {
        var srt = VttToSrt.Convert($"WEBVTT\n\n{timing}\nText\n");

        Assert.Contains(expectedTiming, srt, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_DecodesEntitiesAndKeepsPlainText()
    {
        var srt = VttToSrt.Convert("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nA &amp; B &quot;C&quot; &gt; D\n");

        Assert.Equal("1\n00:00:01,000 --> 00:00:02,000\nA & B \"C\" > D\n\n", srt);
    }

    [Fact]
    public void Convert_NumbersTheCuesInOrder()
    {
        var srt = VttToSrt.Convert(
            "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nU\n\n00:00:03.000 --> 00:00:04.000\nDos\n\n00:00:05.000 --> 00:00:06.000\nTres\n");

        Assert.StartsWith("1\n", srt, StringComparison.Ordinal);
        Assert.Contains("\n\n2\n", srt, StringComparison.Ordinal);
        Assert.Contains("\n\n3\n", srt, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertToFile_WritesUtf8WithoutBomAndCrlf()
    {
        using var folder = new TempFolder();
        var path = folder.Combine("sub.vtt.srt");

        Assert.True(VttToSrt.ConvertToFile("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nHola\n", path));

        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("1\r\n00:00:01,000 --> 00:00:02,000\r\nHola\r\n\r\n", File.ReadAllText(path));
    }

    [Fact]
    public void ConvertToFile_WithoutCues_DoesNotCreateTheFile()
    {
        using var folder = new TempFolder();
        var path = folder.Combine("buit.srt");

        Assert.False(VttToSrt.ConvertToFile("WEBVTT\n\n", path));
        Assert.False(File.Exists(path));
    }
}
