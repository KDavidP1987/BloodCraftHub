# Local testing

How to put a freshly-built `BloodCraftHub.dll` into your live V Rising client and confirm it loads. See [`THUNDERSTORE.md`](THUNDERSTORE.md) for releasing the mod publicly.

## Prerequisites

1. **V Rising** installed.
2. **BepInEx (IL2CPP build) for V Rising** installed. The easiest path is **r2modman / Thunderstore Mod Manager** — create a V Rising profile, install the `BepInExPack_V_Rising` package, and launch the game through r2modman at least once so it sets the directory up. Manual installs also work; download `BepInExPack_V_Rising` from Thunderstore and unzip into `…\steamapps\common\VRising\` so you end up with `…\VRising\BepInEx\` next to `VRising.exe`.

The mod alone won't do anything without BepInEx — it's the loader that injects plugins.

## Build and deploy in one step

`BloodCraftHub.csproj` has a `DeployToClient` flag that, when true, copies the built DLL straight into your plugins folder after a successful build.

### Default target: r2modman "Default" profile

```powershell
cd "C:\Users\KDPen\OneDrive\Documents\!CURSOR PROJECTS\Games\V Rising\BloodCraftUI 2\BloodCraftHub"
dotnet build BloodCraftHub\BloodCraftHub.csproj -c Release -p:DeployToClient=true
```

This drops `BloodCraftHub.dll` into:

```
%APPDATA%\Thunderstore Mod Manager\DataFolder\VRising\profiles\Default\BepInEx\plugins\
```

That's `C:\Users\<you>\AppData\Roaming\…` if you type the variable into Explorer.

### A different r2modman profile

If your profile is named something other than `Default`:

```powershell
dotnet build BloodCraftHub\BloodCraftHub.csproj -c Release -p:DeployToClient=true -p:R2ModmanProfile=MyProfile
```

### A non-r2modman BepInEx install (vanilla Steam path)

```powershell
dotnet build BloodCraftHub\BloodCraftHub.csproj -c Release -p:DeployToClient=true `
  "-p:ClientPluginDirectory=C:\Program Files (x86)\Steam\steamapps\common\VRising\BepInEx\plugins"
```

`-p:ClientPluginDirectory=…` always wins over the r2modman default.

### Knowing which install your game actually uses

If you launch V Rising **via r2modman** → it injects the BepInEx from that specific profile, and your DLL must be in the profile's `plugins/` folder.
If you launch **via Steam directly** → Steam-path BepInEx is what loads, and the DLL has to be in `…\VRising\BepInEx\plugins\`.

When in doubt: drop the DLL into both locations.

## Conflict-avoidance (important the first time)

You already have `panthernet-BloodCraftUI_OnlyFams` and `zfolmt-Eclipse` installed in this profile. They all use different BepInEx GUIDs so BepInEx will happily load all three — **but** they'll draw competing UI and may fight over the same Harmony patch targets, producing weird overlap or NRE spam.

For a clean first test, temporarily disable the other two:

```powershell
$plugins = "$env:APPDATA\Thunderstore Mod Manager\DataFolder\VRising\profiles\Default\BepInEx\plugins"
Rename-Item "$plugins\panthernet-BloodCraftUI_OnlyFams" "panthernet-BloodCraftUI_OnlyFams.disabled"
Rename-Item "$plugins\zfolmt-Eclipse" "zfolmt-Eclipse.disabled"
```

Rename back to re-enable. Or use r2modman's per-mod toggle in its UI (cleaner; preserves r2modman's profile metadata).

## Launch and verify it loaded

1. Launch V Rising — through r2modman if that's how your BepInEx is wired, through Steam otherwise.
2. Get to the main menu, then check the BepInEx log:

```powershell
# r2modman:
Get-Content "$env:APPDATA\Thunderstore Mod Manager\DataFolder\VRising\profiles\Default\BepInEx\LogOutput.log" -Tail 60

