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
