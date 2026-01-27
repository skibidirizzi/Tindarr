Param(
	# Where the API should listen (also used as Vite proxy target)
	[string]$ApiUrl = "http://localhost:5080",

	# Vite dev server port
	[int]$UiPort = 5173,

	# ASP.NET environment
	[ValidateSet("Development", "Staging", "Production")]
	[string]$Environment = "Development",

	# If the API port is in use, automatically pick the next available port.
	[switch]$AutoPort,

	# If the API port is in use, kill processes listening on that port.
	[switch]$KillPortUsers,

	# If set, only run the API (no UI)
	[switch]$ApiOnly,

	# If set, only run the UI (no API)
	[switch]$UiOnly
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
	# build/scripts -> repo root
	return (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

function Get-ListeningPids([int]$port) {
	try {
		# Requires Windows 8+/Server 2012+ (Get-NetTCPConnection)
		return @(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction Stop | Select-Object -ExpandProperty OwningProcess -Unique)
	}
	catch {
		return @()
	}
}

function Kill-PortUsers([int]$port) {
	$pids = Get-ListeningPids -port $port
	if ($pids.Count -eq 0) { return }

	Write-Host "Port $port is in use by PID(s): $($pids -join ', '). Killing..."
	foreach ($pid in $pids) {
		try { Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue } catch { }
	}
	Start-Sleep -Milliseconds 250
}

function Find-FreePort([int]$startPort) {
	$port = $startPort
	while ($port -lt 65535) {
		if ((Get-ListeningPids -port $port).Count -eq 0) { return $port }
		$port++
	}
	throw "No free TCP port found starting at $startPort."
}

function Start-Ui([string]$repoRoot, [string]$apiUrl, [int]$uiPort) {
	$uiDir = Join-Path $repoRoot "ui"
	if (-not (Test-Path $uiDir)) { throw "UI directory not found: $uiDir" }

	Write-Host "Starting UI (Vite) on port $uiPort (proxy -> $apiUrl)..."

	# Keep it simple: let npm manage the dev server process.
	$env:VITE_PROXY_TARGET = $apiUrl
	$env:VITE_API_BASE_URL = ""   # use same-origin + proxy during dev

	Push-Location $uiDir
	try {
		# Ensure deps exist (fast no-op when already installed)
		if (-not (Test-Path (Join-Path $uiDir "node_modules"))) {
			npm install
		}

		# Run Vite dev server in a child process so we can clean it up.
		return Start-Process -FilePath "npm.cmd" -ArgumentList @("run", "dev", "--", "--port", "$uiPort") -WorkingDirectory $uiDir -PassThru
	}
	finally {
		Pop-Location
	}
}

function Start-Api([string]$repoRoot, [string]$apiUrl, [string]$environment) {
	$apiDir = Join-Path $repoRoot "src\Tindarr.Api"
	if (-not (Test-Path $apiDir)) { throw "API directory not found: $apiDir" }

	Write-Host "Starting API (dotnet watch) at $apiUrl ($environment)..."

	$env:ASPNETCORE_URLS = $apiUrl
	$env:ASPNETCORE_ENVIRONMENT = $environment

	Push-Location $apiDir
	try {
		# Run in a child process so we can terminate it when needed.
		return Start-Process -FilePath "dotnet" -ArgumentList @("watch", "run") -WorkingDirectory $apiDir -PassThru
	}
	finally {
		Pop-Location
	}
}

$repoRoot = Resolve-RepoRoot
Write-Host "RepoRoot: $repoRoot"

$effectiveApiUrl = $ApiUrl
if (-not $UiOnly) {
	$uri = [Uri]$ApiUrl
	$port = $uri.Port

	if ($KillPortUsers) {
		Kill-PortUsers -port $port
	}

	if ((Get-ListeningPids -port $port).Count -gt 0) {
		if ($AutoPort) {
			$freePort = Find-FreePort -startPort ($port + 1)
			$effectiveApiUrl = "{0}://{1}:{2}" -f $uri.Scheme, $uri.Host, $freePort
			Write-Host "Port $port is busy; using $effectiveApiUrl instead."
		}
		else {
			throw "API port $port is already in use. Re-run with -KillPortUsers or -AutoPort, or change -ApiUrl."
		}
	}
}

$uiProc = $null
$apiProc = $null

try {
	if (-not $UiOnly) {
		$apiProc = Start-Api -repoRoot $repoRoot -apiUrl $effectiveApiUrl -environment $Environment
	}

	if (-not $ApiOnly) {
		$uiProc = Start-Ui -repoRoot $repoRoot -apiUrl $effectiveApiUrl -uiPort $UiPort
	}

	Write-Host ""
	if ($apiProc) { Write-Host "API PID: $($apiProc.Id)  ($effectiveApiUrl)" }
	if ($uiProc)  { Write-Host "UI  PID: $($uiProc.Id)  (http://localhost:$UiPort)" }
	Write-Host "Press Ctrl+C to stop."

	# Wait until either process exits (or Ctrl+C stops the script)
	while ($true) {
		Start-Sleep -Seconds 1
		if ($apiProc -and $apiProc.HasExited) { throw "API process exited (code $($apiProc.ExitCode))." }
		if ($uiProc -and $uiProc.HasExited) { throw "UI process exited (code $($uiProc.ExitCode))." }
	}
}
finally {
	if ($uiProc -and -not $uiProc.HasExited) {
		Write-Host "Stopping UI..."
		try { Stop-Process -Id $uiProc.Id -Force -ErrorAction SilentlyContinue } catch { }
	}
	if ($apiProc -and -not $apiProc.HasExited) {
		Write-Host "Stopping API..."
		try { Stop-Process -Id $apiProc.Id -Force -ErrorAction SilentlyContinue } catch { }
	}
}

