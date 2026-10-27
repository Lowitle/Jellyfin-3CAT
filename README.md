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

## Características

- **Programas configurables**: se indica el `programatv_id` numérico (`119987484`) o la URL de
  cualquiera de sus vídeos, y el plugin deduce el programa. La lista de programas se guarda en la
  configuración del plugin y se puede editar desde la página de configuración.
- **Catálogo paginado**: recorre todas las páginas del catálogo (`resposta.paginacio.total_pagines`)
  con `items_pagina=100`, así que no se pierde ninguna emisión.
- **Series y episodios**: agrupa los vídeos de cada programa en una serie, con temporadas y el número
  de episodio que publica 3Cat en el campo `capitol`.
- **MP4 progresivo**: cada `.strm` contiene la URL `media.url[0].file` del endpoint de detalle
  (`api-media.3cat.cat`), que responde 200/206 con `Accept-Ranges: bytes` y permite *seek* y
  transcodificación directa.
- **Metadatos**: `tvshow.nfo` y un `.nfo` por episodio con título, sinopsis, fecha de emisión,
  duración, géneros y temporada/capítulo. El id de 3Cat se escribe como `uniqueid type="threecat"`,
  así que la biblioteca se puede identificar sin depender de TVDB ni de TMDb.
- **Carátulas**: descarga `poster.jpg` del programa y `fanart.jpg` desde las imágenes del catálogo
  (`sdom=img`).
- **Subtítulos** (opcionales): descarga los WebVTT de 3Cat y los convierte a SRT junto a cada episodio
  (`*.ca.srt`, `*.es.srt`…). Se pueden marcar varios idiomas o **todos los idiomas disponibles**.
- **Vídeos caducados**: 3Cat publica `data_caducitat` por vídeo. Pasada esa fecha el MP4 responde 404,
  así que el episodio deja de escribirse y se limpian sus archivos anteriores.
- **Aviso de geobloqueo**: si el catálogo no devuelve URL reproducible (geobloqueo o despublicación) la
  ejecución lo anota en el log en vez de reintentar en bucle.

## Página de configuración

| Campo | Por defecto | Descripción |
| --- | --- | --- |
| **Programas** | vacío | Lista de programas a replicar, por `programatv_id` o por URL de un vídeo. Al pulsar **Añadir** el plugin valida la referencia y guarda el título del programa, que es el nombre de la carpeta de la serie. |
| **Carpeta de destino** | vacío | Carpeta raíz donde se escriben las carpetas de serie. **Comprobar carpeta** verifica que existe y que Jellyfin puede escribir; **Crear biblioteca «3Cat»** la da de alta como biblioteca de series. |
| **Intervalo de actualización (horas)** | `12` | Periodicidad de la tarea programada «Actualizar 3Cat». La tarea también se lanza al arrancar el servidor. |
| **Máximo de episodios por programa** | `0` | `0` escribe todos los vídeos del catálogo; un valor positivo se queda con los más recientes. |
| **Escribir archivos .nfo** | activado | Genera `tvshow.nfo` y un `.nfo` por episodio. Es la fuente de metadatos de la biblioteca. |
| **Omitir los vídeos caducados y borrar sus archivos** | activado | Salta los vídeos con `data_caducitat` pasada y limpia lo que ya se hubiera escrito. |
| **Borrar los archivos que ya no están en el catálogo** | desactivado | Elimina `.strm`/`.nfo`/`.srt` de la carpeta de la serie que la ejecución no haya escrito. Si el catálogo responde vacío, la carpeta se queda vacía: úsalo solo para limpiar a mano. |
| **Descargar subtítulos** | activado | Descarga los WebVTT y los convierte a SRT. Sin idiomas marcados no se escribe ningún `.srt`. |
| **Todos los idiomas disponibles de cada vídeo** | desactivado | Descarga todas las pistas que publique 3Cat para cada episodio e ignora las casillas de idioma. |
| **Catalán / Español / Inglés / Francés / Alemán / Italiano / Portugués / Gallego / Euskera** | `ca` | Se pueden marcar varios: se escribe un `.srt` por idioma. Si 3Cat no publica la pista pedida se usa el catalán cuando existe. |

El botón **Ejecutar ahora** guarda la configuración y lanza la tarea sin esperar al siguiente ciclo.

## Preparar la biblioteca en Jellyfin

1. Crea la carpeta de destino en el servidor y compruébala con **Comprobar carpeta**.
2. Pulsa **Crear biblioteca «3Cat»**: da de alta (o reutiliza) una biblioteca de tipo *Programas de TV*
   con lectura de metadatos locales (`Nfo`). Si ya hay una biblioteca de series en el servidor, hereda
   sus opciones (proveedores de imagen y metadatos) para no reiniciar la configuración.
3. En **Panel → Bibliotecas → 3Cat**, **desactiva los metadatos en línea**. La numeración de episodios
   de 3Cat (`capitol`) no coincide con TVDB ni con TMDb: si Jellyfin refresca contra esos proveedores,
   los títulos, las sinopsis y los números de episodio que escribe el plugin se sustituyen por otros.
4. Lanza **Actualizar 3Cat** (o espera al siguiente ciclo) y refresca la biblioteca. La biblioteca se
   deja con monitorización en tiempo real activada, así que los `.strm` nuevos aparecen solos.

> **Docker**: monta un volumen dedicado (`-v /srv/3cat:/media/3cat`) y escribe en *Carpeta de destino*
> la ruta **del contenedor** (`/media/3cat`), no la del host. La ruta debe ser escribible por el usuario
> de Jellyfin (`PUID`/`PGID` en la imagen oficial).

## Estructura generada

```
<Carpeta de destino>/
└── Doraemon/
    ├── tvshow.nfo
    ├── poster.jpg
    ├── fanart.jpg
    └── Season 1/
        ├── Doraemon - S01E1450 - Título del episodio.strm
        ├── Doraemon - S01E1450 - Título del episodio.nfo
        └── Doraemon - S01E1450 - Título del episodio.ca.srt   (uno por idioma marcado)
```

- Un `.strm` por emisión con una sola línea: la URL del MP4.
- `tvshow.nfo` se escribe una vez por serie; los `.nfo` de episodio llevan título, sinopsis, fecha,
  duración y temporada/episodio.
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

Para publicar: sube `Version` en `Directory.Build.props` y en `build.yaml`, crea el tag `v1.0.0.0` y
haz push del tag.

## Estructura del repositorio

```
Jellyfin.Plugin.ThreeCat/
├── Api/ThreeCatPlayController.cs        # endpoints REST (Programs/Resolve, Path/Check, Library/Create, Triggers, Refresh)
├── Configuration/                       # PluginConfiguration.cs y configPage.html (recurso embebido)
├── Library/                             # nombres, rutas, .nfo, .strm y alta de la biblioteca
├── Sync/                                # ThreeCatSyncService*.cs: programas, episodios, subtítulos, carátulas
├── Tasks/ThreeCatRefreshTask.cs         # tarea programada «Actualizar 3Cat»
├── ThreeCat/                            # cliente HTTP, modelos JSON, convertidores, VTT → SRT
├── Plugin.cs · PluginServiceRegistrator.cs · ThreeCatTriggerScheduler.cs
Jellyfin.Plugin.ThreeCat.Tests/          # xUnit: sincronización, .nfo, .strm, client, convertidores
```

## Licencia

GNU GPL v3. Ver [LICENSE](LICENSE).

