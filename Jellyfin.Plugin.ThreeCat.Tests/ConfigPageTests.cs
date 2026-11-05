using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of the configuration page as it ships. The page is an embedded resource of the plugin, so these
/// read the very file Jellyfin serves and catch the disagreement between what the endpoints return and what
/// the page reads from them.
/// </summary>
public sealed class ConfigPageTests
{
    /// <summary>
    /// Reads of a property of a record of the API in camelCase. Jellyfin serialises the records in
    /// PascalCase, so the page has to go through <c>threeCatField</c>: a bare <c>report.message</c> is
    /// <c>undefined</c>, and the user ends up looking at a preview that only says «undefined:».
    /// </summary>
    private static readonly Regex CamelCaseRead = new(
        @"\b(?:report|row|candidate|result|config|block|program)\.(?:programId|title|source|externalId|videos|matched"
        + @"|byDate|byTitle|byOrder|byAnchor|byCounter|counterOffset|anchors|datesReliable|blocks|specials|duplicates|coverage|usesThreeCat"
        + @"|message|rows|candidates"
        + @"|videoId|aired|season|episode|chapter|method|providerTitle|providerAirDate|records|programs"
        + @"|numbered|firstChapter|lastChapter|firstCounter|lastCounter"
        + @"|withTmdb|withTvdb|withImdb|libraries|items|count)\b",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Page_ReadsTheFieldsOfTheServerThroughTheCasingHelper()
    {
        var matches = CamelCaseRead.Matches(ReadPage())
            .Select(match => match.Value)
            .Distinct()
            .ToArray();

        Assert.True(
            matches.Length == 0,
            "La página lee en camelCase campos que el servidor publica en PascalCase: "
            + string.Join(", ", matches));
    }

    [Fact]
    public void Page_SendsTheProgramAndTheTmdbIdOfThePreview()
    {
        var page = ReadPage();

        Assert.Contains("programId: programId,", page, StringComparison.Ordinal);
        Assert.Contains("tmdbId: tmdbId,", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_SendsTheAnchorsOfThePreview()
    {
        // The anchors are the only evidence that ties a dub to the original, so the preview has to forward
        // them or the table would never show what the task is going to write.
        var page = ReadPage();

        Assert.Contains("id=\"ThreeCatMappingAnchors\"", page, StringComparison.Ordinal);
        Assert.Contains("anchors: anchors,", page, StringComparison.Ordinal);
        Assert.Contains("threeCatFloat(threeCatField(report, 'coverage'), 0)", page, StringComparison.Ordinal);
        Assert.Contains("threeCatMappingBlocks(report)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_ReadsTheCoverageAsAShare()
    {
        // The coverage comes as 0 to 1; `threeCatInt` would read it as 0 and the summary would always
        // say «Cobertura: 0 %».
        Assert.Contains(
            "threeCatFloat(threeCatField(report, 'coverage'), 0)",
            ReadPage(),
            StringComparison.Ordinal);
    }

    private static string ReadPage()
    {
        var assembly = typeof(Plugin).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(resource => resource.EndsWith("configPage.html", StringComparison.OrdinalIgnoreCase));

        Assert.False(name is null, "El plugin no lleva la página de configuración como recurso incrustado.");

        using var stream = assembly.GetManifestResourceStream(name!);
        Assert.False(stream is null, "La página de configuración está incrustada pero no se puede leer.");

        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
