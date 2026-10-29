# Hoja de ruta

Notas de trabajo. Estado de la **v1.5.0.0**: los puntos 1 (numeración), 2 (desplegable ilegible) y
3 (espejo de bibliotecas) están implementados y probados; queda por decidir el 4 (películas), que se
plantea más abajo. `README.md` y `resumen.md` ya describen el comportamiento de 1.5.0.0.

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
