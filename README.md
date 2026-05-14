# BloodCraftHub

Unified client-side V Rising UI mod that combines [BloodCraftUI](https://thunderstore.io/c/v-rising/p/panthernet/BloodCraftUI_OnlyFams/) and [Eclipse](https://thunderstore.io/c/v-rising/p/zfolmt/Eclipse/) into a single management UI for the [Bloodcraft](https://thunderstore.io/c/v-rising/p/zfolmt/Bloodcraft/) server mod — with first-class support for [KindredCommands](https://thunderstore.io/c/v-rising/p/odjit/KindredCommands/) and [KindredLogistics](https://thunderstore.io/c/v-rising/p/odjit/KindredLogistics/) on the same server.

**Repo:** https://github.com/KDavidP1987/BloodCraftHub
**Status:** v0.8.1 — public on Thunderstore. 16 tabs across BLOODCRAFT / KINDRED / HELP, 4 secondary overlays, every chat command from the 3 backing server mods surfaced as forms + buttons (~250+ commands).

## Screenshots

> _Screenshots go here. Drop PNGs into `docs/screenshots/` and reference them like:_
> ```markdown
> ![Main panel — Familiars tab](docs/screenshots/01-familiars.png)
> ![Admin: Players tab — Boost collapsibles open](docs/screenshots/02-admin-boost.png)
> ![Quick Start guide](docs/screenshots/03-quickstart.png)
> ```

## What it does

- **Single floating "BCH" button** top-right of the screen → opens the main panel.
- **Tabbed primary UI** with 3 collapsible groups in the left rail:
  - **BLOODCRAFT** (10 tabs): Familiars, Boxes, Class, Weapon Expertise, Blood Legacy, Unarmed + Shift, Prestige, Levels, Daily Quests, Admin
  - **KINDRED** (6 tabs): Logistics, Logistics: Admin, Commands, Admin: Players, Admin: Server, Admin: World
  - **HELP** (3 tabs): Quick Start, Vanilla Admin reference, About
- **Four secondary overlays** (toggle from footer): XP tracker, Familiar quick-glance (active stats), Familiar Browser (box switcher + bind/unbind), Daily Quest tracker. Each is independent draggable + resizable, and visibility persists across sessions.
- **Forms-driven commands** — every chat command with arguments has a real form (player picker + enum dropdowns + numeric inputs + Submit). No more typing `.lvl set <Player> <Level>` in chat.
- **Live data** via Bloodcraft's structured MAC-signed protocol (the same channel Eclipse uses). Falls back to regex parsing for `.fam boxes`/`.fam l` replies.
- **Hover tooltips** for every control, surfaced in a single footer line.
- **Scrollable tabs** — long admin tabs scroll within the viewport instead of clipping.
- **Auto-resizing panel** that grows/shrinks to fit the active tab content (capped at 90% of screen height; toggleable).
- **Game-input suspension** while typing into form fields so WASD doesn't move your character (toggleable).
- **Passive player-name autocomplete cache** that fills as the server prints names into chat.

## Installation

BloodCraftHub is a **client-side** mod. It does nothing on the server; install only on the V Rising client of the player who wants the UI.

### Via Thunderstore Mod Manager (recommended)

1. Open Thunderstore Mod Manager.
2. Select V Rising → your existing profile (or create a new one).
3. Search for **BloodCraftHub** under the Mods tab → Install.
4. Make sure **BepInExPack_V_Rising** (the dependency) is installed in the same profile.
5. Launch V Rising via the mod manager (not Steam directly).

### Via r2modman

Same flow — open r2modman, V Rising profile, search BloodCraftHub, install, launch.

### Manual install

1. Install BepInEx for V Rising (`BepInExPack_V_Rising` 1.733.2 or compatible).
2. Drop `BloodCraftHub.dll` into `<V Rising>/BepInEx/plugins/`.
3. Launch V Rising.

### First launch

When you connect to a server running Bloodcraft, a small floating **BCH** button appears top-right. Click to open the panel. Hover any control to see what it does — the description prints in the panel's bottom footer.

You don't need to be an admin to use BloodCraftHub — non-admin commands work for everyone. Admin tabs return permission errors gracefully if you're not authorized.

## Compatibility

BloodCraftHub talks to the server through chat commands and Bloodcraft's signed Eclipse-protocol channel. It was tested against these versions:

| Mod | Tested version | Role |
|---|---|---|
| **BepInEx for V Rising** | `BepInExPack_V_Rising` 1.733.2 | Loader (hard dependency) |
| **Bloodcraft** (server) | v1.13.21 | Primary integration target |
| **KindredCommands** (server) | v2.5.8 | Admin/player commands surfaced under KINDRED |
| **KindredLogistics** (server) | v1.6.0 | Personal/admin toggles surfaced under KINDRED |
| **Eclipse** (client, optional) | v1.3.13 | Source mod for the signed-protocol design; coexists, doesn't conflict |
| **BloodCraftUI / OnlyFams** (client, optional) | v1.1.0 | Source mod for the panel framework; coexists, doesn't conflict |

If the server is running a newer Bloodcraft, most things should still work — the chat-command grammar is fairly stable. If a tab silently does nothing on click, the server probably renamed a command and `MessageService_Processing.cs`'s `BCCOM_*` constants need a bump.

## Known issues

- **Tooltips occasionally don't render** on some control rows — diagnostic logging is in. If you see "TooltipHover wired" in BepInEx's `LogOutput.log` but the footer never updates while hovering, please open a GitHub issue.
- **No autocomplete dropdown yet** — the player-name cache fills passively from chat, but the form fields don't render a dropdown of known names. You still have to type the name. Planned for a follow-up release.
- **Pagination only on `.clan list`** — the other paginated commands (`.castle plotsowned`, `.search item`, etc.) accept a `page` int but don't have prev/next widgets yet. Type the command in chat for now.
- **Quitting V Rising is required before redeploy** if you're a developer iterating — the running game file-locks `BloodCraftHub.dll` and the post-build copy step errors.

For the full open-issue list, see the project's GitHub issues.

## Why combine BloodCraftUI + Eclipse?

- **BloodCraftUI** has a polished, modular panel UI but parses inbound data via fragile regex on colored chat strings.
- **Eclipse** has the durable signed-protocol pipeline but mutates the game HUD in place, no panel framework.

We take the panel framework from BloodCraftUI, the structured protocol from Eclipse, and add a form-based admin/user UI on top.

---

## For developers

### Dev docs

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

### First-time setup

```powershell
cd BloodCraftHub
.\tools\install-hooks.ps1   # wires up the pre-commit + commit-msg hooks
```

### Build

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

### Layout

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
    ├── icon.png                   ← 256×256 BCH icon
    ├── Behaviors/
    │     CoreUpdateBehavior.cs    ← IL2CPP-registered MonoBehaviour, per-frame loop
    ├── Config/
    │     Settings.cs              ← BepInEx-bound, static accessors
    ├── Patches/                   ← Harmony patches
    │     ClientChatPatch          ← inbound: Eclipse protocol + regex fallback + name harvest
    │     InputActionSystemPatch   ← game-input suspension while typing
    │     InitializationPatch      ← UI bring-up + LocalCharacter/User capture
    │     GameManagerPatch / EscapeMenuPatch / UICanvasSystemPatch / VersionStringPatch
    ├── Services/
    │     MessageService           ← outbound chat injection (immediate send)
    │     MessageService_Processing ← BCCOM_* command constants + regex pipeline
    │     EclipseProtocolService   ← MAC-signed protocol decode + registration
    │     PlayerStateService       ← single source of truth for parsed state
    │     PlayerNameCacheService   ← passive name harvest + autocomplete cache
    ├── UI/
    │     BCHubUIManager           ← root, owns floating button + lazy panels
    │     TooltipHover             ← hover-tooltip polling service
    │     Framework/               ← UniverseLib + CustomLib + ModernLib (folded in)
    │     Forms/
    │         FormBuilder          ← builds form panels from FormField specs
    │         FormField            ← TextField/IntField/EnumField<T>/BoolField/PlayerNameField
    │         CollapsibleSection   ← accordion wrapper, fires Toggled event
    │     ModContent/
    │         FloatingButtonPanel       ← 40×40 BCH button (top-right)
    │         MainPanel                 ← tabbed primary UI (Bloodcraft tabs + Quick Start)
    │         MainPanel.KindredAdmin    ← partial-class file: 3 KindredCommands admin sub-tabs
    │         ExperienceOverlayPanel / FamiliarOverlayPanel
    │     ModContent/Data/PanelType ← enum of every tab and overlay identity
    ├── Utils/                     ← LogUtils + Extensions (Entity.Read/Write/Has, color helpers)
    └── Resources/
        ├── PrefabGUIDs.cs              ← static prefab GUID lookup (stub)
        ├── PrefabNameResolver.cs       ← runtime PrefabCollectionSystem name lookup
        ├── SecretManager.cs            ← HMAC shared-key loader (regex-parses secrets.json)
        ├── secrets.json                ← embedded HMAC key (same default Bloodcraft+Eclipse ship)
        └── Localization/English.json
```

### Source-of-truth references (read-only)

Five upstream mods checked into the parent workspace as reference:

- `../LearningMods/Bloodcraft-main/` — server-side, ~97 commands across 9 groups
- `../LearningMods/BloodCraftUI-master/` — panel UI source
- `../LearningMods/Eclipse-main/` — Eclipse protocol + HMAC verify source
- `../LearningMods/KindredCommands-main/` — KindredCommands (~146 admin commands + 13 player commands surfaced under KINDRED)
- `../LearningMods/KindredLogistics-main/` — KindredLogistics (28 commands surfaced under KINDRED)

## License

[MIT](LICENSE.txt) — see `LICENSE.txt` for the full text plus third-party attribution for ported code from BloodCraftUI (panthernet) and Eclipse (zfolmt).
