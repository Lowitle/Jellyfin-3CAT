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
    public void Page_SendsTheProgramAndTheProviderIdOfThePreview()
    {
        var page = ReadPage();

        Assert.Contains("programId: programId,", page, StringComparison.Ordinal);

        // The field is the id of the series in either provider: the server reads the one of the source it
        // resolves, so the page sends it as both.
        Assert.Contains("tmdbId: providerId,", page, StringComparison.Ordinal);
        Assert.Contains("tvdbId: providerId,", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_OffersTvdbWithItsKeyAndItsPin()
    {
        // TVDB was offered disabled while its numbering did not exist: a page that kept it disabled would
        // make the whole feature unreachable, and one without the key could never log in.
        var page = ReadPage();

        Assert.Contains("<option value=\"2\">TVDB (numeración de TVDB)</option>", page, StringComparison.Ordinal);
        Assert.Contains(
            "<option value=\"3\">TVDB absoluta (temporada única)</option>",
            page,
            StringComparison.Ordinal);
        Assert.DoesNotContain("disabled>TVDB", page, StringComparison.Ordinal);

        Assert.Contains("id=\"ThreeCatTvdbApiKey\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"ThreeCatTvdbPin\"", page, StringComparison.Ordinal);
        Assert.Contains(
            "config.TvdbApiKey = document.querySelector('#ThreeCatTvdbApiKey').value.trim();",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "config.TvdbPin = document.querySelector('#ThreeCatTvdbPin').value.trim();",
            page,
            StringComparison.Ordinal);

        // The preview can try TVDB —and its absolute order— without changing the source of the program first.
        Assert.Contains("<option value=\"tvdb-absolute\">TVDB absoluta</option>", page, StringComparison.Ordinal);
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

    [Fact]
    public void Page_SendsTheOrganisationOfTheLibrary()
    {
        // The checkbox decides whether the server prepares the folders of the layout, and the page has to
        // forward it or «Crear biblioteca» would use whatever was saved the last time.
        var page = ReadPage();

        Assert.Contains("id=\"ThreeCatOrganizeByKind\"", page, StringComparison.Ordinal);
        Assert.Contains("organize: organized ? 'true' : 'false'", page, StringComparison.Ordinal);
        Assert.Contains("config.OrganizeByKind = document.querySelector('#ThreeCatOrganizeByKind').checked;", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_SendsTheNameOfTheSeriesWithoutTheWholeProgramFormat()
    {
        // 3Cat publishes the full emission of a program as a program of its own, so the page offers to keep the
        // name of the show: the server has to receive the choice or a save would undo it.
        var page = ReadPage();

        Assert.Contains("id=\"ThreeCatTrimWholeProgramSuffix\"", page, StringComparison.Ordinal);
        Assert.Contains(
            "threeCatField(config, 'trimWholeProgramSuffix') === true",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "document.querySelector('#ThreeCatTrimWholeProgramSuffix').checked",
            page,
            StringComparison.Ordinal);
        Assert.Contains("config.TrimWholeProgramSuffix =", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_ShowsTheVersionOfThePluginTheServerIsRunning()
    {
        // A page cached by the browser keeps showing the old layout after an update, and a plugin that was
        // installed but not loaded looks exactly like one that changed nothing: the version next to the
        // title is what tells the two apart.
        var page = ReadPage();

        Assert.Contains("id=\"ThreeCatVersion\"", page, StringComparison.Ordinal);
        Assert.Contains("threeCatLoadVersion();", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_ReplacesTheMirrorAndTheManualIdsWithTheKindOfProgram()
    {
        // The kind of program is what the table shows now: the TMDB id and the anchors come from the
        // identity dataset, and a second copy is a sub-library of the whole kind, not a column.
        var page = ReadPage();

        Assert.Contains("<th style=\"width: 28%\">Clasificación</th>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ThreeCatProgramTmdb-", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ThreeCatProgramAnchors-", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ThreeCatProgramLibrary-", page, StringComparison.Ordinal);

        // The reference is not lost: it goes under the title of the program.
        Assert.Contains("threeCatMono threeCatReference", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_SavesTheKindOfProgramAndTheSubLibraries()
    {
        var page = ReadPage();

        Assert.Contains("CategoryOverride: program.CategoryOverride || null,", page, StringComparison.Ordinal);
        Assert.Contains(
            "config.KindLibraries = ThreeCatConfig.kindLibraries.map(function (entry) {",
            page,
            StringComparison.Ordinal);

        // The table of sub-libraries reads the kinds of program from the server, so the folders of the
        // layout are named once, in LibraryCategory.
        Assert.Contains("id=\"ThreeCatKindsBody\"", page, StringComparison.Ordinal);
        Assert.Contains("threeCatUrl('Library/Categories')", page, StringComparison.Ordinal);
        Assert.Contains("threeCatUrl('Library/Kinds')", page, StringComparison.Ordinal);
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
