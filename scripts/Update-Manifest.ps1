#Requires -Version 5.1
<#
.SYNOPSIS
    Agrega la version empaquetada al manifest.json que Jellyfin usa como repositorio de plugins.

.DESCRIPTION
    Calcula el MD5 del .zip (Jellyfin lo compara con el campo "checksum"), inserta la entrada de la
    version al principio de "versions" y escribe el JSON en UTF-8 sin BOM, que es lo que espera el
    servidor al descargar el manifiesto desde una URL de GitHub.

.PARAMETER Version
    Version publicada, por ejemplo 1.0.0.0. Debe coincidir con Directory.Build.props.

.PARAMETER ZipPath
    Paquete que se va a publicar. Por defecto dist/threecat_<Version>.zip.

.PARAMETER ManifestPath
    Manifiesto a actualizar. Por defecto manifest.json en la raiz del repositorio.

.PARAMETER Owner
    Usuario u organizacion de GitHub. Se puede omitir si el manifiesto ya tiene una sourceUrl valida
    o si se ejecuta en GitHub Actions (variable GITHUB_REPOSITORY).

.PARAMETER Repository
    Nombre del repositorio. Misma regla que -Owner.

.PARAMETER TargetAbi
    Version minima de Jellyfin. Por defecto 10.11.0.0.

.PARAMETER Changelog
    Texto de la entrada. Por defecto "Version <Version>".

.PARAMETER MaxVersions
    Cuantas versiones se conservan en el manifiesto (las mas recientes primero). Por defecto 20.

.EXAMPLE
    pwsh ./scripts/Update-Manifest.ps1 -Version 1.0.0.0 -Owner miUsuario -Repository Jellyfin-3CAT

.EXAMPLE
    # En GitHub Actions el owner y el repositorio se deducen del entorno.
    pwsh ./scripts/Update-Manifest.ps1 -Version 1.0.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$ZipPath,
    [string]$ManifestPath,
    [string]$Owner,
    [string]$Repository,
    [string]$TargetAbi = '10.11.0.0',
    [string]$Changelog,
    [int]$MaxVersions = 20
)

$ErrorActionPreference = 'Stop'

$pluginGuid = 'd8b0f3a2-6c4e-4f0a-9e1b-2c3d4e5f6a7b'
$root = Split-Path -Parent $PSScriptRoot

if (-not $ManifestPath) { $ManifestPath = Join-Path $root 'manifest.json' }
if (-not $ZipPath) { $ZipPath = Join-Path (Join-Path $root 'dist') "threecat_$Version.zip" }

if (-not (Test-Path $ManifestPath)) {
    throw "No existe el manifiesto $ManifestPath."
}

if (-not (Test-Path $ZipPath)) {
    throw "No existe el paquete $ZipPath. Ejecuta antes package.ps1."
}

$data = ConvertFrom-Json -InputObject (Get-Content -Path $ManifestPath -Raw -Encoding UTF8)
if ($data -isnot [System.Array]) { $data = @($data) }

$plugin = $data | Where-Object { $_.guid -eq $pluginGuid } | Select-Object -First 1
if (-not $plugin) {
    throw "El manifiesto $ManifestPath no contiene el plugin con guid $pluginGuid."
}

# Owner/repositorio: parametros > GITHUB_REPOSITORY > sourceUrl ya publicada en el manifiesto.
if (-not $Owner -or -not $Repository) {
    $slug = $env:GITHUB_REPOSITORY
    if ($slug -and $slug.Contains('/')) {
        $parts = $slug.Split('/')
        if (-not $Owner) { $Owner = $parts[0] }
        if (-not $Repository) { $Repository = $parts[1] }
    }
}

if ((-not $Owner -or -not $Repository) -and $plugin.versions) {
    $previous = $plugin.versions | Select-Object -First 1
    if ($previous.sourceUrl -match 'github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/') {
        if (-not $Owner) { $Owner = $Matches['owner'] }
        if (-not $Repository) { $Repository = $Matches['repo'] }
    }
}

if (-not $Owner -or -not $Repository) {
    throw 'Indica -Owner y -Repository (o define GITHUB_REPOSITORY) para construir la sourceUrl.'
}

$plugin.owner = $Owner

$checksum = (Get-FileHash -Path $ZipPath -Algorithm MD5).Hash.ToUpperInvariant()
$sourceUrl = "https://github.com/$Owner/$Repository/releases/download/v$Version/threecat_$Version.zip"
if (-not $Changelog) { $Changelog = "Version $Version" }

$entry = [pscustomobject][ordered]@{
    version   = $Version
    changelog = $Changelog
    targetAbi = $TargetAbi
    sourceUrl = $sourceUrl
    checksum  = $checksum
    timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
}

$previousVersions = @($plugin.versions | Where-Object { $_.version -ne $Version })
$plugin.versions = @(@($entry) + $previousVersions | Select-Object -First $MaxVersions)

# PowerShell 5.1 no serializa bien un array pasado con -InputObject (lo envuelve en "value"/"Count"),
# asi que cada plugin se serializa por separado y se compone el array a mano. El manifiesto de
# Jellyfin es siempre un array JSON en la raiz.
$entries = @(foreach ($item in $data) { ConvertTo-Json -InputObject $item -Depth 10 })
$newLine = [Environment]::NewLine
$json = '[' + $newLine + ($entries -join (',' + $newLine)) + $newLine + ']'

$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($ManifestPath, $json + $newLine, $utf8)

Write-Host "Manifiesto actualizado: $ManifestPath" -ForegroundColor Green
Write-Host "  version:   $Version"
Write-Host "  sourceUrl: $sourceUrl"
Write-Host "  checksum:  $checksum (MD5)"
Get-Content -Path $ManifestPath -Encoding UTF8 | Select-Object -First 20
