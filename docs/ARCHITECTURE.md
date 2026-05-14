# BloodCraftHub — Architecture

This document explains why the project is structured the way it is, and provides a porting checklist for migrating code from BloodCraftUI and Eclipse.

## The problem each upstream mod solved (and didn't)

| Concern | BloodCraftUI | Eclipse |
|---|---|---|
| Inbound data from server | **Fragile.** Regex parse of color-tagged chat strings in `MessageService_Processing.cs`. Breaks when other server messages interleave. | **Robust.** Bloodcraft's `EclipseService` sends a structured `[EventId]:csv` payload signed with HMAC-SHA256; client verifies and parses by field index. |
| Outbound commands | Inject local `ChatMessageEvent` ECS entity with `MessageType = Local`. Server treats it as if the player typed in chat. | Same mechanism, but signs the chat string so the server can authenticate the client. |
| UI framework | **Polished.** Hand-rolled panel system on UniverseLib + ModernUI base. Modular `PanelType` routing in `BCUIManager`. | **None.** Mutates existing game HUD prefabs in place via `CanvasService`. |
| Feature coverage | Familiars-focused (`.fam boxes`, stats). | Wide: XP bar, prestige, legacy, expertise, familiars, professions, quests, shift slot. |
| Version coupling | Pinned to Bloodcraft v1.9.7 (regex format-dependent). | Coupled by shared HMAC key + `NetworkEventSubType` schema (more stable). |
| Build hygiene | Dual-maintained version (`csproj` `<Version>` AND `Plugin.cs` `PLUGIN_VERSION`). | Single-source via `BepInEx.PluginInfoProps`. |

## Design decisions

1. **Eclipse's structured protocol is the primary inbound channel.** `EclipseProtocolService` registers with the server (`RegisterUser`), receives `ConfigsToClient` once, and then a steady stream of `ProgressToClient` updates. This drives `PlayerStateService`.

2. **Chat regex parsing is the secondary, opt-in channel.** Keep it for command responses that don't have a structured equivalent (e.g., `.fam boxes` listing, command confirmations). Keep `BCCOM_*` constants and `InterceptFlag` enum from BloodCraftUI's `MessageService_Processing.cs`. Don't try to parse anything Eclipse already gives us structurally.

3. **Outbound is unchanged: inject local `ChatMessageEvent`.** Sign with the HMAC key when the server expects a signed message (Eclipse-style); send plain otherwise. `MessageService.SendMessage(string, bool signed)`.

4. **UI uses BloodCraftUI's panel system as the primary surface.** `BCHubUIManager` extends `UIManagerBase`. New features get their own `PanelType` enum value and a class under `UI/Panels/`. The ModernUI base classes get folded into `UI/Framework/` rather than living in a sibling project.

5. **Eclipse's in-place HUD overlays remain as an optional layer under `UI/Overlays/`.** Players who liked Eclipse's XP-bar-on-the-game-HUD aesthetic can keep it; everything else lives in resizeable panels.

6. **One csproj, auto-generated `MyPluginInfo`.** No more dual-version maintenance. `Version` in the csproj is the only place to bump.

7. **Stricter init guarding.** Use Eclipse's `_initialized` boolean pattern in `Core.Initialize`. Bail re-entry instead of letting double-init corrupt state.

## Module map (which upstream file ports to which target)

When porting, search for `// PORT FROM:` markers in the scaffold files.

