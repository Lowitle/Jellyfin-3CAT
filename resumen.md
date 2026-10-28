# Jellyfin-3CAT — Resumen del proyecto

## Objetivo
Plugin de Jellyfin que integra el catálogo a la carta de 3Cat (TV3, SX3, 33, Esport3) para que los programas aparezcan y se reproduzcan dentro de Jellyfin como una biblioteca normal (Inicio, búsqueda, progreso, colecciones, transcodificación).

## Origen de los datos (verificado el 24/10/2026)
Sin DRM, sin tokens y sin login. Dos endpoints JSON públicos:

1. Catálogo (qué hay publicado):
```
GET https://api.3cat.cat/videos?_format=json&origen=auto&perfil=pc&programatv_id=<PROG>&items_pagina=100&pagina=N&sdom=img&version=2.0&cache=180&https=true&master=yes&perfils_extra=imatges_minim_master
```
Cabeceras: User-Agent de navegador y Referer `https://www.3cat.cat/`.
Respuesta paginada: `resposta.paginacio.total_pagines` y `resposta.items.item[]`.
Campos clave por item: `id` (idint), `capitol`, `titol`, `nom_friendly`, `durada` (NO fiable), `data_publicacio`, `data_caducitat`, `programes_tv[].id` (programatv_id), `bbands[0].desc` (nombre del programa), `geolocalitzacions` (ESPANYA).

2. Detalle (URLs reales):
```
GET https://api-media.3cat.cat/pvideo/media.jsp?media=video&idint=<ID>&profile=pc_3cat
```
Devuelve: `media.url[0].file` (MP4 progresivo), `subtitols[iso="ca"].url` (WebVTT), `informacio.durada.milisegons` (duración fiable), `informacio.programa_id`, `informacio.capitol`.

Verificado en vivo: el MP4 responde 200/206 con `Accept-Ranges: bytes`, `Access-Control-Allow-Origin: *` y `Cache-Control` 1 año, sin exigir Referer ni User-Agent especial. Si `media.url` viene vacío es geobloqueo o despublicación (no reintentar en bucle).

## Qué hace el plugin
Plugin de "biblioteca virtual": resuelve el catálogo y materializa en una carpeta, por cada emisión:
- `<Nombre> - S01E<capitol> - <titol>.strm` (una línea: la URL del MP4)
- `<mismo>.ca.srt` (subtítulos, conversión VTT → SRT)
- `<mismo>.nfo` (título, temporada, episodio, fecha, descripción)
- poster/thumb descargados del catálogo (`sdom=img`)

Después dispara un escaneo de biblioteca (`ValidateMediaLibrary`). Tarea programada (`IScheduledTask`) + servicio (`IServerEntryPoint`) para refrescar periódicamente.

## Decisiones de diseño
- **Numeración:** usar `capitol` de 3Cat como número de episodio (`S01E<capitol>`), porque es numeración propia (no casa con TVDB). Metadatos online desactivados para esa biblioteca; títulos desde `.nfo`.
- **Subtítulos:** convertir el VTT (sucio) a SRT con las 6 reglas: descartar cabeceras/Region/NOTE, punto a coma, solo el primer token tras `-->`, quitar marcado y entidades, no escribir líneas vacías, renumerar. Salida idéntica a la del script de producción.
- **Idioma del audio:** el MP4 trae la pista etiquetada `eng` aunque es catalán; con `.strm` no se puede reetiquetar (se documenta).
- **Clasificación por formato:** 3Cat no publica el formato del programa en el catálogo ni en el detalle, solo en las facetas del buscador (`api.3cat.cat/cercador/tot?facetes=format_contingut,tematica&filtres=...`). Esas facetas se barren una vez (24 peticiones, caché 30 min), se invierten en `programatv_id → formatos/temas` y el formato se escribe como **primer género** del `tvshow.nfo`, delante de los temas. Es una capa opcional sobre el catálogo: si el barrido falla, la serie queda sin clasificar en vez de romper la sincronización.
- **Anime:** 3Cat archiva el anime bajo el mismo formato que el resto de la animación y no publica ninguna etiqueta propia, así que se deduce de la ficha del programa (`/api/programa/{id}/detall`, que sí publica el país de origen y las `agrupacions` con las que se montan las franjas de anime) y no de bases de datos externas ni de una clave de TVDB. Se leen solo los programas de animación (175 de 2201), seis a la vez y con caché de seis horas. El resultado se escribe como «Anime» + «Animació» + temas, para que el filtro de animación existente siga funcionando, y de la misma ficha sale el `originaltitle` del `tvshow.nfo`.
- **Infantil:** cada vídeo del catálogo publica `targets.target` con el público («PUT_INF» Infantil, «PUT_JUV» Juvenil, «PUT_TOTS» Tots els Públics), que es lo que 3Cat usa para montar las franjas del SX3; comprobado en vivo que coincide con los canales `PUC_S3*` (40 programas muestreados, 13 infantiles, 0 discrepancias salvo programas ya caducados sin vídeos). Se lee del mismo catálogo que ya se descarga, sin peticiones extra, y se escribe «Infantil» como segundo o tercer género (después del tipo y de «Animació» si es anime). El canal de la familia SX3 es el respaldo cuando un vídeo no publica público, y también es lo que marca la columna «Categoría» y el filtro «Infantil» de la tabla del catálogo (el buscador no publica el público).
- **Una biblioteca por programa:** `ThreeCatProgramReference.DestinationPath` es la raíz de la serie para un programa concreto; vacío significa la carpeta de destino del plugin. La página rellena el selector con las rutas de las bibliotecas de Jellyfin (`GET ThreeCat/Libraries` → `ILibraryManager.GetVirtualFolders()`), así que el usuario elige «Anime» o «Infantil» sin escribir rutas. No se duplica la serie: se escribe solo en la carpeta elegida. La carpeta de la ejecución anterior se guarda en `WrittenPath` y se borra cuando el programa cambia de destino, pero solo si contiene exclusivamente archivos del plugin (`.strm/.nfo/.srt/.jpg/.jpeg/.png/.webp`) y su `tvshow.nfo` lleva el `uniqueid type="threecat"` de ese programa; una ruta inválida no rompe la tarea: el programa cae en la carpeta de destino con un warning.
- **Config UI:** lista de `programatv_id`, carpeta destino, intervalo de refresco, idioma de subtítulos, botón "actualizar ahora".

