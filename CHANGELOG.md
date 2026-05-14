# Changelog

## 0.1.0 — initial scaffold + UI framework + main shell

**Scaffold (commit `9fdfa56`)**
- Project skeleton with working build (`net6.0`, BepInEx 6.0.0-be.733, VampireReferenceAssemblies 1.1.11).
- `Plugin.cs` + `Core.cs` compile-clean and load on the client.
- Folder structure with `// PORT FROM:` markers for each subsystem.

**Dev structure + hooks (commit `25353c2`)**
- `tools/preflight.ps1` enforces version sync, no `IS_TESTING = true`, no leaked HMAC key, no merge markers, plus release-mode extras (CHANGELOG entry, 256x256 icon, clean tree, dotnet build).
- `tools/bump-version.ps1` atomic version bump across csproj + thunderstore.toml + CHANGELOG.
- `tools/install-hooks.ps1` wires `core.hooksPath = .githooks`.
- `.githooks/pre-commit` + `commit-msg` (POSIX sh wrappers calling PowerShell).
- `.gitattributes` forces LF on hooks/scripts/configs so shebang works on Windows.
- `CONTRIBUTING.md`, `docs/MOD_DESIGN.md`, `docs/PREFLIGHT.md`, `docs/VERSIONING.md`, `docs/THUNDERSTORE.md`.

**Phase 1: UI framework port (commit `17d7425`)**
- Bulk-copied UniverseLib, CustomLib, ModernLib, FrameTimer from BloodCraftUI into `UI/Framework/`; 41 files.
- Renamespaced `BloodCraftUI.UI.*` → `BloodCraftHub.UI.Framework.*` and `BloodCraftUI.{Utils,Behaviors,Config,Services}` → `BloodCraftHub.*`.
- `Settings.cs` rewritten as static class to match the framework's expectations; added `Overlays` section.
- `Behaviors/CoreUpdateBehavior.cs` is the IL2CPP-registered MonoBehaviour with the static `Actions` list (matches the live upstream variant).
- `UI/ModContent/Data/PanelType.cs` defines the merged enum (FamiliarsTab/BoxesTab/ClassTab/ExpertiseTab/UnarmedShiftTab/AdminTab + ExperienceOverlay/FamiliarOverlay + legacy BloodCraftUI ids).
- `Utils/Extensions.cs` ports `GetTransparent` and `Entity.Has<T>` helpers.

**Phase 2: main shell (this commit)**
- `BCHubUIManager` extends `UIManagerBase` and owns the floating button + lazy main panel + lazy overlays.
- `UI/ModContent/FloatingButtonPanel.cs` — small (40×40) top-right always-visible "BCH" button. Click toggles the main panel.
- `UI/ModContent/MainPanel.cs` — tabbed primary panel (600×380, centered, draggable, resizable). Tabs: Familiars, Boxes, Class, Weapon Expertise, Unarmed + Shift, Admin. Footer row with two toggles for the secondary overlays.
- `UI/ModContent/ExperienceOverlayPanel.cs` + `FamiliarOverlayPanel.cs` — placeholder draggable + resizable overlays.
- `Patches/InitializationPatch.cs` — real Harmony patch on `CharacterHUDEntry.Awake` to fire `Plugin.UIOnInitialize()` once the player is in-world; also patches `CommonClientDataSystem.OnUpdate` to capture `LocalCharacter` / `LocalUser`.
- `Plugin.cs` — added `UIOnInitialize()`, `GameDataOnInitialize(World)`, `EntityManager`, `LocalCharacter` to match the pattern referenced by panels and patches.
- `icon.png` — 256×256 placeholder (Thunderstore requirement met).