# Standalone (Steam) install:
Get-Content "C:\Program Files (x86)\Steam\steamapps\common\VRising\BepInEx\LogOutput.log" -Tail 60
```

Lines that mean the loader saw us:

```
[Info   :BepInEx] Loading [BloodCraftHub 0.1.0]
[Info   :BloodCraftHub] Plugin kdpen.BloodCraftHub v0.1.0 loaded.
```

The UI doesn't appear at the main menu — only after you join a server and your character HUD is constructed. Look for:

```
[Info   :BloodCraftHub] Creating BloodCraftHub UI...
[Info   :BloodCraftHub] UI Manager initialized.
[Info   :BloodCraftHub] Client world bound; game data initialized.
```

Then look top-right of your screen — the **BCH** button should be there.

## What to actually click through (Phase 2 state)

- Click **BCH** → the main panel opens, centered.
- Click each tab on the left → content area swaps. Every tab is a placeholder label right now ("Familiars — coming soon."). Real content lands in Phase 4.
- Toggle either checkbox in the footer → matching overlay panel pops up top-left. Both overlays are draggable + resizable independently.
- Drag the main panel by its title bar; drag the floating button by the small handle strip above the button face. Re-open the game and confirm positions persist — they save to `…\BepInEx\config\kdpen.BloodCraftHub.cfg`.
- Open the Esc menu — all UI hides; close the menu — UI returns.

## Iterate loop

```powershell
# 1. FULLY QUIT V Rising first - BepInEx holds the DLL file-locked while the
#    game is running, and `dotnet build -p:DeployToClient=true` will fail
#    with MSB3021 / MSB3027 "The file is locked by: V Rising (PID …)" if you
#    skip this step. Quitting to the main menu is not enough; the process
#    has to be gone (check Task Manager if unsure).
# 2. Edit code.
# 3. Rebuild + redeploy:
dotnet build BloodCraftHub\BloodCraftHub.csproj -c Release -p:DeployToClient=true
# 4. Re-launch via Thunderstore Mod Manager.
```

You do not need to restart Steam or the mod manager — just exit the game and start it again.

## Troubleshooting cheat sheet

| Symptom | Likely cause | Fix |
|---|---|---|
| `dotnet build` succeeded but no `BloodCraftHub.dll` appears in the plugins folder | `DeployToClient` wasn't true, or the path doesn't exist | Re-run with `-p:DeployToClient=true`. MSBuild creates missing directories; if the path is mistyped, the DLL lands somewhere harmless and the game doesn't see it. Verify with `Get-ChildItem` of the expected path. |
| `MSB3021 / MSB3027 "The file is locked by: V Rising (PID …)"` | The game is still running and holds the old DLL file-locked | Fully quit V Rising (not just to main menu), then re-run the build. The compile step itself succeeded — only the copy failed; the new DLL is in `bin\Release\net6.0\` either way. |
| `[BepInEx] Loading [BloodCraftHub …]` line never appears | Wrong BepInEx install is loading | Drop the DLL into both r2modman and Steam paths. Whichever V Rising actually launches with will load it. |
| Plugin loads but no UI appears | `CharacterHUDEntry.Awake` patch didn't fire — usually a V Rising version drift | Search log for `"Creating BloodCraftHub UI..."`. If absent, the patch target name has changed. Open `BloodCraftHub/Patches/InitializationPatch.cs` and adjust the `[HarmonyPatch(typeof(...), nameof(...))]` target to the new name. |
| NRE in `CommonClientDataSystem.OnUpdate` postfix | Auto-generated query field names (`__query_1840110770_0`/`_1`) changed between V Rising patches | Decompile the current `Stunlock.*` / `ProjectM.*` assembly to find the new query field names; update the postfix in `InitializationPatch.cs`. |
| UI appears but is corrupted / overlapping with Eclipse's | All three UI mods running concurrently | Disable the other two as in the section above. |
| Crash on plugin load | Most often IL2CPP type-resolution failure in a copied UniverseLib file | Grab the full stack from `LogOutput.log` and we'll fix the specific call site. |
| Build fails on `dotnet restore` after pulling | NuGet cache for the BepInEx feed went stale | `dotnet nuget locals all --clear` then `dotnet restore`. |
| Build prints transitive-dep TFM warnings | `System.Text.Json 9.0.5` etc. — net6.0 is the IL2CPP requirement, those packages target newer TFMs | Already suppressed in `BloodCraftHub.csproj` via `<SuppressTfmSupportBuildWarnings>true</SuppressTfmSupportBuildWarnings>`. If you see them again, you're on an older csproj. |

## Reading the log live while you play

Open a second terminal and tail the log:

```powershell
Get-Content "$env:APPDATA\Thunderstore Mod Manager\DataFolder\VRising\profiles\Default\BepInEx\LogOutput.log" -Wait -Tail 0
```

This is the fastest way to see what your latest edit did when you reload the game.
