Param(
	[string]$MsiPath = "",
	[string]$ServiceName = "Tindarr.Api",
	[string]$InstallDir = "",
	[string]$LogPath = "",
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

