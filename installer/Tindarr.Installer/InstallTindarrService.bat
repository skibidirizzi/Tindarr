@echo off
setlocal
set "DIR=%~dp0"
set "EXE=%DIR%Tindarr.Api.exe"
if not exist "%EXE%" (
    echo Tindarr.Api.exe not found.
    exit /b 1
)
rem Create service if it does not exist (ignore error if already exists from previous install).
sc create Tindarr.Api binPath= "\"%EXE%\"" start= auto obj= "NT AUTHORITY\LocalService" 2>nul
rem Reconfigure and set auto-start (handles reinstall when service was left behind disabled/stopped).
sc config Tindarr.Api binPath= "\"%EXE%\"" start= auto obj= "NT AUTHORITY\LocalService" 2>nul
sc failure Tindarr.Api reset= 86400 actions= restart/60000/restart/60000/restart/60000 2>nul
sc start Tindarr.Api 2>nul
endlocal
exit /b 0
