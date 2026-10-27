#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the plugin and creates the zip that is dropped into Jellyfin's plugin folder.

.DESCRIPTION
    Creates dist/threecat_<Version>.zip with the compiled assembly. The configuration page travels
    embedded in the .dll (it is an EmbeddedResource of the project), so the zip is a single file.
.EXAMPLE
    pwsh ./package.ps1
    powershell -ExecutionPolicy Bypass -File .\package.ps1 -Version 1.0.0.0
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    # Empty = the Version of Directory.Build.props.
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$projectDir = Join-Path $root 'Jellyfin.Plugin.ThreeCat'
$project = Join-Path $projectDir 'Jellyfin.Plugin.ThreeCat.csproj'

if (-not $Version) {
    [xml]$props = Get-Content -Path (Join-Path $root 'Directory.Build.props')
    $Version = @($props.Project.PropertyGroup)[0].Version
}

Write-Host "Compilando $project ($Configuration)..." -ForegroundColor Cyan
dotnet build $project -c $Configuration --nologo -v minimal
if ($LASTEXITCODE -ne 0) {
    throw "La compilacion ha fallado (codigo $LASTEXITCODE)."
}

$bin = Join-Path (Join-Path (Join-Path $projectDir 'bin') $Configuration) 'net9.0'
$dll = Join-Path $bin 'Jellyfin.Plugin.ThreeCat.dll'
if (-not (Test-Path $dll)) {
    throw "No se ha encontrado el ensamblado compilado en $bin."
}

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$zip = Join-Path $dist "threecat_$Version.zip"
if (Test-Path $zip) {
    Remove-Item $zip -Force
}

# The .zip is flat: Jellyfin extracts it as plugins/<name>_<version>/<files>.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $dll, 'Jellyfin.Plugin.ThreeCat.dll') | Out-Null
} finally {
    $archive.Dispose()
}

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host "Paquete creado: $zip" -ForegroundColor Green
Write-Host "SHA256: $hash"
Write-Host "Contenido:"
[System.IO.Compression.ZipFile]::OpenRead($zip).Entries |
    ForEach-Object { Write-Host "  - $($_.Name) ($($_.Length) bytes)" }
