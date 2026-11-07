# Hoja de ruta

Notas de trabajo. Estado de la **v1.11.0.0**: TVDB es una fuente de numeración completa —en su orden de
emisión y en el absoluto—, y `README.md` y `resumen.md` describen el comportamiento de 1.11.0.0. Las notas
de 1.10.0.0 y 1.5.0.0 se conservan como registro de las decisiones anteriores —tabla de programas,
clasificación por programa, sub-bibliotecas, numeración, desplegable de biblioteca, espejo y películas—,
que siguen explicando por qué el código es como es.

## v1.11.0.0

### 1. TVDB deja de ser una promesa (implementado)

**Decisión.** TVDB entró en el enum en 1.7.0.0 como valor reservado —`MetadataSource.Tvdb = 2`, con un
`Fallback` que dejaba la serie en la numeración de 3Cat— porque sin cliente no había forma de numerar con
ella. Ahora hay cliente: `Providers/TvdbClient.cs` inicia sesión, lee los episodios y los devuelve en el
mismo `ProviderEpisode` que TMDB, así que todo lo que ya sabía hacer el emparejamiento —anclas, fecha,
contador `capitol`, desfase de partes, título, orden, umbral de cobertura y caída a 3Cat— vale igual para
las dos fuentes. La rama de `ResolveNumberingAsync` deja de mirar una sola fuente y pregunta a
`MetadataSources` qué proveedor es.

**Sesión.** TVDB no acepta la clave en cada petición: se cambia por un bearer token en `POST /login` que
dura un mes. El cliente lo guarda en un diccionario estático por par de clave y PIN, serializa los inicios
de sesión con un `SemaphoreSlim` —una actualización que arranca veinte programas no pide veinte tokens—,
lo renueva cuando quedan menos de dos días y lo tira y repite la página cuando una lectura responde 401 (un
token caducado, no una clave mala: una clave rechazada en el login no se reintenta).

**Órdenes.** `GET series/{id}/episodes/{season-type}?page=N` devuelve una página y `links.next`; el cliente
recorre las páginas con un tope de cien. El orden `default` es el de emisión: los especiales (temporada 0)
se quedan fuera, porque no son emisiones de 3Cat y se robarían la pareja de un episodio normal. El orden
`absolute` es el que archiva el anime, una sola temporada con el `absoluteNumber` de cada episodio; si TVDB
no lo ha curado (`data.episodes` vacío) el cliente lee el orden de emisión y traduce cada episodio por su
`absoluteNumber`, y aun así la numeración sale igual en vez de quedarse sin serie.

**Por qué es una fuente y no una casilla.** «TVDB absoluta» es `MetadataSource.TvdbAbsolute = 3`, no un
booleano al lado de la fuente: la fuente ya viaja en la configuración global, en el `Override` de cada
programa, en el informe de la vista previa y en `WrittenSource`, así que un valor más hereda todo eso —la
detección de cambio de numeración que reescribe la carpeta, por ejemplo— y no hay dos ejes que puedan
contradecirse. El enum documenta desde 1.7.0.0 que solo se pueden añadir valores, nunca renombrarlos.

**Un `uniqueid`, dos órdenes.** Las dos fuentes de TVDB escriben el mismo `uniqueid type="tvdb"`: son la
misma serie para Jellyfin y lo que cambia es la numeración de los ficheros. `MetadataSources.UsesTvdb` es
lo que consulta `NfoBuilder`, para que añadir la fuente absoluta no dejara el `.nfo` sin id.

**Pruebas.** `TvdbClientTests` (orden de emisión sin especiales, token reutilizado entre series, páginas,
orden absoluto, caída al orden de emisión, sin clave, sin id, clave rechazada, token caducado que se
renueva), un caso de `NfoBuilderTests` para el `uniqueid` de la fuente absoluta, `ConfigPageTests` (los
selectores, los dos campos nuevos y que TVDB ya no se ofrece deshabilitada) y `PluginConfigurationTests`
(versión 6 y los dos campos vacíos por defecto). `dotnet test`: 487 en verde.

