# BloodCraftHub

Unified client-side V Rising UI mod that combines [BloodCraftUI](https://thunderstore.io/c/v-rising/p/panthernet/BloodCraftUI_OnlyFams/) and [Eclipse](https://thunderstore.io/c/v-rising/p/zfolmt/Eclipse/) into a single management UI for the [Bloodcraft](https://thunderstore.io/c/v-rising/p/zfolmt/Bloodcraft/) server mod.

**Repo:** https://github.com/KDavidP1987/BloodCraftHub
**Status:** v0.1.0 in development. All Bloodcraft tabs functional (8 tabs). KindredLogistics tab (5g) + KindredCommands Player tab (5h) live. KindredCommands admin sub-tabs up next. Not yet published to Thunderstore.

## What it does

- **Single floating "BCH" button** top-right of the screen → opens the main panel.
- **Tabbed primary UI** with 3 collapsible groups in the left rail:
  - **BLOODCRAFT**: Familiars, Boxes, Class, Weapon Expertise, Unarmed + Shift, Prestige, Levels, Admin
  - **KINDRED**: Logistics, Commands (KindredCommands admin sub-tabs coming)
  - **HELP**: Quick Start guide
- **Two secondary overlays** (toggle from footer): XP tracker + Familiar quick-glance. Each is an independent draggable + resizable panel.
- **Forms-driven commands** — every Bloodcraft admin command with arguments has a real form (player picker + enum dropdowns + numeric inputs + Submit). No more typing `.lvl set <Player> <Level>` in chat.
- **Live data** via Bloodcraft's structured MAC-signed protocol (the same channel Eclipse uses). Falls back to regex parsing for `.fam boxes`/`.fam l` replies.
- **Hover tooltips** for every control, surfaced in a single footer line.
- **Auto-resizing panel** that grows/shrinks to fit the active tab content (capped at 90% of screen height; toggleable).
- **Game-input suspension** while typing into form fields so WASD doesn't move your character (toggleable).

See [`docs/MOD_DESIGN.md`](docs/MOD_DESIGN.md) for the full feature/UX spec, [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for internals, [`CHANGELOG.md`](CHANGELOG.md) for the development trail.

## Why combine BloodCraftUI + Eclipse?

- **BloodCraftUI** has a polished, modular panel UI but parses inbound data via fragile regex on colored chat strings.
- **Eclipse** has the durable signed-protocol pipeline but mutates the game HUD in place, no panel framework.

We take the panel framework from BloodCraftUI, the structured protocol from Eclipse, and add a form-based admin/user UI on top.

## Dev docs

| Doc | Purpose |
|---|---|
| [`CHANGELOG.md`](CHANGELOG.md) | Full phase-by-phase history. Read this to catch up on what's been built. |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Daily workflow, commit-message format, release procedure. |
| [`docs/MOD_DESIGN.md`](docs/MOD_DESIGN.md) | User-facing feature/UX spec. |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Internal code structure and porting map from BloodCraftUI/Eclipse. |
| [`docs/VERSIONING.md`](docs/VERSIONING.md) | Semver policy, single-source-of-truth rules, how to bump. |
| [`docs/PREFLIGHT.md`](docs/PREFLIGHT.md) | Pre-commit / pre-release checklist. Enforced by `tools/preflight.ps1`. |
| [`docs/LOCAL_TESTING.md`](docs/LOCAL_TESTING.md) | How to side-load the built DLL into your V Rising client and verify it loads. |
| [`docs/THUNDERSTORE.md`](docs/THUNDERSTORE.md) | Thunderstore packaging + publishing. |

## First-time setup

```powershell
cd BloodCraftHub
.\tools\install-hooks.ps1   # wires up the pre-commit + commit-msg hooks
```

## Build

```powershell
cd BloodCraftHub
dotnet restore BloodCraftHub.sln
dotnet build BloodCraftHub.sln -c Release
```

Optional: deploy the built DLL directly to your client's BepInEx plugins folder:

```powershell
# Default target is r2modman / Thunderstore Mod Manager "Default" profile.
dotnet build BloodCraftHub\BloodCraftHub.csproj -c Release -p:DeployToClient=true

# Override the r2modman profile name:
dotnet build BloodCraftHub\BloodCraftHub.csproj -c Release -p:DeployToClient=true -p:R2ModmanProfile=MyProfile

# Or target a non-r2modman install (vanilla Steam path):
dotnet build BloodCraftHub\BloodCraftHub.csproj -c Release `
    -p:DeployToClient=true `
    "-p:ClientPluginDirectory=C:\Program Files (x86)\Steam\steamapps\common\VRising\BepInEx\plugins"
```

> **Heads up**: fully quit V Rising before redeploying. A running game file-locks the DLL and MSBuild's Copy step errors with `MSB3021`. The compile itself still succeeds — just the copy fails.

## Layout

```
BloodCraftHub/
├── BloodCraftHub.sln
├── docs/                          ← read CHANGELOG + ARCHITECTURE first
├── tools/                         ← preflight.ps1, bump-version.ps1, install-hooks.ps1
├── .githooks/                     ← pre-commit + commit-msg
└── BloodCraftHub/                 ← plugin project
    ├── BloodCraftHub.csproj
    ├── Manifest.props             ← generates Thunderstore manifest.json
    ├── thunderstore.toml          ← package metadata for tcli
    ├── Plugin.cs                  ← BepInEx entry
    ├── Core.cs                    ← runtime singleton
    ├── icon.png                   ← 256×256 placeholder (replace before release)
    ├── Behaviors/
    │     CoreUpdateBehavior.cs    ← IL2CPP-registered MonoBehaviour, per-frame loop
    ├── Config/
    │     Settings.cs              ← BepInEx-bound, static accessors
    ├── Patches/                   ← Harmony patches
    │     ClientChatPatch          ← inbound: Eclipse protocol + regex fallback
    │     InputActionSystemPatch   ← game-input suspension while typing
    │     InitializationPatch      ← UI bring-up + LocalCharacter/User capture
    │     GameManagerPatch / EscapeMenuPatch / UICanvasSystemPatch / VersionStringPatch
    ├── Services/
    │     MessageService           ← outbound chat injection (immediate send)
    │     MessageService_Processing ← BCCOM_* command constants + regex pipeline
    │     EclipseProtocolService   ← MAC-signed protocol decode + registration
    │     PlayerStateService       ← single source of truth for parsed state
    │     PlayerNameCacheService   ← name autocomplete cache
    ├── UI/
    │     BCHubUIManager           ← root, owns floating button + lazy panels
    │     TooltipHover             ← hover-tooltip polling service
    │     Framework/               ← UniverseLib + CustomLib + ModernLib (folded in)
    │     Forms/
    │         FormBuilder          ← builds form panels from FormField specs
    │         FormField            ← TextField/IntField/EnumField<T>/BoolField/PlayerNameField
    │         CollapsibleSection   ← accordion wrapper, fires Toggled event
    │     ModContent/
    │         FloatingButtonPanel  ← 40×40 BCH button (top-right)
    │         MainPanel            ← tabbed primary UI (this file holds all Build*Tab methods)
    │         ExperienceOverlayPanel / FamiliarOverlayPanel
    │     ModContent/Data/PanelType ← enum of every tab and overlay identity
    ├── Utils/                     ← LogUtils + Extensions (Entity.Read/Write/Has, color helpers)
    └── Resources/
        ├── PrefabGUIDs.cs         ← static prefab GUID lookup (stub for now)
        ├── SecretManager.cs       ← HMAC shared-key loader (regex-parses secrets.json)
        ├── secrets.json           ← embedded HMAC key (same default Bloodcraft+Eclipse ship)
        └── Localization/English.json
```

## Source-of-truth references (read-only)

Five upstream mods checked into the parent workspace as reference:

- `../LearningMods/Bloodcraft-main/` — server-side, ~97 commands across 9 groups
- `../LearningMods/BloodCraftUI-master/` — panel UI source
- `../LearningMods/Eclipse-main/` — Eclipse protocol + HMAC verify source
- `../LearningMods/KindredCommands-main/` — KindredCommands (~130+ admin commands, integrating in Phase 5h/5i)
- `../LearningMods/KindredLogistics-main/` — KindredLogistics (18 commands, integrating in Phase 5g)

## License

TODO — pick a license before Thunderstore release. BloodCraftUI is unlicensed in-repo; Eclipse has `LICENSE.md`. We've ported small portions of both (with attribution comments); resolve licensing before publishing.
