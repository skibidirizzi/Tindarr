@echo off
setlocal enabledelayedexpansion
:: Requires Administrator. Reads port from port.txt in this directory.
:: Creates inbound TCP and UDP firewall rules for that port, all profiles, edge traversal allowed.
:: If rules "Tindarr TCP (port X)" and "Tindarr UDP (port X)" already exist, makes no changes.
:: If another rule already uses this port (different name), exits with "Ports already open for another app".

set "SCRIPT_DIR=%~dp0"
set "PORT_FILE=%SCRIPT_DIR%port.txt"

if not exist "%PORT_FILE%" (
    echo port.txt not found at "%PORT_FILE%". Create it with a single port number.
    exit /b 1
)

set /p PORT=<"%PORT_FILE%"
if "%PORT%"=="" (
    echo port.txt is empty or invalid.
    exit /b 1
)

:: Remove leading/trailing spaces
for /f "tokens=* delims= " %%a in ("%PORT%") do set PORT=%%a

set "TCP_NAME=Tindarr TCP (port %PORT%)"
set "UDP_NAME=Tindarr UDP (port %PORT%)"

:: If our rules already exist, make no changes
netsh advfirewall firewall show rule name="%TCP_NAME%" >nul 2>&1
if not errorlevel 1 (
    netsh advfirewall firewall show rule name="%UDP_NAME%" >nul 2>&1
    if not errorlevel 1 (
        echo Firewall rules already exist for Tindarr on port %PORT%. No changes made.
        exit /b 0
    )
)

:: Check if any other rule uses this port (different name) - fail with clear message
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$port = [int]'%PORT%'; $tcpName = 'Tindarr TCP (port %PORT%)'; $udpName = 'Tindarr UDP (port %PORT%)'; $rules = Get-NetFirewallRule -Direction Inbound -ErrorAction SilentlyContinue | Where-Object { $_.Enabled -eq 'True' }; foreach ($r in $rules) { $filter = $r | Get-NetFirewallPortFilter -ErrorAction SilentlyContinue; if ($filter -and $filter.LocalPort) { $ports = @($filter.LocalPort); if ($ports -contains $port) { $name = $r.DisplayName; if ($name -ne $tcpName -and $name -ne $udpName) { Write-Host 'Ports already open for another app'; exit 1 } } } }; exit 0"
if errorlevel 1 (
    echo Ports already open for another app
    exit /b 1
)

netsh advfirewall firewall add rule name="%TCP_NAME%" dir=in action=allow protocol=TCP localport=%PORT% profile=any edge=yes
netsh advfirewall firewall add rule name="%UDP_NAME%" dir=in action=allow protocol=UDP localport=%PORT% profile=any edge=yes

echo Firewall rules added for TCP and UDP on port %PORT% (all profiles, edge traversal enabled).
endlocal
