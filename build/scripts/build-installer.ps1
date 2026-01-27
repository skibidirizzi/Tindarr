Param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Runtime = "win-x64",

    [string]$PublishDir = "",

    [string]$MsiPath = "",

    # Intentionally opt-in: public MSI builds should NOT embed a shared TMDB key.
    [switch]$InjectTmdbApiKey
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

# Normalize paths for tools that resolve relative to their input files.
$PublishDir = (Resolve-Path $PublishDir).Path
$MsiPath = (Resolve-Path (Split-Path -Parent $MsiPath)).Path + "\" + (Split-Path -Leaf $MsiPath)

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

if ($InjectTmdbApiKey) {
    function Get-TmdbApiKey {
        # Prefer a simple env var name; allow config-style fallback.
        if (-not [string]::IsNullOrWhiteSpace($env:TMDB_API_KEY)) { return $env:TMDB_API_KEY }
        if (-not [string]::IsNullOrWhiteSpace($env:Tmdb__ApiKey)) { return $env:Tmdb__ApiKey }
        return ""
    }

    function Inject-TmdbApiKeyIntoPublishConfig([string]$publishDir) {
        $apiKey = Get-TmdbApiKey
        if ([string]::IsNullOrWhiteSpace($apiKey)) {
            throw "InjectTmdbApiKey was specified but no TMDB API key was found in env vars (TMDB_API_KEY / Tmdb__ApiKey)."
        }

        $appsettingsPath = Join-Path $publishDir "appsettings.json"
        if (-not (Test-Path $appsettingsPath)) {
            throw "Published appsettings.json not found: $appsettingsPath"
        }

        Write-Host "Injecting TMDB API key into published appsettings.json (not source-controlled)..."

        $json = Get-Content -Path $appsettingsPath -Raw | ConvertFrom-Json
        if ($null -eq $json.Tmdb) {
            $json | Add-Member -MemberType NoteProperty -Name "Tmdb" -Value ([pscustomobject]@{})
        }

        # Do not print/log the key value.
        $json.Tmdb.ApiKey = $apiKey

        $json | ConvertTo-Json -Depth 50 | Set-Content -Path $appsettingsPath -Encoding UTF8
    }

    Inject-TmdbApiKeyIntoPublishConfig -publishDir $PublishDir
}

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
    -ext WixToolset.UI.wixext `
    -o "$MsiPath"

Write-Host ""
Write-Host "Done."
Write-Host "MSI: $MsiPath"

