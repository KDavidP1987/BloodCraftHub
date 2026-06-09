# Lessons Learned — v0.1.1 → v0.8.1

Captured during the post-0.1.0 development sweep that took BCH from "first
release prep done, never shipped" to "v0.8.1 public on Thunderstore +
GitHub". Every entry below cost real debugging time at least once — write
them on the wall before they cost it again.

## V Rising / Unity / TMPro gotchas

### TMPro fallback font lacks several common Unicode glyphs

V Rising's TMPro font ships with a partial fallback set. **Tested-OK glyphs:**
`←` `→` (LEFTWARDS / RIGHTWARDS ARROW, U+2190 / U+2192), `★` (U+2605),
`⚔` (U+2694), `•` (U+2022).

**Tested-missing glyphs (render as blank squares):** `◄` `►` (BLACK
LEFT/RIGHT-POINTING POINTER, U+25C4 / U+25BA), `↻` (CLOCKWISE OPEN
CIRCLE ARROW, U+21BB).

If you need an icon-style glyph, prefer the proven-working set or use a
literal text label ("Reload" beat `↻` in the Familiar Browser header). Same
goes for hint strings inside a panel — don't reference a glyph the user
can't see ("click ↻ to refresh" was meaningless when ↻ was a square).

### `InputActionSystem.OnUpdate` Harmony prefix returning `false` wedges UI input

Suspending V Rising's gameplay input by skipping its `InputActionSystem.OnUpdate`
also wedges Unity's UI input pipeline — clicks on our own panel stop registering,
the user can't even toggle the suspend setting back off. This bit us four times
(0.1.1 added it on, 0.1.2 narrowed scope to BCH-owned fields, 0.1.3 forced
default-off + force-disabled-on-load to stop trapping users, **0.8.2 removed
the feature entirely** after friend-testing surfaced that even with all the
guards, some testers still got fully locked games and had to force-quit).

Eclipse-main's reference patch (`Patches/InputActionSystemPatch.cs`) is a
**postfix observer** that never blocks the system — confirming the prefix-
return-false approach is inherently incompatible with V Rising's input pipeline.
A re-implementation would need a completely different patch target: capturing
input at the key-read consumer level (or filtering the InputState component
after the system writes it), not skipping the system update.

Until that redesign happens, the feature is gone. The `SuspendGameInputWhileTyping`
config key is no longer registered; stale entries in user .cfg files are inert.

### Init order: `CharacterHUDEntry.Awake` fires BEFORE `MessageService` binds

`InitializationPatch` calls `Plugin.UIOnInitialize` from `CharacterHUDEntry.Awake`
postfix. `CommonClientDataSystem.OnUpdate` is what calls
`MessageService.SetUser`/`SetCharacter` — and that has a guard that requires
`Plugin.UIManager.IsInitialized` first. So during `UIOnInitialize`,
`MessageService.IsInitialized` is **false**.

Pre-0.6.0 this didn't matter (overlays were always toggled-on by the user, well
after init). 0.6.0's overlay-restore-on-login feature exposed it: the
Familiar Browser overlay's inline `if (MessageService.IsInitialized) auto-pull`
silently skipped, so restored overlays opened to empty lists with no fetch.

**Pattern for any code that needs MessageService at construct-time:** register
a per-frame ticker (`CoreUpdateBehavior.Actions.Add`) that polls
`MessageService.IsInitialized`, fires the deferred work once it's true, then
unregisters itself. See `FamiliarBrowserOverlayPanel.TickDeferredAutoPull`.

### Vanilla V Rising admin commands are CONSOLE commands, not chat commands

`adminauth`, `BanUser`, `Kick`, `give`, `giveset`, `SpawnUnit`, `Banhammer`,
`Save`, `List`, `Help`, `Connectinfo` etc. are typed into the in-game console
(default key F1), not the chat box. They use a different input pipeline.

