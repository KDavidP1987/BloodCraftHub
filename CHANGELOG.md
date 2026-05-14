# Changelog

All notable changes to BloodCraftHub. Pre-1.0; commits group into phases tracked by the development conversation. Once we ship to Thunderstore, this file transitions to standard semver-tagged entries.

## 0.1.0 (unreleased) — feature build-out

The 27 commits to date deliver:
- The full Bloodcraft user/admin UI (8 tabs under BLOODCRAFT)
- The Eclipse-protocol data spine + regex fallback pipeline
- A reusable form framework (5 field types + collapsible sections)
- Live data overlays + auto-resizing main panel + hover tooltips + game-input suspension
- Documentation tab under HELP
- KINDRED group placeholder ready for the integration phases

GitHub: https://github.com/KDavidP1987/BloodCraftHub (commits viewable at /commits/main).

### Phase 1 — UI framework port (commit `17d7425`)

Copied UniverseLib, CustomLib, ModernLib, FrameTimer from `LearningMods/BloodCraftUI-master/BloodCraftUI/UI/` into `UI/Framework/`. Renamespaced `BloodCraftUI.UI.*` → `BloodCraftHub.UI.Framework.*` and `BloodCraftUI.{Utils,Behaviors,Config,Services}` → `BloodCraftHub.*`. Replaced `PluginInfo.PLUGIN_GUID` references with auto-generated `MyPluginInfo.PLUGIN_GUID`. `Settings.cs` rewritten as a static class to match the framework's call-site expectations. Behaviors/CoreUpdateBehavior uses the upstream-live static `Actions` list (not the dead ModernLib `ExecuteOnUpdate` variant). 41 files ported.

### Phase 2 — main shell (commit `d8d2f5a`)

`BCHubUIManager` extends `UIManagerBase`; owns the always-visible 40×40 `FloatingButtonPanel` (top-right, draggable) and lazily constructs `MainPanel` (600×380, centered, draggable, resizable) + `ExperienceOverlayPanel` + `FamiliarOverlayPanel` placeholders on first use. `MainPanel` has 6 tabs (Familiars/Boxes/Class/Weapon Expertise/Unarmed+Shift/Admin) and a footer toggle row for the secondary overlays. `Patches/InitializationPatch.cs` is a real Harmony patch on `CharacterHUDEntry.Awake` (fires UI bring-up) and `CommonClientDataSystem.OnUpdate` (captures `LocalCharacter`/`LocalUser`). Placeholder 256×256 `icon.png` generated via PowerShell + System.Drawing.

**Verified in-game** 2026-05-14 via Thunderstore Mod Manager — IL2CPP bindings, Harmony patches, UniverseLib panel construction all hold up under V Rising.

### Phase 3 — data pipeline (commits `0902b98`, `b89ffca`, `8f9209d`, `87c30b5`)

**Phase 3a — outbound MessageService**: `Services/MessageService.cs` partial pair with `MessageService_Processing.cs`. Public `EnqueueMessage(string)` builds a `ChatMessageEvent` ECS entity (`FromCharacter` + `NetworkEventType` + `SendNetworkEventTag` + `ChatMessageEvent`). `MessageService_Processing.cs` holds `BCCOM_*` command-string constants (single source of truth for command renames across Bloodcraft versions). `Patches/InitializationPatch.cs` wires `SetUser`/`SetCharacter` from the `CommonClientDataSystem` capture. Plugin.cs registers `ProcessAllMessages` per-frame.

**Phase 3c — Eclipse structured protocol**: `Services/EclipseProtocolService.cs` implements the signed `[ECLIPSE][N]:csv;mac<base64>` protocol (HMAC-SHA256 with the shared key from `Resources/secrets.json`). Sends `RegisterUser` handshake once `MessageService` is bound; consumes `ConfigsToClient` (server config snapshot) and `ProgressToClient` (periodic player state). `Patches/ClientChatPatch.cs` is a Harmony prefix on `ClientChatSystem.OnUpdate` that walks `_ReceiveChatMessagesQuery`, verifies MACs, decodes via `EclipseProtocolService`, and falls through to the regex pipeline for non-protocol messages. The HMAC key is the same hardcoded base64 default Bloodcraft and Eclipse ship — no admin intervention needed.

**Phase 3d — XP overlay live data**: `Services/PlayerStateService.cs` exposes typed structs + `Changed` events per substate. `ExperienceOverlayPanel` subscribes to `ExperienceChanged` and renders three labels (Level / XP% / Class).

