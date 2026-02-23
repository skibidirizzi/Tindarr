<#
.SYNOPSIS
    Counts lines of code in the workspace, excluding comments and blank lines.
.DESCRIPTION
    Scans src, ui, and tests for common code extensions (.cs, .ts, .tsx, .js, .jsx, .css, .yaml, .yml).
    Excludes node_modules, obj, bin, dist, .git, and similar. Counts only lines that contain code
    after stripping single-line (//, #) and block (/* */) comments.
.EXAMPLE
    .\count-loc.ps1
.EXAMPLE
    .\count-loc.ps1 -IncludeBuild
#>

Param(
    # Include build\scripts in the count
    [switch]$IncludeBuild
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $scriptDir = $PSScriptRoot
    if (-not $scriptDir) { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
    return (Resolve-Path (Join-Path $scriptDir "..\..")).Path
}

# Directories to skip (relative to repo root or as segment)
$excludeDirs = @(
    'node_modules', 'obj', 'bin', 'dist', '.git', 'packages', 'wwwroot'
)

# Extension -> comment style: 'slash' (// and /* */), 'hash' (# to EOL)
$extensions = @{
    '.cs'   = 'slash'
    '.ts'   = 'slash'
    '.tsx'  = 'slash'
    '.js'   = 'slash'
    '.jsx'  = 'slash'
    '.css'  = 'slash'
    '.yaml' = 'hash'
    '.yml'  = 'hash'
}

function ShouldSkipPath {
    param([string]$relativePath)
    $segments = $relativePath -split [regex]::Escape([IO.Path]::DirectorySeparatorChar)
    foreach ($dir in $excludeDirs) {
        if ($segments -contains $dir) { return $true }
    }
    return $false
}

function RemoveBlockCommentsSlash {
    param([string]$text)
    # Remove /* ... */ (non-greedy, across lines). Replace with space to avoid joining tokens.
    return [regex]::Replace($text, '/\*[\s\S]*?\*/', ' ')
}

function Get-CodeLinesSlash {
    param([string]$fullPath)
    $raw = [IO.File]::ReadAllText($fullPath)
    $raw = RemoveBlockCommentsSlash -text $raw
    $lines = $raw -split "`r?`n"
    $count = 0
    foreach ($line in $lines) {
        $t = $line.Trim()
        if ($t -eq '') { continue }
        # Single-line comment: line is only whitespace + // (allow /// for doc comments)
        if ($t -match '^\s*//') { continue }
        $count++
    }
    return $count
}

function Get-CodeLinesHash {
    param([string]$fullPath)
    $lines = [IO.File]::ReadAllLines($fullPath)
    $count = 0
    foreach ($line in $lines) {
        $t = $line.Trim()
        if ($t -eq '') { continue }
        # # to EOL comment (skip line if it's just comment)
        $beforeHash = ($t -split '#', 2)[0].Trim()
        if ($beforeHash -eq '') { continue }
        $count++
    }
    return $count
}

function Get-CodeLineCount {
    param([string]$fullPath, [string]$commentStyle)
    if ($commentStyle -eq 'slash') {
        return Get-CodeLinesSlash -fullPath $fullPath
    }
    if ($commentStyle -eq 'hash') {
        return Get-CodeLinesHash -fullPath $fullPath
    }
    return 0
}

$root = Resolve-RepoRoot
$searchDirs = @(
    (Join-Path $root 'src'),
    (Join-Path $root 'ui'),
    (Join-Path $root 'tests')
)
if ($IncludeBuild) {
    $searchDirs += (Join-Path $root 'build')
}

$byExt = @{}
$totalFiles = 0
$totalLines = 0

foreach ($dir in $searchDirs) {
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) { continue }
    foreach ($ext in $extensions.Keys) {
        $style = $extensions[$ext]
        $files = Get-ChildItem -Path $dir -Recurse -File -Filter "*$ext" -ErrorAction SilentlyContinue
        foreach ($f in $files) {
            $rel = $f.FullName.Substring($root.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
            if (ShouldSkipPath -relativePath $rel) { continue }
            $totalFiles++
            $n = Get-CodeLineCount -fullPath $f.FullName -commentStyle $style
            if (-not $byExt[$ext]) { $byExt[$ext] = @{ Files = 0; Lines = 0 } }
            $byExt[$ext].Files++
            $byExt[$ext].Lines += $n
            $totalLines += $n
        }
    }
}

# Output
Write-Host "Lines of code (excluding comments and blank lines)"
Write-Host "Root: $root"
Write-Host ""
$byExt.Keys | Sort-Object | ForEach-Object {
    $e = $byExt[$_]
    Write-Host ("  {0,6}  {1,6} files  {2}" -f $e.Lines, $e.Files, $_)
}
Write-Host "  ------  -------"
Write-Host ("  {0,6}  {1,6} files  TOTAL" -f $totalLines, $totalFiles)
