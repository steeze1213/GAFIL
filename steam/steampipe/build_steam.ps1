# Builds the Steam (API-key-only) edition into steam\steampipe\content and, if asked, uploads it with steamcmd.
# Usage:
#   .\build_steam.ps1                                   # build only
#   .\build_steam.ps1 -Upload -SteamUser <account>      # build + upload (steamcmd asks for password / Steam Guard itself)
# Before the first upload, replace APP_ID and DEPOT_ID in app_build.vdf with the numbers from Steamworks.
param([switch]$Upload, [string]$SteamUser, [string]$SteamCmd = "steamcmd")
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here "..\..")
$content = Join-Path $here "content"

# Use a dotnet that has an SDK (a runtime-only dotnet on PATH can't publish)
$dotnet = $null
foreach ($c in @((Get-Command dotnet -ErrorAction SilentlyContinue).Source, (Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"), "$env:ProgramFiles\dotnet\dotnet.exe")) {
  if ($c -and (Test-Path $c) -and (& $c --list-sdks 2>$null)) { $dotnet = $c; break }
}
if (-not $dotnet) { throw ".NET 8 SDK not found: https://dotnet.microsoft.com/download/dotnet/8.0" }

if (Test-Path $content) { Remove-Item -Recurse -Force $content }
& $dotnet publish (Join-Path $repo "ClaudeDesk.csproj") -c Release -p:ApiKeyOnly=true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -o $content
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Copy-Item (Join-Path $repo "LICENSE") $content
Get-ChildItem $content -Filter *.pdb | Remove-Item
Write-Host "Built:" ; Get-ChildItem $content | Format-Table Name, Length

if ($Upload) {
  if (-not $SteamUser) { throw "-SteamUser is required for upload" }
  if ((Get-Content (Join-Path $here "app_build.vdf") -Raw) -match "APP_ID|DEPOT_ID") { throw "Set APP_ID and DEPOT_ID in app_build.vdf first" }
  & $SteamCmd +login $SteamUser +run_app_build (Join-Path $here "app_build.vdf") +quit
}
