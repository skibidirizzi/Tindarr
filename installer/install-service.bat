@echo off
setlocal enabledelayedexpansion
:: Requires Administrator. Installs the given exe as a Windows service.
:: Usage: install-service.bat "C:\Path\To\Tindarr.Api.exe"
:: Service: TindarrApi, LocalSystem, Automatic, description "Tindarr Web API"
:: If service already exists with same exe and start=auto, makes no changes but starts service if not running.

set "LOG_FILE=%~dp0install-service.log"
echo [%date% %time%] Started. argv1=[%~1] >> "%LOG_FILE%"

set "EXE_PATH=%~1"
set "EXE_SHORT=%~s1"
set "SVC_NAME=TindarrApi"
set "SVC_DESC=Tindarr Web API"

:: Use short path (e.g. C:\Progra~2\...) for checks so (x86) doesn't break if/parsing
if "%~s1"=="" (
    echo [%date% %time%] Error: no argument. >> "%LOG_FILE%"
    exit /b 1
)
dir /b "%EXE_SHORT%" >nul 2>&1
if errorlevel 1 (
    echo [%date% %time%] Error: executable not found. >> "%LOG_FILE%"
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
    set "SVC_START="
    for /f "tokens=2* delims=:" %%a in ('sc qc %SVC_NAME% 2^>nul ^| findstr "START_TYPE"') do set "SVC_START=%%a%%b"
    for /f "tokens=1" %%t in ("!SVC_START!") do set "SVC_START=%%t"
    :: Expected: BINARY_PATH_NAME matches our exe (short path), START_TYPE 2 (auto)
    set "EXPECTED=0"
    if /i "!SVC_BIN!"=="%EXE_SHORT%" set "EXPECTED=1"
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

sc description %SVC_NAME% "%SVC_DESC%"
sc config %SVC_NAME% obj= LocalSystem

echo [%date% %time%] Starting service >> "%LOG_FILE%"
sc start %SVC_NAME% >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo [%date% %time%] sc start failed - start manually with: sc start %SVC_NAME% >> "%LOG_FILE%"
) else (
    echo [%date% %time%] Service started. >> "%LOG_FILE%"
)

echo Service "%SVC_NAME%" installed: %SVC_DESC%, Automatic, LocalSystem.
endlocal