**Bug fixes that landed mid-phase-3**:
- `8f9209d`: dropped the `System.Text.Json 9.0.5` NuGet reference. The transitive DLLs weren't bundled with our solo-DLL deploy, causing `FileNotFoundException` at `Plugin.Load`. Replaced with regex parsing of `secrets.json` (we only read one property anyway).
- `87c30b5`: removed `static readonly ComponentType[]` initializers from `EclipseProtocolService`. They called `ComponentType.ReadOnly(Il2CppType.Of<T>())` in the class's cctor, which fired during `Plugin.Load` — but `Unity.Entities.TypeManager` isn't built yet at that point. NRE crashed the plugin load. Same fields were dead code (we use `MessageService.SendRaw` for outbound) so the simplest fix was deletion.

### Phase 3b — inbound regex pipeline (folded into Phase 4f, commit `dd4b695`)

`MessageService_Processing.cs` regex state machine for `.fam boxes` and `.fam l` replies (Bloodcraft sends these as plain colored chat, not the structured protocol). Box-name regex `<color=[^>]+>(?<box>[^<]+)</color>` and box-content entry regex `<color=yellow>(?<idx>\d+)</color>\|<color=(?<color>[^>]+)>(?<name>[^<]+)</color>`. State flips on outbound (`NoteOutboundForIntercept`) and unwinds on first non-matching reply line.

### Phase 4 — tab contents (commits `0c5718e`, `c8e2eaf`, `8b1cfe1`, `dd4b695`)

**4a — full state model**: `PlayerStateService` decodes all 46 fields of `ProgressToClient` (Experience/Legacy/Expertise/Familiar/8 Professions/Daily quest/Weekly quest/Shift spell) into typed structs with per-substate Changed events. Mirrored Bloodcraft enums (`PlayerClass`, `WeaponType`, `BloodType`, `TargetType`, `WeaponStatType`, `BloodStatType`).

**4b — Familiars tab + Familiar overlay**: live active-familiar info (name/level/prestige/HP-PP-SP) + 4 action buttons (Unbind/Toggle/Combat/Prestige). Overlay shows compact version, subscribes to `FamiliarChanged`.

**4c — Class tab**: current class display + 4 read-only buttons (List Classes / List Spells / List Stats / Toggle Shift).

**4d — Weapon Expertise tab**: weapon type / level / progress / decoded bonus stat names + 5 action buttons.

**4e — Unarmed + Shift Skill tab**: shift-spell PrefabGUID display + unarmed expertise (shown when fists are equipped) + 3 action buttons.

**4f — Boxes tab + regex inbound pipeline (Phase 3b)**: collapse-on-select pattern — picker section shows the box list; clicking a box sends `.fam cb <name>` + `.fam l`, hides the picker, shows the content section with a "← Back" button. Click any familiar → `.fam b <index>` binds it.

**4g — Admin tab**: server diagnostics row (`.misc health` button) + reference list of admin commands (later migrated to forms in Phase 5e).

### Phase 5a — UI fixes (commits `537c20a`, `3ab7e0b`)

Button sizing: `AddCommandButton` switched to `flexibleWidth: 1` (buttons share row width), inner TMP gets `enableWordWrapping=false` + `overflowMode=Overflow` + center alignment + fontSize 13. Action rows changed to `childControlWidth: true` so the layout group distributes width. Worst-offender labels shortened.

`MessageService.EnqueueMessage` now sends immediately (was 2-second throttled queue). Upstream BloodCraftUI's queue tick was commented out as a `//todo` and apparently broken; we never benefited from it. Removing the throttle removes the ~2s delay on every button click.

Boxes tab refactor: collapse-on-select with a "← Back" button. Two sibling sections (`_boxesPickerSection` / `_boxesContentSection`) toggle via `SetActive`.

Boxes-tab overlap fix: switched four vertical groups from `childControlHeight: false` to `true`.

### Phase 5b — form framework + tooltips + auto-resize + input-block (commits `789e61c`, `8c98cec`, `17d7fcb`)

**Form framework**: `UI/Forms/FormBuilder.Build(parent, title, commandTemplate, fields...)` constructs title → field rows → status label + Submit. Substitutes `{field.Name}` tokens. `UI/Forms/FormField.cs` defines `TextField`, `IntField` (with Min/Max), `EnumField<T>` (auto-dropdowns from `Enum.GetNames`), `BoolField`, `PlayerNameField`. First demo form: `Admin → Set player level (.lvl set)`.

