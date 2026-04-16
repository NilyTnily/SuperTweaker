<#
.SYNOPSIS
  Push main + tag v1.0.0, then create GitHub Release with MSI and portable ZIP.

  Prerequisites (once per machine):
    1) winget install GitHub.cli   (or install gh manually)
    2) gh auth login

  Ensure `git remote get-url origin` points at your github.com/<owner>/SuperTweaker repo.
#>
param(
    [string] $Tag = "v1.0.0",
    [string] $Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $RepoRoot

function Get-GhExe {
    $pf86 = ${env:ProgramFiles(x86)}
    $candidates = @(
        (Join-Path $env:ProgramFiles "GitHub CLI\gh.exe"),
        (Join-Path $pf86 "GitHub CLI\gh.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\GitHub CLI\gh.exe")
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }
    $cmd = Get-Command gh -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

$gh = Get-GhExe
if (-not $gh) {
    Write-Error "GitHub CLI (gh) not found. Install: winget install --id GitHub.cli"
}

# Prefer non-interactive auth: classic PAT with `repo` scope in env GITHUB_TOKEN
if ($env:GITHUB_TOKEN) {
    Write-Host "Using GITHUB_TOKEN for gh auth (repo scope)..."
    $env:GITHUB_TOKEN | & $gh auth login --with-token 2>$null
}

cmd /c "`"$gh`" auth status >nul 2>&1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Not logged in. Either set env GITHUB_TOKEN (classic PAT, repo scope) or run: gh auth login" -ForegroundColor Yellow
    exit 1
}

$remote = (git remote get-url origin 2>$null).Trim()
if (-not $remote) {
    Write-Error "No git remote 'origin'. Add: git remote add origin https://github.com/<you>/SuperTweaker.git"
}

# owner/repo from https://github.com/owner/repo.git or git@github.com:owner/repo.git
$repoSlug = $null
if ($remote -match "github\.com[:/]([^/]+)/([^/.]+)") {
    $repoSlug = "$($Matches[1])/$($Matches[2])"
}
if (-not $repoSlug) {
    Write-Error "Could not parse owner/repo from origin: $remote"
}

Write-Host "Remote repo: $repoSlug" -ForegroundColor Green

# Ensure artifacts
$msi = Join-Path $RepoRoot "artifacts\SuperTweaker-$Version-x64.msi"
$zip = Join-Path $RepoRoot "artifacts\SuperTweaker-$Version-win-x64-portable.zip"
if (-not (Test-Path $msi) -or -not (Test-Path $zip)) {
    Write-Host "Building release artifacts..."
    & (Join-Path $RepoRoot "scripts\Build-ReleaseArtifacts.ps1") -Version $Version
}

Write-Host "Pushing main and tag $Tag..."
git push -u origin main
if ($LASTEXITCODE -ne 0) {
    if (-not $env:GITHUB_TOKEN) {
        Write-Error 'git push failed. Create empty repo SuperTweaker on GitHub, confirm origin, or set GITHUB_TOKEN (classic PAT, repo scope) and re-run.'
    }
    Write-Host "Retrying: create repo if needed, then push with token..."
    & $gh repo create $repoSlug --public --description "SuperTweaker - Windows optimization utility" 2>$null
    $authed = "https://x-access-token:$($env:GITHUB_TOKEN)@github.com/${repoSlug}.git"
    git push -u $authed main
    if ($LASTEXITCODE -ne 0) { Write-Error "git push with token failed." }
    git push $authed $Tag
    git remote set-url origin "https://github.com/${repoSlug}.git"
} else {
    git push origin $Tag 2>$null
    if ($LASTEXITCODE -ne 0) {
        git push origin $Tag --force
    }
}

$notes = "SuperTweaker $Version for Windows 10/11 x64 (Administrator). MSI = installer; ZIP = portable self-contained app."

$existing = & $gh release view $Tag --repo $repoSlug 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Release $Tag exists - uploading assets..."
    & $gh release upload $Tag $msi $zip --repo $repoSlug --clobber
} else {
    Write-Host "Creating release $Tag..."
    & $gh release create $Tag $msi $zip --repo $repoSlug --title "SuperTweaker $Version" --notes $notes
}

$url = "https://github.com/$repoSlug/releases/tag/$Tag"
Write-Host "Done. Release: $url" -ForegroundColor Green