| Target | Pulls from |
|---|---|
| `Plugin.cs` | Both `Plugin.cs` files. Use Eclipse's `MyPluginInfo` auto-gen + `Harmony.CreateAndPatchAll(Assembly)`. Use BloodCraftUI's `IS_TESTING` switch. |
| `Core.cs` | Eclipse's `Core.cs` (singleton, local entity caches, coroutine helpers). |
| `Behaviors/CoreUpdateBehavior.cs` | BloodCraftUI's `Behaviors/CoreUpdateBehavior.cs` (list of per-frame Actions). |
| `Config/Settings.cs` | Both. Union the BloodCraftUI gating flags (`IsFamStatsPanelEnabled`, etc.) with Eclipse's `_leveling`/`_prestige`/etc. |
| `Patches/ClientChatPatch.cs` | **Both.** Single Harmony patch on `ClientChatSystem.OnUpdate`. Inside: check Eclipse `[EventId]:` prefix first → route to `EclipseProtocolService`. Otherwise fall through to regex pipeline (`MessageService_Processing.HandleMessage`). |
| `Patches/GameManagerPatch.cs` | BloodCraftUI. Calls `Core.Initialize(world)` once, then unpatches itself. |
| `Patches/InitializationPatch.cs` | BloodCraftUI. Once `LocalCharacter` is found, calls `UIManager.SetupAndShowUI()` and starts `EclipseProtocolService` registration. |
| `Patches/EscapeMenuPatch.cs` | BloodCraftUI. |
| `Patches/UICanvasSystemPatch.cs` | BloodCraftUI for panels; Eclipse for in-place HUD canvas (`Core.SetCanvas`). Merge: attach both. |
| `Patches/VersionStringPatch.cs` | BloodCraftUI. Show `BloodCraftHub vX.Y.Z` in main menu. |
| `Services/MessageService.cs` | Both. BloodCraftUI for queue + outbound ChatMessageEvent construction; Eclipse for HMAC signing. |
| `Services/EclipseProtocolService.cs` | Eclipse's `Services/DataService.cs` (parsing, MAC verify) split from state holding. |
| `Services/PlayerStateService.cs` | Merge of BloodCraftUI's `BloodCraftStateService` (familiar boxes, equipment, etc.) and Eclipse's `DataService` (XP, prestige, legacy, expertise, professions, quests). |
| `Services/CanvasService.cs` | Eclipse's `Services/CanvasService.cs` for in-place HUD overlays. |
| `Services/LocalizationService.cs` | Eclipse's `Services/LocalizationService.cs`. |
| `Systems/` | Eclipse's `Systems/FamiliarHealthChangeSystem.cs` (optional). |
| `UI/Framework/` | BloodCraftUI's `UI/UniverseLib/` + `UI/ModernLib/` + the sibling `ModernUI/` project. |
| `UI/Controls/` | BloodCraftUI's `UI/CustomLib/`. |
| `UI/Panels/` | BloodCraftUI's `UI/ModContent/*Panel.cs` + new panels for Eclipse-only features (Prestige, Profession, Quest). |
| `UI/Overlays/` | Eclipse's HUD-mutation code (XP bar, shift slot, etc.). |
| `Utils/Extensions.cs` | BloodCraftUI's `Utils/Extensions.cs` (Entity.Read/Has/Write helpers). |
| `Resources/PrefabGUIDs.cs` | Eclipse's `Resources/PrefabGUIDs.cs`. |
| `Resources/SecretManager.cs` + `secrets.json` | Eclipse's `Resources/Secrets.cs` + `secrets.json`. |
| `Resources/Localization/English.json` | Eclipse's. |

## Suggested port order

1. **Build the empty project.** Confirm `dotnet build` succeeds before adding any code. (Plugin.cs is already wired enough to load with zero patches.)
2. **Config.** Port `Settings.cs` and prove BepInEx writes the cfg file.
3. **Core + GameManager patch.** Get `Core.Initialize` firing once on world boot. Log it.
4. **MessageService outbound.** Send a `.fam boxes` from a hotkey, see it appear in chat.
5. **Eclipse protocol inbound.** Wire `SecretManager` + `EclipseProtocolService`, verify MAC on incoming `[1]:...` messages, watch logs.
6. **PlayerStateService.** Wire `EclipseProtocolService` decode → state struct → observable events.
7. **UI framework.** Fold ModernUI + UniverseLib in under `UI/Framework/`. Get a blank `BCHubUIManager` panel showing.
8. **Familiar panel.** First real panel — easiest, mostly port from BloodCraftUI.
9. **Progress/XP overlay.** Decide: panel or Eclipse-style in-place. Probably keep Eclipse's in-place look as an Overlay.
10. **Iterate on remaining systems** (prestige, legacy, expertise, profession, quest) one at a time.

## Things to be careful about

- **`Plugin.IS_TESTING`** must stay `false` for any release build.
- **HMAC secret key** in `Resources/secrets.json` must match the server. Do not commit a real key — the server admin distributes it. The placeholder in the scaffold is intentionally empty.
- **Bloodcraft server version** changes can break both the regex pipeline AND the structured protocol. Pin the supported version in `README.md` once the merge is functional.
- **Eclipse_Research_WIP** inside the BloodCraftUI repo is an abandoned exploration of the Eclipse protocol — interesting context, but the canonical implementation is in `Eclipse-main`. Don't port from `Eclipse_Research_WIP`.
- **VampireReferenceAssemblies vs VRising.Unhollowed.Client** — the scaffold uses the former (Eclipse's choice, newer). If you hit missing types, check whether the BloodCraftUI source you're porting referenced types only exposed by `VRising.Unhollowed.Client`; the fix is usually just a namespace tweak.
