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
   plugin se sustituyen por otros.
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
```

## Licencia

GNU GPL v3. Ver [LICENSE](LICENSE).