BCH only sends chat messages (via `ChatMessageEvent` ECS entities). It cannot
trigger console commands directly. Workarounds:

- KindredCommands re-implements most common admin actions as chat commands
  (`.kick`, `.ban`, `.unban`, `.give`, `.spawnnpc`, `.teleport`) — wire those
  in the Kindred admin sub-tabs and point users there.
- For the rest, document the console commands and explain the F1 workflow
  (see `BuildVanillaAdminTab` in `MainPanel.cs`).

### Unity Slider's click-on-track doesn't fire in our canvas hierarchy

`Slider.OnPointerDown` is supposed to set the value when you click anywhere
on the bar. In our nested canvas/scroll setup it only fires for the handle —
the bar is dead. Fix: per-frame `SliderClickRegistry.TickClickOnTrack` that
detects mouse-down within a registered slider's rect (excluding the handle)
and computes the value directly. See `AutoSliderScrollbar.cs`.

### TMP_Dropdown's outside-click blocker doesn't dismiss in our hierarchy

Same root cause class as the slider issue. `TMP_Dropdown` creates a transparent
"Blocker" GameObject when expanded that's supposed to close the dropdown when
the user clicks outside, but our panel renders above it in sort-order. Fix:
`FormDropdownRegistry.TickCloseOnOutsideClick` — registers every dropdown we
create, per-frame check for outside-click, manual `Hide()`. See
`UI/Forms/FormField.cs`.

### Layout: fixed `preferredHeight` is the recurring foot-gun

A `LayoutElement.preferredHeight` set on a container with dynamic content
makes the parent reserve only that amount, and the actual content overflows
on top of siblings. Repeatedly bit us — `CollapsibleSection`'s 80px content
slot, `FormBuilder`'s `70 + fields*34` heuristic, etc.

**Default: don't set `preferredHeight` on dynamic-content containers.** Let
the inner `VerticalLayoutGroup` auto-compute, then `AutoResize` measures the
actual `LayoutUtility.GetPreferredHeight` and grows the panel to fit.

### Server-side toggles can't be "remembered" client-side

`Toggle Emotes` (`.fam e`), `Toggle Combat` (`.fam c`), `Toggle Shift`
(`.class shift`), `Toggle XP Log` (`.lvl log`), all the `.l ss / cr / dpl /
...` Logistics toggles — every one flips a flag the **server** owns, then
reports the new state in chat. The client never sees the underlying value.

Don't try to mirror them in `Settings.cs`. Document the limitation in a UI
note (Levels tab Player Tools section already does this) so users don't
expect persistence.

### Bloodcraft `.fam ub` doesn't actually delete familiars

Despite "unbind" sounding like the lighter operation, `.fam ub` destroys the
in-world entity — but the box record (level/prestige/shiny) is preserved.
You can re-bind from the box afterward.

The truly destructive command is `.fam r [N]` which removes the familiar
from the collection permanently. Don't conflate them. Our 0.2.1 release
unwound the wrong labels — "Unbind" is `.fam ub` (releases the active
entity), "Permanently Delete" is `.fam r` (collection-level deletion).

### Bloodcraft's `.fam b` won't bind if you have an active familiar — even a dismissed one

`HasActiveFamiliar()` only returns false when the entity is **destroyed**.
`.fam t` (toggle/dismiss) doesn't free the slot; the familiar is still
"active". So switching familiars requires `.fam ub` (destroy) → `.fam b N`.

There's no non-destructive switch. The Boxes tab and Familiar Browser
overlay surface this with a two-click confirm + clear messaging — never
silently destroy.

### The 0.16.x intermittent load crash is an upstream Il2CppInterop GC bug

