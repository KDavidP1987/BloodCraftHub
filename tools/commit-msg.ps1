<#
.SYNOPSIS
    Validate that a commit message subject follows the Conventional-Commits-lite
    format. Called from .githooks/commit-msg.

.DESCRIPTION
    Pattern: type(scope)?!?: subject
      types: feat fix chore docs refactor test build ci perf style revert
    Empty / Merge / Revert / fixup! / squash! messages are passed through.
#>

param([string]$MessageFile)

if (-not $MessageFile -or -not (Test-Path $MessageFile)) { exit 0 }

$lines = Get-Content $MessageFile -ErrorAction SilentlyContinue
if (-not $lines) { exit 0 }

$first = $lines[0]
if ([string]::IsNullOrWhiteSpace($first))           { exit 0 }
if ($first -match '^(Merge|Revert|fixup!|squash!)') { exit 0 }

$pattern = '^(feat|fix|chore|docs|refactor|test|build|ci|perf|style|revert)(\([^)]+\))?!?:\s.+'
if ($first -match $pattern) {
    if ($first.Length -gt 100) {
        Write-Host "commit-msg: subject longer than 100 chars - keep it tight." -ForegroundColor Yellow
    }
    exit 0
}

Write-Host ""
Write-Host "commit-msg: subject does not match the Conventional-Commits-lite format." -ForegroundColor Red
Write-Host "  Got:      $first"
Write-Host "  Expected: type(scope)?: subject"
Write-Host "  Types:    feat fix chore docs refactor test build ci perf style revert"
Write-Host "  Examples: feat(ui): add familiar stats panel"
Write-Host "            fix(protocol): reject invalid MAC silently instead of throwing"
Write-Host "            chore(release): v0.2.0"
Write-Host ""
Write-Host "Bypass once with: git commit --no-verify -m '...'"
exit 1
