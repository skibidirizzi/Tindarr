Param(
	# Where the API should listen (also used as Vite proxy target)
	[string]$ApiUrl = "http://localhost:5080",

	# Vite dev server port
	[int]$UiPort = 6565,

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
	foreach ($processId in $pids) {
		try { Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue } catch { }
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

function Get-PrivateIPv4 {
	try {
		$addrs = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
			Where-Object {
				$ip = $_.IPAddress
				$ip -and $ip -ne "127.0.0.1" -and (
					$ip.StartsWith("10.") -or
					$ip.StartsWith("192.168.") -or
					($ip.StartsWith("172.") -and ([int]($ip.Split('.')[1])) -ge 16 -and ([int]($ip.Split('.')[1])) -le 31)
				)
			} |
			Select-Object -First 1

		return $addrs.IPAddress
	}
	catch {
		return $null
	}
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
		return Start-Process -FilePath "npm.cmd" -ArgumentList @("run", "dev", "--", "--host", "0.0.0.0", "--port", "$uiPort") -WorkingDirectory $uiDir -PassThru
	}
	finally {
		Pop-Location
	}
}

function Start-Api([string]$repoRoot, [string]$listenUrl, [string]$environment, [string]$lanBaseUrl) {
	$apiDir = Join-Path $repoRoot "src\Tindarr.Api"
	if (-not (Test-Path $apiDir)) { throw "API directory not found: $apiDir" }

	Write-Host "Starting API (dotnet watch) listening at $listenUrl ($environment)..."
	if ($lanBaseUrl) {
		Write-Host "BaseUrl:Lan -> $lanBaseUrl"
	}

	$env:ASPNETCORE_URLS = $listenUrl
	$env:ASPNETCORE_ENVIRONMENT = $environment
	if ($lanBaseUrl) {
		$env:BaseUrl__Lan = $lanBaseUrl
		if (-not $env:BaseUrl__Wan) {
			$env:BaseUrl__Wan = $lanBaseUrl
		}
	}

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

$uri = [Uri]$effectiveApiUrl
$apiPort = $uri.Port
$scheme = $uri.Scheme

# Bind API to all interfaces by default (Chromecast must reach the host).
$listenHost = $uri.Host
if ($listenHost -eq "localhost" -or $listenHost -eq "127.0.0.1" -or $listenHost -eq "::1") {
	$listenHost = "0.0.0.0"
}
$listenUrl = "{0}://{1}:{2}" -f $scheme, $listenHost, $apiPort

# Keep Vite proxy target on localhost for easiest browser dev.
$proxyTarget = $effectiveApiUrl
if ($uri.Host -eq "0.0.0.0") {
	$proxyTarget = "{0}://localhost:{1}" -f $scheme, $apiPort
}

$lanIp = Get-PrivateIPv4
$lanBaseUrl = $null
if ($lanIp) {
	$lanBaseUrl = "{0}://{1}:{2}" -f $scheme, $lanIp, $apiPort
}

try {
	if (-not $UiOnly) {
		$apiProc = Start-Api -repoRoot $repoRoot -listenUrl $listenUrl -environment $Environment -lanBaseUrl $lanBaseUrl
	}

	if (-not $ApiOnly) {
		$uiProc = Start-Ui -repoRoot $repoRoot -apiUrl $proxyTarget -uiPort $UiPort
	}

	Write-Host ""
	if ($apiProc) { Write-Host "API PID: $($apiProc.Id)  (listen: $listenUrl)" }
	if ($lanBaseUrl) { Write-Host "LAN Base: $lanBaseUrl" }
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