**Player-name cache**: `Services/PlayerNameCacheService.cs` SortedSet, populated from form submissions for now. `Refresh()` kicks `.clan list` (parsing wired in a later phase).

**Collapsible sections**: `UI/Forms/CollapsibleSection.cs` — accordion with ▶/▼ header. Static `Toggled` event so `MainPanel.AutoResizeIfEnabled` can re-fit.

**Hover tooltips**: `UI/TooltipHover.cs` — static service, polls mouse-over-rect each frame (no IL2CPP MonoBehaviour subclass needed; `IPointerEnterHandler` becomes a class under IL2CPP, blocking the obvious implementation). Sink is a `TextMeshProUGUI` at panel bottom, written every frame.

**Auto-resize toggle** (`Settings.IsPanelAutoResizeEnabled`, default true): `MainPanel.AutoResizeIfEnabled` walks active page + tab strip, `Math.Max(pageHeight, stripHeight)` + 76px chrome, clamps to `[MinHeight, 0.9 × Screen.height]`, sets `Rect.sizeDelta.y`. Subscribes to `CollapsibleSection.Toggled` so admin form expansion grows the panel.

**Game-input suspension**: `Patches/InputActionSystemPatch.cs` — Harmony prefix returns false (skip InputActionSystem.OnUpdate) when a focused active interactable `TMP_InputField` is the EventSystem's selected GameObject. Toggleable via `Settings.SuspendGameInputWhileTyping`. `CollapsibleSection` and `FormBuilder.Submit` auto-clear `EventSystem.current.SetSelectedGameObject(null)` to avoid stale-focus phantoms.

### Phase 5c — tab grouping refactor (commit `f3f3c6b`)

Left rail split into 3 collapsible groups: BLOODCRAFT (expanded by default, the 6 existing tabs), KINDRED (collapsed, "(coming soon)"), HELP (collapsed). Each group has a clickable header with ▶/▼ glyph. `BuildTabGroup` per group; `ToggleGroup` flips state + fires `AutoResizeIfEnabled`.

### Phase 5d — Prestige + Levels tabs (commit `a978894`)

**Prestige tab**: live current-prestige labels across 4 systems + 4 quick-action buttons + 4 collapsible forms using `EnumField<PrestigeType>` (28 values) / `EnumField<ExoformVariant>`. Validates the form framework with real EnumField usage.

**Levels tab**: read-only overview across all leveling systems — Player Experience, Blood Legacy, Weapon Expertise (with decoded bonus stats), Familiar (with HP/PP/SP), 8 professions in 4 two-column rows. Subscribes to all relevant `Changed` events.

### Phase 5e — Admin forms migration (commit `6d911db`)

All 8 remaining chat-reference lines on the Admin tab migrated to collapsible forms with `PlayerNameField` + `EnumField<T>` + `IntField` per command. Bloodcraft admin surface is now 100% form-driven.

Forms: Set player level / Set player prestige / Reset player prestige / Set blood legacy / Set weapon expertise / Set profession / Set familiar level / Refresh quests / Complete quest. New enums: `ProfessionType`, `QuestSchedule`.

### Phase 5e polish — layout-cascade fixes (commits `64e3e4b`, `d231ceb`)

Round 1:
- Tab strip vertical group switched to `childControlHeight: true` (was overlapping BLOODCRAFT submenu under KINDRED/HELP).
- `CollapsibleSection`'s outer LayoutElement no longer has a hardcoded `preferredHeight: 28` (was forcing the parent to allocate only 28px regardless of expansion state; content rendered outside the box).
- `CreateTabPage` no longer hardcodes `preferredHeight: 320` (auto-resize was clamping to 396px instead of growing).
- `AutoResizeIfEnabled` now uses a real `ComputeChildrenSumHeight` walk (sum of `LayoutUtility.GetPreferredHeight` per visible child + spacing + padding) instead of trusting the page's `LayoutElement.preferredHeight`.
- Tooltip diagnostic logs (`TooltipHover wired: ...`, one-shot `first hover registered`).

Round 2:
- `AutoResizeIfEnabled` takes `Math.Max(pageHeight, stripHeight)` so the panel grows for the tab strip too (was hiding bottom BLOODCRAFT sub-tabs behind KINDRED/HELP).
- `InputActionSystemPatch` requires `sel.activeInHierarchy && input.interactable` before blocking (was blocking forever on stale focus from collapsed forms, manifesting as "`.fam boxes` only loads when I press Enter to open game chat").
- `CollapsibleSection` collapses now call `EventSystem.current.SetSelectedGameObject(null)` proactively.
- Boxes list/content containers no longer have hardcoded `preferredHeight: 200` (auto-resize now sees the actual list length).

