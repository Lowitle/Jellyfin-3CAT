namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Formats and themes 3Cat files a program under, read from the facets of the search endpoint.
/// </summary>
/// <param name="Formats">Codes of <c>format_contingut</c> the program appears in («PUFF_SDIBU»...).</param>
/// <param name="Themes">Codes of <c>tematica</c> the program appears in («PUFICCIO»...).</param>
public sealed record ThreeCatProgramTaxonomy(
    IReadOnlyList<string> Formats,
    IReadOnlyList<string> Themes);
