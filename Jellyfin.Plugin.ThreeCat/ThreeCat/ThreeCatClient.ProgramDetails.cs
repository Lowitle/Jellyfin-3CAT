using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Reads the page of a program (<c>api.3cat.cat/programestv/{id}</c>), the only endpoint of 3Cat that
/// publishes the collections a program is filed under and its country of origin.
/// </summary>
/// <remarks>
/// The endpoint answers one program per request (about 6 KB), unlike the facets of the search endpoint,
/// which return up to 500 programs per page. It is read only for the programs whose classification needs
/// it (the 175 that 3Cat files under animation), and every answer is cached for
/// <see cref="DetailCacheDuration"/>, so a catalog reload or a refresh of two programs pays for it once.
/// </remarks>
public sealed partial class ThreeCatClient
{
    /// <summary>
    /// Detail requests sent at the same time. 3Cat throttles bursts of demand, and the 175 programs of
    /// the animation take about two seconds six at a time.
    /// </summary>
    private const int DetailConcurrency = 6;

    /// <summary>
    /// How many programs are kept before the expired entries are swept out of the cache.
    /// </summary>
    private const int DetailCacheCapacity = 4096;

    /// <remarks>
    /// <c>master=no</c> drops the master-size copies of the images of the program, which are the bulk of
    /// the answer and are of no use here: the card of the program is what is read.
    /// </remarks>
    internal const string ProgramDetailUrl =
        "https://api.3cat.cat/programestv/{0}/?_format=json&sdom=img&version=2.0&cache=180&https=true&master=no";

    /// <summary>
    /// How long the page of a program is kept in memory. The country of origin and the collections of a
    /// program do not change, but the endpoint is cheap enough to be read again a couple of times a day.
    /// </summary>
    private static readonly TimeSpan DetailCacheDuration = TimeSpan.FromHours(6);

    private static readonly ConcurrentDictionary<string, DetailCacheEntry> DetailCache =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Reads the page of a program.
    /// </summary>
    /// <param name="programId">Program id (<c>programatv_id</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The program, or <c>null</c> when 3Cat does not publish it.</returns>
    /// <remarks>
    /// An answer that comes back empty is not cached, so a hiccup of 3Cat is retried on the next call.
    /// </remarks>
    public async Task<ThreeCatProgramDetails?> GetProgramDetailsAsync(
        string? programId,
        CancellationToken cancellationToken)
    {
        var id = programId?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        if (DetailCache.TryGetValue(id, out var cached) && cached.Expires > DateTimeOffset.UtcNow)
        {
            return cached.Details;
        }

        var envelope = await GetJsonAsync<ThreeCatProgramDetailEnvelope>(
            DetailUrl(id),
            cancellationToken).ConfigureAwait(false);

        var details = ThreeCatProgramDetails.From(envelope?.Response?.Item);
        if (details is null)
        {
            _logger.LogDebug("3Cat no publicó la ficha del programa {ProgramId}.", id);
            return null;
        }

        DetailCache[id] = new DetailCacheEntry(DateTimeOffset.UtcNow.Add(DetailCacheDuration), details);
        SweepCache();

        return details;
    }

    /// <summary>
    /// Reads the pages of several programs, a few at a time.
    /// </summary>
    /// <param name="programIds">Ids of the programs to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The programs 3Cat published, keyed by <c>programatv_id</c>.</returns>
    /// <remarks>
    /// A program that 3Cat does not answer is left out of the table instead of failing the whole read:
    /// the caller then treats it as a program it knows nothing about, which is what it is.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, ThreeCatProgramDetails>> GetProgramDetailsAsync(
        IEnumerable<string?> programIds,
        CancellationToken cancellationToken)
    {
        var ids = programIds
            .Select(id => id?.Trim())
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var details = new ConcurrentDictionary<string, ThreeCatProgramDetails>(StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return details;
        }

        using var gate = new SemaphoreSlim(DetailConcurrency);

        var requests = ids.Select(async id =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var program = await GetProgramDetailsAsync(id, cancellationToken).ConfigureAwait(false);
                if (program is not null)
                {
                    details[id] = program;
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(requests).ConfigureAwait(false);

        var missing = ids.Count - details.Count;
        if (missing > 0)
        {
            _logger.LogDebug("3Cat no publicó la ficha de {Missing} de {Total} programas.", missing, ids.Count);
        }

        return details;
    }

    private static string DetailUrl(string programId)
        => string.Format(CultureInfo.InvariantCulture, ProgramDetailUrl, Uri.EscapeDataString(programId));

    /// <summary>
    /// Drops the expired entries once the cache has grown past <see cref="DetailCacheCapacity"/>.
    /// </summary>
    private static void SweepCache()
    {
        if (DetailCache.Count < DetailCacheCapacity)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in DetailCache)
        {
            if (entry.Value.Expires <= now)
            {
                DetailCache.TryRemove(entry.Key, out _);
            }
        }
    }

    /// <summary>
    /// A program of the cache, with the instant it stops being fresh.
    /// </summary>
    /// <param name="Expires">Instant the entry expires.</param>
    /// <param name="Details">Program read from 3Cat.</param>
    private readonly record struct DetailCacheEntry(DateTimeOffset Expires, ThreeCatProgramDetails Details);
}
