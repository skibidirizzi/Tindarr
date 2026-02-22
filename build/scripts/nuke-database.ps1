<#
.SYNOPSIS
	Removes local SQLite databases and TMDB image cache under src/Tindarr.Api (dev reset).
.DESCRIPTION
	Deletes: tindarr.db (+ -shm/-wal), plexcache.db, embycache.db, jellyfincache.db,
	tmdbmetadata.db (+ -shm/-wal), and the tmdb-images folder.
	Ensure the API and Workers are not running to avoid locked files.
#>
Param(
	[switch]$WhatIf
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
	# build/scripts -> repo root
	return (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

$root = Get-RepoRoot
$apiDir = Join-Path $root "src\Tindarr.Api"

$dbBaseNames = @(
	"tindarr",
	"plexcache",
	"embycache",
	"jellyfincache",
	"tmdbmetadata"
)

$filesToRemove = @()
foreach ($base in $dbBaseNames) {
	foreach ($suffix in @("", "shm", "wal")) {
		$name = $base + ".db" + $(if ($suffix) { "-$suffix" } else { "" })
		$path = Join-Path $apiDir $name
		if (Test-Path $path) { $filesToRemove += $path }
	}
}

$dirsToRemove = @()
$tmdbImages = Join-Path $apiDir "tmdb-images"
if (Test-Path $tmdbImages) { $dirsToRemove += $tmdbImages }

$total = $filesToRemove.Count + $dirsToRemove.Count
if ($total -eq 0) {
	Write-Host "Nothing to remove under $apiDir."
	exit 0
}

Write-Host "Nuke database: removing $($filesToRemove.Count) file(s) and $($dirsToRemove.Count) folder(s) under $apiDir"
foreach ($f in $filesToRemove) {
	if ($WhatIf) { Write-Host "  [WhatIf] Remove file: $f" } else { Remove-Item -LiteralPath $f -Force }
}
foreach ($d in $dirsToRemove) {
	if ($WhatIf) { Write-Host "  [WhatIf] Remove folder: $d" } else { Remove-Item -LiteralPath $d -Recurse -Force }
}

if (-not $WhatIf) { Write-Host "Done." }
