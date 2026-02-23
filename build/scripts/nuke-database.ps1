<#
.SYNOPSIS
	Removes local SQLite databases and TMDB image cache (dev reset).
.DESCRIPTION
	Deletes: tindarr.db (+ -shm/-wal), plexcache.db, embycache.db, jellyfincache.db,
	tmdbmetadata.db (+ -shm/-wal), tindarr_test.db (+ -shm/-wal), and the tmdb-images folder.
	Targets: src/Tindarr.Api, src/Tindarr.Workers/bin/Debug, src/Tindarr.Workers/bin/Debug/net8.0,
	and tests/Tindarr.IntegrationTests/bin/Debug (tindarr_test.db only).
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

$dbBaseNames = @(
	"tindarr",
	"plexcache",
	"embycache",
	"jellyfincache",
	"tmdbmetadata"
)

# Directories to scan for main DBs (same set of base names in each)
$mainDirs = @(
	(Join-Path $root "src\Tindarr.Api"),
	(Join-Path $root "src\Tindarr.Workers\bin\Debug"),
	(Join-Path $root "src\Tindarr.Workers\bin\Debug\net8.0")
)

# Integration test DB (tindarr_test.db only)
$testDir = Join-Path $root "tests\Tindarr.IntegrationTests\bin\Debug"

$filesToRemove = @()
foreach ($dir in $mainDirs) {
	if (-not (Test-Path $dir)) { continue }
	foreach ($base in $dbBaseNames) {
		foreach ($suffix in @("", "shm", "wal")) {
			$name = $base + ".db" + $(if ($suffix) { "-$suffix" } else { "" })
			$path = Join-Path $dir $name
			if (Test-Path $path) { $filesToRemove += $path }
		}
	}
}
foreach ($suffix in @("", "shm", "wal")) {
	$name = "tindarr_test.db" + $(if ($suffix) { "-$suffix" } else { "" })
	$path = Join-Path $testDir $name
	if (Test-Path $path) { $filesToRemove += $path }
}
# Also check test net8.0 output
$testDirNet = Join-Path $root "tests\Tindarr.IntegrationTests\bin\Debug\net8.0"
if (Test-Path $testDirNet) {
	foreach ($suffix in @("", "shm", "wal")) {
		$name = "tindarr_test.db" + $(if ($suffix) { "-$suffix" } else { "" })
		$path = Join-Path $testDirNet $name
		if (Test-Path $path) { $filesToRemove += $path }
	}
}

$dirsToRemove = @()
$tmdbImages = Join-Path $root "src\Tindarr.Api\tmdb-images"
if (Test-Path $tmdbImages) { $dirsToRemove += $tmdbImages }

$total = $filesToRemove.Count + $dirsToRemove.Count
if ($total -eq 0) {
	Write-Host "Nothing to remove."
	exit 0
}

Write-Host "Nuke database: removing $($filesToRemove.Count) file(s) and $($dirsToRemove.Count) folder(s)"
foreach ($f in $filesToRemove) {
	if ($WhatIf) { Write-Host "  [WhatIf] Remove file: $f" } else { Remove-Item -LiteralPath $f -Force }
}
foreach ($d in $dirsToRemove) {
	if ($WhatIf) { Write-Host "  [WhatIf] Remove folder: $d" } else { Remove-Item -LiteralPath $d -Recurse -Force }
}

if (-not $WhatIf) { Write-Host "Done." }
