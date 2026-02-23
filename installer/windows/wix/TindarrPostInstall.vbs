Option Explicit

' Deferred custom action: writes port to port.txt (install dir and ProgramData\Tindarr),
' then optionally runs add-firewall-rules.bat and install-service.bat elevated.
' CustomActionData format: InstallDir|Port|InstallService|OpenPorts|CommonAppDataFolder
' (pipe-separated so paths can contain semicolons)

Const ForWriting = 2
Const CreateForAppend = 8

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

	' Ensure trailing backslash for directory
	If Right(installDir, 1) <> "\" Then installDir = installDir & "\"

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
	comSpec = "C:\Windows\System32\cmd.exe"
	comSpec = shell.Environment("Process")("COMSPEC")
	If comSpec = "" Then comSpec = "C:\Windows\System32\cmd.exe"

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
	If Right(installDir, 1) <> "\" Then installDir = installDir & "\"
	Set shell = CreateObject("WScript.Shell")
	comSpec = shell.Environment("Process")("COMSPEC")
	If comSpec = "" Then comSpec = "C:\Windows\System32\cmd.exe"
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
