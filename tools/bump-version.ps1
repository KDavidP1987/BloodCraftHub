<#
.SYNOPSIS
    Bump BloodCraftHub's version atomically across all the places it lives,
    then re-build so the deployed DLL embeds the new version.

.DESCRIPTION
    Updates:
      - <Version> in BloodCraftHub/BloodCraftHub.csproj
      - versionNumber in BloodCraftHub/thunderstore.toml
      - Prepends a "## <new> — TODO" stub entry to CHANGELOG.md
      - Runs dotnet build -c Release so the bin/Release DLL is in sync.
        (Skip with -NoBuild if you only want to update the version files.)

    Stages all three files but does NOT commit — review before committing.

    Why the auto-build: pre-0.10.7 the script left rebuilding to the user;
    v0.10.6 shipped a DLL still embedding PLUGIN_VERSION=0.10.5 because
    the bump happened AFTER the last build. The About-tab version
    surfaced 0.10.5 in-game even though every text file said 0.10.6.

.EXAMPLE
    .\tools\bump-version.ps1 -To 0.2.0
    .\tools\bump-version.ps1 -To 0.2.0 -NoBuild
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$To,
    [switch]$NoBuild
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
Write-Host "Version files bumped to $To. Staged for commit." -ForegroundColor Green

if ($NoBuild) {
    Write-Host "Skipping build (-NoBuild specified)." -ForegroundColor Yellow
    Write-Host "Remember to run dotnet build before deploying or the DLL will embed the OLD version."
} else {
    $sln = Join-Path $repoRoot 'BloodCraftHub.sln'
    Write-Host ""
    Write-Host "Rebuilding so the DLL embeds PLUGIN_VERSION=$To ..." -ForegroundColor Cyan
    & dotnet build $sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet build failed after version bump. The version files are bumped but the DLL was NOT rebuilt — fix the build, then re-run dotnet build before deploying."
        exit 4
    }
    # Sanity-check the built DLL reports the right version.
    $dll = Join-Path $repoRoot 'BloodCraftHub\bin\Release\net6.0\BloodCraftHub.dll'
    if (Test-Path $dll) {
        try {
            $asmName = [System.Reflection.AssemblyName]::GetAssemblyName($dll)
            $built = $asmName.Version
            $expected = "$To.0" # .NET appends a 4th part (revision) of 0
            if ($built.ToString() -eq $expected -or $built.ToString() -eq $To) {
                Write-Host "DLL AssemblyVersion: $built (matches $To)" -ForegroundColor Green
            } else {
                Write-Warning "DLL AssemblyVersion is $built but bump target was $To. Check that <Version> wasn't reverted in csproj."
            }
        } catch {
            Write-Warning "Could not read AssemblyVersion from $dll : $($_.Exception.Message)"
        }
    } else {
        Write-Warning "Built DLL not found at $dll — something went wrong with the build target output path."
    }
}

Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Edit CHANGELOG.md to replace 'TODO' with real notes."
Write-Host "  2. .\tools\preflight.ps1 -Mode Release"
Write-Host "  3. git commit -m `"chore(release): v$To`""
