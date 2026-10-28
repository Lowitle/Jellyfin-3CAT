using System.Globalization;
using Jellyfin.Plugin.ThreeCat.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Program catalog reads of <see cref="ThreeCatClient"/>.
/// </summary>
/// <remarks>
/// The catalog endpoint (<c>api.3cat.cat/videos</c>) is read per program, so it cannot list the programs
/// of 3Cat. The search endpoint of the portal (<c>api.3cat.cat/cercador/tot</c>) accepts an empty text
/// and can be filtered by typology, which turns it into the program catalog: with
/// <c>tipologia=WCR_PROGRAMA,PTVC_PROGRAMA</c> it publishes the 2201 programs of 3Cat, newest first.
/// </remarks>
public sealed partial class ThreeCatClient
{
    /// <summary>
    /// Programs requested per search page. 3Cat accepts 500 and the whole catalog (2201 programs) fits in
    /// five requests.
    /// </summary>
    public const int ProgramPageSize = 500;

    /// <summary>
    /// Hard cap of program pages, so a wrong <c>total_pagines</c> never turns into an endless loop.
    /// </summary>
    internal const int MaxProgramPages = 40;

    /// <summary>
    /// Search requests sent at the same time. 3Cat throttles bursts of demand, so the pages are read a
    /// few at a time instead of all at once.
    /// </summary>
    private const int ProgramPageConcurrency = 4;

    /// <summary>
    /// Typology of the programs that have a video catalog. The <c>WCR_PROGRAMA</c> programs (radio and
    /// web) answer 404 or 500 when their catalog is read.
    /// </summary>
    internal const string TvProgramTypology = "PTVC_PROGRAMA";

    /// <remarks>
    /// <c>master=no</c> drops the master-size copies of every image of the program, which halves the
    /// answer (4 MB instead of 8 MB for the whole catalog). The table shows no artwork, so the smaller
    /// answer is the one to read.
    /// </remarks>
    internal const string ProgramSearchUrl =
        "https://api.3cat.cat/cercador/tot?_format=json&text={0}"
        + "&tipologia=WCR_PROGRAMA,PTVC_PROGRAMA&data_publicacio=&ordre=-data_publicacio"
        + "&items_pagina={1}&pagina={2}&sdom=img&version=2.0&cache=180&https=true&master=no";

    /// <summary>
    /// Formats of the program date published by the search endpoint («23/10/2026 08:00:00»), plus the
    /// ISO variants in case 3Cat changes the format.
    /// </summary>
    private static readonly string[] ProgramDateFormats =
    [
        "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy HH:mm",
        "dd/MM/yyyy",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd"
    ];

    /// <summary>
    /// How long the whole program catalog is kept in memory. The catalog moves slowly and a load is five
    /// requests of about 0.8 MB, so opening the configuration page again does not read it twice.
    /// </summary>
    private static readonly TimeSpan ProgramCacheDuration = TimeSpan.FromMinutes(10);

    private static readonly SemaphoreSlim ProgramGate = new(1, 1);

    private static (DateTimeOffset Expires, IReadOnlyList<ThreeCatCatalogProgram> Items) _programCache =
        (DateTimeOffset.MinValue, []);

    /// <summary>
    /// Reads the whole program catalog of 3Cat: every program of the portal, television, web and radio.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Every program of the catalog, in the order 3Cat publishes them, with the radio and web programs
    /// flagged with <see cref="ThreeCatCatalogProgram.HasVideo"/> = <c>false</c>.
    /// </returns>
    /// <remarks>
    /// The answer is cached for <see cref="ProgramCacheDuration"/>. A second caller waits for the load
    /// instead of reading the same five pages at the same time, and a load that comes back empty is not
    /// cached, so a hiccup of 3Cat is retried on the next request.
    /// </remarks>
    public Task<IReadOnlyList<ThreeCatCatalogProgram>> ListAllProgramsAsync(CancellationToken cancellationToken)
        => ListAllProgramsAsync(null, null, cancellationToken);

    /// <summary>
    /// Reads the whole program catalog of 3Cat and tags each program with the kind 3Cat files it under.
    /// </summary>
    /// <param name="taxonomy">
    /// Formats and themes of each program, keyed by <c>programatv_id</c>, or <c>null</c> to leave the
    /// programs unclassified.
    /// </param>
    /// <param name="animeIds">
    /// Programs that are anime, or <c>null</c> when they are not told apart from the rest of the
    /// animation. <c>PUFF_SDIBU</c> covers both, so the category of these programs is «Anime».
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every program of the catalog, with <see cref="ThreeCatCatalogProgram.Category"/> filled.</returns>
    /// <remarks>
    /// The catalog itself is cached without the category and the classification is applied on the way
    /// out: the facets of a program last longer than the catalog, so a catalog read again is classified
    /// with the table already in memory instead of sweeping the facets once more.
    /// </remarks>
    public async Task<IReadOnlyList<ThreeCatCatalogProgram>> ListAllProgramsAsync(
        IReadOnlyDictionary<string, ThreeCatProgramTaxonomy>? taxonomy,
        IReadOnlySet<string>? animeIds,
        CancellationToken cancellationToken)
    {
        var programs = await ListProgramsAsync(cancellationToken).ConfigureAwait(false);

        if (taxonomy is not { Count: > 0 })
        {
            return programs;
        }

        return programs
            .Select(program => taxonomy.TryGetValue(program.Id, out var facets)
                ? program with
                {
                    Category = ProgramClassifier.Classify(
                        facets.Formats,
                        facets.Themes,
                        null,
                        animeIds?.Contains(program.Id) == true).Kind
                }
                : program)
            .ToList();
    }

