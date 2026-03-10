<#
.SYNOPSIS
  Builds the Tindarr Inno Setup installer (.exe).
.DESCRIPTION
  Requires Inno Setup (iscc), .NET SDK, and Node/npm. Run from repo root.
  Builds the UI (npm run build in ui), publishes the API (so wwwroot is current), then runs iscc.
	Run "npm install" in ui once if needed. Output: dist\Tindarr-1.3.0-setup.exe
.PARAMETER PublishDir
  Directory containing published API output. Default: dist\api (publish is run if missing).
#>
Param(
	[string]$PublishDir = "",
	[string]$OutDir = "",
	[string]$OutBaseFilename = "",
	[string]$Runtime = "",
	[switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$InstallerDir = Join-Path $RepoRoot "installer\windows"
$DistApi = Join-Path $RepoRoot "dist\api"
$DistOutDefault = Join-Path $RepoRoot "dist"

if (-not $PublishDir) { $PublishDir = $DistApi }
if (-not $OutDir) { $OutDir = $DistOutDefault }
if (-not $OutBaseFilename) { $OutBaseFilename = "Tindarr-1.3.0-setup" }

# Build UI first so API publish gets latest wwwroot (CopySpaDistToWwwroot copies ui/dist -> wwwroot).
$UiDir = Join-Path $RepoRoot "ui"
Write-Host "Building UI in $UiDir ..."
Push-Location $UiDir
try {
	# Ensure npm/vite warnings on stderr don't become terminating errors when $ErrorActionPreference = 'Stop'.
	cmd /c "npm run build 2>&1"
	if ($LASTEXITCODE -ne 0) { throw "npm run build failed." }
} finally {
	Pop-Location
}

# Always publish so wwwroot gets the UI we just built (CopySpaDistToWwwroot runs on Build).
Write-Host "Publishing Tindarr.Api to $PublishDir ... (may take 1-2 min)"
$ApiProj = Join-Path $RepoRoot "src\Tindarr.Api\Tindarr.Api.csproj"

# Clean publish output to avoid stale files from previous publish modes (e.g., self-contained) breaking app startup.
if (Test-Path $PublishDir) {
	Remove-Item -Recurse -Force $PublishDir
}

$publishArgs = @(
	$ApiProj,
	"-c", "Release",
	"-o", $PublishDir,
	"-v", "n"
)

if ($Runtime) {
	$publishArgs += @("-r", $Runtime)
}

$publishArgs += "--self-contained"
if ($SelfContained) {
	$publishArgs += "true"
} else {
	$publishArgs += "false"
}

dotnet publish @publishArgs
if (-not $?) { throw "dotnet publish failed." }

$PublishDirFull = (Resolve-Path $PublishDir).Path
$OutDirFull = (Resolve-Path $OutDir).Path
$IssPath = Join-Path $InstallerDir "Tindarr.iss"

$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc) {
	$isccPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
	if (-not (Test-Path $isccPath)) { $isccPath = "$env:ProgramFiles\Inno Setup 6\ISCC.exe" }
	if (-not (Test-Path $isccPath)) { throw "Inno Setup (iscc) not found. Install Inno Setup 6 and ensure ISCC.exe is in PATH or Program Files." }
	& $isccPath "/DSourceDir=$PublishDirFull" "/O$OutDirFull" "/F$OutBaseFilename" $IssPath
} else {
	& iscc "/DSourceDir=$PublishDirFull" "/O$OutDirFull" "/F$OutBaseFilename" $IssPath
}

if (-not $?) { throw "iscc failed." }

$outExe = Join-Path $OutDirFull "$OutBaseFilename.exe"
Write-Host "Done: $outExe"