**Ficheros.** `Providers/TvdbClient.cs`, `Providers/TvdbModels.cs`,
`Configuration/MetadataSources.cs`, `Configuration/MetadataSource.cs`,
`Configuration/PluginConfiguration.cs`, `Sync/ThreeCatSyncService.Providers.cs`,
`Sync/ThreeCatSyncService.cs`, `Sync/ThreeCatSyncService.Programs.cs`, `Library/NfoBuilder.cs`,
`ThreeCat/ProgramReference.cs`, `PluginServiceRegistrator.cs`, `Api/ThreeCatPlayController.cs`,
`Configuration/configPage.html`, `Tests/TvdbClientTests.cs`, `Tests/NfoBuilderTests.cs`,
`Tests/ConfigPageTests.cs`, `Tests/PluginConfigurationTests.cs`, `Directory.Build.props`, `build.yaml`,
`README.md`, `resumen.md`.

**Atribución.** TheTVDB licencia su API para proyectos que leen datos en nombre de sus usuarios con
suscripción o licencia con atribución; el plugin no reparte ninguna clave (cada instalación pone la suya) y
la página lo dice junto al campo. Si la clave no se escribe, la fuente TVDB se comporta como en 1.10.0.0:
la serie se numera con 3Cat y el registro dice por qué.

## v1.10.0.0

### 1. La tabla de programas deja de pedir lo que ya se sabe (implementado)

**Decisión.** La tabla enseña lo que el usuario decide, no lo que el plugin deduce. El título y la
referencia comparten celda —el `programatv_id` es un dato de consulta, no una columna que se ordene— y
desaparecen las columnas del id de TMDB, de las anclas y de la copia por programa: el id y las anclas se
resuelven con el dataset de identificadores y se revisan en la vista previa de la numeración, y una
segunda copia dejó de ser una decisión por programa. La columna **Fuente de metadatos** se queda, porque
es una elección por programa que ninguna otra vista muestra.

**Compatibilidad.** Nada de lo que sale de la tabla se pierde: `TmdbId`, `Anchors` y `DestinationPath`
siguen en `ThreeCatProgramReference`, la tarea los sigue leyendo y la página los reescribe tal cual al
guardar. Una copia configurada por una versión anterior se muestra bajo la clasificación, con el botón
que la quita.

### 2. La clasificación se fija por programa (implementado)

**Decisión.** El tipo de programa —la carpeta donde se escribe— pasa a ser una decisión del usuario y no
solo un efecto del catálogo. La columna **Clasificación** muestra el tipo que resolvió la última
ejecución («Automático (Anime)») y permite fijar otro, que se guarda en
`ThreeCatProgramReference.CategoryOverride` y gana en la siguiente ejecución
(`LibraryCategory.Resolve(classification, pinned)`).

**Límites.** Fijar la clasificación cambia la carpeta, nunca los géneros: un documental movido a
«Infantil» sigue llevando «Documental» como primer género, porque los dos datos vienen del catálogo y
una biblioteca filtrada por género no puede contradecir a la carpeta. «Movies» no se puede fijar: no es
una carpeta, es la forma de los archivos (una película se escribe como película), y `LibraryCategory`
ignora el valor. Un valor que no sea una carpeta del layout tampoco se inventa.

**Caché.** La clasificación es un barrido de las facetas del buscador de 3Cat, así que la página no
puede calcularla: la última ejecución deja el resultado en `ThreeCatProgramReference.Category`, que es
lo que la columna muestra cuando el valor es automático.

### 3. Sub-bibliotecas por tipo, sin copiar archivos (implementado)

**Decisión.** Una biblioteca curada («Anime», «Infantil», «Documentals»…) se prepara añadiendo la
carpeta de un tipo a una biblioteca que ya exista, no copiando los archivos de cada programa: Jellyfin
indexa una carpeta en todas las bibliotecas que la apuntan, así que el programa aparece en «3Cat» y en
la curada con un solo juego de archivos y cada biblioteca lleva su propio «visto». La página guarda
pares tipo → biblioteca en `PluginConfiguration.KindLibraries` y los aplica con
`LibraryProvisioner.Attach`, que es idempotente: aplicar dos veces el mismo tipo no duplica la carpeta.

**Por qué es manual.** Añadir una carpeta indexada cambia la biblioteca del usuario, así que es una
decisión suya, con su botón («Aplicar a las bibliotecas»), y nunca un efecto secundario de guardar la
página o de una actualización. `POST ThreeCat/Library/Kinds` lee la configuración guardada —la página
guarda antes de pedirlo— y `GET ThreeCat/Library/Categories` es lo que llena la tabla: los nombres de
las carpetas se escriben una sola vez, en `LibraryCategory`.

## v1.5.0.0

### 1. Numeración de temporadas y episodios (implementado)

