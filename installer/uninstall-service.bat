@echo off
setlocal
:: Requires Administrator. Stops TindarrApi service, removes it from services.msc, and removes Tindarr firewall rules.
:: Usage: run from install directory (contains port.txt). Reads port from port.txt for firewall removal.

set "LOG_FILE=%~dp0uninstall-service.log"
echo [%date% %time%] Started. >> "%LOG_FILE%"

set "SVC_NAME=TindarrApi"

:: 1. Stop the service
sc query %SVC_NAME% >nul 2>&1
if not errorlevel 1 (
    echo [%date% %time%] Stopping service %SVC_NAME% >> "%LOG_FILE%"
    sc stop %SVC_NAME% >> "%LOG_FILE%" 2>&1
    timeout /t 3 /nobreak >nul 2>&1
)

:: 2. Remove the service from services.msc
sc query %SVC_NAME% >nul 2>&1
if not errorlevel 1 (
    echo [%date% %time%] Deleting service %SVC_NAME% >> "%LOG_FILE%"
    sc delete %SVC_NAME% >> "%LOG_FILE%" 2>&1
    timeout /t 2 /nobreak >nul 2>&1
) else (
    echo [%date% %time%] Service %SVC_NAME% not present. >> "%LOG_FILE%"
)

:: 3. Remove firewall rules (uses port.txt in same directory)
if exist "%~dp0remove-firewall-rules.bat" (
    echo [%date% %time%] Removing firewall rules >> "%LOG_FILE%"
    call "%~dp0remove-firewall-rules.bat"
) else (
    echo [%date% %time%] remove-firewall-rules.bat not found, skipping. >> "%LOG_FILE%"
)

echo [%date% %time%] Uninstall complete. >> "%LOG_FILE%"
echo Service %SVC_NAME% stopped and removed; firewall rules removed.
endlocal
