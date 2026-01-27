Param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Runtime = "win-x64",

    [string]$PublishDir = "",

    [string]$MsiPath = ""
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    # build/scripts -> repo root
    return (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}

$repoRoot = Resolve-RepoRoot
$defaultPublishDir = Join-Path $repoRoot "artifacts\publish\api"
$defaultMsiPath = Join-Path $repoRoot "artifacts\Tindarr.msi"

if ([string]::IsNullOrWhiteSpace($PublishDir)) { $PublishDir = $defaultPublishDir }
if ([string]::IsNullOrWhiteSpace($MsiPath)) { $MsiPath = $defaultMsiPath }

$apiCsproj = Join-Path $repoRoot "src\Tindarr.Api\Tindarr.Api.csproj"
$wxsPath = Join-Path $repoRoot "installer\windows\wix\Tindarr.wxs"
$uiDir = Join-Path $repoRoot "ui"
$uiDistDir = Join-Path $uiDir "dist"

Write-Host "RepoRoot:   $repoRoot"
Write-Host "Config:     $Configuration"
Write-Host "Runtime:    $Runtime"
Write-Host "PublishDir: $PublishDir"
Write-Host "MSI:        $MsiPath"

New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $MsiPath) | Out-Null

# Ensure we never package a stale publish output.
Get-ChildItem -Path $PublishDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Publishing API..."
dotnet publish "$apiCsproj" `
    -c $Configuration `
    -r $Runtime `
    /p:SelfContained=true `
    /p:PublishSingleFile=true `
    /p:PublishTrimmed=false `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    -o "$PublishDir"

Write-Host ""
Write-Host "Building UI..."
if (-not (Test-Path $uiDir)) {
    throw "UI directory not found: $uiDir"
}

Push-Location $uiDir
try {
    if (-not (Test-Path (Join-Path $uiDir "node_modules"))) {
        npm ci
    }
    else {
        npm install
    }
    npm run build
}
finally {
    Pop-Location
}

if (-not (Test-Path $uiDistDir)) {
    throw "UI build output not found: $uiDistDir"
}

Write-Host "Copying UI into publish wwwroot..."
$publishWebRoot = Join-Path $PublishDir "wwwroot"
New-Item -ItemType Directory -Force -Path $publishWebRoot | Out-Null
Get-ChildItem -Path $publishWebRoot -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -Path (Join-Path $uiDistDir "*") -Destination $publishWebRoot -Recurse -Force

Write-Host ""
Write-Host "Building MSI..."
wix build "$wxsPath" `
    -arch x64 `
    -d PublishDir="$PublishDir" `
    -o "$MsiPath"

Write-Host ""
Write-Host "Done."
Write-Host "MSI: $MsiPath"