**Decisión.** La fuente de la numeración es el **título**, y solo el título: `T22xC6` es lo que
muestra la web de 3Cat y lo que el usuario espera. Los campos `capitol` y `capitol_temporada` se
ignoran, y `temporades` no se pide siquiera: coincide con la `T` del título en el 100 % de los casos
comprobados, así que depender de él solo añadiría una forma de quedarse sin número.

**Síntoma.** Series como «El detectiu Conan» se escriben como
`El detectiu Conan - S06E907 - T22xC6 - Visita als inferns de l'amor (Episodi d'Oita).strm`:
la numeración aparece dos veces y no correlaciona. «Crackòvia» y «Polònia» acaban casi enteras
en `Season 1`.

**Causa.** El plugin interpreta el campo `capitol_temporada` del catálogo como la temporada y
el campo `capitol` como el episodio:

```csharp
var seasonNumbered  = usable.All(video => video.Season  is > 0);
var episodeNumbered = usable.All(video => video.Chapter is > 0);
var season = seasonNumbered  ? video.Season!.Value  : 1;
var number = episodeNumbered ? video.Chapter!.Value : index + 1;
```

3Cat no publica ahí eso. Comprobado en vivo el 27/10/2026 con `videos?programatv_id=…`:

| Campo | Significado real | Ejemplo (Conan) |
| --- | --- | --- |
| `temporades` (`{id, desc, main}`) | **Temporada real** | `{"id":"PUTEMP_22","desc":"22a Temporada"}` |
| `capitol_temporada` | **Número de episodio dentro de su temporada** (el «C6» de la ficha) | `6` |
| `capitol` | **Número global del episodio** en todo el programa | `907` |

Verificación sobre 10 programas al azar (183 vídeos con título `TaxCy`): `temporades` coincide
con la `T` del título en el 100 % de los casos y `capitol_temporada` con la `C` en el 100 %.

Además el fallback es **todo o nada**: si un solo vídeo del programa no publica temporada,
toda la serie cae a `Season 1` (de ahí `S01E857`, `S01E1205`…). Polònia publica temporada en
821 de sus 879 vídeos, Crackòvia en 237 de 247: esos pocos huérfanos arrastran al resto.

**Corrección propuesta.**

1. Añadir `ThreeCatVideo.Seasons` (`temporades`, con `SingleOrArrayConverter<ThreeCatLabel>`)
   y un `SeasonNumber` calculado: número del `id` (`PUTEMP_22` → 22) y, si no lo lleva, del
   `desc` (`22a Temporada` → 22).
