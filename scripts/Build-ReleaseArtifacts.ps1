<#
.SYNOPSIS
  Publishes SuperTweaker for GitHub release: MSI (per-machine) + portable ZIP (self-contained single-file folder).

.DESCRIPTION
  1. Self-contained win-x64 publish (folder) → MSI via WiX 5
  2. Self-contained single-file publish + Data/Assets → ZIP for portable use

  Prerequisites: .NET 8 SDK, WiX CLI (`dotnet tool restore` from repo root).

  Bump $Version and installer/SuperTweaker.Package.wxs Package@Version together when releasing.
#>
param(
    [string] $Version = "1.0.0",
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$RepoRoot   = Resolve-Path (Join-Path $PSScriptRoot "..")
$Project    = Join-Path $RepoRoot "SuperTweaker\SuperTweaker\SuperTweaker.csproj"
$Artifacts  = Join-Path $RepoRoot "artifacts"
$FolderPub  = Join-Path $Artifacts "publish\win-x64"
$SinglePub  = Join-Path $Artifacts "publish\win-x64-singlefile"
$WixScript  = Join-Path $RepoRoot "installer\SuperTweaker.Package.wxs"
$MsiOut     = Join-Path $Artifacts "SuperTweaker-$Version-x64.msi"
$ZipOut     = Join-Path $Artifacts "SuperTweaker-$Version-win-x64-portable.zip"

if (-not (Test-Path $Project)) {
    Write-Error "Project not found: $Project"
}

Write-Host "==> dotnet tool restore (WiX)"
Push-Location $RepoRoot
try {
    dotnet tool restore
} finally {
    Pop-Location
}

$WixExe = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"
if (-not (Test-Path $WixExe)) {
    Write-Error "WiX CLI not found at $WixExe. Run: dotnet tool restore (from repo root)."
}

Write-Host "==> Publish folder (MSI payload, ReadyToRun)"
dotnet publish $Project -c $Configuration -r win-x64 --self-contained true -o $FolderPub `
    -p:Platform=x64 -p:PublishReadyToRun=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> Publish single-file portable (self-contained + compression)"
dotnet publish $Project -c $Configuration -r win-x64 --self-contained true -o $SinglePub `
    -p:Platform=x64 `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> Zip portable folder -> $ZipOut"
if (Test-Path $ZipOut) { Remove-Item $ZipOut -Force }
$tempZip = "$ZipOut.tmp.zip"
if (Test-Path $tempZip) { Remove-Item $tempZip -Force }
Compress-Archive -Path (Join-Path $SinglePub "*") -DestinationPath $tempZip -Force
Move-Item -Path $tempZip -Destination $ZipOut -Force

Write-Host "==> WiX MSI -> $MsiOut"
$folderResolved = (Resolve-Path $FolderPub).Path
& $WixExe build $WixScript -bindpath "PublishDir=$folderResolved" -o $MsiOut
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Done. Outputs:"
Write-Host "  MSI:       $MsiOut"
Write-Host "  Portable:  $ZipOut"
Write-Host "  (folder)  $SinglePub"
