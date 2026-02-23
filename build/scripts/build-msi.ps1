<#
.SYNOPSIS
  Builds the Tindarr MSI installer (WiX). Publishes the API, harvests files with Heat, then compiles and links.
.DESCRIPTION
  Requires WiX Toolset (candle, heat, light) and .NET SDK. Run from repo root.
  Output: dist\Tindarr-1.0.2.msi (or -OutPath).
.PARAMETER PublishDir
  Directory containing published API output. Default: dist\api (publish is run if missing).
.PARAMETER OutPath
  Path for the output MSI. Default: dist\Tindarr-1.0.2.msi.
#>
Param(
	[string]$PublishDir = "",
	[string]$OutPath = ""
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$WixDir = Join-Path $RepoRoot "installer\windows\wix"
$DistApi = Join-Path $RepoRoot "dist\api"
$DefaultMsi = Join-Path $RepoRoot "dist\Tindarr-1.0.2.msi"

if (-not $PublishDir) { $PublishDir = $DistApi }
if (-not $OutPath) { $OutPath = $DefaultMsi }

# Publish API if output missing
if (-not (Test-Path (Join-Path $PublishDir "Tindarr.Api.exe"))) {
	Write-Host "Publishing Tindarr.Api to $PublishDir ..."
	$ApiProj = Join-Path $RepoRoot "src\Tindarr.Api\Tindarr.Api.csproj"
	dotnet publish $ApiProj -c Release -o $PublishDir --self-contained false
	if (-not $?) { throw "dotnet publish failed." }
}

# WiX tools: use PATH, or refresh from registry, or use well-known install path
$heat = Get-Command heat -ErrorAction SilentlyContinue
if (-not $heat) {
	# Current session may not have latest PATH; refresh from Machine + User
	$env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
	$heat = Get-Command heat -ErrorAction SilentlyContinue
}
if (-not $heat) {
	# Try well-known WiX v3 install path (e.g. after adding to PATH but before restarting terminal)
	$wixBins = @(
		"${env:ProgramFiles(x86)}\WiX Toolset v3.14\bin",
		"${env:ProgramFiles(x86)}\WiX Toolset v3.11\bin",
		"${env:ProgramFiles(x86)}\WiX Toolset v3.10\bin"
	)
	foreach ($bin in $wixBins) {
		if (Test-Path (Join-Path $bin "heat.exe")) {
			$env:Path = $bin + ";" + $env:Path
			break
		}
	}
	$heat = Get-Command heat -ErrorAction SilentlyContinue
}
if (-not $heat) {
	throw "WiX Toolset (heat, candle, light) not found. Add WiX to PATH or install from https://wixtoolset.org/"
}
$candle = Get-Command candle -ErrorAction SilentlyContinue
$light = Get-Command light -ErrorAction SilentlyContinue
if (-not $candle -or -not $light) {
	throw "WiX Toolset (heat, candle, light) must be in PATH. Install from https://wixtoolset.org/"
}

$PublishDirFull = (Resolve-Path $PublishDir).Path
$HarvestWxs = Join-Path $WixDir "harvest.wxs"

Push-Location $WixDir
try {
	Write-Host "Harvesting $PublishDirFull -> harvest.wxs ..."
	& heat dir $PublishDirFull -cg ApiFiles -dr INSTALLDIR -gg -srd -scom -sreg -var var.PublishDir -out harvest.wxs
	if (-not $?) { throw "heat failed." }

	Write-Host "Compiling WiX ..."
	& candle -dPublishDir="$PublishDirFull" Tindarr.wxs WixUI_Tindarr.wxs harvest.wxs -ext WixUIExtension
	if (-not $?) { throw "candle failed." }

	$MsiDir = [System.IO.Path]::GetDirectoryName($OutPath)
	if (-not [string]::IsNullOrEmpty($MsiDir) -and -not (Test-Path $MsiDir)) {
		New-Item -ItemType Directory -Path $MsiDir -Force | Out-Null
	}

	Write-Host "Linking MSI -> $OutPath ..."
	& light -ext WixUIExtension -out $OutPath -b $PublishDirFull Tindarr.wixobj WixUI_Tindarr.wixobj harvest.wixobj -cultures:en-us
	if (-not $?) { throw "light failed." }

	Write-Host "Done: $OutPath"
} finally {
	Pop-Location
}
