Param(
	[switch]$RemoveDataDir,
	[switch]$Quiet
)

$ErrorActionPreference = "Stop"

function Test-IsAdmin {
	$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
	$principal = New-Object Security.Principal.WindowsPrincipal($identity)
	return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-IsGuid([string]$value) {
	if ([string]::IsNullOrWhiteSpace($value)) { return $false }
	return [regex]::IsMatch($value.Trim(), "^\{[0-9A-Fa-f\-]{36}\}$")
}

function Get-MsiProductCodeFromUninstallString([string]$uninstallString) {
	if ([string]::IsNullOrWhiteSpace($uninstallString)) { return $null }
	# Example: "msiexec.exe /I{GUID}" or "/X{GUID}"
	$match = [regex]::Match($uninstallString, "\{[0-9A-Fa-f\-]{36}\}")
	if ($match.Success) { return $match.Value }
	return $null
}

function Find-TindarrMsiInstall {
	$regPaths = @(
		"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
		"HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
	)

	foreach ($path in $regPaths) {
		$items = Get-ItemProperty -Path $path -ErrorAction SilentlyContinue |
			Where-Object {
				$_.DisplayName -and ($_.DisplayName -like "Tindarr*" -or $_.DisplayName -eq "Tindarr")
			}

		foreach ($item in $items) {
			# Some MSI entries store the product code as the registry key name.
			$productCode = $null
			if ($item.PSChildName -and (Test-IsGuid $item.PSChildName)) {
				$productCode = $item.PSChildName
			} else {
				$productCode = Get-MsiProductCodeFromUninstallString $item.UninstallString
			}

			if ($productCode) {
				return [pscustomobject]@{
					ProductCode     = $productCode
					InstallLocation = $item.InstallLocation
					DisplayName     = $item.DisplayName
					UninstallString = $item.UninstallString
				}
			}
		}
	}

	return $null
}

function Stop-And-DeleteService([string]$serviceName) {
	$svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
	if (-not $svc) {
		Write-Host "Service not found: $serviceName"
		return
	}

	if ($svc.Status -ne "Stopped") {
		Write-Host "Stopping service: $serviceName"
		# Use sc.exe for best compatibility
		& sc.exe stop $serviceName | Out-Null
		Start-Sleep -Seconds 2
	}

	Write-Host "Deleting service: $serviceName"
	& sc.exe delete $serviceName | Out-Null
}

function Stop-RunningProcesses {
	$names = @("Tindarr.Api", "Tindarr.Workers")
	foreach ($n in $names) {
		$procs = Get-Process -Name $n -ErrorAction SilentlyContinue
		foreach ($p in $procs) {
			Write-Host "Stopping process: $($p.ProcessName) (pid $($p.Id))"
			try {
				Stop-Process -Id $p.Id -Force -ErrorAction Stop
			} catch {
				Write-Warning "Failed to stop pid $($p.Id): $($_.Exception.Message)"
			}
		}
	}
}

function Remove-DirWithRetries([string]$path, [int]$retries = 5) {
	if (-not (Test-Path $path)) { return }
	for ($i = 1; $i -le $retries; $i++) {
		try {
			Remove-Item -Recurse -Force $path -ErrorAction Stop
			return
		} catch {
			if ($i -eq $retries) {
				Write-Warning "Failed to remove '$path': $($_.Exception.Message)"
				return
			}
			Start-Sleep -Seconds 1
		}
	}
}

if (-not $Quiet) {
	Write-Host "Note: pass -Quiet to reduce MSI UI."
}

if (-not (Test-IsAdmin)) {
	Write-Warning "Not running as Administrator. Service deletion and MSI uninstall may fail."
}

# 0) Stop any running processes (avoids locked files)
Stop-RunningProcesses

# 1) Stop/remove service (best-effort; MSI uninstall should remove it too)
Stop-And-DeleteService -serviceName "Tindarr.Api"

# 2) Uninstall MSI if installed
$install = Find-TindarrMsiInstall
if ($install -and $install.ProductCode) {
	Write-Host "Uninstalling MSI: $($install.DisplayName) $($install.ProductCode)"
	$msiArgs = @("/x", $install.ProductCode, "/norestart")
	if ($Quiet) { $msiArgs += "/qn" }
	$proc = Start-Process -FilePath "msiexec.exe" -ArgumentList $msiArgs -PassThru -Wait
	Write-Host "msiexec exit code: $($proc.ExitCode)"
} else {
	Write-Host "No Tindarr MSI install found in registry."
}

# 3) Cleanup folders (best-effort)
$installDir = if ($install -and $install.InstallLocation) { $install.InstallLocation } else { Join-Path $env:ProgramFiles "Tindarr" }
$startMenuDir = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\Tindarr"
$dataDir = Join-Path $env:ProgramData "Tindarr"

foreach ($dir in @($installDir, $startMenuDir)) {
	if (Test-Path $dir) {
		Write-Host "Removing: $dir"
		Remove-DirWithRetries -path $dir -retries 5
	}
}

if ($RemoveDataDir) {
	if (Test-Path $dataDir) {
		Write-Host "Removing data dir: $dataDir"
		Remove-DirWithRetries -path $dataDir -retries 5
	}
} else {
	Write-Host "Keeping data dir (pass -RemoveDataDir to delete): $dataDir"
}

Write-Host "Uninstall complete."