    /// <summary>
    /// Reads the catalog itself, cached for <see cref="ProgramCacheDuration"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The programs of the catalog, without their category.</returns>
    private async Task<IReadOnlyList<ThreeCatCatalogProgram>> ListProgramsAsync(CancellationToken cancellationToken)
    {
        var cached = _programCache;
        if (IsFresh(cached))
        {
            return cached.Items;
        }

        await ProgramGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = _programCache;
            if (IsFresh(cached))
            {
                return cached.Items;
            }

            var programs = await ReadAllProgramsAsync(cancellationToken).ConfigureAwait(false);

            if (programs.Count > 0)
            {
                _programCache = (DateTimeOffset.UtcNow.Add(ProgramCacheDuration), programs);
            }

            return programs;
        }
        finally
        {
            ProgramGate.Release();
        }
    }

    /// <summary>
    /// Projects a program read from the search endpoint into the catalog entry of the plugin API.
    /// </summary>
    /// <param name="program">Program read from 3Cat.</param>
    /// <returns>The catalog entry.</returns>
    internal static ThreeCatCatalogProgram ToCatalogProgram(ThreeCatSearchProgram program)
    {
        var id = program.Id?.Trim() ?? string.Empty;
        var title = string.IsNullOrWhiteSpace(program.Title) ? id : program.Title.Trim();
        var slug = program.Slug?.Trim();
        var hasVideo = string.Equals(program.Typology, TvProgramTypology, StringComparison.OrdinalIgnoreCase);

        return new ThreeCatCatalogProgram(
            id,
            title,
            string.IsNullOrWhiteSpace(slug)
                ? null
                : $"https://www.3cat.cat/3cat/{Uri.EscapeDataString(slug)}/",
            hasVideo ? "TV" : "Web/radio",
            MainDescription(program.Channels),
            Labels(program.Languages),
            MainDescription(program.AgeRatings),
            NormalizeProgramDate(program.Published),
            hasVideo,
            Children: ProgramClassifier.IsChildren(
                null,
                program.Channels?.Select(channel => channel.Id)));
    }

    /// <summary>
    /// Normalizes a date published by the search endpoint to <c>yyyy-MM-dd</c>, the format that sorts
    /// chronologically as plain text.
    /// </summary>
    /// <param name="value">Date as published by 3Cat.</param>
    /// <returns>The date, or <c>null</c> when 3Cat publishes no usable date.</returns>
    internal static string? NormalizeProgramDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(
            value.Trim(),
            ProgramDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// Reads every page of the program catalog. A page that 3Cat does not answer is skipped (the failure
    /// is already logged by <see cref="GetJsonAsync{T}"/>), so a hiccup in one page does not throw away
    /// the other four.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The programs of the catalog, without duplicates.</returns>
    private async Task<IReadOnlyList<ThreeCatCatalogProgram>> ReadAllProgramsAsync(CancellationToken cancellationToken)
    {
        var first = await GetJsonAsync<ThreeCatSearchEnvelope>(
            ProgramPageUrl(1),
            cancellationToken).ConfigureAwait(false);

        var firstPage = first?.Response;
        if (firstPage?.Items?.Items is not { Count: > 0 } items)
        {
            _logger.LogWarning(
                "3Cat devolvió un catálogo de programas vacío (status {Status}).",
                firstPage?.Status ?? "sin respuesta");

            return [];
        }

        var programs = new List<ThreeCatSearchProgram>(items);
        var totalPages = Math.Clamp(firstPage.Pagination?.TotalPages ?? 1, 1, MaxProgramPages);

        if (totalPages > 1)
        {
            var remaining = new List<ThreeCatSearchProgram>?[totalPages - 1];
            using var gate = new SemaphoreSlim(ProgramPageConcurrency);

            var requests = Enumerable.Range(2, totalPages - 1).Select(async page =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var envelope = await GetJsonAsync<ThreeCatSearchEnvelope>(
                        ProgramPageUrl(page),
                        cancellationToken).ConfigureAwait(false);

                    remaining[page - 2] = envelope?.Response?.Items?.Items;
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(requests).ConfigureAwait(false);

            // The order of the answer is the order of 3Cat, page by page: a program that appears in two
            // pages (the catalog moves while it is read) is listed once.
            foreach (var page in remaining)
            {
                if (page is not null)
                {
                    programs.AddRange(page);
                }
            }
        }

        return programs
            .Where(program => !string.IsNullOrWhiteSpace(program.Id))
            .DistinctBy(program => program.Id, StringComparer.Ordinal)
            .Select(ToCatalogProgram)
            .ToList();
    }

    private static bool IsFresh((DateTimeOffset Expires, IReadOnlyList<ThreeCatCatalogProgram> Items) cached)
        => cached.Items.Count > 0 && cached.Expires > DateTimeOffset.UtcNow;

    private static string ProgramPageUrl(int page)
        => string.Format(
            CultureInfo.InvariantCulture,
            ProgramSearchUrl,
            string.Empty,
            ProgramPageSize,
            page);

    /// <summary>
    /// Picks the <c>desc</c> of the main label, falling back to the first one.
    /// </summary>
    /// <param name="labels">Labels published by 3Cat (channels, age ratings).</param>
    /// <returns>The description, or <c>null</c> when 3Cat publishes none.</returns>
    private static string? MainDescription(List<ThreeCatLabel>? labels)
    {
        var main = labels?
            .FirstOrDefault(label => label.Main && !string.IsNullOrWhiteSpace(label.Description));

        return (main ?? labels?.FirstOrDefault(label => !string.IsNullOrWhiteSpace(label.Description)))
            ?.Description
            ?.Trim();
    }

    private static List<string> Labels(List<ThreeCatLabel>? labels)
        => labels?
            .Select(label => label.Description?.Trim())
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .Select(description => description!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
}
