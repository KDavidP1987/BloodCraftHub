<#
.SYNOPSIS
    Builds the 0.17.2 crash-bisect TEST variants as install-and-go mod-manager zips.

.DESCRIPTION
    For diagnosing the intermittent 0.16.x load crash with NON-TECHNICAL testers:
    each zip is a complete, importable BloodCraftHub build with ONE patch group
    compiled OFF (regardless of the user's saved config), so a tester just imports
    the zip in their mod manager and runs it — no .cfg editing, no cache clearing.

    All variants share BloodCraftHub's plugin GUID (kdpen.BloodCraftHub), so a tester
    must DISABLE their normal BloodCraftHub before enabling a test build (one toggle
    in r2modman / Thunderstore Mod Manager). Each carries a DISTINCT manifest name so
    they show up as separate mods in the manager.

    Variants produced (all version_number 0.17.2):
      Baseline   - everything on + deferred UI restore (= what 0.17.2 ships). "Does
                   the deferral alone fix it?"
      TEST-A     - input-suppression patches OFF
      TEST-B     - chat-system patches OFF (no tabbed chat / command parsing)
      TEST-C     - overlay-layering patch OFF
      TEST-D     - ALL optional patches OFF (only the mandatory init patch remains)

    Output: dist/test-variants/<name>.zip

    Leaves bin/Release on a clean NORMAL build when done.

.EXAMPLE
    .\tools\package-test-variants.ps1
#>

[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$csproj   = Join-Path $repoRoot 'BloodCraftHub\BloodCraftHub.csproj'
$dll      = Join-Path $repoRoot 'BloodCraftHub\bin\Release\net6.0\BloodCraftHub.dll'
$manifest = Join-Path $repoRoot 'BloodCraftHub\obj\Release\net6.0\manifest.json'
$icon     = Join-Path $repoRoot 'BloodCraftHub\icon.png'
$license  = Join-Path $repoRoot 'LICENSE.txt'

$verMatch = [regex]::Match((Get-Content $csproj -Raw), '<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>')
if (-not $verMatch.Success) { throw "Could not parse <Version> from $csproj" }
$version = $verMatch.Groups[1].Value

$outRoot = Join-Path $repoRoot 'dist\test-variants'
if (Test-Path $outRoot) { Remove-Item -Recurse -Force $outRoot }
New-Item -ItemType Directory -Path $outRoot | Out-Null

# variant: CrashVariant key | manifest name | one-line label | what's off (for README)
$variants = @(
  @{ Key='';          Name='BloodCraftHub_TEST0_Baseline';   Label='Baseline (all features ON, deferred UI restore)'; Off='Nothing is disabled. This is exactly what 0.17.2 ships: every feature on, but overlays now rebuild a few seconds AFTER you load in instead of during the busy login moment. Test this first -- if it no longer crashes, the deferral alone was the fix.' },
  @{ Key='NOINPUT';   Name='BloodCraftHub_TESTA_NoInput';    Label='TEST-A: input-suppression OFF';                   Off='The "don''t move / cast / open menus while typing in a BCH form or chat" patches are compiled OUT. Everything else works.' },
  @{ Key='NOCHAT';    Name='BloodCraftHub_TESTB_NoChat';     Label='TEST-B: chat-system hooks OFF';                   Off='BCH does not patch the game chat at all -- the tabbed chat window and command-reply parsing are compiled OUT. Buttons/overlays still work.' },
  @{ Key='NOLAYER';   Name='BloodCraftHub_TESTC_NoLayering'; Label='TEST-C: overlay-layering OFF';                    Off='The patch that lets overlays render BEHIND in-game menus is compiled OUT (overlays always draw on top). Everything else works.' },
  @{ Key='NOPATCHES'; Name='BloodCraftHub_TESTD_NoPatches';  Label='TEST-D: ALL optional patches OFF';                Off='Every optional game-system patch is compiled OUT (input-suppression + chat + overlay-layering). Only the mandatory startup patch remains. The UI, buttons, and overlays still build. If THIS still crashes, the trigger is not BCH''s patches.' }
)

Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($v in $variants) {
    Write-Host "`n=== Building $($v.Name)  [$($v.Label)] ===" -ForegroundColor Cyan

    # NOTE: only PluginName is overridden (no spaces -> safe on the MSBuild command
    # line). The description is left at its default; the manifest NAME + the bundled
    # README make it unmistakably a test build. (Passing a spaced PluginDescription
    # via -p trips MSB1006.)
    $dotnetArgs = @('build', $csproj, '-c', 'Release', '--nologo',
                    "-p:PluginName=$($v.Name)")
    if ($v.Key) { $dotnetArgs += "-p:CrashVariant=$($v.Key)" }

    & dotnet @dotnetArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $($v.Name)." }
    if (-not (Test-Path $dll))      { throw "DLL missing after build: $dll" }
    if (-not (Test-Path $manifest)) { throw "manifest missing after build: $manifest" }

    # Sanity: confirm the generated manifest name took.
    $mj = Get-Content $manifest -Raw
    if ($mj -notmatch [regex]::Escape($v.Name)) { throw "manifest.json did not pick up PluginName=$($v.Name):`n$mj" }

    # Stage (files at zip root, Thunderstore/r2modman layout).
    $stage = Join-Path $outRoot $v.Name
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item $dll      (Join-Path $stage 'BloodCraftHub.dll')
    Copy-Item $manifest (Join-Path $stage 'manifest.json')
    Copy-Item $icon     (Join-Path $stage 'icon.png')
    if (Test-Path $license) { Copy-Item $license (Join-Path $stage 'LICENSE.txt') }

    # Per-variant README.
    $readme = @"
# BloodCraftHub - CRASH TEST BUILD

**$($v.Label)**

This is a **diagnostic build of BloodCraftHub v$version**, not a normal release.
It exists to help track down the intermittent crash a few seconds after loading
into a server that some players hit when running BloodCraftHub alongside other
client mods.

## What is different in this build

$($v.Off)

## How to install (one extra step)

1. In your mod manager (r2modman / Thunderstore Mod Manager), **disable your normal
   BloodCraftHub** if you have it. (This build uses the same plugin ID, so only one
   can be active.)
2. **Import local mod** and pick this zip. Enable it.
3. Launch the game and play as you normally would for a few minutes.

No config editing and no cache clearing are required - the change is built in.

## What to report back

- Did the game **crash on load / shortly after loading in**, or did it run fine?
- Roughly how long you played without a crash.
- Which other client-side mods you have installed.

You can confirm you are on this build from the in-game **About** tab - it shows
``v$version`` followed by ``[$($v.Label)]``.

When you are done testing, re-enable your normal BloodCraftHub.

Thank you!
"@
    Set-Content -Path (Join-Path $stage 'README.md') -Value $readme -Encoding UTF8

    $zip = Join-Path $outRoot "$($v.Name)-$version.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
    Remove-Item -Recurse -Force $stage   # keep only the zips

    $kb = [math]::Round((Get-Item $zip).Length / 1024, 1)
    Write-Host "  -> $zip  ($kb KB)" -ForegroundColor Green
}

# Restore bin/Release to a clean NORMAL build so later normal packaging isn't a variant.
Write-Host "`nRestoring normal Release build..." -ForegroundColor Cyan
& dotnet build $csproj -c Release --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Normal restore build failed." }

Write-Host "`nDone. Test-variant zips in: $outRoot" -ForegroundColor Green
Get-ChildItem $outRoot -Filter *.zip | ForEach-Object { Write-Host "  $($_.Name)" }