### Phase 5f — Quick Start Guide (commit `4f73a0e`)

`UI/ModContent/MainPanel.BuildQuickStartTab` — static documentation tab under HELP with 8 sections (Welcome / Leveling / Familiars / Classes / Weapon Expertise / Unarmed+Shift / Prestige / Tips). `AddGuideSection` helper renders heading + word-wrapped body. `EstimateHeight` heuristic gives the parent layout an honest preferredHeight so the page grows for the full guide.

### Pushed to GitHub (commit `4f73a0e` on `origin/main`)

Created public repo at https://github.com/KDavidP1987/BloodCraftHub via `gh repo create --public --source=. --remote=origin --push` after one-time `gh auth login` flow. Origin wired, main tracks origin/main, all 27 commits visible.

### Phase 5g — KindredLogistics tab

First KINDRED-group tab lands. Surveyed the KindredLogistics server mod (`LearningMods/KindredLogistics-main/`) and inventoried 28 chat commands across three command groups: 11 personal toggles (`.l <flag>`), 11 admin globals (`.lg <flag>`), and 6 un-grouped utilities (`.stash`, `.pull`, `.fi`, `.fc`, `.emptytrash`, `.adminstash`).

- `Services/MessageService_Processing.cs` — new BCCOM_KL_* constant region with all 28 command strings (zero-arg constants + `_FORMAT` templates for the 3 utility forms and `.adminstash`). Kept in its own region so the Bloodcraft constants stay isolated.
- `UI/ModContent/Data/PanelType.cs` — `KindredLogisticsTab` enum value, slotted between Bloodcraft tabs and Help.
- `UI/ModContent/MainPanel.cs` — KINDRED group's `(coming soon)` placeholder replaced with `("Logistics" → KindredLogisticsTab)`; `BuildContentArea` switch dispatches to new `BuildKindredLogisticsTab`. New `AddKLRow` helper for action rows.
- `BuildKindredLogisticsTab` layout:
  - Intro paragraph clarifying KindredLogistics dependency + personal-vs-admin scope.
  - "Personal Toggles (.l)" section — 3 rows × 4 buttons covering all 11 personal toggles + Show Settings.
  - "Utility" section — `[Stash All]` button + 3 collapsible forms (`.pull` item+qty, `.fi` item, `.fc` name).
  - "Admin Globals (.lg)" — wrapped in a single `CollapsibleSection` (collapsed by default) so non-admins can hide it. Contains 3 rows × 4 buttons covering all 11 admin toggles + Empty Trash, plus a nested collapsible `.adminstash` spawn form. Tooltips on every control.
- KindredLogistics returns no structured data, so this tab is fire-and-forget: server echoes confirmation/state into chat and the player can read it with `.l s` / `.lg s` Show Settings buttons. No `PlayerStateService` extension or regex pipeline work needed.

### Phase 5h — KindredCommands Player tab

Second KINDRED tab. Surveyed `LearningMods/KindredCommands-main/` for non-admin commands; KindredCommands' surface area is dominated by admin tools, so the player view is a tight 13-command list.

- `Services/MessageService_Processing.cs` — new BCCOM_KC_* region (10 zero-arg constants + 2 `_FORMAT` templates).
- `UI/ModContent/Data/PanelType.cs` — `KindredCommandsPlayerTab` enum value.
- `UI/ModContent/MainPanel.cs` — KINDRED group now lists `("Logistics", "Commands")`; `BuildKindredCommandsPlayerTab` lives in MainPanel and reuses `AddKLRow` for action rows.
- Layout: "Self" section (AFK, Ping, Pace), "Server info" section in two rows (Server Time, Online Staff, Open Plots, Soulshards / Boss List, Region List, Clan List), "Lookups" section with two collapsible forms (`.checklevel` with PlayerNameField, `.clan members` with TextField).
- `.clan list` is wired as a zero-arg button (server defaults to page 1). Deeper pages still require typing in chat — pagination UI is Phase 5j polish.
- KindredCommands player replies are plain chat strings; no structured-protocol parsing needed.

## Phases remaining

- **5i** — KindredCommands Admin sub-tabs (Players / Server / World). ~120 admin commands.
- **5j** — Polish: prefab name lookup for shift spell, optional ScrollRect, name-cache autocomplete wired to chat-reply parsing.
- **6** — Release: license, README + screenshots, real icon, Thunderstore upload via `tcli`.
