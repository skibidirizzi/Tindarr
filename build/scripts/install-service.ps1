Param(
	[string]$MsiPath = "",
	[string]$ServiceName = "Tindarr.Api",
	[string]$InstallDir = "",
	[string]$LogPath = "",
	[string]$TmdbApiKey = "",
	[switch]$Quiet,
	[switch]$NoStart,
	[int]$WaitSeconds = 15
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
	# build/scripts -> repo root
	return (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

function Test-IsAdmin {
	$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
	$principal = New-Object Security.Principal.WindowsPrincipal($identity)
	return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Quote-MsiProp([string]$value) {
	# MSI property values that include spaces should be wrapped in quotes.
	if ($value -match '\s') { return '"' + $value.Replace('"', '""') + '"' }
	return $value
}

function Resolve-TmdbApiKey([string]$explicitKey, [switch]$quiet) {
	if (-not [string]::IsNullOrWhiteSpace($explicitKey)) { return $explicitKey }

	# Smooth dev testing: if the caller already has it set, just use it.
	if (-not [string]::IsNullOrWhiteSpace($env:TMDB_API_KEY)) { return $env:TMDB_API_KEY }
	if (-not [string]::IsNullOrWhiteSpace($env:Tmdb__ApiKey)) { return $env:Tmdb__ApiKey }

	# If this is a quiet install, do not prompt and do not fail.
	if ($quiet) { return "" }

	# Interactive prompt (does not echo). Empty is allowed.
	$secure = Read-Host "TMDB API key not found. Enter TMDB API key (or press Enter to skip)" -AsSecureString
	if ($secure.Length -eq 0) { return "" }

	$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
	try {
		return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
	}
	finally {
		[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
	}
}

function Set-ServiceEnvironmentVariable([string]$serviceName, [string]$name, [string]$value) {
	# Important: setting machine env vars doesn't reliably flow into services.exe immediately.
	# The SCM supports per-service environment variables via the service registry key.
	$svcKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
	if (-not (Test-Path $svcKey)) {
		throw "Service registry key not found: $svcKey"
	}

	$current = @()
	try {
		$existing = Get-ItemProperty -Path $svcKey -Name Environment -ErrorAction SilentlyContinue
		if ($existing -and $existing.Environment) { $current = @($existing.Environment) }
	}
	catch { }

	# Remove any existing entry for this var, then add the new one.
	$current = $current | Where-Object { $_ -notmatch ("^" + [Regex]::Escape($name) + "=") }
	$current += ("$name=$value")

	New-ItemProperty -Path $svcKey -Name Environment -PropertyType MultiString -Value $current -Force | Out-Null
}

$repoRoot = Resolve-RepoRoot
if ([string]::IsNullOrWhiteSpace($MsiPath)) {
	$MsiPath = Join-Path $repoRoot "artifacts\Tindarr.msi"
}

if (-not (Test-Path $MsiPath)) {
	throw "MSI not found: $MsiPath"
}

if (-not (Test-IsAdmin)) {
	throw "This script must be run as Administrator (required for service install)."
}

Write-Host "MSI:         $MsiPath"
Write-Host "ServiceName:  $ServiceName"
Write-Host "Quiet:        $Quiet"
Write-Host "NoStart:      $NoStart"

$resolvedTmdbApiKey = Resolve-TmdbApiKey -explicitKey $TmdbApiKey -quiet:$Quiet
if ([string]::IsNullOrWhiteSpace($resolvedTmdbApiKey)) {
	Write-Host "TMDB:        not configured"
}
else {
	Write-Host "TMDB:        configured (key not shown)"
}

$msiArgs = @("/i", $MsiPath, "INSTALLSERVICE=1", "/norestart")

if (-not [string]::IsNullOrWhiteSpace($InstallDir)) {
	# INSTALLFOLDER is the Directory Id in the WiX file.
	$msiArgs += ("INSTALLFOLDER=" + (Quote-MsiProp $InstallDir))
	Write-Host "InstallDir:   $InstallDir"
}

if ($Quiet) {
	$msiArgs += "/qn"
}

if ([string]::IsNullOrWhiteSpace($LogPath)) {
	$LogPath = Join-Path (Split-Path -Parent $MsiPath) "install-service.log"
}
Write-Host "Log:         $LogPath"
$msiArgs += @("/l*v", $LogPath)

Write-Host ""
Write-Host "Installing MSI (service mode)..."
$proc = Start-Process -FilePath "msiexec.exe" -ArgumentList $msiArgs -PassThru -Wait
Write-Host "msiexec exit code: $($proc.ExitCode)"

if ($proc.ExitCode -ne 0) {
	throw "MSI install failed. See log: $LogPath"
}

Write-Host ""
Write-Host "Checking service..."
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
	throw "Service not found after install: $ServiceName"
}

Write-Host "Service status: $($svc.Status)"

if (-not $NoStart) {
	if ([string]::IsNullOrWhiteSpace($resolvedTmdbApiKey)) {
		throw "TMDB API key is required to run. Set env var 'TMDB_API_KEY' (or pass -TmdbApiKey) and retry."
	}

	# If we have a key, attach it to the service environment before first start.
	if (-not [string]::IsNullOrWhiteSpace($resolvedTmdbApiKey)) {
		Write-Host "Configuring TMDB key for the service..."
		Set-ServiceEnvironmentVariable -serviceName $ServiceName -name "TMDB_API_KEY" -value $resolvedTmdbApiKey
	}

	if ($svc.Status -ne "Running") {
		Write-Host "Starting service..."
		& sc.exe start $ServiceName | Out-Null
	}

	$deadline = (Get-Date).AddSeconds([Math]::Max(1, $WaitSeconds))
	do {
		Start-Sleep -Seconds 1
		$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
	} while ($svc -and $svc.Status -ne "Running" -and (Get-Date) -lt $deadline)

	if (-not $svc) {
		throw "Service disappeared unexpectedly: $ServiceName"
	}

	Write-Host "Service status: $($svc.Status)"

	if ($svc.Status -ne "Running") {
		throw "Service failed to start within $WaitSeconds seconds. Check Windows Event Viewer and: $LogPath"
	}
}

Write-Host ""
Write-Host "Done."

