# BloodCraftHub

Unified client-side V Rising UI mod that combines and enhances [BloodCraftUI](../LearningMods/BloodCraftUI-master) and [Eclipse](../LearningMods/Eclipse-main) into a single management UI for the [Bloodcraft](../LearningMods/Bloodcraft-main) server mod.

**Status:** scaffold. The build infrastructure compiles, but feature porting from BloodCraftUI and Eclipse is not yet done. Each subsystem file carries a `// PORT FROM:` comment pointing at the source it should be merged from.

## Why combine the two?

- **BloodCraftUI** has a polished, modular panel UI (UniverseLib + ModernUI base) — but its inbound channel is fragile regex parsing of colored chat strings.
- **Eclipse** uses Bloodcraft's structured event protocol (`[EventId]:csv` payload, HMAC-SHA256 signed) — much more robust — but its UI is in-place HUD mutation with no panel framework.

The merge: keep Eclipse's protocol as the **primary inbound data channel** and BloodCraftUI's **panel UI** as the visual layer. Chat injection is retained only for outbound commands.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full design rationale and porting plan, and [`docs/THUNDERSTORE.md`](docs/THUNDERSTORE.md) for publishing the package to Thunderstore.io (the V Rising mod repository / mod-manager source).

## Dev docs

| Doc | Purpose |
|---|---|
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Daily workflow, commit-message format, release procedure. |
| [`docs/MOD_DESIGN.md`](docs/MOD_DESIGN.md) | User-facing feature/UX spec. What the mod does, for whom. |
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
dotnet build BloodCraftHub\BloodCraftHub.csproj -c Release -p:DeployToClient=true
# or override the path:
dotnet build BloodCraftHub\BloodCraftHub.csproj -c Release `
    -p:DeployToClient=true `
    "-p:ClientPluginDirectory=D:\Games\VRising\BepInEx\plugins"
```

The default deploy path is `C:\Program Files (x86)\Steam\steamapps\common\VRising\BepInEx\plugins`.

## Layout

```
BloodCraftHub/
├── BloodCraftHub.sln
├── docs/
│   └── ARCHITECTURE.md       ← read this before porting
└── BloodCraftHub/                ← plugin project
    ├── BloodCraftHub.csproj
    ├── Manifest.props            ← generates Thunderstore manifest.json
    ├── nuget.config              ← BepInEx + Samboy feeds
    ├── thunderstore.toml         ← package metadata
    ├── Plugin.cs                 ← BepInEx entry
    ├── Core.cs                   ← runtime singleton (world / local entity / shared key)
    ├── Behaviors/                ← per-frame update host
    ├── Config/                   ← BepInEx-bound settings
    ├── Patches/                  ← Harmony patches
    │     ClientChatPatch.cs        — inbound message pump (regex + Eclipse protocol)
    │     GameManagerPatch.cs       — fires Core.Initialize once
    │     InitializationPatch.cs    — fires UI bring-up once LocalCharacter exists
    │     EscapeMenuPatch.cs        — hide/show panels w/ escape menu
    │     UICanvasSystemPatch.cs    — canvas attachment
    │     VersionStringPatch.cs     — main menu version label
    ├── Services/                 ← business logic
    │     MessageService.cs              — OUTBOUND: enqueue/inject chat commands
    │     EclipseProtocolService.cs      — INBOUND structured: MAC verification, event decode
    │     PlayerStateService.cs          — merged: BCStateService + Eclipse DataService
    │     CanvasService.cs               — in-place HUD overlays (Eclipse pattern)
    │     LocalizationService.cs         — Stunlock.Localization shim
    ├── Systems/                  ← optional ECS systems (e.g. FamiliarHealthChange)
    ├── UI/
    │     BCHubUIManager.cs            — root UI manager + PanelType routing
    │     Framework/                   ← ModernUI + UniverseLib base, folded in
    │     Controls/                    ← cells, toggles, resizeable panel base
    │     Panels/                      ← concrete panels (Familiar, Progress, Prestige, …)
    │     Overlays/                    ← Eclipse-style in-place HUD overlays
    ├── Utils/                    ← Extensions (Entity.Read/Has/Write), LogUtils
    └── Resources/
        ├── PrefabGUIDs.cs        ← static prefab GUID lookup
        ├── SecretManager.cs      ← HMAC shared-key loader
        ├── secrets.json          ← embedded HMAC key (placeholder — see file)
        └── Localization/English.json
```

## Source-of-truth references (read-only)

Both upstream mods are checked into the parent workspace as reference:

- `../LearningMods/BloodCraftUI-master/` — panel UI, ChatMessageEvent injection
- `../LearningMods/Eclipse-main/` — structured protocol, HMAC verify, in-place HUD
- `../LearningMods/Bloodcraft-main/` — server-side command surface (~97 commands across 9 groups) and the structured event channel (`NetworkEventSubType` in `EclipseService` + `LocalizationService.HandleServerReply`)

## License

TODO — pick a license (BloodCraftUI is unlicensed in-repo; Eclipse has `LICENSE.md`). If you fork code from either, respect their original licenses.
