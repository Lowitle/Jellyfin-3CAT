using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.ThreeCat;

/// <summary>
/// Catalog reads of <see cref="ThreeCatClient"/>.
/// </summary>
public sealed partial class ThreeCatClient
{
    /// <summary>
    /// Reads the whole catalog of a program, page by page.
    /// </summary>
    /// <param name="programId">Program id (<c>programatv_id</c>).</param>
    /// <param name="maxItems">Maximum number of videos to keep (0 means no limit).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The videos of the program and the pagination reported by 3Cat.</returns>
    public async Task<ThreeCatCatalogPage> GetProgramVideosAsync(
        string programId,
        int maxItems,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programId);

        var videos = new List<ThreeCatVideo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var totalPages = 1;
        var totalItems = 0;
        var truncated = false;
        var page = 1;

        while (page <= totalPages && page <= MaxPages)
        {
            var url = string.Format(
                CultureInfo.InvariantCulture,
                CatalogUrl,
                Uri.EscapeDataString(programId),
                PageSize,
                page);

            var envelope = await GetJsonAsync<ThreeCatEnvelope>(url, cancellationToken).ConfigureAwait(false);
            var response = envelope?.Response;
            var batch = response?.Items?.Items;

            if (batch is null || batch.Count == 0)
            {
                if (page == 1)
                {
                    _logger.LogWarning(
                        "3Cat devolvió un catálogo vacío para el programa {ProgramId} (status {Status}).",
                        programId,
                        response?.Status ?? "sin respuesta");
                }

                break;
            }

            totalPages = Math.Max(1, response!.Pagination?.TotalPages ?? page);
            totalItems = (int)Math.Min(int.MaxValue, response.Pagination?.TotalItems ?? 0);

            foreach (var video in batch)
            {
                if (string.IsNullOrWhiteSpace(video.Id) || !seen.Add(video.Id))
                {
                    continue;
                }

                videos.Add(video);

                if (maxItems > 0 && videos.Count >= maxItems)
                {
                    truncated = true;
                    break;
                }
            }

            if (truncated)
            {
                break;
            }

            page++;
        }

        if (page > MaxPages)
        {
            _logger.LogWarning(
                "El programa {ProgramId} publica más de {Pages} páginas: se han leído solo las primeras {Max}.",
                programId,
                totalPages,
                MaxPages);
        }

        return new ThreeCatCatalogPage(videos, totalPages, totalItems, truncated);
    }
}
