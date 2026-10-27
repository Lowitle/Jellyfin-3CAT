using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the HTML to plain text conversion applied to the 3Cat descriptions.
/// </summary>
public sealed class HtmlTextTests
{
    [Theory]
    [InlineData("<p>Hola <b>món</b></p>", "Hola món")]
    [InlineData("<p>Primera</p><p>Segona</p>", "Primera Segona")]
    [InlineData("A &amp; B &quot;C&quot;", "A & B \"C\"")]
    [InlineData("  <div>\n  Text   amb   espais  \n</div>  ", "Text amb espais")]
    public void FromHtml_ReturnsPlainText(string html, string expected)
    {
        Assert.Equal(expected, HtmlText.FromHtml(html));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p> </p>")]
    public void FromHtml_ReturnsNullWhenThereIsNoText(string? html)
    {
        Assert.Null(HtmlText.FromHtml(html));
    }
}
