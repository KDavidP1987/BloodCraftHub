<#
.SYNOPSIS
    Install the BloodCraftHub git hooks.

.DESCRIPTION
    Sets git config core.hooksPath to .githooks so the tracked hooks under
    .githooks/ are picked up automatically. Run once after cloning the repo.

    To uninstall: git config --unset core.hooksPath

.EXAMPLE
    .\tools\install-hooks.ps1
#>

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if (-not (Test-Path (Join-Path $repoRoot '.git'))) {
    Write-Error "Not inside a git repo (no .git/ found at $repoRoot)."
    exit 2
}

& git config core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) {
    Write-Error "git config failed."
    exit 1
}

# Verify pwsh is callable — the hooks run via `pwsh`.
$pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
if (-not $pwsh) {
    Write-Host "WARNING: 'pwsh' (PowerShell 7+) not found on PATH." -ForegroundColor Yellow
    Write-Host "  The hooks rely on it. Install from https://aka.ms/powershell or your package manager." -ForegroundColor Yellow
}

Write-Host "Git hooks installed (core.hooksPath = .githooks)." -ForegroundColor Green
Write-Host "  pre-commit  -> tools/preflight.ps1 -Mode Commit"
Write-Host "  commit-msg  -> Conventional-Commits-lite subject format check"