2. Resolver temporada **por vídeo**, no para todo el programa:
   1. `temporades` del vídeo;
   2. si no, `T(\d+)` del título (`titol` / `nom_friendly`);
   3. si no, y el programa **sí** publica temporadas en otros vídeos → `Season 0` (especiales
      de Jellyfin: encaja con «Polònia, collita 2019», «Especial confinament», «Lo Cap d'Any»);
   4. si no, y el programa **no** publica ninguna temporada → `Season 1`.
3. Resolver episodio: `capitol_temporada` → `C(\d+)` del título → siguiente número libre de esa
   temporada en orden de emisión. Hay vídeos con temporada publicada y capítulo `-1` (Polònia
   T1 tiene 25 vídeos para 21 números), así que «siguiente libre» no es un caso raro. Duplicados
   reales de `(temporada, capítulo)` medidos: Conan 3, Polònia 1, Crackòvia 24 (T9 tiene 36
   vídeos para 35 números). El más antiguo por `data_emissio` conserva su número y el resto toma
   el primero libre de la temporada, para que Jellyfin no vea dos ficheros del mismo episodio
   (hoy acaban como `… [2]` en el nombre pero con el mismo `season`/`episode` en el `.nfo`).
4. Quitar el prefijo `T22xC6 - ` del título del episodio (`StripEpisodePrefix`), porque la
   numeración ya está en `SxxEyy`: es la segunda mitad de la queja.
5. `capitol` (global) solo se usa como último recurso del episodio y se registra en el log.

Resultado esperado: `El detectiu Conan - S22E06 - Visita als inferns de l'amor (Episodi d'Oita)`,
`Polònia - S22E06 - La lluita d'egos de Rufián i Colau, a "Polònia"`,
`Polònia - S00E07 - Especial confinament`.

**Simulación con los catálogos reales del 27/10/2026** (temporada + capítulo resueltos, sin
desempatar todavía):

| Programa | Vídeos | Sin temporada | Reparto resultante |
| --- | --- | --- | --- |
| El detectiu Conan | 385 | 0 | S1 43 · S2 45 · S3 47 · S20 2 (E40-E41) · S21 43 · S22 42 · S23 40 · S24 14 · S27 27 (E9-E35) · S28 43 · S29 37 · S30 2 |
| Polònia | 879 | 58 | S0 58 · S1 25 · S2-S21 37-44 por temporada · S22 6 |
| Crackòvia | 247 | 7 | S0 7 · S2 1 · S3-S9 17-40 · S10 1 |

Los huecos (S27 empieza en E9) son los capítulos que 3Cat no tiene publicados: es lo correcto,
Jellyfin los muestra como huecos de la temporada y no inventa nada.

**Implementado en `Sync/EpisodeNumbering.cs`** (con tests en `EpisodeNumberingTests.cs`):

1. `Parse` lee `T(\d+)xC(\d+)` al inicio del título (`T22C6` y espacios sueltos también valen).
2. `StripNumber` quita ese prefijo, así que el título del episodio y el nombre del fichero quedan
   limpios: `El detectiu Conan - S22E06 - Visita als inferns`.
3. `Assign` numera el programa en orden de emisión:
   - los vídeos con `TxCy` en el título toman esa temporada y ese capítulo;
   - los que no llevan numeración van a la **temporada 0** con número correlativo si el programa
     numera alguno (especiales, «Gran Reserva», revetlles, resums), o a la **temporada 1** si no
     numera ninguno (los diarios tipo «Tarda oberta»);
   - un vídeo repetido literalmente se descarta (`IsDuplicate`), y si dos emisiones distintas
     comparten numeración la segunda pasa a la temporada 0 en vez de sobrescribir la primera.
4. `WithAirDate` pone la fecha de emisión delante del título de los especiales
   (`2026-10-27 - Especial confinament`), para que la temporada 0 se ordene como se emitió.

**Simulación con los catálogos reales del 29/10/2026** (título + `data_emissio`, ya con la
numeración implementada):

| Programa | Vídeos | Reparto resultante |
| --- | --- | --- |
| El detectiu Conan | 385 | S1 43 · S2 45 · S3 47 · S20 2 · S21 43 · S22 42 · S23 40 · S24 14 · S27 27 · S28 · S29 · S30 |
| Polònia | 879 | S0 64 · S1 20 · S2-S22 37-44 por temporada · S22 6 |
| Crackòvia | 247 | S0 30 · S2 1 · S3 16 · S4-S7 29-34 · S8 32 · S9 35 · S10 1 |

Los huecos (S27 empieza en E9) son los capítulos que 3Cat no tiene publicados: es lo correcto,
Jellyfin los muestra como huecos de la temporada y no inventa nada.

**Migración (implementada).** Los nombres de fichero cambian, así que `PluginConfiguration` gana
`NamingVersion` (constante `CurrentNamingVersion`): mientras valga menos que la del build, el
sincronizador borra la carpeta de la serie —solo con `StrmLibrary.TryDeleteSeriesFolder`, que exige
que sea una carpeta del plugin para ese programa— y la reescribe con la numeración nueva. La marca
se guarda en `SaveRunState` al terminar la ejecución, de modo que una ejecución interrumpida
reintenta las carpetas que no llegó a tocar. Jellyfin vuelve a importar esos episodios y se pierde
el «visto» de la serie afectada: es el precio de renombrarlos.

**Ficheros:** `Sync/EpisodeNumbering.cs` (nuevo), `Sync/ThreeCatSyncService.Programs.cs`,
`Sync/ThreeCatSyncService.Episodes.cs`, `Sync/ThreeCatSyncService.cs`,
`Configuration/PluginConfiguration.cs`, `ThreeCat/ThreeCatVideo.cs` (documentación de los campos que
no se usan), `Jellyfin.Plugin.ThreeCat.Tests/EpisodeNumberingTests.cs` (nuevo).

### 2. Desplegable «Biblioteca» ilegible (bug de CSS)

**Síntoma.** Al abrir el desplegable de la columna «Biblioteca» las opciones salen con fondo
blanco y el texto claro: no se leen. Afecta a ese selector y a los filtros «Tipo»,
«Categoría» y «Canal» del catálogo, que usan la misma clase.

**Causa.** `#ThreeCatConfigPage .threeCatSelect` define `background: transparent` y
`color: inherit`. La lista nativa del `<select>` se pinta con el fondo blanco por defecto y
hereda el texto claro del tema oscuro de Jellyfin → blanco sobre blanco.

**Corrección aplicada.** Color explícito en el control y en sus opciones (no depender del tema):

```css
#ThreeCatConfigPage .threeCatSelect {
    /* … */
    background-color: #14171c;
    color: #e8e8e8;
}

#ThreeCatConfigPage .threeCatSelect option {
    background-color: #14171c;
    color: #e8e8e8;
}
```

Se mantiene el `border` actual. Un gris casi negro se lee igual en el tema oscuro de Jellyfin (donde
el control ya era oscuro) y en el claro (donde queda como un control oscuro, con contraste de sobra).

**Fichero:** `Configuration/configPage.html` (regla `.threeCatSelect`).

### 3. La serie siempre en la biblioteca 3Cat, y opcionalmente también en otra (implementado)

**Petición.** Los programas deben escribirse **siempre** en la carpeta de destino del plugin
(biblioteca «3Cat»); la columna «Biblioteca» debe servir para **tenerlos además** en otra
biblioteca, no para moverlos.

**Cómo está hecho: espejo de los ficheros ligeros.** El plugin no copia vídeo: escribe
`.strm` (una línea con la URL), `.nfo`, `.srt` y las carátulas, unos pocos KB por episodio. Se
llama dos veces a `StrmLibrary.Write` —una por raíz— y se copian las imágenes:

1. `root` primario = `configuration.DestinationPath`, siempre (`SyncProgramAsync`).
2. `ThreeCatProgramReference.DestinationPath` pasa a significar «biblioteca adicional» (se conserva
   el nombre del campo para no romper configuraciones existentes; los valores ya guardados empiezan
   a comportarse como espejo, que es justo lo que se pedía). La columna de la página pasa a
   llamarse «También en» y la opción vacía, «Sin copia».
3. `ResolveMirrorRoot` valida la carpeta como antes (`LibraryPath.Check`): si no existe, no admite
   escritura o es la propia carpeta de destino, el programa se escribe solo en el plugin con un
   aviso en el registro.
4. `StrmLibrary.Write` es idempotente (compara el contenido antes de escribir), así que la segunda
   pasada solo escribe lo que falte.
5. `SaveArtworkAsync` escribe las imágenes en el primario y `CopyArtwork` las copia al espejo (no se
   vuelven a descargar).
6. `WrittenPath` sigue siendo la carpeta primaria y `WrittenMirror` (nuevo) la del espejo: con ella
   `RemoveMovedMirror` borra la copia cuando el usuario vacía la columna o la serie se renombra,
   con la misma comprobación por `uniqueid type="threecat"` que impide borrar carpetas ajenas.
7. La migración de numeración (`NamingVersion`) y la limpieza de episodios caducados se aplican a
   las dos carpetas.

**Ventajas:** sin symlinks ni junctions (frágiles en Docker/NAS y a veces ignorados por
Jellyfin), sin duplicar vídeo, y la serie aparece en las dos bibliotecas. **Coste:** Jellyfin
escanea la serie dos veces y el estado «visto» es independiente en cada biblioteca; está dicho en
el README y en la descripción de la columna.

**Alternativas descartadas:** junction/symlink por serie (Jellyfin puede no seguirlos según el
sistema de archivos y el contenedor); una única biblioteca con varias carpetas (no resuelve
«también en Anime»); un `string[] MirrorPaths` por programa (más campos en la configuración y en el
formulario para un caso que un espejo por programa ya cubre).

**Ficheros:** `Configuration/PluginConfiguration.cs` (`WrittenMirror`),
`Sync/ThreeCatSyncService.Programs.cs` (`ResolveMirrorRoot`, `RemoveMovedMirror`,
`RemoveMovedFolder`, `DeleteForMigration`), `Sync/ThreeCatSyncService.Artwork.cs` (`CopyArtwork`),
`Sync/ThreeCatSyncService.cs` (`SaveRunState`), `Configuration/configPage.html` (columna, texto de
ayuda, `threeCatLibraryOptions`), `README.md`.

### 4. Películas del catálogo (`PUFF_FILM`) — se implementa en la 1.6.0.0

**Decisión (v1.5.0.0):** se implementa la opción A en la próxima versión. La 1.5.0.0 sale sin cine:
el catálogo de series, el espejo de bibliotecas y la numeración quedan cerrados y probados, y las
películas entran como funcionalidad nueva con su propia versión.

El buscador devuelve 280 películas con `format_contingut=PUFF_FILM`, pero solo 10 tienen
programa contenedor: las demás son vídeos sueltos (`programes_tv` nulo) que el plugin nunca ve
porque solo recorre `tipologia=WCR_PROGRAMA,PTVC_PROGRAMA`. El endpoint `pvideo/media.jsp`
sirve esos ids igual que los de un episodio.

Opciones planteadas:

- **A. Implementar importación de películas (elegida):** opción nueva («importar películes»), segunda
  pasada sobre `PUFF_FILM` (sin filtro de tipología, 1 página, 280 ítems), filtro de duración
  ≥ 60 min para descartar clips, y un `MovieStrmLibrary` que escriba
  `<Título (Año)>/<Título>.strm` + `movie.nfo` en una biblioteca de tipo «Películas».
- **B. Solo documentar** que el cine de 3Cat vive dentro de programas contenedores
  («Sala 33», «Ermessenda») y se ve como episodios de esa serie.

**Pendiente de la 1.6.0.0:** carpeta de destino propia para las películas (o la misma del plugin y
que el usuario separe por tipo de biblioteca), espejo opcional de las películas con el mismo
`CopyArtwork`, y decidir si el filtro de duración descarta o solo avisa.

### 5. Identificadores externos (dataset de cruce con Wikidata) — fases 0.5 y A completadas

El plugin solo escribe el id de IMDb cuando la ficha del programa lo publica, que es casi nunca. Sin
`uniqueid` de TMDB o TVDB, Jellyfin no puede usar sus proveedores nativos: la carátula, el reparto y la
sinopsis se quedan en las cuatro cosas que publica 3Cat.

La fase 0.5 deja montado el puente sin tocar el plugin, y el dataset vive ya en su propio repositorio,
[**open-tv-ids**](https://github.com/Lowitle/open-tv-ids), porque no es una parte del plugin:

- **El constructor (`IdsBuilder`)** está allí, en `src/IdsBuilder`. Lee el volcado de `tools/CatalogDump`
  (no llama a 3Cat) y busca cada título en las Wikipedias catalana, española e inglesa con una sola petición
  por búsqueda (`generator=search` + `prop=pageprops`), que devuelve el artículo y su elemento de Wikidata.
  Después lee los identificadores en lotes de 50 con una consulta SPARQL (`P345`, `P4983`, `P4947`, `P4835`,
  `P580` y `P31/P279*` de Q5398426) y puntúa el emparejamiento.
- **El dataset**: `data/3cat/series.jsonl` (una línea por programa publicado, con
  `schema/series.schema.json`), `report/3cat/cobertura.md`, `report/3cat/revision.csv` y
  `overrides/3cat.json`, con su propio `git init`, CI (`build.yaml`) y refresco semanal (`refresh.yaml`).
- **89 tests** en `tests/IdsBuilder.Tests`, que corren en el CI de ese repositorio.

**Fase A en el plugin (1.6.0.0).** El plugin ya consume el dataset sin cambiar la numeración:
`Sync/OpenTvIdsClient.cs` lo descarga (`series.jsonl`, con la URL en la configuración), lo cachea en
`<datos del plugin>/3cat/open-tv-ids-3cat.jsonl` y lo indexa por `emitterId`; una línea corrupta se
ignora y una descarga fallida conserva la copia anterior, así que el dataset nunca detiene una
sincronización. La tarea solo lo descarga cuando algún programa usa una fuente externa. La página
muestra «dataset: N identidades, actualizado el AAAA-MM-DD» con la cobertura de los programas
configurados (TMDB/TVDB/IMDb) y un botón de actualización manual. `PluginConfiguration` sube a la
versión 3 (`MetadataSource` global, `ThreeCat` por defecto, `OpenTvIdsUrl` y, por programa, `Override`,
`TmdbId` y `TvdbId`); `LibrarySeries` gana `TmdbId`, `TvdbId` y `NumberingSource`, y `NfoBuilder`
escribe la matriz de `uniqueid`: `threecat` siempre y como `default`, `tmdb`/`tvdb` solo cuando la
fuente de numeración es ese proveedor y hay id, e `imdb` al final. Con la fuente por defecto el
`tvshow.nfo` es byte a byte el de 1.5.0.0 (lo fija un test), así que no se renombra ni se reescribe
ninguna serie. La numeración de episodios sigue siendo `EpisodeNumbering`; el emparejamiento por
fecha/título, el cliente de TMDB/TVDB y el selector de fuente por programa son la fase B.

Dos decisiones de diseño que costaron una iteración cada una:

1. **Evaluar tres candidatos y puntuarlos**, en vez de quedarse con la primera coincidencia. La búsqueda de
   «Polònia» devuelve primero el país (Q36) y la de «Merlí», la página de desambiguación; el elemento bueno
   («Polònia (programa)» → Q2688944, «Merlí (sèrie de televisió)» → Q21036200) es el segundo o el tercero.
2. **El año del catálogo no penaliza.** `primera_emissio` es la fecha de alta en 3Cat, no la de estreno
   («Bola de Drac Súper» figura en 2025 y la serie es de 2015), así que solo suma cuando coincide.

Pendiente de la fase 1, antes de que el plugin lo consuma:

1. Segunda fuente para marcar `verified`: TMDB `/find` a partir del id de IMDb y TVmaze
   `/lookup/shows?imdb=`, que solo se piden para los registros ya publicados.
2. Repasar `report/3cat/revision.csv` (sobre todo los programas propios de 3Cat, que no tienen artículo en
   ninguna Wikipedia) y volcar las correcciones a `overrides/3cat.json`.
3. ~~Que el plugin lea el dataset.~~ Hecho en la 1.6.0.0 (fase A): el dataset se descarga y se cachea,
   `LibrarySeries` tiene `TmdbId`/`TvdbId` y el `.nfo` escribe `tmdb`/`tvdb` cuando la fuente de
   numeración es ese proveedor. ~~Falta el emparejamiento con la numeración del proveedor.~~ Hecho en la
   1.7.0.0 (fase B) para TMDB: `ProviderNumbering` empareja por fecha y título y la serie se numera como el
   proveedor, con caída a 3Cat por debajo del 50 % de cobertura. Quedan TVDB y el modo absoluto (fase C).
4. Segundo emisor del dataset (**rtve**): confirmar su endpoint público de programas y escribir su volcado
   con el contrato de `input/README.md`.

## v1.7.0.0 (fase B: numeración del proveedor)

### 1. Numeración con TMDB (implementado)

**Decisión.** Cuando la fuente de un programa es TMDB, la temporada y el episodio que se escriben en el
nombre del fichero, en el `.nfo` y en la carpeta `Season NN` son los de TMDB, no los de 3Cat. El
emparejamiento es interno del plugin y su salida es un `SxxEyy` ajeno: Jellyfin identifica un episodio por
`(temporada, episodio)` y después pide al proveedor el episodio con ese índice, así que el nombre del
fichero es el contrato.

**Síntoma.** «El detectiu Conan» publica en 3Cat las temporadas 1-3, 20-24 y 27-30 con la numeración de su
título (`T22xC6`) y TMDB las numera de forma continua: con la numeración de 3Cat, Jellyfin busca el
episodio 6 de la temporada 22 del proveedor y encuentra el de otra temporada, o ninguno.

**Causa.** La numeración de 3Cat (`EpisodeNumbering`, la `T` y la `C` del título) y la del proveedor son
dos ejes distintos y no existe ninguna correspondencia publicada entre ellos. La única evidencia común es
la fecha de emisión y, en menor medida, el título del episodio.

**Corrección.** `Sync/ProviderNumbering.cs` (función pura, sin red) cruza los vídeos con los episodios del
proveedor en este orden: fecha exacta → fecha ±1 día (zona horaria y cambios de parrilla) → título
normalizado exacto (sin numeración, sin el nombre de la serie, sin acentos ni puntuación) → contención del
título, solo con títulos de 8 caracteres o más y solo cuando un único episodio lo reclama → orden dentro de
cada hueco entre dos parejas ya emparejadas, y solo cuando el hueco tiene el mismo número de vídeos que de
episodios y sus fechas no se contradicen. Un episodio del proveedor no se asigna dos veces (gana el vídeo
más antiguo), los vídeos repetidos de 3Cat se siguen descartando (`IsDuplicate`), los huecos del proveedor
se respetan (`S27E09` se queda en 9) y la temporada 0 del proveedor se ignora. Si la cobertura queda por
debajo del **50 %**, el resultado pide la numeración de 3Cat (`UseThreeCat`) y la sincronización usa
`EpisodeNumbering`: mejor una serie consistente que una serie con huecos falsos.

`Providers/TmdbClient.cs` lee `tv/{id}` y una petición por temporada (`tv/{id}/season/{n}`), cachea seis
horas por serie, distingue la clave v3 (`api_key`) del token v4 (`Bearer`) por su forma, reintenta con
`HttpRetryPolicy` y no escribe nunca la clave en el registro. Un fallo devuelve una lista vacía, nunca una
excepción.

**Informe.** Cada programa deja una línea en el registro con la fuente, el id, los episodios emparejados
por fecha, por título y por orden, los que caen a la temporada 0, los duplicados y la cobertura; y, cuando
se queda en 3Cat, el motivo (sin id, sin clave, TMDB no publica la serie, cobertura insuficiente). Sin eso,
un cambio de numeración es indistinguible de un error.

**Decisión de seguridad.** El id de TMDB nunca se busca: sale del dataset o de un valor escrito a mano, y
el id manual gana. Nunca se escribe un `uniqueid` de proveedor sin numeración del proveedor: si la serie se
queda en 3Cat, el `tvshow.nfo` es el de 1.5.0.0 (el test de la fase A lo fija byte a byte).

**Reescritura al cambiar de fuente.** Cada programa guarda con qué fuente se escribió (`WrittenSource`); si
la fuente resuelta no coincide, la carpeta se borra antes de reescribirla con el borrado seguro que ya
existía (`StrmLibrary.TryDeleteSeriesFolder`, solo carpetas del plugin para ese programa). La migración fina
—aviso de reimportación en la página, limpieza de restos— es la fase D.

**Validación antes de renumerar.** `GET ThreeCat/Mapping?programId=…` devuelve la tabla
`vídeo de 3Cat ↔ SxxEyy ↔ evidencia ↔ episodio de TMDB` sin escribir ningún fichero, y la página la pinta
con el botón **Ver el emparejamiento con TMDB**. Es la comprobación que pide el diseño sobre «El detectiu
Conan» antes de tocar una biblioteca: sin una clave de TMDB real no se puede ejecutar aquí, pero la tabla
es exactamente lo que hará la tarea.

**Pruebas.** `ProviderNumberingTests` (16 casos: fecha exacta y ±1 día, acentos y numeración delante,
contención, huecos del proveedor, temporada 0 del proveedor, primero gana, duplicados, hueco forzado por
orden, hueco rechazado por fechas, umbral y caída a 3Cat, normalización) y `TmdbClientTests` (temporadas,
orden, temporada 0 ignorada, sin clave, sin id, 404, clave v3 y token v4, fecha vacía). `dotnet test`:
343 en verde.

**Ficheros.** `Providers/TmdbClient.cs`, `Providers/TmdbModels.cs`, `Sync/ProviderNumbering.cs`,
`Sync/ThreeCatSyncService.Providers.cs`, `Sync/ThreeCatSyncService.cs`,
`Sync/ThreeCatSyncService.Programs.cs`, `Configuration/PluginConfiguration.cs`,
`PluginServiceRegistrator.cs`, `Api/ThreeCatPlayController.cs`, `Configuration/configPage.html`,
`Tests/ProviderNumberingTests.cs`, `Tests/TmdbClientTests.cs`, `Tests/PluginConfigurationTests.cs`,
`Directory.Build.props`, `build.yaml`, `README.md`.

**Resuelto en la 1.11.0.0.** El cliente de TVDB, el `absoluteNumber` y la numeración absoluta como fuente
explícita del programa (`MetadataSource.TvdbAbsolute`) están implementados: ver la sección de 1.11.0.0.

## Comprobaciones antes de publicar

- `dotnet test` en verde y `dotnet build -c Release`.
- Sincronizar de verdad «El detectiu Conan», «Polònia» y «Crackòvia» y revisar los nombres.
- Subir `Version` en `Directory.Build.props` y `build.yaml` (y el `changelog` de `build.yaml`).
- Publicar: `git push` de `main` y del tag `vX.Y.Z.W`. El workflow `Release` compila, empaqueta,
  crea la release con `dist/threecat_<versión>.zip`, reescribe `manifest.json` con el checksum del
  zip que ha construido y lo commitea como `manifest: 3Cat <versión>`.
- Después, rehacer el manifiesto local con `scripts/Update-Manifest.ps1 -Version <versión>
  -Changelog '<texto descriptivo>'` y commitear `manifest: changelog descriptivo de 3Cat <versión>`:
  es el texto que ve el usuario en el catálogo de plugins.
