# Developer-loop helper. This script:
#   1. runs `dotnet publish DiffViewer\DiffViewer.csproj -c Release`
#      (single-file/self-contained/win-x64 settings come from the Release
#      PropertyGroup in DiffViewer.csproj, matching the Visual Studio
#      Folder profile and the CI release workflow),
#   2. stops any running DiffViewer.exe whose image path is C:\Tools\DiffViewer.exe,
#   3. copies the publish output into C:\Tools, and
#   4. launches C:\Tools\DiffViewer.exe on three repos.

#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$publishDir = Join-Path $PSScriptRoot 'DiffViewer\bin\Release\net8.0-windows\win-x64\publish'
$destDir    = 'C:\Tools'
$targetExe  = Join-Path $destDir 'DiffViewer.exe'
$csproj     = Join-Path $PSScriptRoot 'DiffViewer\DiffViewer.csproj'
$repos = @(
    'C:\Repos\jotjson',
    'C:\Repos\jotjson-alt',
    'C:\Repos\DiffViewer'
)

# 0. Build + publish BEFORE taking any destructive action (kill / copy).
#    The Release PropertyGroup in DiffViewer.csproj pins
#    PublishSingleFile / SelfContained / win-x64 /
#    IncludeNativeLibrariesForSelfExtract, so a bare `dotnet publish -c
#    Release` matches the Visual Studio Folder profile and CI publish.
#    Repo paths are intentionally NOT pre-validated; missing repos are
#    skipped with a warning at launch time so a partial deploy still
#    benefits the repos that do exist.
if (-not (Test-Path -LiteralPath $csproj)) {
    throw "DiffViewer.csproj not found at '$csproj'."
}

Write-Host "Publishing DiffViewer (Release / win-x64) from '$csproj'"
& dotnet publish $csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedExe = Join-Path $publishDir 'DiffViewer.exe'
if (-not (Test-Path -LiteralPath $publishDir)) {
    throw "Publish output directory not found at '$publishDir' after publish."
}
if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw "Publish output is missing DiffViewer.exe at '$publishedExe' after publish."
}

# 1. Kill running DiffViewer instances whose image path matches $targetExe.
#    Pre-narrow by process name so we don't enumerate MainModule for every
#    process on the system; then filter precisely by MainModule.FileName to
#    avoid touching `dotnet run` instances or a debug-build DiffViewer.exe
#    running from a different location.
$candidates = Get-Process -Name DiffViewer -ErrorAction SilentlyContinue
$running = @(
    $candidates | Where-Object {
        try { $_.MainModule.FileName -ieq $targetExe } catch { $false }
    }
)

foreach ($p in $running) {
    Write-Host "Stopping DiffViewer.exe (PID $($p.Id))"
    Stop-Process -Id $p.Id -Force -ErrorAction Stop
}

foreach ($p in $running) {
    try { $p.WaitForExit(5000) | Out-Null } catch { }
}

# 2. Copy publish output to C:\Tools (overlay; no clean).
if (-not (Test-Path -LiteralPath $destDir)) {
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
}

Write-Host "Copying publish output from '$publishDir' to '$destDir'"
Copy-Item -Path (Join-Path $publishDir '*') -Destination $destDir -Recurse -Force

if (-not (Test-Path -LiteralPath $targetExe)) {
    throw "Expected '$targetExe' to exist after copy."
}

# 3. Launch DiffViewer on each repo; skip with a warning if a repo path is missing.
foreach ($repo in $repos) {
    if (-not (Test-Path -LiteralPath $repo)) {
        Write-Warning "Skipping missing repo: $repo"
        continue
    }
    Write-Host "Launching DiffViewer on $repo"
    Start-Process -FilePath $targetExe -ArgumentList @($repo) | Out-Null
}