Some players crashed a few seconds after loading into a server on 0.16.0/0.16.1;
others (incl. the dev) never did. The fault is
`Il2CppInterop.Runtime.Injection.Hooks.GarbageCollector_RunFinalizer_Patch` — a
since-removed-upstream interop GC-finalizer hook — with **no managed exception in
BCH's log**. It's non-deterministic and environment-specific: BCH alone is stable,
but it shows up for players running BCH **plus other client mods**, where the
combined IL2CPP allocation/finalizer churn in the busy login window tips the latent
bug. A diagnostic build with **every feature defaulted off still crashed**, which
ruled out the toggleable features (recipes, shift icon, overlay layering) and
pointed at BCH's **always-on Harmony patches** (applied at load regardless of any
setting). It is NOT the same fault as the BCH+Eclipse crash (that one is in
Eclipse's `CanvasService` BufferLookup and IS in the stack — see the v0.16/v0.17
crash-investigation memory).

**0.17.2 mitigations (two levers, not a proven fix):**

1. **Selective patch manifest** — `Plugin.ApplyPatches` replaced
   `CreateAndPatchAll(Assembly)`. `InitializationPatch` is always applied;
   `ClientChatPatch`, the five input-suppression patches, and `UICanvasSystemPatch`
   are each gated behind a `[Compatibility]` config switch. Switching one off means
   the Harmony detour is **never installed** (not a no-op prefix), so an affected
   tester can bisect which always-on group triggers the crash. NOTE: the complete
   actively-patching inventory is those classes — `EscapeMenuPatch`,
   `VersionStringPatch`, `GameManagerPatch` carry no live `[HarmonyPatch]` targets,
   and there are NO Harmony patches in the vendored UniverseLib framework. If you
   add a new patch class, add it to `ApplyPatches` or it won't be applied.
2. **Deferred overlay restore** — `RestoreOverlaysFromSettings` + scanner init moved
   off the spawn frame to a quiet one `UiBuildDelaySeconds` later (default 3, 0 =
   legacy). `SetupAndShowUI` (canvas + launcher) stays synchronous because it sets
   `IsInitialized`, which `CommonClientDataSystem_OnUpdate_Postfix` needs to capture
   `LocalCharacter`/`LocalUser`.

**The actual root fix is user-side:** delete `BepInEx/interop` + `BepInEx/cache`
(they rebuild) and update the BepInEx (V Rising) pack — newer interop dropped the
buggy finalizer hook.

### Logout → exit-to-desktop crash: reset session state on `ClientBootstrapSystem.OnDestroy`, never touch UI in a teardown hook (0.18.1)

**Symptom:** with BCH installed, choosing **Leave Game** crashed the client straight
to desktop instead of returning to the main menu. BCH-only, deterministic on logout.
Cost ~5 failed fix attempts — write the conclusions on the wall.

