<#
.SYNOPSIS
  Builds the Tindarr Inno Setup installer (.exe).
.DESCRIPTION
  Requires Inno Setup (iscc), .NET SDK, and Node/npm. Run from repo root.
  Builds the UI (npm run build in ui), publishes the API (so wwwroot is current), then runs iscc.
	Run "npm install" in ui once if needed. Output: dist\Tindarr-1.2.2-setup.exe
.PARAMETER PublishDir
  Directory containing published API output. Default: dist\api (publish is run if missing).
#>
Param(
	[string]$PublishDir = ""
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$InstallerDir = Join-Path $RepoRoot "installer\windows"
$DistApi = Join-Path $RepoRoot "dist\api"

if (-not $PublishDir) { $PublishDir = $DistApi }

# Build UI first so API publish gets latest wwwroot (CopySpaDistToWwwroot copies ui/dist -> wwwroot).
$UiDir = Join-Path $RepoRoot "ui"
Write-Host "Building UI in $UiDir ..."
Push-Location $UiDir
try {
	npm run build
	if (-not $?) { throw "npm run build failed." }
} finally {
	Pop-Location
}

# Always publish so wwwroot gets the UI we just built (CopySpaDistToWwwroot runs on Build).
Write-Host "Publishing Tindarr.Api to $PublishDir ... (may take 1-2 min)"
$ApiProj = Join-Path $RepoRoot "src\Tindarr.Api\Tindarr.Api.csproj"
dotnet publish $ApiProj -c Release -o $PublishDir --self-contained false -v n
if (-not $?) { throw "dotnet publish failed." }

$PublishDirFull = (Resolve-Path $PublishDir).Path
$IssPath = Join-Path $InstallerDir "Tindarr.iss"

$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc) {
	$isccPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
	if (-not (Test-Path $isccPath)) { $isccPath = "$env:ProgramFiles\Inno Setup 6\ISCC.exe" }
	if (-not (Test-Path $isccPath)) { throw "Inno Setup (iscc) not found. Install Inno Setup 6 and ensure ISCC.exe is in PATH or Program Files." }
	& $isccPath $IssPath /DSourceDir="$PublishDirFull"
} else {
	& iscc $IssPath /DSourceDir="$PublishDirFull"
}

if (-not $?) { throw "iscc failed." }

$outExe = Join-Path $RepoRoot "dist\Tindarr-1.2.2-setup.exe"
Write-Host "Done: $outExe"