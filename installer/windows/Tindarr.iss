; Tindarr installer - Inno Setup script
; Build: iscc /DSourceDir="C:\path\to\dist\api" "installer\windows\Tindarr.iss"
; Or run: .\build\scripts\build-inno.ps1

#ifndef SourceDir
  #define SourceDir "..\..\dist\api"
#endif

[Setup]
AppName=Tindarr
AppVersion=1.2.2
AppVerName=Tindarr 1.2.2
AppPublisher=SkibidiRizzi
DefaultDirName={autopf}\Tindarr
DefaultGroupName=Tindarr
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=..\..\dist
OutputBaseFilename=Tindarr-1.2.2-setup
SetupIconFile=..\..\artifacts\tindarr.ico
WizardImageFile=..\..\artifacts\tindarrsidebanner.png
WizardSmallImageFile=..\..\artifacts\tindarrcropped.png
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=wix\License.rtf
InfoBeforeFile=
UninstallDisplayIcon={app}\Tindarr.Api.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Published API output (all files from SourceDir)
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Batch files for firewall and service (run elevated when options checked)
Source: "..\add-firewall-rules.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\install-service.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\remove-firewall-rules.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\uninstall-service.bat"; DestDir: "{app}"; Flags: ignoreversion

[UninstallRun]
Filename: "{app}\uninstall-service.bat"; Parameters: ""; StatusMsg: "Stopping Tindarr service and removing firewall rules..."; RunOnceId: "TindarrUninstall"

[Code]
var
  PortPage: TInputQueryWizardPage;
  OptionsPage: TInputOptionWizardPage;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
  ExePath, CmdTail, PsCmd: String;
begin
  Result := True;
  { If not elevated, re-launch via PowerShell -Verb RunAs so UAC is shown }
  Exec(ExpandConstant('{cmd}'), '/c net session >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode <> 0 then
  begin
    ExePath := ExpandConstant('{srcexe}');
    CmdTail := GetCmdTail;
    { Escape single quotes in CmdTail for PowerShell }
    StringChange(CmdTail, '''', '''''');
    if CmdTail <> '' then
      PsCmd := 'Start-Process -FilePath ''' + ExePath + ''' -ArgumentList ''' + CmdTail + ''' -Verb RunAs'
    else
      PsCmd := 'Start-Process -FilePath ''' + ExePath + ''' -Verb RunAs';
    { Escape double quotes for -Command "..." }
    StringChange(PsCmd, '"', '\"');
    Exec(ExpandConstant('{cmd}'), '/c powershell -NoProfile -ExecutionPolicy Bypass -Command "' + PsCmd + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Result := False;
  end;
end;

procedure InitializeWizard;
begin
  PortPage := CreateInputQueryPage(wpSelectDir,
    'Port and options',
    'Set the port for the merged UI and API.',
    'Port is saved to port.txt. The API reads from Program Data\Tindarr\port.txt.');
  PortPage.Add('Port:', False);
  PortPage.Values[0] := '6565';

  OptionsPage := CreateInputOptionPage(PortPage.ID,
    'Port and options',
    'Choose post-install steps.',
    'Select the options you want (installer runs elevated):',
    False, False);
  OptionsPage.Add('Install Tindarr as a service?');
  OptionsPage.Add('Open ports for Tindarr to be accessed via LAN or WAN.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  PortVal, AppDir, ProgramDataTindarr, PortFilePath: String;
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    PortVal := Trim(PortPage.Values[0]);
    if PortVal = '' then PortVal := '6565';

    AppDir := ExpandConstant('{app}');
    if AppDir[Length(AppDir)] <> '\' then AppDir := AppDir + '\';

    { Write port to ProgramData\Tindarr\port.txt (API reads this) }
    ProgramDataTindarr := ExpandConstant('{commonappdata}') + '\Tindarr';
    ForceDirectories(ProgramDataTindarr);
    PortFilePath := ProgramDataTindarr + '\port.txt';
    SaveStringToFile(PortFilePath, PortVal, False);

    { Write port to install dir\port.txt (add-firewall-rules.bat reads this) }
    PortFilePath := AppDir + 'port.txt';
    SaveStringToFile(PortFilePath, PortVal, False);

    if OptionsPage.Values[1] then
    begin
      Exec(ExpandConstant('{cmd}'), '/c "' + AppDir + 'add-firewall-rules.bat"', AppDir, SW_HIDE, ewWaitUntilTerminated, ResultCode);
      if ResultCode <> 0 then
        SuppressibleMsgBox('Ports already open for another app. Open firewall step was skipped.', mbError, MB_OK, IDOK);
    end;

    if OptionsPage.Values[0] then
    begin
      { Write a launcher batch so we don't pass quoted paths from Inno to cmd (avoids parsing issues). }
      SaveStringToFile(AppDir + 'run-service-install.bat',
        '@echo off' + #13#10 +
        'call "' + AppDir + 'install-service.bat" "' + AppDir + 'Tindarr.Api.exe"' + #13#10,
        False);
      Exec(ExpandConstant('{cmd}'), '/c run-service-install.bat', AppDir, SW_HIDE, ewWaitUntilTerminated, ResultCode);
      DeleteFile(AppDir + 'run-service-install.bat');
      if ResultCode <> 0 then
        SuppressibleMsgBox('Installing Tindarr as a Windows service failed (exit code ' + IntToStr(ResultCode) + '). See ' + AppDir + 'install-service.log for details, or run install-service.bat from the install folder as Administrator.', mbError, MB_OK, IDOK);
    end;
  end;
end;