## Entorno de destino
- Jellyfin Server "Lowyfin" **10.11.6** (Debian, Linux, x64) en un LXC de Proxmox, IP `192.168.87.170:8096`.
- Datos en `/var/lib/jellyfin`, plugins en `/var/lib/jellyfin/plugins`.
- Ya existe un árbol `.strm` montado: `/mnt/media/strm/media/{anime,tv,movies,programes tv,documentals}`. Bibliotecas existentes: Anime, Series, Infantil (tvshows), Pel·lícules, etc.
- **Target del plugin:** `net9.0`; paquetes `Jellyfin.Controller` y `Jellyfin.Model` **10.11.6**; `targetAbi` **10.11.0.0**.

## Estructura prevista del código
- Namespace `Jellyfin.Plugin.ThreeCat`: `Plugin.cs` (`BasePlugin<PluginConfiguration>`), `PluginConfiguration`, `configPage.html`.
- `ThreeCatClient` (HTTP con UA + Referer; catálogo paginado; `media.jsp`; detección de geobloqueo).
- `VttToSrt` (las 6 reglas).
- `ScheduledTask` + `EntryPoint` (materializar `.strm`/`.srt`/`.nfo`/poster, borrar caducados, `ValidateMediaLibrary`).
- `meta.json` (GUID + targetAbi).

## Trampas y riesgos
- **Caducidad fija:** Doraemon entero caduca el 07/06/2027 23:59:59 (lo no bajado se pierde; el `.strm` pasará a 404).
- **Geobloqueo ESPANYA:** el servidor debe salir por IP española; si no, `media.url` vacío para todo.
- Audio etiquetado `eng` aunque es catalán.
- `capitol` es numeración propia de 3Cat.
- Una emisión puede traer dos capítulos (`titol` separado por " / ").
- Tamaño ~730 MB/24 min (~4 Mbps): el `.strm` no ocupa disco pero consume ancho de banda en cada reproducción; transcodifica si el cliente no hace direct play.
- Leer el JSON siempre como UTF-8 (PowerShell 5.1 rompe acentos por tubería).

## Privacidad / arquitectura de red
Los usuarios son cuentas de Jellyfin; 3Cat no ve usuarios (API anónima). Jellyfin hace de proxy: el cliente solo habla con Jellyfin, no hay redirección a `3cat.cat`. El geobloqueo depende de la IP del servidor, no del usuario final.

## Publicación en GitHub (previsto)
Nombre `Jellyfin.Plugin.ThreeCat`. Licencia **GPLv3** (obligada por enlazar con Jellyfin). README con instalación y configuración, CI (dotnet 9.0) que publica el `.zip`, `manifest.json` (`targetAbi 10.11.0.0`). Aviso de no afiliado con CCMA/3Cat. Sin secretos (no usa credenciales).

## Referencias
- Guía de descarga: `C:\localapps\Descargar_de_3cat.cat.md`
- Script portable: `C:\localapps\3cat-dl.ps1`
- Implementación de producción: `Z:\projectes\Doraemon\pipeline\trescat.py`