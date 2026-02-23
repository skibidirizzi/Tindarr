@echo off
setlocal
:: Requires Administrator. Reads port from port.txt in this directory.
:: Removes inbound firewall rules named "Tindarr TCP (port X)" and "Tindarr UDP (port X)" only.

set "SCRIPT_DIR=%~dp0"
set "PORT_FILE=%SCRIPT_DIR%port.txt"

if not exist "%PORT_FILE%" (
    echo port.txt not found at "%PORT_FILE%". Skipping firewall rule removal.
    exit /b 0
)

set /p PORT=<"%PORT_FILE%"
if "%PORT%"=="" (
    echo port.txt is empty. Skipping firewall rule removal.
    exit /b 0
)

:: Remove leading/trailing spaces
for /f "tokens=* delims= " %%a in ("%PORT%") do set PORT=%%a

set "TCP_NAME=Tindarr TCP (port %PORT%)"
set "UDP_NAME=Tindarr UDP (port %PORT%)"

netsh advfirewall firewall delete rule name="%TCP_NAME%" >nul 2>&1
netsh advfirewall firewall delete rule name="%UDP_NAME%" >nul 2>&1

echo Firewall rules removed for Tindarr on port %PORT%.
endlocal
