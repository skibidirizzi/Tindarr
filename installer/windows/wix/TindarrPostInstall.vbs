Option Explicit

' Deferred custom action: writes port to port.txt (install dir and ProgramData\Tindarr),
' then optionally runs add-firewall-rules.bat and install-service.bat elevated.
' CustomActionData format: InstallDir|Port|InstallService|OpenPorts|CommonAppDataFolder
' (pipe-separated so paths can contain semicolons)

Const ForWriting = 2
Const CreateForAppend = 8

Function EnsureTrailingBackslash(path)
	If path = "" Then
		EnsureTrailingBackslash = ""
		Exit Function
	End If
	If Right(path, 1) <> "\" Then
		EnsureTrailingBackslash = path & "\"
	Else
		EnsureTrailingBackslash = path
	End If
End Function

Function GetComSpec(shell)
	Dim comSpec
	comSpec = shell.Environment("Process")("COMSPEC")
	If comSpec = "" Then comSpec = "C:\Windows\System32\cmd.exe"
	GetComSpec = comSpec
End Function

Function ReadPortFromProgramData(commonAppData)
	Dim fso, programDataTindarr, portFileAppData, port
	ReadPortFromProgramData = ""
	On Error Resume Next
	If commonAppData = "" Then Exit Function
	Set fso = CreateObject("Scripting.FileSystemObject")
	programDataTindarr = fso.BuildPath(commonAppData, "Tindarr")
	portFileAppData = fso.BuildPath(programDataTindarr, "port.txt")
	If fso.FileExists(portFileAppData) Then
		port = Trim(fso.OpenTextFile(portFileAppData, 1).ReadAll)
		If IsNumeric(port) Then
			ReadPortFromProgramData = CStr(CInt(port))
		End If
	End If
End Function

Function CommandSucceeds(shell, cmdLine)
	Dim ret
	On Error Resume Next
	ret = shell.Run(cmdLine, 0, True)
	CommandSucceeds = (ret = 0)
End Function

' Immediate custom action: best-effort init defaults from existing machine state.
' - PORT_PROPERTY from ProgramData\Tindarr\port.txt (if present)
' - INSTALL_SERVICE if TindarrApi service exists
' - OPEN_PORTS if our firewall rules exist for that port
Function InitFromExisting(Session)
	Dim shell, comSpec, commonAppData, port, tcpName, udpName
	Dim currentPort, currentInstallService, currentOpenPorts

	InitFromExisting = 0
	On Error Resume Next
	If Session Is Nothing Then Exit Function

	commonAppData = Session.Property("CommonAppDataFolder")
	port = ReadPortFromProgramData(commonAppData)

	currentPort = Trim(Session.Property("PORT_PROPERTY"))
	If port <> "" Then
		If currentPort = "" Or currentPort = "6565" Then
			Session.Property("PORT_PROPERTY") = port
			currentPort = port
		End If
	End If

	Set shell = CreateObject("WScript.Shell")
	comSpec = GetComSpec(shell)

	currentInstallService = Trim(Session.Property("INSTALL_SERVICE"))
	If (currentInstallService = "" Or currentInstallService = "0") Then
		If CommandSucceeds(shell, comSpec & " /c sc query TindarrApi >nul 2>&1") Then
			Session.Property("INSTALL_SERVICE") = "1"
		End If
	End If

	currentOpenPorts = Trim(Session.Property("OPEN_PORTS"))
	If (currentOpenPorts = "" Or currentOpenPorts = "0") And currentPort <> "" Then
		tcpName = "Tindarr TCP (port " & currentPort & ")"
		udpName = "Tindarr UDP (port " & currentPort & ")"
		If CommandSucceeds(shell, comSpec & " /c netsh advfirewall firewall show rule name=\"" & tcpName & "\" >nul 2>&1") Then
			If CommandSucceeds(shell, comSpec & " /c netsh advfirewall firewall show rule name=\"" & udpName & "\" >nul 2>&1") Then
				Session.Property("OPEN_PORTS") = "1"
			End If
		End If
	End If
