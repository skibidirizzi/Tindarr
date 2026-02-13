[CmdletBinding(SupportsShouldProcess = $true)]
param(
  # Frontend dev server port
  [Parameter()]
  [ValidateRange(1, 65535)]
  [int]$Port = 6565,

  # Open TCP, UDP, or both (you asked for TCP/UDP)
  [Parameter()]
  [ValidateSet('TCP', 'UDP', 'Both')]
  [string]$Protocol = 'Both',

  # Firewall profiles to apply the rule to
  [Parameter()]
  [ValidateSet('Any', 'Domain', 'Private', 'Public')]
  [string]$FwProfile = 'Any',

  # Friendly base name for the firewall rules
  [Parameter()]
  [string]$RuleBaseName = 'Tindarr Frontend',

  # Remove matching rules instead of adding them
  [Parameter()]
  [switch]$Remove
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Admin {
  if ($WhatIfPreference) {
    return
  }
  $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
  if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This script must be run from an elevated PowerShell (Run as Administrator).'
  }
}

function Get-FirewallProfileValue([string]$fwProfileName) {
  if ($fwProfileName -eq 'Any') { return 'Any' }
  return $fwProfileName
}

function Set-FirewallRule {
  param(
    [Parameter(Mandatory = $true)][string]$displayName,
    [Parameter(Mandatory = $true)][string]$protocol,
    [Parameter(Mandatory = $true)][int]$port,
    [Parameter(Mandatory = $true)][string]$fwProfile
  )

  $existing = Get-NetFirewallRule -DisplayName $displayName -ErrorAction SilentlyContinue
  if ($null -ne $existing) {
    # Replace to ensure EdgeTraversal stays correct even if edited.
    Remove-NetFirewallRule -DisplayName $displayName -ErrorAction SilentlyContinue | Out-Null
  }

  $ruleParams = @{
    DisplayName          = $displayName
    Direction            = 'Inbound'
    Action               = 'Allow'
    Enabled              = 'True'
    Profile              = $fwProfile
    Protocol             = $protocol
    LocalPort            = $port
    EdgeTraversalPolicy  = 'Allow'
    Description          = "Allow inbound $protocol/$port for Tindarr frontend (Edge Traversal enabled)."
  }

  New-NetFirewallRule @ruleParams | Out-Null
}

function Remove-FirewallRuleByName {
  param([Parameter(Mandatory = $true)][string]$displayName)
  Remove-NetFirewallRule -DisplayName $displayName -ErrorAction SilentlyContinue | Out-Null
}

Assert-Admin

$fwProfileValue = Get-FirewallProfileValue $FwProfile

$ruleNames = @()
if ($Protocol -eq 'TCP' -or $Protocol -eq 'Both') {
  $ruleNames += "$RuleBaseName (TCP $Port)"
}
if ($Protocol -eq 'UDP' -or $Protocol -eq 'Both') {
  $ruleNames += "$RuleBaseName (UDP $Port)"
}

if ($Remove) {
  foreach ($name in $ruleNames) {
    if ($PSCmdlet.ShouldProcess($name, 'Remove firewall rule')) {
      Remove-FirewallRuleByName -displayName $name
    }
  }

  Write-Host "Removed firewall rules: $($ruleNames -join ', ')"
  exit 0
}

foreach ($name in $ruleNames) {
  $proto = if ($name -like '*TCP*') { 'TCP' } else { 'UDP' }
  if ($PSCmdlet.ShouldProcess($name, "Create firewall rule for $proto/$Port")) {
    Set-FirewallRule -displayName $name -protocol $proto -port $Port -fwProfile $fwProfileValue
  }
}

Write-Host "Opened inbound firewall for: $($ruleNames -join ', ')"
Write-Host "Edge traversal: Allow"
Write-Host "Profile: $fwProfileValue"
