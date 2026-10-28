using System.Diagnostics;
using System.Globalization;
using System.Text;
using Jellyfin.Plugin.ThreeCat.Library;
using Jellyfin.Plugin.ThreeCat.ThreeCat;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThreeCat.CatalogDump;

/// <summary>
/// Dumps the whole catalog of 3Cat to a pair of CSV files: one row per program and one row per video.
/// </summary>
/// <remarks>
/// <para>
/// It reuses the client and the classifier of the plugin instead of reimplementing them, so a value of
/// the dump is the value the synchronization writes to the library: the categories («Anime», «Infantil»,
/// «Sèrie»...), the genres of the <c>tvshow.nfo</c>, the audience and the fields the configuration page
/// shows. The only thing the dump adds is the raw payload of every endpoint, which is what makes it
/// useful to study the catalog.
/// </para>
/// <para>
/// The CSVs are written with «;» as separator (the one Excel expects in Spanish), UTF-8 with BOM,
/// ISO dates and no culture-dependent numbers.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>Requests of the same kind sent at the same time.</summary>
    private const int DefaultConcurrency = 6;

    /// <summary>Facet requests sent at the same time, as in the plugin.</summary>
    private const int FacetConcurrency = 4;

    /// <summary>Name of the CSV of programs.</summary>
    private const string ProgramFile = "catalogo-3cat-programas.csv";

    /// <summary>Name of the CSV of videos.</summary>
    private const string VideoFile = "catalogo-3cat-videos.csv";

    /// <summary>
    /// Columns of the CSV of programs: the raw fields of the search endpoint, the fields the plugin
    /// derives from them, the page of the program and the aggregates of the catalog of its videos.
    /// </summary>
    private static readonly string[] ProgramColumns =
    [
        "id",
        "titol",
        "slug",
        "tipologia",
        "kind",
        "has_video",
        "data_publicacio",
        "published",
        "url",
        "canals",
        "channel",
        "idiomes",
        "languages",
        "codis_etics",
        "age_rating",
        "children_busqueda",
        "category",
        "facet_formats",
        "facet_themes",
        "detail_title",
        "detail_original_title",
        "detail_formats",
        "detail_groups",
        "detail_countries",
        "is_animation",
        "anime",
        "children_sync",
        "series_genres",
        "genres",
        "videos_3cat",
        "videos_leidos",
        "videos_caducados",
        "videos_vigentes",
        "primera_emissio",
        "ultima_emissio",
        "ultima_caducitat",
        "duracion_total_segundos",
        "duracion_media_segundos",
        "temporadas",
        "capitulos",
        "video_targets",
        "video_canals",
        "video_idiomes",
        "video_tematiques",
        "video_geolocalitzacions",
        "num_imagenes",
        "poster_url",
        "paginas_catalogo",
        "error"
    ];

    /// <summary>Columns of the CSV of videos: every field of the catalog entry of a video.</summary>
    private static readonly string[] VideoColumns =
    [
        "programa_id",
        "programa_titol",
        "video_id",
        "capitol",
        "temporada",
        "titol",
        "nom_friendly",
        "permatitle",
        "tipus_contingut",
        "durada",
        "durada_segundos",
        "data_publicacio",
        "data_emissio",
        "data_caducitat",
        "caducat",
        "programes_tv",
        "bbands",
        "tematiques",
        "targets",
        "canals",
        "idiomes",
        "geolocalitzacions",
        "num_imatges",
        "poster_url"
    ];

    /// <summary>Entry point.</summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Zero on success.</returns>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // A redirected console has no encoding to change.
        }

        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usa --help para ver las opciones.");
            return 1;
        }

        if (options.ShowHelp)
        {
            Options.PrintHelp();
            return 0;
        }

        using var factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(options.Verbose ? LogLevel.Information : LogLevel.Warning)
            .AddSimpleConsole(console => console.SingleLine = true));

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
        http.BaseAddress = new Uri("https://api.3cat.cat/", UriKind.Absolute);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("JellyfinThreeCat/1.4 (+https://github.com/jellyfin)");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ca-ES,ca;q=0.9,es;q=0.8");

        var client = new ThreeCatClient(http, factory.CreateLogger<ThreeCatClient>());

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            return await DumpAsync(client, options, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelado: los CSV escritos hasta ahora están completos por filas.");
            return 2;
        }
    }

    /// <summary>
    /// Reads the catalog and writes the CSV files.
    /// </summary>
    /// <param name="client">Client of 3Cat.</param>
    /// <param name="options">Options of the run.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Zero on success.</returns>
    private static async Task<int> DumpAsync(
        ThreeCatClient client,
        Options options,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputDirectory);

        var started = Stopwatch.StartNew();

        Console.WriteLine("3Cat: leyendo las facetas del catálogo…");
        var taxonomy = await client.GetProgramTaxonomyAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"   {taxonomy.Count} programas con facetas ({started.Elapsed:mm\\:ss}).");

        // The page of a program is the only place that publishes its country of origin and its anime
        // collections, so the animation is read first: it is the list the plugin reads to tell an anime
        // from the rest of the animation.
        var animationIds = taxonomy
            .Where(entry => ProgramClassifier.IsAnimation(entry.Value.Formats, entry.Value.Themes))
            .Select(entry => entry.Key)
            .ToList();

        Console.WriteLine($"3Cat: leyendo la ficha de {animationIds.Count} programas de animación…");
        var details = new Dictionary<string, ThreeCatProgramDetails>(
            await client.GetProgramDetailsAsync(animationIds, cancellationToken).ConfigureAwait(false),
            StringComparer.Ordinal);

        var animeCount = details.Values.Count(detail => ProgramClassifier.IsAnime(detail.Groups, detail.Countries));
        Console.WriteLine($"   {animeCount} programas son anime ({started.Elapsed:mm\\:ss}).");

        Console.WriteLine("3Cat: leyendo el catálogo de programas…");
        var programs = await ReadProgramsAsync(client, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"   {programs.Count} programas ({started.Elapsed:mm\\:ss}).");

        if (options.ReadAllDetails)
        {
            var pending = programs
                .Where(program => string.Equals(
                    program.Typology,
                    ThreeCatClient.TvProgramTypology,
                    StringComparison.OrdinalIgnoreCase))
                .Select(program => program.Id!.Trim())
                .Where(id => !details.ContainsKey(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Console.WriteLine($"3Cat: leyendo la ficha de {pending.Count} programas de televisión…");
            foreach (var entry in await client.GetProgramDetailsAsync(pending, cancellationToken).ConfigureAwait(false))
            {
                details[entry.Key] = entry.Value;
            }

            Console.WriteLine($"   {details.Count} fichas leídas ({started.Elapsed:mm\\:ss}).");
        }

        var selected = options.Limit > 0 ? programs.Take(options.Limit).ToList() : programs.ToList();
        var programPath = Path.Combine(options.OutputDirectory, ProgramFile);
        var videoPath = Path.Combine(options.OutputDirectory, VideoFile);

        using var programWriter = NewWriter(programPath);
        using var videoWriter = options.WriteVideos ? NewWriter(videoPath) : null;

        WriteRow(programWriter, ProgramColumns);
        if (videoWriter is not null)
        {
            WriteRow(videoWriter, VideoColumns);
        }

        var processed = 0;
        await Parallel.ForEachAsync(
            selected,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Concurrency,
                CancellationToken = cancellationToken
            },
            async (program, token) =>
            {
                var row = await DumpProgramAsync(client, program, taxonomy, details, options, videoWriter, token)
                    .ConfigureAwait(false);

                if (row.Length != ProgramColumns.Length)
                {
                    throw new InvalidOperationException(
                        $"La fila del programa {program.Id} tiene {row.Length} columnas y se esperaban {ProgramColumns.Length}.");
                }

                lock (programWriter)
                {
                    WriteRow(programWriter, row);
                }

                var count = Interlocked.Increment(ref processed);
                if (count % 100 == 0 || count == selected.Count)
                {
                    Console.WriteLine($"   {count}/{selected.Count} programas ({started.Elapsed:mm\\:ss}).");
                }
            }).ConfigureAwait(false);

        programWriter.Flush();
        videoWriter?.Flush();

        Console.WriteLine($"Hecho en {started.Elapsed:mm\\:ss}.");
        Console.WriteLine($"   {programPath} ({Size(programPath)})");
        if (videoWriter is not null)
        {
            Console.WriteLine($"   {videoPath} ({Size(videoPath)})");
        }

        return 0;
    }

    /// <summary>
    /// Reads the catalog of a program and builds its row of the CSV.
    /// </summary>
    /// <param name="client">Client of 3Cat.</param>
    /// <param name="program">Program read from the search endpoint.</param>
    /// <param name="taxonomy">Facets of every program, keyed by id.</param>
    /// <param name="details">Page of the programs that were read, keyed by id.</param>
    /// <param name="options">Options of the run.</param>
    /// <param name="videoWriter">Writer of the CSV of videos, or <c>null</c> when it is not written.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The row of the program, in the order of <see cref="ProgramColumns"/>.</returns>
    private static async Task<string?[]> DumpProgramAsync(
        ThreeCatClient client,
        ThreeCatSearchProgram program,
        IReadOnlyDictionary<string, ThreeCatProgramTaxonomy> taxonomy,
        IReadOnlyDictionary<string, ThreeCatProgramDetails> details,
        Options options,
        TextWriter? videoWriter,
        CancellationToken cancellationToken)
    {
        var catalog = ThreeCatClient.ToCatalogProgram(program);
        taxonomy.TryGetValue(catalog.Id, out var facets);
        details.TryGetValue(catalog.Id, out var detail);

        var error = string.Empty;
        var page = new ThreeCatCatalogPage([], 0, 0, false);

        if (catalog.HasVideo)
        {
            try
            {
                page = await client
                    .GetProgramVideosAsync(catalog.Id, options.MaxVideosPerProgram, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }

        var videos = page.Items;

        if (videoWriter is not null)
        {
            foreach (var video in videos)
            {
                var videoRow = VideoRow(catalog.Id, catalog.Title, video);
                lock (videoWriter)
                {
                    WriteRow(videoWriter, videoRow);
                }
            }
        }

        // The classification is the one of ThreeCatSyncService.ClassifyProgramAsync with the two
        // switches of the configuration page on: «genres» is the value the tvshow.nfo would get.
        var seriesGenres = SeriesGenres(videos);
        var animation = ProgramClassifier.IsAnimation(facets?.Formats, facets?.Themes);
        var anime = animation && detail is not null && ProgramClassifier.IsAnime(detail.Groups, detail.Countries);
        var targetIds = videos.SelectMany(video => video.Targets?.Targets ?? []).Select(target => target.Id).ToList();
        var channelIds = videos.SelectMany(video => video.Channels?.Channels ?? []).Select(channel => channel.Id).ToList();
        var children = ProgramClassifier.IsChildren(targetIds, channelIds);
        var classification = ProgramClassifier.Classify(
            facets?.Formats ?? detail?.Formats,
            facets?.Themes,
            seriesGenres,
            anime,
            children);

        var now = DateTimeOffset.UtcNow;
        var expired = videos.Count(video => video.Expires is { } expires && expires < now);
        var broadcasts = videos.Where(video => video.Broadcast.HasValue).Select(video => video.Broadcast!.Value).ToList();
        var expirations = videos.Where(video => video.Expires.HasValue).Select(video => video.Expires!.Value).ToList();
        var durations = videos.Where(video => video.Duration.HasValue).Select(video => video.Duration!.Value).ToList();
        var images = videos
            .SelectMany(video => video.Programs ?? [])
            .SelectMany(node => node.Images ?? [])
            .Where(image => !string.IsNullOrWhiteSpace(image.Url))
            .ToList();

        return
        [
            catalog.Id,
            catalog.Title,
            program.Slug,
            program.Typology,
            catalog.Kind,
            B(catalog.HasVideo),
            program.Published,
            catalog.Published,
            catalog.Url,
            LabelList(program.Channels),
            catalog.Channel,
            LabelList(program.Languages),
            string.Join(" | ", catalog.Languages),
            LabelList(program.AgeRatings),
            catalog.AgeRating,
            B(catalog.Children),
            classification.Kind,
            TextList(facets?.Formats),
            TextList(facets?.Themes),
            detail?.Title,
            detail?.OriginalTitle,
            TextList(detail?.Formats),
            TextList(detail?.Groups),
            string.Join(" | ", detail?.Countries ?? []),
            B(animation),
            B(anime),
            B(children),
            string.Join(" | ", seriesGenres),
            string.Join(" | ", classification.Genres),
            Number(page.TotalItems > 0 ? page.TotalItems : videos.Count),
            Number(videos.Count),
            Number(expired),
            Number(videos.Count - expired),
            Date(broadcasts.Count == 0 ? null : broadcasts.Min()),
            Date(broadcasts.Count == 0 ? null : broadcasts.Max()),
            Date(expirations.Count == 0 ? null : expirations.Max()),
            Number((long)durations.Sum(duration => duration.TotalSeconds)),
            Number(durations.Count == 0 ? null : durations.Average(duration => duration.TotalSeconds)),
            Number(videos.Where(video => video.Season is > 0).Select(video => video.Season!.Value).Distinct().Count()),
            Number(videos.Where(video => video.Chapter is > 0).Select(video => video.Chapter!.Value).Distinct().Count()),
            TextList(targetIds),
            TextList(channelIds),
            TextList(videos.SelectMany(video => video.Languages ?? []).Select(language => language.Id)),
            TextList(videos.SelectMany(video => video.Themes ?? []).Select(theme => theme.Description)),
            TextList(videos.SelectMany(video => video.Geolocations ?? []).Select(geo => geo.Id)),
            Number(images.Select(image => image.Url).Distinct(StringComparer.OrdinalIgnoreCase).Count()),
            FindPoster(images),
            Number(page.TotalPages),
            error.Length == 0 ? null : error
        ];
    }

    /// <summary>
    /// Reads every page of the program catalog of 3Cat, the same five requests the configuration page
    /// makes.
    /// </summary>
    /// <param name="client">Client of 3Cat.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The programs, in the order 3Cat publishes them.</returns>
    private static async Task<IReadOnlyList<ThreeCatSearchProgram>> ReadProgramsAsync(
        ThreeCatClient client,
        CancellationToken cancellationToken)
    {
        var first = await client
            .GetJsonAsync<ThreeCatSearchEnvelope>(PageUrl(1), cancellationToken)
            .ConfigureAwait(false);

        var page = first?.Response;
        if (page?.Items?.Items is not { Count: > 0 } items)
        {
            throw new InvalidOperationException(
                $"3Cat no devolvió el catálogo de programas (status {page?.Status ?? "sin respuesta"}).");
        }

        var programs = new List<ThreeCatSearchProgram>(items);
        var totalPages = Math.Clamp(page.Pagination?.TotalPages ?? 1, 1, ThreeCatClient.MaxProgramPages);

        if (totalPages > 1)
        {
            var remaining = new List<ThreeCatSearchProgram>?[totalPages - 1];
            using var gate = new SemaphoreSlim(FacetConcurrency);

            await Task.WhenAll(Enumerable.Range(2, totalPages - 1).Select(async number =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var envelope = await client
                        .GetJsonAsync<ThreeCatSearchEnvelope>(PageUrl(number), cancellationToken)
                        .ConfigureAwait(false);

                    remaining[number - 2] = envelope?.Response?.Items?.Items;
                }
                finally
                {
                    gate.Release();
                }
            })).ConfigureAwait(false);

            foreach (var batch in remaining)
            {
                if (batch is not null)
                {
                    programs.AddRange(batch);
                }
            }
        }

        return programs
            .Where(program => !string.IsNullOrWhiteSpace(program.Id))
            .DistinctBy(program => program.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Builds the URL of a page of the program catalog.
    /// </summary>
    /// <param name="page">Page number, 1 based.</param>
    /// <returns>Absolute URL.</returns>
    private static string PageUrl(int page)
        => string.Format(
            CultureInfo.InvariantCulture,
            ThreeCatClient.ProgramSearchUrl,
            string.Empty,
            ThreeCatClient.ProgramPageSize,
            page);

    /// <summary>
    /// Builds the row of a video.
    /// </summary>
    /// <param name="programId">Id of the program the video belongs to.</param>
    /// <param name="programTitle">Title of the program.</param>
    /// <param name="video">Video read from the catalog.</param>
    /// <returns>The row, in the order of <see cref="VideoColumns"/>.</returns>
    private static string?[] VideoRow(string programId, string programTitle, ThreeCatVideo video)
        =>
        [
            programId,
            programTitle,
            video.Id,
            Number(video.Chapter),
            Number(video.Season),
            video.Title,
            video.FriendlyName,
            video.PermaTitle,
            video.ContentType,
            video.Duration?.ToString("c", CultureInfo.InvariantCulture),
            Number(video.Duration?.TotalSeconds),
            Date(video.Published),
            Date(video.Broadcast),
            Date(video.Expires),
            B(video.Expires is { } expires && expires < DateTimeOffset.UtcNow),
            ProgramList(video.Programs),
            BandList(video.Bands),
            LabelList(video.Themes),
            LabelList(video.Targets?.Targets),
            LabelList(video.Channels?.Channels),
            LabelList(video.Languages),
            LabelList(video.Geolocations),
            Number(video.Images?.Count(image => !string.IsNullOrWhiteSpace(image.Url)) ?? 0),
            FindPoster(video.Programs)
        ];

    /// <summary>
    /// Reads the genres 3Cat publishes per video, which are the fallback of the classification and the
    /// themes the series keeps when 3Cat files it under no known facet.
    /// </summary>
    /// <param name="videos">Videos of the program.</param>
    /// <returns>The distinct themes, the main ones when the catalog flags any.</returns>
    private static List<string> SeriesGenres(IReadOnlyList<ThreeCatVideo> videos)
    {
        var themes = videos
            .SelectMany(video => video.Themes ?? [])
            .Where(theme => !string.IsNullOrWhiteSpace(theme.Description))
            .ToList();

        var genres = themes
            .Where(theme => theme.Main)
            .Select(theme => theme.Description!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return genres.Count > 0
            ? genres
            : themes
                .Select(theme => theme.Description!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    /// <summary>
    /// Finds the poster of the programs node of a video.
    /// </summary>
    /// <param name="programs">Programs node of a video.</param>
    /// <returns>The URL of the poster, of the first image, or <c>null</c>.</returns>
    private static string? FindPoster(IEnumerable<ThreeCatProgram>? programs)
        => programs is null ? null : FindPoster(programs.SelectMany(program => program.Images ?? []));

    /// <summary>
    /// Finds the poster of a set of images.
    /// </summary>
    /// <param name="images">Images of the catalog.</param>
    /// <returns>The URL of the poster, of the first image, or <c>null</c>.</returns>
    private static string? FindPoster(IEnumerable<ThreeCatImage> images)
    {
        var candidates = images
            .Where(image => !string.IsNullOrWhiteSpace(image.Url))
            .ToList();

        return (candidates.FirstOrDefault(IsPoster) ?? candidates.FirstOrDefault())?.Url;
    }

    private static bool IsPoster(ThreeCatImage image)
        => image.Relation is not null
            && image.Relation.Contains("POSTER", StringComparison.OrdinalIgnoreCase);

    private static string B(bool value) => value ? "true" : "false";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(double? value)
        => value?.ToString("0", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Date(DateTimeOffset? value)
        => value?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string TextList(IEnumerable<string?>? values)
        => string.Join(
            " | ",
            (values ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string LabelList(IEnumerable<ThreeCatLabel>? labels)
        => string.Join(
            " | ",
            (labels ?? [])
                .Where(label => !string.IsNullOrWhiteSpace(label.Description))
                .Select(label => $"{label.Id}:{label.Description}{(label.Main ? "*" : string.Empty)}"));

    private static string ProgramList(IEnumerable<ThreeCatProgram>? programs)
        => string.Join(
            " | ",
            (programs ?? []).Select(program => $"{program.Id}:{program.Description ?? program.Title}"));

    private static string BandList(IEnumerable<ThreeCatBand>? bands)
        => string.Join(
            " | ",
            (bands ?? []).Select(band => $"{band.Id}:{band.Description}{(band.Main ? "*" : string.Empty)}"));

    /// <summary>
    /// Writes a row of a CSV, quoting every value so the separator never splits a field.
    /// </summary>
    /// <param name="writer">Writer of the file.</param>
    /// <param name="values">Values of the row.</param>
    private static void WriteRow(TextWriter writer, IEnumerable<string?> values)
    {
        var first = true;

        foreach (var value in values)
        {
            if (!first)
            {
                writer.Write(';');
            }

            first = false;
            writer.Write('"');
            writer.Write(value?
                .Replace("\"", "\"\"", StringComparison.Ordinal)
                .Replace('\r', ' ')
                .Replace('\n', ' '));
            writer.Write('"');
        }

        writer.Write("\r\n");
    }

    /// <summary>
    /// Creates a writer of UTF-8 with BOM, the encoding Excel expects for the accents.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <returns>The writer.</returns>
    private static StreamWriter NewWriter(string path)
        => new(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
        {
            AutoFlush = false,
            NewLine = "\r\n"
        };

    /// <summary>
    /// Formats the size of a file.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <returns>The size in KB or MB.</returns>
    private static string Size(string path)
    {
        var bytes = new FileInfo(path).Length;

        return bytes < 1024 * 1024
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024} KB")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.0} MB");
    }

    /// <summary>
    /// Options of the run.
    /// </summary>
    private sealed class Options
    {
        /// <summary>
        /// Gets the directory the CSVs are written to.
        /// </summary>
        public string OutputDirectory { get; private set; } = Directory.GetCurrentDirectory();

        /// <summary>
        /// Gets a value indicating whether the CSV of videos is written.
        /// </summary>
        public bool WriteVideos { get; private set; } = true;

        /// <summary>
        /// Gets a value indicating whether the page of every television program is read, not only the
        /// page of the animation.
        /// </summary>
        public bool ReadAllDetails { get; private set; } = true;

        /// <summary>
        /// Gets how many videos are read per program. Zero reads the whole catalog of the program.
        /// </summary>
        public int MaxVideosPerProgram { get; private set; }

        /// <summary>
        /// Gets how many programs are read at the same time.
        /// </summary>
        public int Concurrency { get; private set; } = DefaultConcurrency;

        /// <summary>
        /// Gets how many programs are dumped. Zero dumps the whole catalog.
        /// </summary>
        public int Limit { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the log of the client is shown.
        /// </summary>
        public bool Verbose { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the help was asked for.
        /// </summary>
        public bool ShowHelp { get; private set; }

        /// <summary>
        /// Reads the command line.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <returns>The options.</returns>
        /// <exception cref="ArgumentException">An option is unknown or misses its value.</exception>
        public static Options Parse(string[] args)
        {
            var options = new Options();

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--output":
                    case "-o":
                        options.OutputDirectory = Value(args, ref index);
                        break;
                    case "--no-videos":
                        options.WriteVideos = false;
                        break;
                    case "--details-animation":
                        options.ReadAllDetails = false;
                        break;
                    case "--max-videos":
                        options.MaxVideosPerProgram = Number(Value(args, ref index), 0);
                        break;
                    case "--limit":
                        options.Limit = Number(Value(args, ref index), 0);
                        break;
                    case "--concurrency":
                        options.Concurrency = Math.Clamp(Number(Value(args, ref index), 1), 1, 32);
                        break;
                    case "--verbose":
                    case "-v":
                        options.Verbose = true;
                        break;
                    case "--help":
                    case "-h":
                        options.ShowHelp = true;
                        break;
                    default:
                        throw new ArgumentException($"Opción desconocida: {args[index]}.");
                }
            }

            return options;
        }

        /// <summary>
        /// Writes the help.
        /// </summary>
        public static void PrintHelp()
            => Console.WriteLine(
                """
                3Cat: volcado del catálogo a CSV.

                Uso:
                  dotnet run --project tools/CatalogDump -- [opciones]

                Opciones:
                  -o, --output <dir>       Carpeta donde se escriben los CSV (por defecto, la actual).
                      --no-videos          No escribe el CSV de vídeos, solo el de programas.
                      --details-animation  Lee la ficha solo de los programas de animación (más rápido,
                                           pero detail_* sale vacío en los demás programas).
                      --max-videos <n>     Lee como mucho n vídeos por programa (0 = todos, por defecto).
                      --limit <n>          Vuelca como mucho n programas (0 = todos, por defecto).
                      --concurrency <n>    Programas leídos a la vez (1-32, por defecto 6).
                  -v, --verbose            Muestra el log del cliente de 3Cat.
                  -h, --help               Muestra esta ayuda.

                Ficheros:
                  catalogo-3cat-programas.csv  Una fila por programa: los campos del buscador, los que
                                               deriva el plugin (kind, category, anime, children_sync,
                                               genres...) y los agregados de sus vídeos.
                  catalogo-3cat-videos.csv     Una fila por vídeo de cada programa.

                Detalles:
                  «category», «anime», «children_sync» y «genres» se calculan con las dos casillas de la
                  configuración activadas (clasificar programas y separar anime e infantil), con la
                  misma lectura que hace la sincronización: es lo que acabaría en el tvshow.nfo.
                  «videos_3cat» es el total que publica 3Cat; «videos_leidos», lo que se ha leído.
                """);

        /// <summary>
        /// Reads the value of an option.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <param name="index">Position of the option; it is moved to its value.</param>
        /// <returns>The value.</returns>
        private static string Value(string[] args, ref int index)
        {
            index++;

            return index < args.Length
                ? args[index]
                : throw new ArgumentException("Falta el valor de la última opción.");
        }

        /// <summary>
        /// Reads a number of the command line.
        /// </summary>
        /// <param name="value">Text of the number.</param>
        /// <param name="minimum">Smallest accepted value.</param>
        /// <returns>The number.</returns>
        private static int Number(string value, int minimum)
            => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                && number >= minimum
                    ? number
                    : throw new ArgumentException($"«{value}» no es un número mayor o igual que {minimum}.");
    }
}