End Function

' Deferred custom action: stop the service (used during upgrades to avoid file locks).
Function StopService(Session)
	Dim shell, comSpec
	StopService = 0
	On Error Resume Next
	Set shell = CreateObject("WScript.Shell")
	comSpec = GetComSpec(shell)
	Call shell.Run(comSpec & " /c sc stop TindarrApi >nul 2>&1", 0, True)
End Function

Function PostInstall(Session)
	Dim data, parts, installDir, port, installService, openPorts, commonAppData
	Dim fso, programDataTindarr, portFileAppData, portFileInstall
	Dim shell, cmd, comSpec, exePath, ret

	PostInstall = 0
	On Error Resume Next
	If Session Is Nothing Then Exit Function
	data = ""
	data = Session.CustomActionData
	If IsNull(data) Or data = "" Then Exit Function

	parts = Split(data, "|", -1, vbBinaryCompare)
	If UBound(parts) < 4 Then Exit Function

	installDir = Trim(parts(0))
	port = Trim(parts(1))
	installService = Trim(parts(2))
	openPorts = Trim(parts(3))
	commonAppData = Trim(parts(4))

	installDir = EnsureTrailingBackslash(installDir)

	Set fso = CreateObject("Scripting.FileSystemObject")

	' Create ProgramData\Tindarr and write port.txt
	programDataTindarr = fso.BuildPath(commonAppData, "Tindarr")
	If Not fso.FolderExists(programDataTindarr) Then
		fso.CreateFolder programDataTindarr
	End If
	portFileAppData = fso.BuildPath(programDataTindarr, "port.txt")
	WritePortFile fso, portFileAppData, port

	' Write port.txt in install directory (for add-firewall-rules.bat)
	portFileInstall = installDir & "port.txt"
	WritePortFile fso, portFileInstall, port

	Set shell = CreateObject("WScript.Shell")
	' Full path to cmd (Environment can fail when running as SYSTEM in deferred CA)
	comSpec = GetComSpec(shell)

	' Run add-firewall-rules.bat elevated (batch uses %~dp0 for port.txt)
	If openPorts = "1" Then
		cmd = comSpec & " /c """ & installDir & "add-firewall-rules.bat"""
		ret = shell.Run(cmd, 1, True)
		If ret <> 0 Then
			PostInstall = 1
			Exit Function
		End If
	End If

	' Run install-service.bat with exe path (full path to batch, then quoted exe path as arg)
	If installService = "1" Then
		exePath = installDir & "Tindarr.Api.exe"
		cmd = comSpec & " /c """ & installDir & "install-service.bat"" """ & exePath & """"
		ret = shell.Run(cmd, 1, True)
		If ret <> 0 Then
			' Log but do not fail install
		End If
	End If

	PostInstall = 0
End Function

' Uninstall custom action: stop TindarrApi service, remove service, remove Tindarr firewall rules.
' CustomActionData = INSTALLDIR (trailing backslash not required).
Function PostUninstall(Session)
	Dim installDir, shell, cmd, comSpec, ret
	PostUninstall = 0
	On Error Resume Next
	If Session Is Nothing Then Exit Function
	installDir = Session.CustomActionData
	If IsNull(installDir) Or installDir = "" Then Exit Function
	installDir = EnsureTrailingBackslash(installDir)
	Set shell = CreateObject("WScript.Shell")
	comSpec = GetComSpec(shell)
	cmd = comSpec & " /c """ & installDir & "uninstall-service.bat"""
	ret = shell.Run(cmd, 1, True)
	PostUninstall = 0
End Function

Sub WritePortFile(fso, path, port)
	Dim f
	Set f = fso.CreateTextFile(path, True)
	f.Write port
	f.Close
End Sub
