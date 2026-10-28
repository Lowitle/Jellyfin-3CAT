using Jellyfin.Plugin.ThreeCat.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Classification of the programs of the catalog: the formats and themes 3Cat files each one under.
/// </summary>
/// <remarks>
/// The search endpoint accepts the facets <c>format_contingut</c> and <c>tematica</c>, and one read per
/// facet returns the programs of that format (up to 500 in a single page). 3Cat publishes no endpoint
/// that says which facets a program belongs to, so the classification is the sweep of the facets: the
/// programs of a facet are read once and inverted into a table keyed by program id. The sweep lasts a
/// little longer than the catalog itself and is cached, because opening the page again, syncing two
/// programs or reloading the catalog must not read 24 more pages.
/// </remarks>
public sealed partial class ThreeCatClient
{
    /// <summary>
    /// Facet requests sent at the same time. 3Cat throttles bursts of demand, so the facets are read a
    /// few at a time instead of all at once.
    /// </summary>
    private const int FacetConcurrency = 4;

    /// <summary>
    /// Query parameter of the format facet, which is the one the classifier is built on.
    /// </summary>
    private const string FormatFacet = "format_contingut";

    /// <summary>
    /// Query parameter of the theme facet, the fallback of the classifier.
    /// </summary>
    private const string ThemeFacet = "tematica";

    /// <summary>
    /// How long the classification is kept in memory. The formats of a program change when 3Cat files it
    /// under another one, which is rare, so it is kept three times longer than the catalog.
    /// </summary>
    private static readonly TimeSpan TaxonomyCacheDuration = TimeSpan.FromMinutes(30);

    private static readonly SemaphoreSlim TaxonomyGate = new(1, 1);

    private static (DateTimeOffset Expires, IReadOnlyDictionary<string, ThreeCatProgramTaxonomy> Items)
        _taxonomyCache = (DateTimeOffset.MinValue, new Dictionary<string, ThreeCatProgramTaxonomy>());

    /// <summary>
    /// Reads the formats and themes of every program of the catalog.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The facets of each program, keyed by <c>programatv_id</c>. The programs 3Cat files under no known
    /// facet are not in the table, so they are classified by nothing and shown with no category.
    /// </returns>
    /// <remarks>
    /// The answer is cached for <see cref="TaxonomyCacheDuration"/>. A second caller waits for the sweep
    /// instead of reading the same facets at the same time, and a sweep that comes back empty is not
    /// cached, so a hiccup of 3Cat is retried on the next request.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, ThreeCatProgramTaxonomy>> GetProgramTaxonomyAsync(
        CancellationToken cancellationToken)
    {
        var cached = _taxonomyCache;
        if (IsFreshTaxonomy(cached))
        {
            return cached.Items;
        }

        await TaxonomyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = _taxonomyCache;
            if (IsFreshTaxonomy(cached))
            {
                return cached.Items;
            }

            var taxonomy = await ReadProgramTaxonomyAsync(cancellationToken).ConfigureAwait(false);

            if (taxonomy.Count > 0)
            {
                _taxonomyCache = (DateTimeOffset.UtcNow.Add(TaxonomyCacheDuration), taxonomy);
            }

            return taxonomy;
        }
        finally
        {
            TaxonomyGate.Release();
        }
    }

    private static bool IsFreshTaxonomy(
        (DateTimeOffset Expires, IReadOnlyDictionary<string, ThreeCatProgramTaxonomy> Items) cached)
        => cached.Items.Count > 0 && cached.Expires > DateTimeOffset.UtcNow;

    /// <summary>
    /// Sweeps the facets of the classifier and inverts the answer into a table keyed by program id.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The facets of each program.</returns>
    private async Task<IReadOnlyDictionary<string, ThreeCatProgramTaxonomy>> ReadProgramTaxonomyAsync(
        CancellationToken cancellationToken)
    {
        var facets = ProgramClassifier.FormatFacets
            .Select(code => (Facet: FormatFacet, Code: code))
            .Concat(ProgramClassifier.ThemeFacets.Select(code => (Facet: ThemeFacet, Code: code)))
            .ToList();

        using var gate = new SemaphoreSlim(FacetConcurrency);

        var results = await Task.WhenAll(facets.Select(async facet =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ids = await ReadFacetAsync(facet.Facet, facet.Code, cancellationToken).ConfigureAwait(false);
                return (facet.Facet, facet.Code, Ids: ids);
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        var formats = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var themes = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (facet, code, ids) in results)
        {
            var target = string.Equals(facet, FormatFacet, StringComparison.Ordinal) ? formats : themes;

            foreach (var id in ids)
            {
                if (!target.TryGetValue(id, out var codes))
                {
                    target[id] = codes = [];
                }

                codes.Add(code);
            }
        }

        var taxonomy = formats.Keys
            .Union(themes.Keys, StringComparer.Ordinal)
            .ToDictionary(
                id => id,
                id => new ThreeCatProgramTaxonomy(
                    formats.TryGetValue(id, out var programFormats) ? programFormats : [],
                    themes.TryGetValue(id, out var programThemes) ? programThemes : []),
                StringComparer.Ordinal);

        var classified = taxonomy.Values.Count(entry => entry.Formats.Count > 0);

        _logger.LogInformation(
            "3Cat: {Programs} programas clasificados por formato o tema ({WithFormat} con formato)",
            taxonomy.Count,
            classified);

        return taxonomy;
    }

    /// <summary>
    /// Reads the ids of the programs of one facet, page by page.
    /// </summary>
    /// <param name="facet">Query parameter of the facet.</param>
    /// <param name="code">Value of the facet.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ids of the programs the facet returns.</returns>
    private async Task<List<string>> ReadFacetAsync(string facet, string code, CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        var envelope = await GetJsonAsync<ThreeCatSearchEnvelope>(
            FacetUrl(facet, code, 1),
            cancellationToken).ConfigureAwait(false);

        var page = envelope?.Response;
        AddFacetIds(page, ids);

        // With the typology filter every facet of the catalog fits in one page of 500; the loop is kept
        // so a format that grows past it is still read whole.
        var pages = Math.Clamp(page?.Pagination?.TotalPages ?? 1, 1, MaxProgramPages);

        for (var number = 2; number <= pages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var next = await GetJsonAsync<ThreeCatSearchEnvelope>(
                FacetUrl(facet, code, number),
                cancellationToken).ConfigureAwait(false);

            AddFacetIds(next?.Response, ids);
        }

        return ids;
    }

    private static void AddFacetIds(ThreeCatSearchResponse? page, List<string> ids)
    {
        foreach (var program in page?.Items?.Items ?? [])
        {
            var id = program.Id?.Trim();
            if (!string.IsNullOrEmpty(id))
            {
                ids.Add(id);
            }
        }
    }

    private static string FacetUrl(string facet, string code, int page)
        => ProgramPageUrl(page) + "&" + facet + "=" + Uri.EscapeDataString(code);
}
