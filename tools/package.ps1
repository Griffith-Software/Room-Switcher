# Builds, tests and packages Room Switcher.
#   release\RoomSwitcher_v<version>.zip          the release: RoomSwitcher.exe, README, LICENSE, NOTICE, CHANGELOG, THIRD-PARTY-NOTICES
#   release\RoomSwitcher_TestKit_v<version>.zip  hardware test kit: DisplayPoc.exe + TESTING.md (not for publishing)
# Usage: powershell -ExecutionPolicy Bypass -File tools\package.ps1 [-Dotnet <path to dotnet.exe>]
param([string]$Dotnet = "")

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Dotnet) {
    $portable = Join-Path (Split-Path -Parent $root) 'build-tools\dotnet-sdk-10\dotnet.exe'
    $Dotnet = if (Test-Path $portable) { $portable } else { 'dotnet' }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup[0].Version
$release = Join-Path $root 'release'
$dist = Join-Path $root 'dist'
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $release, "$dist\app", "$dist\poc-build" | Out-Null

Write-Host "Testing..."
& $Dotnet test (Join-Path $root 'tests\RoomSwitcher.Tests') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

Write-Host "Publishing RoomSwitcher.exe..."
& $Dotnet publish (Join-Path $root 'src\RoomSwitcher.App') -c Release -o "$dist\app" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Write-Host "Publishing DisplayPoc.exe..."
& $Dotnet publish (Join-Path $root 'poc\DisplayPoc') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "$dist\poc-build" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Publish of DisplayPoc failed." }

$stage = Join-Path $dist 'RoomSwitcher'
New-Item -ItemType Directory -Force $stage | Out-Null
Remove-Item "$stage\*" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "$dist\app\RoomSwitcher.exe", (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE'), (Join-Path $root 'CHANGELOG.md'), (Join-Path $root 'NOTICE'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') $stage
$zip = Join-Path $release "RoomSwitcher_v$version.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path "$stage\*" -DestinationPath $zip
# A checksum file next to the zip, in the usual "<hash>  <file name>" form, so downloads can be checked.
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii -NoNewline

$kit = Join-Path $dist 'TestKit'
New-Item -ItemType Directory -Force $kit | Out-Null
Copy-Item "$dist\poc-build\DisplayPoc.exe", (Join-Path $root 'poc\TESTING.md') $kit
$kitZip = Join-Path $release "RoomSwitcher_TestKit_v$version.zip"
Remove-Item $kitZip -ErrorAction SilentlyContinue
Compress-Archive -Path "$kit\*" -DestinationPath $kitZip

Get-Item $zip, "$zip.sha256", $kitZip | ForEach-Object { "{0}  {1:N1} MB" -f $_.FullName, ($_.Length / 1MB) }