**Root cause (the non-obvious part):** when the player leaves the game, ProjectM
disposes the client `World`, but BCH's per-frame ECS code keeps ticking for a frame
or two against the **disposing world**. Reading it (`ToEntityArray`, `EntityManager`
access, etc.) is a **NATIVE crash that no managed `try/catch` can catch** — the same
interop-fault class as the 0.16.x crash, and there is **no managed trace in the log**
(confirm via `Player.log` at `%LOCALAPPDATA%Low\Stunlock Studios\VRising\Player.log`,
not BepInEx's `LogOutput.log`; the BepInEx log just stops). An earlier build happened
not to crash only because an unrelated init *deadlock* left `MessageService`
uninitialized, so all that per-frame code was dormant — masking the bug while ALSO
blanking all overlay data.

**What did NOT work (don't retry these):**
- Patching `EscapeMenuView.OnDestroy` to tear down / `UIManager.Reset()` — doing
  **GameObject/UI work during teardown** is itself a native crash. `EscapeMenuPatch`
  is now a permanent empty stub; do **not** re-hook that method.
- Wrapping the per-frame postfixes in `try/catch` — necessary but insufficient (the
  crash is native, uncatchable).
- Adding `World.IsCreated`/`.Exists()` guards to only the two chat/data patches —
  insufficient, because **all** the `CoreUpdateBehavior` services were still live.

**The fix:** patch **`ClientBootstrapSystem.OnDestroy`** (prefix) — the SAFE
leave-game teardown signal Eclipse itself patches, and which `Player.log` shows
firing as a clean step. In it, reset BCH session state with **pure field assignments
only** (no UI, no ECS, no GameObject work, so the prefix can't crash):
`MessageService.Destroy()` (sets `IsInitialized=false` → every per-frame service
gates out), `EclipseProtocolService.Reset()`, `Core.Reset()`, `Plugin.ResetGameData()`
(nulls `Plugin._client` so `IsClientNull()` is true → any straggler ECS access throws
a *managed* NRE that `CoreUpdateBehavior` catches, instead of native-crashing; and lets
`GameDataOnInitialize` re-bind the next world on relog). This makes the whole mod
dormant the instant the world tears down — cleanly, not via a deadlock — then re-inits
on the next login.

**Corollary — overlays lingering over the main menu (Issue 1).** The UIManager + its
canvas are `DontDestroyOnLoad`, so they survive leaving the game; nothing was hiding
them, so every overlay + the floating launcher stayed drawn over the main menu. You
**can't** hide them in the `OnDestroy` hook (GameObject work = crash). Pattern that
works: the hook sets a **pure flag** (`UIManager.RequestHideForLogout()`); a
`CoreUpdateBehavior` tick (`TickPendingHide`, which keeps ticking in the main menu)
does the actual `SetActive(false)` on the **next frame, after teardown completes**,
where GameObject toggles are safe. Re-show on relog routes through
`CharacterHUDEntry.Awake → UIOnInitialize → RestoreAfterRelogIfNeeded` (the
`Awake` postfix no longer early-outs on `IsInitialized`); restore is **config-driven**
(`RestoreOverlaysFromSettings` reads the persistent `Settings.Show*` flags, since
`SetActive(false)` clears each panel's live `Enabled`), and is gated on a `_loggedOut`
flag so a HUD `Awake` that isn't a relog can't thrash the overlays.

**Corollary — per-session static state leaks across a server-switch (0.18.1).** Once
logout works, a player can hop server→server **without restarting the game**, so the
process (and every `static` field) lives on. Any per-session detection/handshake state
must be reset in this same teardown hook or it leaks. We hit this with Beelzebub: the
static `BeelzProtocolService.DetectionGaveUp` flag (set after ~4 unanswered
`.beelz api version` probes on a non-Beelzebub server) stuck, so `Tick` hit
`if (DetectionGaveUp) return;` and never re-probed — a Beelzebub server reached via
server-switch showed the tab group permanently "Unavailable"
(`IsTabGroupAvailable` = `IsPresent || !DetectionGaveUp`). Fix: a `BeelzProtocolService.Reset()`
(+ `BeelzState.Reset()`) called from the `OnDestroy` hook, mirroring the existing
`EclipseProtocolService.Reset()` (which already clears `RegistrationGaveUp` + feature
flags for the same reason). **When you add any client-side server probe / handshake,
add its reset to the `ClientBootstrapSystem.OnDestroy` teardown list** — pure field
resets, no event fires (the UI re-gates on relog when detection re-runs).

**General rule:** in any V Rising client-world teardown hook, do **pure field resets
only**; defer all UI/GameObject/ECS work to a normal frame (a `CoreUpdateBehavior`
tick), which runs after the disposing world is gone.

### The game's input pipeline is an IInputContext stack — join it, don't fight it (0.25.0)

Three attempts (0.16–0.18) tried to stop game keybinds firing while typing into BCH
fields, and each failed for a structural reason that only became clear after
reflecting over the interop assemblies:

- **Skipping/detouring the input + menu ECS systems** — partially worked, but the
  three menu-system detours were the 0.16.x load-crash trigger and were removed.
- **Draining `OpenMenuEvent`/`GoToHUDMenu` request entities** — only catches menus
  that go through request entities. B/M/K/J, the wheels, action-bar/shapeshift/
  emote/admin hotkeys are **direct `ButtonInputAction` reads** — the drain never
  saw them. This is why menus kept opening mid-typing in the field reports.
- **Writing a `BlockInputState` component on an entity (0.18.2)** — structurally
  wrong. `BlockInputState` is **not ECS data**; `HasComponent<BlockInputState>`
  threw "Unknown Type" forever because no entity carries it.

**How input actually flows:** `InputActionSystem` dispatches every frame to an
ORDERED STACK of `ProjectM.IInputContext` consumers (`InputContextOrder`:
ChatInput=99 … HUDMenu=502, MenuInput=600, ActionWheel=700, ActionBar=900,
Camera=1000, Gameplay=1001). Each context receives `HandleInput(InputState)` and
reports `GetConsumedInputs(ref BlockInputState)`; actions consumed by a higher
(lower-numbered) context are filtered from everything below. **The native chat's
typing lock is just a nested `ChatFocusedInputContext` registered at order 99.**

**The fix that works (`Patches/TypingInputLock.cs`):** inject a managed class
implementing `IInputContext` (Il2CppInterop `RegisterTypeOptions.Interfaces`;
byref struct params ARE supported — `IsTypeSupported` unwraps `IsByRef`), register
it once per world via the public `InputActionSystem.AddInputContext(ctx, world,
100)` API, and have `GetConsumedInputs` consume every `ButtonInputAction` except
the `Menu_*` UI-navigation range while `ShouldBlockMenus()` is true. No Harmony
detour anywhere near the hot menu systems; the dying world unregisters it
automatically (drop the refs in the `ClientBootstrapSystem.OnDestroy` hook).

**Reusable knowledge:** anything that needs to eat/observe game input (typing
locks, click-through guards, scroll-zoom-over-UI suppression) should be an
`IInputContext` in this stack — `ChatHoveredInputContext` (what stops chat-window
scroll from zooming the camera) shows Stunlock uses the same tool for hover.

**0.25.0 (dev cycle, attempt 3) — NEVER hand the game a vtable that calls back into managed code with
struct parameters.** The cycle's second attempt — an injected `IInputContext` implementation (ClassInjector
with `RegisterTypeOptions.Interfaces`) registered fine — and then the game
NATIVE-CRASHED (instant close, nothing in the BepInEx log) the moment
`InputActionSystem` first dispatched into it, on the menu/connect screen. The
interface's callbacks take a by-value `InputState` struct and a by-ref
`BlockInputState` struct; Il2CppInterop's `IsTypeSupported` ACCEPTS that shape, but
the generated native→managed trampoline does not survive the actual call —
**signature acceptance is not marshaling correctness**, and no amount of try/catch
in the managed bodies helps because the crash happens at/around the call boundary.
The fix that works: register an instance of a GAME-IMPLEMENTED context instead —
`ClientChatSystem.ChatFocusedInputContext` (public parameterless ctor, stateless,
consumes the native-chat blocking set unconditionally) — and gate by
ADDING/REMOVING it from the stack on blocking edges, exactly how the native chat
itself gates it. Native code end-to-end; the dispatcher never calls BCH code.

**0.25.0 dev-cycle corrections — what the first in-game test caught:**

- **`GetExistingSystemManaged<InputActionSystem>()` on the BCH-bound client world
  returns NULL** — the system lives in a different world — and because `ias == null`
  was the one retry branch with no log line, 0.25.0's registration silently looped
  forever and the context never entered the stack. The tester's whole session ran
  on the legacy protections; the BepInEx log's tell was *zero* `[TypingLock]` lines
  of any kind. Two lessons: (a) get game systems from an **injected reference on a
  system you already touch** (every input consumer carries an `_InputActionSystem`
  property; we capture it from `GameplayInputSystem.__instance` in a prefix we
  already own — right instance, right world, no lookup), and (b) **never leave a
  retry path silent** — every early-return that can persist needs at least a
  Diagnostic line, or a failed feature looks identical to a working one.
- **BCH-chat typing was being protected by the NATIVE chat gate, not by BCH.** The
  Enter-takeover leaves the native chat open (hidden) while typing, so the native
  `ChatFocusedInputContext` (order 99) did the blocking — which masked the dead
  context during chat tests. A *mouse-click* focus of the BCH chat never opens the
  native chat, so that path had no real protection until the context actually
  registers.
- **Console keybindings (`keybinding create` — the "admin hotkeys") bypass the
  IInputContext stack entirely.** `ConsoleKeybinding_Unity.CheckIfPressed` reads
  `ButtonControl`s raw, which is why those binds fire even while the NATIVE chat
  has focus. The sanctioned gate is `StunConsole.UI.EnableKeybindingUpdates`:
  the game's own `DisableConsoleKeybindingsOnFocus` component writes it `false`
  every frame its field is focused, and `ResetConsoleKeybindingsSystem` re-arms it
  `true` every frame — so the correct (self-healing) usage is "keep writing false
  while blocking, never write true."

## Process gotchas

### Audit-via-agent is unreliable — verify with grep

Both the Bloodcraft and KindredCommands "command coverage" audits done by
Agent calls produced **lots of false positives** (commands flagged "missing"
that were actually wired). The agent doesn't reliably cross-reference the
constants list it's given; it scans both sides separately and reports
mismatches.

**Always verify agent gap-audit findings with a direct `grep` against the
source.** Pattern:

```bash
# Extract every [Command(name:...)] across the source
grep -ohE '\[Command\("[^"]+"' Commands/*.cs | grep -ohE '"[^"]+"' | sort -u

# Then cross-check against your BCCOM_* constants manually.
```

Also watch for **commented-out commands** — both sources have entire blocks
of `//[Command(...)]` or `/*[Command(...)]*/` that grep happily includes
unless you filter them out.

### `Edit` with `replace_all: true` on a generic substring is dangerous

In 0.8.0 I used `replace_all` on `minHeight: 22, preferredHeight: 24,
flexibleHeight: 0` to shrink one specific slot in the Familiar Browser. That
string appeared in 3 places — it shrank all of them. Two were intended, one
caused a regression (button rows shrunk too small).

Be specific. Find unique surrounding context, or do multiple narrow Edits.

### Conventional-commits hook will reject `release: ...` subjects

The repo's `commit-msg` hook expects `type(scope)?: subject`. Allowed types:
`feat fix chore docs refactor test build ci perf style revert`. Use
`chore(release): vX.Y.Z` for release commits.

### Format-string constants vs FormBuilder template tokens

We have constants like `BCCOM_KL_PULL_ITEM_FORMAT = ".pull {0} {1}"` —
classic `string.Format` placeholders. But `FormBuilder.Build` uses **named**
tokens (`.pull {item} {quantity}`) substituted via `template.Replace("{" +
field.Name + "}", ...)`.

The two are incompatible. The `_FORMAT` constants ended up unused —
documentation-only — because the forms hardcode the named-token version
inline. Don't mistake "constant exists" for "constant is wired"; check
references with `grep -rn BCCOM_NAME`.

## Architecture notes worth remembering

### Where to add a new chat command

1. Add `BCCOM_*` constant in `Services/MessageService_Processing.cs`.
   Group with related commands; comment any non-obvious format quirks.
2. Wire the UI in the appropriate tab build method
   (`MainPanel.cs` for BLOODCRAFT/HELP, `MainPanel.KindredAdmin.cs` for
   KINDRED admin sub-tabs).
3. For arg-taking commands, use `FormBuilder.Build` with named tokens
   matching `FormField.Name` values. For destructive ones, add a
   `BoolField(..., requireTrue: true)` confirm gate.
4. For commands that should refresh related state after submit, pass an
   `onSubmitted: () => EnqueueOrWarn(...)` callback to FormBuilder.

### Where to add a new tab

1. Add a value to `UI/ModContent/Data/PanelType.cs`.
2. Add the entry to the appropriate `TabGroupDef` in `MainPanel.cs`'s
   `TabGroups` array.
3. Add a dispatch case to the switch in `BuildContentArea` so
   `BuildXxxTab(page)` runs for the new type.
4. Implement `BuildXxxTab(GameObject page)` — model on existing tabs.

### Where to add a new overlay

1. Add a value to `PanelType.cs`.
2. Implement `XxxOverlayPanel : ResizeablePanelBase` — model on
   `FamiliarBrowserOverlayPanel.cs` for a stateful overlay with subscriptions
   or `ExperienceOverlayPanel.cs` for a simple read-only display.
3. Wire in `BCHubUIManager.cs`: field, Reset cleanup, SetActive cascade,
   `ToggleOverlay` switch case + `Settings.SetShowXxx` persistence,
   `IsOverlayOpen` lookup, `EnsureXxxOverlay` lazy constructor,
   `RestoreOverlaysFromSettings` restore-on-login.
4. Add a `ShowXxx` setting in `Settings.cs` + `InitConfigEntry` registration.
5. Add a footer toggle in `MainPanel.BuildOverlayFooter` (the wrapper has
   two rows — pick the appropriate one).

### In-UI parsing of multi-line chat replies (PrestigeInfo/BloodInfo pattern)

For commands that reply with structured multi-line text (e.g. `.prestige
get`, `.bl get [Type]`):

1. Add `InterceptFlag.AwaitingXxx` + `ReceivingXxx` states to
   `MessageService_Processing.cs`'s enum.
2. Add a struct to `PlayerStateService.cs` (`XxxInfo { ... }`) plus
   `XxxLatest` snapshot + `XxxChanged` event + `UpdateXxx(in info)` mutator.
3. Add a header-line regex to the Processing partial. Match in
   `HandleInboundChat`'s `AwaitingXxx` case to transition to `ReceivingXxx`.
4. In `ReceivingXxx`, capture each line until the timeout flush hits
   (`TickInterceptTimeouts` already exists; add a switch case for
   `ReceivingXxx → FlushXxx`).
5. Arm the intercept in `NoteOutboundForIntercept` when the user submits
   the corresponding form.
6. Render in the relevant tab — subscribe to `XxxChanged`, render structured
   data in a panel below the form. Unsubscribe in `MainPanel.Reset`.

This pattern is in active use for `PrestigeInfo` (0.3.0) and `BloodInfo`
(0.6.0). Mirror them for any future "make X visible in UI instead of chat".

### NEVER destroy a NETWORKED entity client-side — it crashes the client via ReceivePacketSystem (0.18.4)

A tester crashed to desktop when renaming a chest/storage box while the BCH chat window was enabled.
Player.log was decisive:

```
CreateEntitiesJob: NetworkedIdToEntityMap contained a destroyed entity. This shouldn't happen!
  Entity: 144466:6 NetworkId: '(Normal 284986:78)' PrefabGuid: 0.
  ProjectM.Network.ReceivePacketSystem:DestroyEntities  ... :OnUpdate
Networked Entity was missing NetworkSnapshot component...
System.ArgumentException: The entity does not exist. ... EntityComponentStore::AppendDestroyedEntityRecordError
  This Exception was thrown from a job compiled with Burst ... burst will now abort the Application.
```

**Cause chain (BCH-only, chat-window-on only):** BCH's chat takeover (`ClientChatPatch.OnUpdate_Prefix`)
intercepts the Enter key to focus the BCH chat input. Pressing Enter to confirm the storage RENAME was
grabbed by that takeover → focused BCH chat → set `InputSuppression.ChatInputActive=true` → that armed
`InputSuppression.DrainMenuOpenRequests`, which did a blanket `em.DestroyEntity(query)` over
`OpenMenuEvent`/`GoToHUDMenu`. The storage UI's transition entity was **networked** (carried a
`ProjectM.Network.NetworkId`); destroying it client-side left `ReceivePacketSystem`'s
`NetworkedIdToEntityMap` pointing at a dead entity → the Burst job ABORTS THE PROCESS (uncatchable).

**Rules learned:**
1. **A client mod must NEVER `DestroyEntity` an entity that has a `NetworkId`/`NetworkSnapshot`.** The
   network system owns its lifetime; killing it corrupts the map and Burst-aborts. Any blanket
   `DestroyEntity(query)` MUST filter out networked entities (`em.HasComponent<NetworkId>(e)` → skip).
   `DrainMenuOpenRequests` now iterates + skips networked entities (`DrainNonNetworked`). Local stray
   menu-open requests (M/B/K…) carry no NetworkId, so suppression still works.
2. **The chat takeover must not steal the Enter key while a GAME text field is focused** (rename box,
   any non-BCH `TMP_InputField`). `ChatInputActive` is already false in that branch, so any focused
   `TMP_InputField` the EventSystem reports is a foreign/game field — guard with
   `IsForeignUiInputActive()` before `FocusChatInput()`. Stealing Enter also (mis)armed the drain above.
3. Crashes that "only happen with feature X on" + abort from a **Burst job in a `*System.OnUpdate`** are
   almost always a mod destroying/mutating an entity the engine job still expects. Read Player.log for
   `NetworkedIdToEntityMap`/`AppendDestroyedEntityRecordError` — they name the exact NetworkId.

### A cached EntityQuery (or any world-bound handle) goes STALE on a server-switch → native crash (0.18.4)

**Symptom:** open BCH shortly after switching servers (without fully quitting) → instant crash to desktop,
no managed trace, no dump (the game's Backtrace/Crashpad returns 403 and never initializes). It reproduced
in BOTH directions and on every server, always on the action that touched ECS first.

**Cause:** the client **World is disposed and recreated on a server-switch** (same finding as the 0.18.1
logout crash — `ClientBootstrapSystem.OnDestroy` is the disposal signal). Any `EntityQuery` you cached with
`em.CreateEntityQuery(...)` is owned by the World it was created in. After a switch it's a handle into a
**dead world**; calling `ToEntityArray` / `DestroyEntity(query)` on it is an uncatchable native crash. BCH
cached two: `InputSuppression._drainOpenMenuQuery/_drainGoToHudQuery` (the menu-open drain, which runs on
panel-open when `SuppressGameInputWhileUIOpen` is on, and on typing) and `PlayerRosterService._userQuery`.
Both only rebuilt the query on a *managed* exception (`catch { _ready = false; }`) — which a native crash
never throws — so the stale query was reused after the switch.

**Rules:**
1. **Drop every cached `EntityQuery` / world-bound handle on `ClientBootstrapSystem.OnDestroy`** (a pure
   `_ready = false` field reset — safe in the teardown hook). It rebuilds against the new world on next use.
   BCH does this via `InputSuppression.OnWorldTeardown()` + `PlayerRosterService.OnWorldTeardown()` wired
   into `InitializationPatch.ClientBootstrapSystem_OnDestroy_Prefix`, next to the other session resets.
2. Belt-and-braces: before using a cached query, bail if `!em.World.IsCreated` (and reset the ready flag).
3. A `catch { _ready = false; }` does NOT protect you here — the fault is native, not managed.
4. When auditing for this, grep `CreateEntityQuery` and check each cache is reset on teardown. (Re-fetching
   the system/map each call — like ShiftCooldownService / AbilityIconResolver do via PrefabCollectionSystem
   — sidesteps the problem entirely; prefer that for infrequent lookups.)
