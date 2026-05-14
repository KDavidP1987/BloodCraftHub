<#
.SYNOPSIS
    Bump BloodCraftHub's version atomically across all the places it lives.

.DESCRIPTION
    Updates:
      - <Version> in BloodCraftHub/BloodCraftHub.csproj
      - versionNumber in BloodCraftHub/thunderstore.toml
      - Prepends a "## <new> — TODO" stub entry to CHANGELOG.md

    Stages all three files but does NOT commit — review before committing.

.EXAMPLE
    .\tools\bump-version.ps1 -To 0.2.0
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$To
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if ($To -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    Write-Error "Target version '$To' is not a 3-part semver (e.g. 0.2.0)."
    exit 2
}

function Get-Version-CsProj($path) {
    $m = [regex]::Match((Get-Content $path -Raw), '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>')
    if (-not $m.Success) { throw "Could not read <Version> from $path" }
    $m.Groups[1].Value
}

function VersionToInt($v) {
    $p = $v.Split('.')
    return [int64]$p[0] * 1000000 + [int64]$p[1] * 1000 + [int64]$p[2]
}

$csproj = Join-Path $repoRoot 'BloodCraftHub\BloodCraftHub.csproj'
$toml   = Join-Path $repoRoot 'BloodCraftHub\thunderstore.toml'
$changelog = Join-Path $repoRoot 'CHANGELOG.md'

$current = Get-Version-CsProj $csproj
Write-Host "Current version: $current"
Write-Host "Target version:  $To"

if ((VersionToInt $To) -le (VersionToInt $current)) {
    Write-Error "Target version $To is not greater than current $current."
    exit 3
}

# 1. csproj
$csContent = Get-Content $csproj -Raw
$csNew = [regex]::Replace($csContent, '<Version>\s*[0-9]+\.[0-9]+\.[0-9]+\s*</Version>', "<Version>$To</Version>")
Set-Content -Path $csproj -Value $csNew -NoNewline

# 2. thunderstore.toml
$tomlContent = Get-Content $toml -Raw
$tomlNew = [regex]::Replace($tomlContent, '(versionNumber\s*=\s*)"[0-9]+\.[0-9]+\.[0-9]+"', "`$1`"$To`"")
Set-Content -Path $toml -Value $tomlNew -NoNewline

# 3. CHANGELOG.md — prepend a stub entry.
$existing = if (Test-Path $changelog) { Get-Content $changelog -Raw } else { "# Changelog`n" }
# Skip prepending if an entry for this version already exists at the top.
if ($existing -match "(?m)^##\s+$([regex]::Escape($To))\b") {
    Write-Host "CHANGELOG.md already has an entry for $To — leaving as is."
} else {
    $stub = "## $To — TODO`n`n- TODO: describe what changed.`n`n"
    # Insert after the first '# Changelog' heading if present, else at top.
    if ($existing -match '^(#\s+Changelog[^\n]*\n+)') {
        $head = $matches[1]
        $body = $existing.Substring($head.Length)
        $new = $head + $stub + $body
    } else {
        $new = "# Changelog`n`n" + $stub + $existing
    }
    Set-Content -Path $changelog -Value $new -NoNewline
}

& git add $csproj $toml $changelog | Out-Null

Write-Host ""
Write-Host "Version bumped to $To. Staged for commit." -ForegroundColor Green
Write-Host "Next steps:"
Write-Host "  1. Edit CHANGELOG.md to replace 'TODO' with real notes."
Write-Host "  2. .\tools\preflight.ps1 -Mode Release"
Write-Host "  3. git commit -m `"chore(release): v$To`""
