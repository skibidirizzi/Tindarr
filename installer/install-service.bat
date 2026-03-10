@echo off
setlocal enabledelayedexpansion
:: Requires Administrator. Installs the given exe as a Windows service.
:: Usage: install-service.bat "C:\Path\To\Tindarr.Api.exe"
:: If no argument is provided, uses "%~dp0Tindarr.Api.exe".
:: Service: TindarrApi, LocalSystem, Automatic, description "Tindarr Web API"
:: If service already exists with same exe and start=auto, makes no changes but starts service if not running.

set "LOG_FILE=%~dp0install-service.log"
echo [%date% %time%] Started. argv1=[%~1] >> "%LOG_FILE%"

set "EXE_PATH=%~1"
if "%EXE_PATH%"=="" set "EXE_PATH=%~dp0Tindarr.Api.exe"
for %%I in ("%EXE_PATH%") do set "EXE_SHORT=%%~sI"
set "SVC_NAME=TindarrApi"
set "SVC_DESC=Tindarr Web API"

echo [%date% %time%] Resolved exe=[%EXE_PATH%] short=[%EXE_SHORT%] >> "%LOG_FILE%"

:: Use short path (e.g. C:\Progra~2\...) for checks so (x86) doesn't break if/parsing
if "%EXE_SHORT%"=="" (
    echo [%date% %time%] Error: no argument and default exe short path could not be resolved. >> "%LOG_FILE%"
    echo Usage: install-service.bat "C:\Path\To\Tindarr.Api.exe"
    echo Or run from the install directory with Tindarr.Api.exe present.
    exit /b 1
)
if exist "!EXE_PATH!" (
    echo [%date% %time%] Executable found. >> "%LOG_FILE%"
) else (
    rem Use delayed expansion here to avoid parse-time expansion breaking blocks when the path contains parentheses (e.g. Program Files (x86)).
    echo [%date% %time%] Error: executable not found at "!EXE_PATH!". >> "%LOG_FILE%"
    echo Error: executable not found: "!EXE_PATH!"
    exit /b 1
)

:: If service already exists, check if it is in expected state (same exe, start=auto)
sc query %SVC_NAME% >nul 2>&1
if not errorlevel 1 (
    echo [%date% %time%] Service exists, checking expected state >> "%LOG_FILE%"
    set "SVC_BIN="
    for /f "tokens=2* delims=:" %%a in ('sc qc %SVC_NAME% 2^>nul ^| findstr "BINARY_PATH_NAME"') do set "SVC_BIN=%%a%%b"
    :: Trim leading spaces
    if defined SVC_BIN (
        for /f "tokens=* delims= " %%s in ("!SVC_BIN!") do set "SVC_BIN=%%s"
    )
    :: Strip quotes (sc qc commonly returns a quoted command line)
    set "SVC_BIN=!SVC_BIN:"=!"
    set "SVC_START="
    for /f "tokens=2* delims=:" %%a in ('sc qc %SVC_NAME% 2^>nul ^| findstr "START_TYPE"') do set "SVC_START=%%a%%b"
    for /f "tokens=1" %%t in ("!SVC_START!") do set "SVC_START=%%t"
    :: Expected: BINARY_PATH_NAME matches our exe (short path), START_TYPE 2 (auto)
    set "EXPECTED=0"
    echo !SVC_BIN! | find /I "%EXE_SHORT%" >nul 2>&1
    if not errorlevel 1 set "EXPECTED=1"
    if "!SVC_START!"=="2" if "!EXPECTED!"=="1" (
        echo [%date% %time%] Service already in expected state, starting if not running >> "%LOG_FILE%"
        sc query %SVC_NAME% | findstr "RUNNING" >nul 2>&1
        if errorlevel 1 (
            sc start %SVC_NAME% >> "%LOG_FILE%" 2>&1
            if not errorlevel 1 echo [%date% %time%] Service started. >> "%LOG_FILE%"
        ) else (
            echo [%date% %time%] Service already running. >> "%LOG_FILE%"
        )
        echo Service "%SVC_NAME%" already configured: %SVC_DESC%. Started if needed.
        endlocal
        exit /b 0
    )
    echo [%date% %time%] Stopping and removing existing service (config differs) >> "%LOG_FILE%"
    sc stop %SVC_NAME% >> "%LOG_FILE%" 2>&1
    timeout /t 2 /nobreak >nul 2>&1
    sc delete %SVC_NAME% >> "%LOG_FILE%" 2>&1
    timeout /t 2 /nobreak >nul 2>&1
)

echo [%date% %time%] Creating service >> "%LOG_FILE%"

:: Use short path for sc create so (x86) doesn't break the command line
echo [%date% %time%] Running: sc create %SVC_NAME% binPath= "%EXE_SHORT%" start= auto >> "%LOG_FILE%"
sc create %SVC_NAME% binPath= "%EXE_SHORT%" start= auto >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo [%date% %time%] sc create failed. >> "%LOG_FILE%"
    exit /b 1
)
echo [%date% %time%] sc create succeeded >> "%LOG_FILE%"

sc description %SVC_NAME% "%SVC_DESC%" >> "%LOG_FILE%" 2>&1
set "ERR_DESC=!errorlevel!"
echo [%date% %time%] sc description errorlevel=!ERR_DESC! >> "%LOG_FILE%"
if not "!ERR_DESC!"=="0" exit /b 1

sc config %SVC_NAME% obj= LocalSystem >> "%LOG_FILE%" 2>&1
set "ERR_CFG=!errorlevel!"
echo [%date% %time%] sc config errorlevel=!ERR_CFG! >> "%LOG_FILE%"
if not "!ERR_CFG!"=="0" exit /b 1

echo [%date% %time%] Starting service >> "%LOG_FILE%"
sc start %SVC_NAME% >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo [%date% %time%] sc start failed - start manually with: sc start %SVC_NAME% >> "%LOG_FILE%"
    exit /b 1
) else (
    echo [%date% %time%] Service started. >> "%LOG_FILE%"
)

echo Service "%SVC_NAME%" installed: %SVC_DESC%, Automatic, LocalSystem.
endlocal
exit /b 0
