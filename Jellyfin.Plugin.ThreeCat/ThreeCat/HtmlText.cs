using System.Net;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Converts the HTML fragments returned by the 3Cat API (descriptions, genres) into plain text.
/// </summary>
internal static partial class HtmlText
{
    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// Converts an HTML fragment to plain text.
    /// </summary>
    /// <param name="html">HTML fragment.</param>
    /// <returns>Plain text, or <c>null</c> when there is no text.</returns>
    internal static string? FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = TagRegex().Replace(html, " ");
        text = WebUtility.HtmlDecode(text);
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }
}
