# 3Cat para Jellyfin

Plugin no oficial para **Jellyfin 10.11** que replica el catálogo a la carta de
[3Cat](https://www.3cat.cat/) (TV3, SX3, 33, Esport3) dentro de una biblioteca de Jellyfin. El plugin
**no descarga vídeo**: escribe archivos `.strm` que apuntan al MP4 progresivo que publica 3Cat, junto
con los metadatos `.nfo`, las carátulas y los subtítulos en SRT.

> **Aviso legal**: este proyecto no está afiliado, patrocinado ni respaldado por la CCMA. Todo el
> contenido reproducido sigue alojado en los servidores de 3Cat y sujeto a sus condiciones de uso.
> Úsalo únicamente con contenido al que tengas derecho de acceso.

## Instalación rápida

Añade este repositorio en **Panel de control → Plugins → Repositorios → +** e instala **3Cat** desde el
catálogo (a partir de ahí se actualiza sola con cada versión publicada):

```
https://raw.githubusercontent.com/Lowitle/Jellyfin-3CAT/main/manifest.json
```

Instalación manual: copia el contenido de `threecat_<versión>.zip` en
`plugins/3Cat_<versión>/` y reinicia Jellyfin.

### Quién actualiza el plugin

El plugin no se actualiza a sí mismo. Es Jellyfin quien compara este `manifest.json` con la versión
instalada y, cuando el manifiesto publica una superior, la instala desde la tarea programada
**Actualizar plugins** (`PluginUpdates`: al arrancar y cada 24 horas). Por eso una copia manual con el
mismo número de versión se queda como está, mientras que una copia manual de una versión **anterior**
a la publicada desaparece en el siguiente arranque. Para que no se actualice sola:

- **Panel de control → Tareas programadas → Actualizar plugins**: desactívala (o quítale los
  disparadores). El catálogo sigue ofreciendo el botón de actualizar, pero nada se instala sin pulsarlo.
- O quita el repositorio en **Panel de control → Plugins → Repositorios**: sin manifiesto no hay
  actualizaciones que ofrecer.

## Si el plugin no aparece en el catálogo

El servidor descarta cualquier versión del manifiesto cuyo `targetAbi` sea mayor que su propia versión
(lo hace `InstallationManager.GetAvailablePackages` en Jellyfin 10.11), así que necesitas **Jellyfin 10.11
o superior**. Si ya estás en 10.11, revisa en este orden:

1. **Pestaña equivocada**: la página de plugins se abre en *Instalados*, y 3Cat, al no estar instalado,
   solo sale al pulsar *Ver todo* o al abrir `…/web/index.html#/dashboard/plugins.html?status=All`.
2. **Caché del navegador**: el catálogo cachea `GET /Packages` durante quince minutos; reiniciar Jellyfin
   no vacía esa caché. Recarga con `Ctrl+F5` o abre la página en una pestaña nueva.
3. **Repositorio guardado y habilitado**: *Panel de control → Plugins → Repositorios* debe listar la URL
   de arriba (`GET /Repositories`).
4. **Sin salida a internet o DNS filtrado**: el log del servidor muestra `Cannot locate the plugin
   manifest` o `Failed to deserialize the plugin manifest`. Con una API key de administrador,
   `GET /Packages` debe incluir `guid: d8b0f3a2-6c4e-4f0a-9e1b-2c3d4e5f6a7b`.

Si nada de eso lo resuelve, instala a mano (ver arriba): la instalación manual no depende del catálogo.

## Características

- **Programas configurables**: se indica el `programatv_id` numérico (`119987484`) o la URL de
  cualquiera de sus vídeos, y el plugin deduce el programa. La lista de programas se guarda en la
  configuración del plugin y se puede editar desde la página de configuración.
- **Catálogo de programas**: el botón **Ver todos los programas** lee el buscador público de 3Cat
  (`api.3cat.cat/cercador/tot`, 2201 programas en cinco peticiones de 500) y los muestra en una tabla
  con búsqueda por texto y filtros por tipo, categoría, canal, solo con vídeos y ya añadidos, columnas
  ordenables (nombre, id, tipo, categoría, canal, idiomas, edad y fecha de publicación), selección de las
  filas visibles y paginación de 200 en 200. Los programas web y de radio aparecen marcados pero no se
  pueden añadir: su catálogo de vídeos responde 404.
- **Catálogo paginado**: recorre todas las páginas del catálogo (`resposta.paginacio.total_pagines`)
  con `items_pagina=100`, así que no se pierde ninguna emisión.
- **Series y episodios**: agrupa los vídeos de cada programa en una serie. La temporada y el capítulo
  salen del propio título, que es la numeración que muestra la web de 3Cat: `T22xC6 - Visita als
  inferns` se escribe como `El detectiu Conan - S22E06 - Visita als inferns`. Las emisiones que 3Cat no
  numera (especiales, *Gran Reserva*, revetlles, resums de temporada) van a la **temporada 0** con la
  fecha de emisión delante del título, `S00E01 - 2026-10-27 - Especial confinament`, así que se ordenan
  como se emitieron; los programas que no numeran **nada** (los diarios tipo *Tarda oberta*) se quedan
  en la temporada 1 en orden de emisión. Los vídeos repetidos literalmente se descartan y, si dos
  emisiones distintas comparten numeración, la segunda pasa a la temporada 0 en vez de perderse.
- **Numeración de un proveedor (TMDB)**: la fuente de metadatos —global y por programa— elige entre el
  catálogo de 3Cat, que es lo de siempre, y TMDB. Con TMDB, el plugin empareja cada vídeo con el episodio
  que TMDB publica para la serie y escribe **la temporada y el episodio de TMDB** en el nombre del fichero,
  en el `.nfo` y en la carpeta `Season NN`, que es lo que hace que Jellyfin case la ficha online de cada
  episodio. La evidencia se usa en este orden: **anclas** del usuario, fecha de emisión (el mismo día, luego
  ±1 día) —solo si son densas, ver abajo—, el **`capitol` de 3Cat** (su numeración absoluta, ver abajo),
  título normalizado (igualdad y después contención) y orden dentro
  del hueco entre dos parejas ya emparejadas, solo cuando el hueco tiene el mismo número de vídeos que de
  episodios. Un episodio de TMDB nunca se asigna dos veces y los huecos de su numeración se respetan
  (`S27E09` se queda en 9). Si el emparejamiento no
  cubre **la mitad** de los vídeos, si el dataset no resuelve el id, si no hay clave de TMDB o si TMDB no
  responde, la serie entera se queda con la numeración de 3Cat y el registro dice por qué: nunca queda una
  serie a medias, con huecos falsos y episodios repetidos. La página de configuración tiene una **vista
  previa** de la tabla «vídeo de 3Cat — episodio de TMDB» que no escribe ningún fichero, y cambiar la
  fuente de un programa reescribe su carpeta (solo si es del plugin y su `tvshow.nfo` nombra ese programa)
  porque Jellyfin reimporta esos episodios y se pierde el «visto» de esa serie.
- **Cruce por la numeración absoluta (`capitol`)**: el `capitol` que publica 3Cat es un contador del
  **programa entero**, no de la temporada: «El detectiu Conan» numera sus 385 vídeos del 1 al 1.206 sin un
  solo paso atrás (T1 acaba en 43 y T2 empieza en 44, T3 en 89 y, tras los 720 capítulos que 3Cat no aloja,
  T20 empieza en 857). Como la numeración de la serie es la misma en cualquier catálogo, el vídeo es el
  episodio que ocupa esa posición en TMDB y el emparejamiento es una consulta, no una conjetura: es la única
  pasada que **respeta los huecos** del catálogo (las pasadas que caminan las dos listas numeran el 857 como
  136) y la única que sobrevive a un doblaje con títulos y fechas propias. La pasada solo entra si lo publican
  al menos la mitad de los vídeos, si numera cada vídeo una sola vez (un índice de temporada repite los números
  que ya ha dado) y si anuncia más capítulos que cualquier temporada suelta; y solo rellena lo que no ha
  reclamado una fecha o un título. El orden en que llegan los vídeos no se juzga, y eso también lo enseñó «El
  detectiu Conan»: 3Cat fecha su catálogo con la emisión del doblaje y lo fecha mal —el capítulo 53 del programa
  va fechado después del 79, y el 40 después del 41—, así que lo único que se lee como reemisión es un vídeo que
  vuelve al principio del programa. Medido sobre el catálogo real: 329 de 385 vídeos emparejados (85,5 %) con
  **cero** desviaciones, y el 100 % si el proveedor lista los 1.206 capítulos.
- **Anclas**: cuando el doblaje y el original no comparten nada —«Detectiu Conan» se dobló entre 2001 y
  2025 y TMDB publica los episodios en japonés desde 1996, así que ni los títulos ni las fechas sirven— el
  usuario puede escribir anclas con la misma sintaxis de los títulos de 3Cat: `T1=1` (la temporada 1 de 3Cat
  empieza en el episodio 1 de TMDB), `T21xC1=858`, `T22xC6=907` o `T1xC1=S01E01`, separadas por `;`. Su papel
  con el contador es calibrar el **desfase** entre los dos contadores con una sola línea (y así lo dice la
  vista previa); sin contador, el plugin coloca las anclas y numera el resto caminando la lista de TMDB hacia
  arriba y abajo, parando en la temporada siguiente o en el episodio que otra ancla ya ha reclamado. Se ven en
  la tabla de programas (columna **Anclas**) y en la propia vista previa, que además lista las temporadas de
  3Cat con su `capitol` para escribirlas sin abrir el catálogo, y dice cuántas anclas se han podido colocar.
- **MP4 progresivo**: cada `.strm` contiene la URL `media.url[0].file` del endpoint de detalle
  (`api-media.3cat.cat`), que responde 200/206 con `Accept-Ranges: bytes` y permite *seek* y
  transcodificación directa.
- **Metadatos**: `tvshow.nfo` y un `.nfo` por episodio con título, sinopsis, fecha de emisión,
  duración, géneros y temporada/capítulo. El id de 3Cat se escribe como `uniqueid type="threecat"`,
  así que la biblioteca se puede identificar sin depender de TVDB ni de TMDb. Las series de anime
  llevan además el título original que publica 3Cat (`originaltitle`), para que «Bola de Drac» se
  muestre como «Dragon Ball» sin que Jellyfin sustituya el título catalán.
- **Clasificación por formato**: el formato bajo el que 3Cat archiva cada programa («Sèrie»,
  «Documental», «Animació», «Cinema», «Concurs»…) se escribe **como primer género** de la serie, delante
  de sus temas («Ficció», «Esports»…), así que la biblioteca se filtra por tipo de programa con los
  filtros nativos de Jellyfin y sin plugins añadidos. 3Cat no publica esa clasificación en ningún
  endpoint: el plugin barre las facetas `format_contingut` y `tematica` del buscador (24 peticiones,
  cacheadas media hora) y las invierte en una tabla por `programatv_id`.
- **Anime**: 3Cat archiva «Bola de Drac» o «Doraemon» bajo el mismo formato que «Mironins» o «Louie»
  y no publica ninguna etiqueta de anime, así que el plugin lo deduce de la ficha del programa —el país
  de origen del programa («Japó») y las colecciones internas con las que 3Cat monta sus franjas de
  anime—, sin bases de datos externas ni clave de TVDB. Las series de anime se escriben con «Anime»
  como primer género y «Animació» como segundo, así que la biblioteca se filtra por anime sin perder el
  filtro de animación, y el catálogo de la página de configuración las marca con la categoría «Anime».
  La ficha se lee solo para los programas de animación (175 de los 2201 del catálogo, seis peticiones a
  la vez, unos dos segundos, en caché seis horas).
- **Infantil**: 3Cat clasifica cada vídeo por público («Infantil», «Juvenil», «Tots els Públics») y monta
  con esa marca las franjas del SX3. El plugin la lee del mismo catálogo que ya descarga —sin peticiones
  extra— y escribe «Infantil» como género justo después del tipo, así que «Anime» y «Animació» siguen
  funcionando y la biblioteca se filtra por «Infantil». El canal de la familia SX3 (`PUC_S3`,
  `PUC_S3_S3`, `PUC_S3_X3`) hace de respaldo cuando un vídeo no publica público. El catálogo de la página
  de configuración marca esos programas en la columna **Categoría** y añade «Infantil» al filtro.
- **La serie en la biblioteca del plugin y, opcionalmente, en otra**: cada programa se escribe siempre
  en la carpeta de destino del plugin. La columna **También en** de la lista de programas guarda además
  una copia en la carpeta de otra biblioteca de Jellyfin («Anime», «Infantil», «Programas de TV»…), para
  que esa biblioteca la muestre con sus propias etiquetas. La copia son los mismos ficheros ligeros
  (`.strm`, `.nfo`, subtítulos y carátulas), así que la serie aparece dos veces en el servidor y cada
  biblioteca lleva su propio «visto». Quitar la carpeta de la columna borra solo la copia, y si la serie
  cambia de nombre la carpeta antigua del espejo se elimina igual que la de la biblioteca del plugin:
  solo cuando contiene únicamente archivos suyos y su `tvshow.nfo` identifica el programa. Una carpeta que
  ya no existe o no admite escritura no rompe la tarea: ese programa se queda solo en la biblioteca del
  plugin, con un aviso en el registro. Si en la carpeta elegida ya hay una serie con el mismo nombre, el
  plugin escribe encima sus archivos de metadatos y episodios, así que conviene comprobar antes qué hay en
  esa biblioteca.
- **Carátulas**: descarga `poster.jpg` del programa y `fanart.jpg` desde las imágenes del catálogo
  (`sdom=img`).
- **Subtítulos** (opcionales): descarga los WebVTT de 3Cat y los convierte a SRT junto a cada episodio
  (`*.ca.srt`, `*.es.srt`…). Se descargan **todas las pistas que publique cada vídeo**, una por idioma.
- **Vídeos caducados**: 3Cat publica `data_caducitat` por vídeo. Pasada esa fecha el MP4 responde 404,
  así que el episodio deja de escribirse y se limpian sus archivos anteriores.
- **Aviso de geobloqueo**: si el catálogo no devuelve URL reproducible (geobloqueo o despublicación) la
  ejecución lo anota en el log en vez de reintentar en bucle.

## Página de configuración

| Campo | Por defecto | Descripción |
| --- | --- | --- |
| **Programas** | vacío | Lista de programas a replicar, por `programatv_id` o por URL de un vídeo. Al pulsar **Añadir** el plugin valida la referencia y guarda el título del programa, que es el nombre de la carpeta de la serie. **Ver todos los programas** abre la tabla del catálogo completo de 3Cat para buscarlos y añadirlos de una vez. La columna **También en** guarda una copia de la serie en la carpeta de otra biblioteca de Jellyfin además de la de destino. |
| **Carpeta de destino** | vacío | Carpeta raíz donde se escriben las carpetas de serie. **Comprobar carpeta** verifica que existe y que Jellyfin puede escribir; **Crear biblioteca «3Cat»** la da de alta como biblioteca de series. Es la biblioteca que el plugin escribe siempre, con copia o sin ella. |
| **Intervalo de actualización (horas)** | `12` | Periodicidad de la tarea programada «Actualizar 3Cat». La tarea también se lanza al arrancar el servidor. |
| **Máximo de episodios por programa** | `0` | `0` escribe todos los vídeos del catálogo; un valor positivo se queda con los más recientes. |
| **Escribir archivos .nfo** | activado | Genera `tvshow.nfo` y un `.nfo` por episodio. Es la fuente de metadatos de la biblioteca. |
| **Clasificar los programas por su formato** | activado | Escribe el formato de 3Cat («Sèrie», «Documental», «Animació»…) como primer género de la serie y lo muestra en la columna **Categoría** del catálogo, que también se puede filtrar. Desactivado, los géneros son solo los temas de los vídeos y el catálogo no lee las facetas. |
| **Separar el anime de la animación** | activado | Distingue el anime del resto de la animación por lo que publica la ficha del programa (país de origen «Japó» y colecciones de anime de 3Cat) y escribe «Anime» como primer género, con «Animació» detrás, y la categoría **Anime** en el catálogo. La ficha aporta también el título original de la serie (`originaltitle`). Cuesta una petición por programa de animación sincronizado, y las 175 del catálogo se leen en unos dos segundos al abrir la tabla (seis a la vez, en caché seis horas). Requiere la clasificación por formato. |
| **Marcar los programas infantiles como «Infantil»** | activado | Lee el público que 3Cat publica en cada vídeo («Infantil», «Juvenil», «Tots els Públics») y escribe «Infantil» como género detrás del tipo, así que la biblioteca se filtra por «Infantil» sin perder «Anime» ni «Animació». Si un vídeo no publica público, manda el canal de la familia SX3. El catálogo marca esos programas en la columna **Categoría** y en el filtro. No cuesta ninguna petición extra. Requiere la clasificación por formato. |
| **Omitir los vídeos caducados y borrar sus archivos** | activado | Salta los vídeos con `data_caducitat` pasada y limpia lo que ya se hubiera escrito. |
| **Borrar los archivos que ya no están en el catálogo** | desactivado | Elimina `.strm`/`.nfo`/`.srt` de la carpeta de la serie que la ejecución no haya escrito. Si el catálogo responde vacío, la carpeta se queda vacía: úsalo solo para limpiar a mano. |
| **Descargar subtítulos** | activado | Descarga **todas** las pistas WebVTT que publique cada vídeo y las convierte a SRT, un archivo por idioma. Casi todo el catálogo llega en catalán; los programas doblados o con audiodescripción añaden más pistas. |

El botón **Ejecutar ahora** guarda la configuración y lanza la tarea sin esperar al siguiente ciclo.

## Preparar la biblioteca en Jellyfin

1. Crea la carpeta de destino en el servidor y compruébala con **Comprobar carpeta**.
2. Pulsa **Crear biblioteca «3Cat»**: da de alta (o reutiliza) una biblioteca de tipo *Programas de TV*
   con lectura de metadatos locales (`Nfo`). Si ya hay una biblioteca de series en el servidor, hereda
   sus opciones (proveedores de imagen y metadatos) para no reiniciar la configuración.
3. En **Panel → Bibliotecas → 3Cat**, **desactiva los metadatos en línea**. La numeración que usa el
   plugin sale del título de 3Cat (`T22xC6`) y no coincide con la de TVDB ni con la de TMDb: si Jellyfin
   refresca contra esos proveedores, los títulos, las sinopsis y los números de episodio que escribe el
   plugin se sustituyen por otros. La 1.6.0.0 descarga el dataset de identificadores pero sigue numerando
   con 3Cat, así que esta recomendación no cambia todavía.
4. Lanza **Actualizar 3Cat** (o espera al siguiente ciclo) y refresca la biblioteca. La biblioteca se
   deja con monitorización en tiempo real activada, así que los `.strm` nuevos aparecen solos.

> **Una carpeta para cada biblioteca**: si quieres el anime o los programas infantiles en bibliotecas
> aparte, crea esas bibliotecas en Jellyfin con la carpeta que prefieras (`/media/anime`,
> `/media/infantil`…) y elige esa carpeta en la columna **También en** de cada programa. El plugin escribe
> la serie en la biblioteca «3Cat» y una copia allí; si después quitas la carpeta de la columna, borra la
> copia en cuanto esa carpeta solo contenga archivos suyos.

> **Docker**: monta un volumen dedicado (`-v /srv/3cat:/media/3cat`) y escribe en *Carpeta de destino*
> la ruta **del contenedor** (`/media/3cat`), no la del host. La ruta debe ser escribible por el usuario
> de Jellyfin (`PUID`/`PGID` en la imagen oficial).

## Estructura generada

```
<Carpeta de destino>/
└── El detectiu Conan/
    ├── tvshow.nfo
    ├── poster.jpg
    ├── fanart.jpg
    ├── Season 0/
    │   └── El detectiu Conan - S00E01 - 2026-10-27 - Especial confinament.strm
    └── Season 22/
        ├── El detectiu Conan - S22E06 - Visita als inferns.strm
        ├── El detectiu Conan - S22E06 - Visita als inferns.nfo
        └── El detectiu Conan - S22E06 - Visita als inferns.ca.srt   (uno por idioma publicado)
```

- Un `.strm` por emisión con una sola línea: la URL del MP4.
- La carpeta de la serie se crea siempre dentro de la carpeta de destino del plugin. Si el programa tiene
  asignada otra biblioteca en la columna **También en**, la misma estructura se escribe otra vez en la
  carpeta de esa biblioteca, carátulas incluidas.
- `tvshow.nfo` se escribe una vez por serie; los `.nfo` de episodio llevan título, sinopsis, fecha,
  duración y temporada/episodio.
- Las temporadas son las del título de 3Cat: `Season 22` para los `T22xCx` y `Season 0` para las
  emisiones que no llevan numeración, con la fecha de emisión al principio del título.
- Si dos emisiones acaban con el mismo nombre de archivo, la segunda se escribe con el sufijo ` [2]`
  para que Jellyfin no las funda en un solo episodio.
- Los subtítulos se deduplican por idioma: nunca se escribe dos veces el mismo `.srt`.

## Identificadores cruzados (dataset)

El catálogo de 3Cat identifica cada programa con un `programatv_id` que solo significa algo dentro de 3Cat,
y los proveedores de metadata de Jellyfin (TheMovieDb, TheTVDB, OMDb) trabajan con los suyos. La traducción
entre los dos —una línea JSON por programa con sus identificadores de **Wikidata**, **IMDb**, **TMDB** y
**TVDB**— vive en su propio repositorio,
[**open-tv-ids**](https://github.com/Lowitle/open-tv-ids): es un dataset, no una parte del plugin, y su
refresco no debería depender de la versión que tenga instalada un cliente.

```powershell
dotnet run --project tools/CatalogDump     # volcado del catálogo a CSV: la entrada del dataset
```

```json
{"id":"3cat:73476","emitter":"3cat","emitterId":"73476","kind":"anime","title":"Bola de Drac Súper","originalTitle":"Dragon Ball Super","ids":{"wikidata":"Q19839121","imdb":"tt4644488","tmdb":"62715","tvdb":"295068"},"match":{"method":"wikidata","confidence":0.9,"verified":false}}
```

- El plugin solo aporta el volcado: `CatalogDump` escribe `catalogo-3cat-programas.csv` con los ids y los
  títulos que usa la biblioteca, y ese fichero es lo que consume el repositorio del dataset
  (`input/3cat.csv`), donde se cruza con Wikidata.
- El esquema de cada línea y la cobertura del último volcado están en ese repositorio
  (`schema/series.schema.json`, `report/3cat/cobertura.md`).
- Solo se publica desde 0,90 de confianza; lo dudoso queda en `report/3cat/revision.csv` y se corrige a
  mano en `overrides/3cat.json`, que manda sobre el matcher.
- Los identificadores vienen de Wikidata (CC0), leída a través de los artículos de Wikipedia: el dataset no
  redistribuye ni el catálogo ni el texto de los artículos.
- Los tres ids son los `uniqueid` que necesita el `tvshow.nfo` para que Jellyfin descargue carátulas y
  sinopsis con sus proveedores nativos. Desde la 1.6.0.0 el plugin descarga y cachea el dataset en su
  carpeta de datos (en la tarea de refresco, o con el botón **Actualizar dataset ahora** de la página),
  muestra cuántas identidades hay y cuántos de los programas configurados tienen id, y el `tvshow.nfo`
  sabe escribir `tmdb` y `tvdb`. Desde la 1.7.0.0 el id deja de ser decorativo: con la fuente `TMDB` el
  plugin escribe la numeración de TMDB en los ficheros, así que el id que viaja en el `.nfo` y la
  numeración de los episodios son los mismos. Con la fuente por defecto —3Cat— no cambia nada: el
  `tvshow.nfo` sigue escribiendo solo `threecat` y ninguna serie se renumera.
- La vista previa del emparejamiento acepta el programa escrito como sea: su `programatv_id`, la URL de su
  página (`https://www.3cat.cat/3cat/detectiu-conan/`), su `slug`, su título o el id de cualquiera de sus
  vídeos, porque la URL y el título se resuelven contra el catálogo de programas que la página ya tiene en
  memoria (el buscador de 3Cat publica el `slug` de cada programa junto a su id). El segundo campo es el id
  de TMDB, o la URL de su ficha, y manda sobre el del dataset y sobre el de la tabla de programas, así que
  sirve para probar una serie sin tocar su configuración. La respuesta es siempre una tabla con su mensaje
  —también cuando no hay nada que emparejar— y, si la referencia encaja con varios programas, la página los
  ofrece para elegir uno con un clic. Desde la 1.7.1.0 el mensaje dice qué falta cuando falta algo: el id
  de TMDB, la clave de la API o la respuesta de TMDB.
- Desde la 1.8.0.0 las fechas dejan de ser evidencia cuando son las de otro estreno. 3Cat publica la fecha
  de **su** emisión: «Detectiu Conan» se emitió en TV3 de 2001 a 2025 mientras Japón lo emitía desde 1996,
  y como el doblaje iba a cinco capítulos por semana y el original a uno, cada cinco vídeos las dos fechas
  volvían a coincidir por puro azar —46 coincidencias de 385 vídeos, todas falsas—. Ahora la fecha solo
  cuenta si al menos la mitad de los vídeos con fecha caen en el día (o el día antes o después) de un
  episodio de TMDB; si no, se descarta entera para ese programa y la vista previa lo dice. El `capitol` de
  3Cat, en cambio, sí se usa: el catálogo lo publica en el 100 % de los programas (43 de 44 en una muestra
  de 60 programas de todos los formatos), es un contador continuo por bloques y conserva los huecos, así que
  la vista previa lista las temporadas de 3Cat con su rango de `capitol` para escribir las anclas.
  `capitol_temporada` no sirve como base (solo la mitad de los programas lo publican: Doraemon devuelve
  `-1` en todos sus vídeos), así que la temporada sigue saliendo del título.
- Desde la 1.8.1.0 el `capitol` deja de ser solo una pista y pasa a ser evidencia: se lee como la posición
  absoluta del vídeo en la serie, así que un programa con huecos en el catálogo se empareja sin una sola
  ancla (antes, las 12 anclas necesarias daban un 85 % de cobertura y el relleno solo alcanzaba la temporada
  del ancla porque se detenía en cada cambio de temporada). Las anclas que queden escritas se siguen
  respetando: fijan el desfase entre los dos contadores y el resto del programa lo hereda. La vista previa
  añade el contador `capitol` al resumen, la columna **Evidencia** marca «capitol» y avisa del desfase.
- Desde la 1.8.1.1 la pasada del `capitol` deja de mirar el orden en que llegan los vídeos. La 1.8.1.0 pedía
  que el contador creciera con el orden de emisión, y resulta que 3Cat fecha su catálogo con la emisión del
  doblaje y lo fecha mal: en «Detectiu Conan» el capítulo 40 va fechado después del 41 y el 53 después del 79,
  cinco saltos atrás en 385 vídeos, así que la guarda rechazaba el contador y la serie volvía al 0 % de
  cobertura. Ahora lo que se comprueba es que el contador numere cada vídeo una sola vez (un índice de
  temporada repite los números que ya ha dado) y que ninguna emisión vuelva al principio del programa, que es
  lo que es una reemisión y no una fecha unos días fuera de sitio. La vista previa, además, dice por qué no se
  ha usado el contador cuando no se ha usado: si el proveedor lista menos de la mitad de los capítulos que 3Cat
  anuncia, o si los números se repiten.
- Desde la 1.8.2.0 el contador deja de ir desfasado cuando 3Cat emite un episodio en varias partes. El
  `capitol` cuenta emisiones de 3Cat y no episodios del original —en «Detectiu Conan», 183 de sus 385 vídeos son
  una parte de algo—, así que desde el primer corte cada vídeo iba un episodio por delante y el error llegaba a
  57 capítulos al final del catálogo. Los títulos de los dos catálogos se comparan palabra a palabra, con más
  peso para la palabra rara que para la que sale en cien títulos, y cada vídeo cuyo título se parece al de un
  episodio vota cuántas posiciones está desfasado el contador: los vecinos descartan la coincidencia suelta y el
  escalón se coloca donde 3Cat partió el episodio. Con el desfase aplicado las dos listas se alinean en vez de
  consultarse, y los vídeos que son la segunda o tercera parte de un episodio que el proveedor numera una sola
  vez se escriben como `- part1` y `- part2` del mismo episodio, que es como Jellyfin los reproduce en orden. Si
  los títulos no dicen nada, el contador se usa igual que antes.

- La clave de TMDB es del usuario y se guarda en la configuración del plugin (nunca en el registro): la
  página acepta tanto una clave v3 como un token de lectura v4 y los distingue por su forma. Sin clave,
  las series se siguen numerando con 3Cat.
- Si la descarga falla, se usa la copia anterior (o ninguna) y la sincronización continúa igual: el
  dataset nunca bloquea una actualización.

## Compilar y publicar

Requisitos: **.NET SDK 9.0** (o superior, la solución usa `RollForward=LatestMajor`).

```powershell
dotnet build --configuration Release      # plugin + tests
dotnet test  --configuration Release      # pruebas del plugin y de la librería
pwsh ./package.ps1                        # dist/threecat_<versión>.zip
```

- La versión vive en `Directory.Build.props` (4 componentes: `1.0.0.0`) y se repite en `build.yaml`. El
  workflow de release comprueba que la versión publicada coincide con `Directory.Build.props` y falla
  si no coincide.
- `package.ps1` compila en `Release` y crea el `.zip` con `Jellyfin.Plugin.ThreeCat.dll` (la página de
  configuración va embebida en el ensamblado).
- `scripts/Update-Manifest.ps1 -Version <versión>` inserta la versión en `manifest.json` con su
  `checksum` MD5 y su `sourceUrl` de GitHub Releases; lo ejecuta el workflow de release, no hace falta
  a mano salvo para publicar desde un equipo local:

  ```powershell
  pwsh ./scripts/Update-Manifest.ps1 -Version 1.0.0.0 -Owner Lowitle -Repository Jellyfin-3CAT
  ```

### Workflows

| Workflow | Cuándo | Qué hace |
| --- | --- | --- |
| `.github/workflows/build.yaml` | push a `main`, pull request o manual | Compila, ejecuta las pruebas, empaqueta y sube el `.zip` como artefacto. |
| `.github/workflows/release.yaml` | tag `v*` o manual | Comprueba la versión, ejecuta las pruebas, empaqueta, actualiza `manifest.json`, crea la release de GitHub y hace commit del manifiesto. |

Para publicar: sube `Version` en `Directory.Build.props` y en `build.yaml`, crea el tag `v<versión>`
(por ejemplo `v1.2.0.0`) y haz push del tag. El manifiesto lo actualiza el propio workflow con el
`checksum` del `.zip` que sube al release.

## Estructura del repositorio

```
Jellyfin.Plugin.ThreeCat/
├── Api/ThreeCatPlayController.cs        # endpoints REST (Programs/Resolve, Path/Check, Library/Create, Triggers, Refresh)
├── Configuration/                       # PluginConfiguration.cs y configPage.html (recurso embebido)
├── Library/                             # nombres, rutas, .nfo, .strm, clasificación y alta de la biblioteca
├── Sync/                                # ThreeCatSyncService*.cs: programas, episodios, subtítulos, carátulas
├── Tasks/ThreeCatRefreshTask.cs         # tarea programada «Actualizar 3Cat»
├── ThreeCat/                            # cliente HTTP, modelos JSON, convertidores, VTT → SRT
├── Plugin.cs · PluginServiceRegistrator.cs · ThreeCatTriggerScheduler.cs
Jellyfin.Plugin.ThreeCat.Tests/          # xUnit: sincronización, .nfo, .strm, client, convertidores
tools/CatalogDump/                       # volcado del catálogo de 3Cat a CSV (entrada del dataset)
```

El dataset de identificadores cruzados y su constructor (`IdsBuilder`) están en un repositorio aparte,
[open-tv-ids](https://github.com/Lowitle/open-tv-ids): este repositorio solo produce el volcado que lo
alimenta.

## Licencia

GNU GPL v3. Ver [LICENSE](LICENSE).

