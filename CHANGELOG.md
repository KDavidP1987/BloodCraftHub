# Changelog
## 0.17.0 — TODO

- TODO: describe what changed.


## 0.16.1 — Crash hotfix: stop triggering the Il2CppInterop GC-finalizer crash at login

A stability hotfix for an **intermittent crash a few seconds after loading into
a game** that some players hit on 0.16.0 (and which others, on the same build,
never saw). It surfaced as a fault deep inside Il2CppInterop's garbage collector:

```
Unhandled exception. System.NullReferenceException
   at Il2CppInterop.Runtime.Injection.Hooks.GarbageCollector_RunFinalizer_Patch.Hook(...)
```

### What it actually was

That stack is entirely inside **Il2CppInterop**, not BCH — `GarbageCollector_RunFinalizer_Patch`
is a known-unstable piece of the interop layer (since removed upstream) that can
fault under GC pressure during load. The crash is **non-deterministic**: same
build, different outcome per machine; it "came and went" for testers with no
code change; no BCH exception is ever logged. BCH wasn't *failing* — it was
**triggering** that latent interop bug by doing too much GC-pressuring work in
the busy login window. The chief offender: `RecipeService` ran a burst of ECS
structural changes synchronously, right as registration + the familiar probe +
the chat flood all landed, forcing GC sync points at the worst moment.

### Changes

**Custom recipes now default OFF and apply on a deferred, quiet frame.** The
custom-recipe feature is the single new-0.16.0 element most correlated with the
crash window, so `EnableCustomRecipes` now defaults to **false** — turn it on to
opt in. When on, application is **deferred a few seconds after login** to a quiet
frame (instead of running inline in the Eclipse config handler), keeping its
structural-change burst out of the volatile load window. (Existing configs that
already set `EnableCustomRecipes = true` keep their value — see Notes.)

**Recipe mutation hardened.** Even when enabled, every mutation block is now
isolated in its own try/catch (a version-mismatched prefab can't abort the rest
or half-apply, and logs which step it skipped) and shape-checked before any
buffer access (entity exists + is really a recipe; buffers present and non-empty
before slot `[0]` is touched).

**SHIFT-spell icon resolution gated + guarded.** The managed `AbilityTooltipData`
read (`GetComponentObject`) added in 0.16.0 now only runs when the SHIFT overlay
is actually shown, `ShowShiftSpellIcon` is a real kill-switch (it previously only
hid an already-resolved icon), stale entities are skipped, and a circuit-breaker
latches it off after repeated faults. Cooldown readout is unaffected.

**Smaller, shrinkable Quick Actions overlay.** Friend-test: the "Stash All"
button was oversized and wouldn't shrink. Lowered the overlay's minimum footprint
and the button's minimums so it starts smaller and resizes down.

### Notes

- This is a **probability reduction for a non-deterministic interop-layer race**,
  not a guaranteed fix — but it removes the login-time trigger for the default
  configuration. The underlying fault lives in Il2CppInterop; keeping your
  BepInEx (V Rising) pack up to date is recommended.
- **Existing installs** that already have `EnableCustomRecipes = true` in their
  config keep that value (the new default only affects fresh configs). If a user
  is still crashing, set `EnableCustomRecipes = false` — the deferral also
  reduces the risk for those who leave it on.

## 0.16.0 — Input suppression, custom recipes, SHIFT-spell icon, exoform fix, Quick Actions overlay, overlay layering + resize discoverability

A player-feedback release. The marquee addition is an optional setting that
freezes the character's in-game actions while the BCH UI is open; bundled
alongside are Bloodcraft custom crafting recipes surfaced in the stations, the
SHIFT-spell icon, an exoform prestige tracking fix, a new Quick Actions overlay,
configurable overlay layering, and a more discoverable resize edge.

### Added: optionally freeze character actions while the BCH panel is open

The most-requested fix from friend-testing: with the main panel open, the
character kept acting in the background — moving on WASD, swinging on mouse
clicks, casting hotkeyed abilities, and opening game menus (B = build, M = map,
etc.). Especially disruptive for admins who keep commands/abilities on hotkeys
and trip them while clicking around the UI.

New `SuppressGameInputWhileUIOpen` option (default **off**), toggled in
Settings → Display ("Freeze character actions while the main panel is open").
When on, while the main panel is open BCH suppresses the character's gameplay
input:

- **Movement + aim** stop.
- **Ability / hotkey casts** stop, and any attack already mid-swing from the
  click that opened the panel is cancelled — so the primary attack doesn't get
  stuck firing.
- **Game-menu hotkeys** (build, map, inventory, etc.) no longer open menus
  behind the panel, and queued open-requests are drained so they don't all fire
  the instant you close it.

Implementation note (for the curious): this suppresses only V Rising's
*gameplay* input producer systems (`GameplayInputSystem` for movement/aim,
`AbilityInputSystem` for casts) plus the HUD-menu chokepoint
(`OpenHUDMenuSystem`, where every menu hotkey resolves). It deliberately never
touches the system that drives the UI itself, so the panel, cursor, and form
text-entry stay fully responsive — and the client can't freeze. (An earlier
naive approach that blocked the shared input system did freeze the client;
this one is structurally different.) Every hook is wrapped so any failure
falls back to normal input. Off by default — opt in if you want it.

### Added: Quick Actions overlay (one-click Stash All)

A new draggable, resizable overlay of one-click command buttons. It ships with
a single **Stash All** button (KindredLogistics `.stash`) for the common "just
got back to base, dump everything into storage" moment — no opening a tab or
typing the command. Toggle it from the footer overlay row or Settings → Display
like every other overlay; position + visibility persist across sessions. The
overlay is intentionally generic ("Quick Actions") so more one-click actions can
drop in over future releases without resetting your saved settings.

### Added: Bloodcraft custom recipes in the in-game crafting stations

Bloodcraft can enable a set of extra crafting recipes (vampiric dust,
copper wires, silver ingot, charged battery, soul-shard extraction,
primal jewel, blood crystal, primal stygian, plus a batch of new
salvage outputs). Until now only the Eclipse client mod surfaced these
in the vanilla crafting/refinement UI; BCH users who had dropped Eclipse
in favour of BCH lost that surface. BCH now provides it natively.

Mechanism (ported from Eclipse's `Utilities/Recipes.cs`): the recipe
TABLE is not broadcast — Bloodcraft's server only sends a single boolean
`extraRecipes` flag (plus an optional `primalCost` item GUID) in its
Eclipse `ConfigsToClient` payload. Both Eclipse and BCH carry an
identical hard-coded recipe definition and apply it locally so the
recipes appear in the station windows. This is **client-side display
only** — the server still validates every real craft.

Gating (so it never appears where it shouldn't):
- Applied **only** when the server's `extraRecipes` flag is true. If the
  server admin hasn't enabled extra recipes, nothing is injected.
- Skipped entirely when the standalone **Eclipse** mod is also installed
  (Eclipse performs the same prefab mutations itself; doing it twice
  would duplicate station recipes / requirement buffers).
- Gated behind a new `EnableCustomRecipes` config option (default on).
- Applied once per session, marked applied *before* mutating so a
  mid-way failure can never double-inject, and wrapped so any failure
  logs a warning instead of disrupting the client.

This is the first BCH feature that *mutates* the game's ECS data (BCH is
otherwise read-only); it is version-coupled to Bloodcraft's recipe set.
Each recipe lookup uses a safe `TryGetValue` so a prefab that doesn't
exist on the running game version is skipped rather than throwing.

> Note: Eclipse also renames/re-icons the crafted jewel ("Primal Jewel")
> using its own embedded sprite cache. BCH has no equivalent sprite cache
> so that purely-cosmetic rename is omitted — the recipe itself works; the
> crafted item just keeps its default template name.

### Added: SHIFT-spell overlay now shows the actual spell icon

The Shift-spell cooldown overlay previously showed only a colored tile +
countdown. It now displays the real icon of whatever spell is in the
shift slot, matching Eclipse. The icon is read from the slotted ability
group entity's `AbilityTooltipData.Icon` (the same engine-resolved sprite
the vanilla ability bar uses), cached per-spell, with the radial cooldown
sweep shading over it and the countdown digits on top. When ready, the
redundant "Ready" label is dropped so the icon reads cleanly.

New `ShowShiftSpellIcon` config option (default on); turning it off falls
back to the plain colored tile. When no spell is slotted, or the icon
can't be resolved, the overlay behaves exactly as before.

> Known limitation: for Bloodcraft **class spells** that override the shift
> slot, the icon resolves on first cast — the slotted ability's tooltip data
> isn't available client-side until the spell is used once. This matches
> Eclipse's behavior. Standard (non-overridden) shift abilities show their
> icon immediately.

### Fixed: exoform (Exo) prestige was never tracked

Players reported the EXO prestige line never populated. Root cause: BCH
*did* already query `.prestige get Exo` and had an "EXO Prestige" line in
the Experience overlay — but the reply parser never matched it. Exo's
reply uses a different shape from every other prestige type: there is no
`"<Type> Prestige Info:"` header line, and the level line itself carries
the type name inline:

```
Current <color=#90EE90>Exo</color> Prestige Level: <color=yellow>{lvl}</color>/{max} | Max Form Duration: ...
```

BCH's state machine was waiting for a header that never arrived, so it
never even examined the level line. Added dedicated parsing for the exo
reply shape (and for "You have not prestiged in Exo yet."). The existing
Experience-overlay EXO line now populates, and a matching **Exo prestige
card was added to the Prestige tab** (auto-fetched on tab open, mirroring
the overlay). The **combined info overlay's XP section** also gained the EXO
prestige line, which had been absent there. Normal prestige types are
unaffected — the new patterns are additive and only match the exo shape.

### Added: overlays can sit behind in-game menus

Feedback: BCH's overlays float on top of the vanilla in-game menus
(inventory, character sheet, map, etc.), obscuring them. New
`OverlaysBehindGameMenus` config option (default on) drops BCH's canvases
behind the menu while one is open, using the game's own menu signal
(`UICanvasBase.HUDMenuParent`), and restores them on close. Set to false
to keep the pre-0.16 always-on-top behavior. Independent of Bloodcraft —
it hooks a vanilla UI system and is wrapped so it can never disrupt the
game's canvas update.

### Fixed: can't close the panel in fullscreen on smaller screens

On laptop / smaller monitors, the fullscreen main panel's own window
controls (the `—` close and `[X]` restore buttons in its title bar)
landed underneath BCH's always-on-top floating launcher button, which
intercepted the click — leaving no way to close the panel. The floating
launcher now hides while the main panel is fullscreen (it's redundant
then; close/restore from the title bar) and reappears on exit.

### Improved: drag-to-resize is now discoverable

Players noted the main panel's drag-to-resize edge was hard to find —
"you had to click the exact pixel." The resize grab ring was widened
(10 → 16 px) and a subtle accent border now lights up while the cursor is
over the resize edge, alongside the existing directional resize cursor.

## 0.15.1 — Hotfix: hotkey double-toggle + per-system disabled detection + familiar auto-probe

Hotfix on top of v0.15.0 covering four friend-test reports.

### Fixed: hotkey appears to work first time then stops responding

Friend-test 0.15.0: after binding Shift+Alpha1 to the "Open main panel"
hotkey, pressing it once visibly closed the panel — but every press
afterwards looked like nothing happened, even though the
`[DIAG] Hotkey fired ... ToggleMainPanel: False -> True / True -> False`
log lines showed the state correctly alternating.

Root cause: a long-standing latent bug in `CoreUpdateBehavior.Setup`
that v0.15.0's new hotkey listener exposed. `Setup()` was being called
twice — once from `Plugin.Load()` and once from `UniversalUI.Init()`
— each call constructing its own GameObject + `CoreUpdateBehavior`
MonoBehaviour. Both MonoBehaviours ran `Update()` every frame
iterating the **same** static `Actions` list, so every registered
action was invoked **2× per frame**.

Pre-v0.15 callers (`ProcessAllMessages`, `TickInterceptTimeouts`,
`VBloodScannerService.Tick`, etc.) are all idempotent so the
double-call was invisible. `TickHotkeys` is NOT idempotent —
`Input.GetKeyDown` returns true for the *entire frame* after the key
transitions Up→Down, so calling it twice per frame double-toggled the
panel (open→close→open in one frame, net visible effect = nothing).
The diagnostic logs caught it perfectly: each user keypress generated
two `Hotkey fired` lines with alternating `False → True` / `True →
False` transitions.

Fix: `CoreUpdateBehavior.Setup()` now uses a static `_hostObject`
guard so the GameObject + MonoBehaviour are created exactly once
regardless of how many places allocate a `CoreUpdateBehavior`
instance. The `_obj` instance field was promoted to a static field
and the `Dispose()` cleanup also targets the static.

### Improved: Quest detection — "(none yet)" → "Quests disabled on this server"

Friend-test 0.15.0: server had every Bloodcraft system enabled
**except** Quests. The Daily Quest overlay + the Daily Quests tab
both showed the empty-state placeholder ("(none yet)") indefinitely,
with no signal that the feature was actually off on that server.

v0.15.0's generic `IsSystemEnabled` heuristic couldn't catch this
because the Quest signal (`DailyQuest.Goal > 0 OR WeeklyQuest.Goal > 0`)
only fires when the user has an *active* quest — empty quest data is
ambiguous between "Quest system disabled" and "Quest system enabled
but no quest assigned yet."

Fix: new `PlayerStateService.IsSystemReliablyDisabled(SystemKind)`
helper adds a **cross-system corroboration** requirement. A system is
reliably disabled when (a) the settling window has elapsed, (b) the
system itself has shown zero data the entire time, AND (c) at least
one OTHER Bloodcraft system has shown non-zero data during the
window. Condition (c) proves the structured protocol is up and
broadcasts are flowing, which means an empty signal for the target
system reflects real server-side state rather than "data hasn't
arrived yet."

Three render sites updated to consult the helper:

- **Daily Quest overlay** — empty rows now show "(Quests disabled
  on this server)" + a muted sub-line ("The server admin has
  Bloodcraft's QuestSystem turned off") when reliably detected.
- **Daily Quests tab** — empty placeholder strings swap to the
  same message.
- **Combined info overlay** — `QUEST` section's empty rows show
  "Daily: (disabled on this server)" / "Weekly: (disabled on this
  server)" so users in combined-mode get the same signal.

The cross-corroboration approach is conservative: it only fires when
we have proof the protocol is working. New players who haven't
engaged with Quests yet on a Quest-enabled server still see the
neutral "(none yet)" placeholder until other systems prove the
broadcast is flowing.

### Improved: per-system disabled detection extended to XP / Familiar / Weapon / Blood / Professions

The Quest treatment above intentionally shipped first as a targeted
fix. Friend follow-up on the inverse scenario — server with **only**
QuestSystem enabled, every other Bloodcraft feature off — confirmed
the same gap existed for every other system: the cross-corroboration
heuristic detected them correctly under the hood, but the render
sites kept showing zeroed data as "functional" with no signal that
the feature was off server-side. Notable friend-test quote: "the
weapon here I'm actually unarmed but it doesn't register because
unarmed is part of weapon expertise which is off."

The `IsSystemReliablyDisabled` helper from earlier in this release
is now consulted at every per-system render site:

**Combined info overlay sections**

- **XP section** → "(Leveling disabled on this server)" + bar
  hidden when Leveling reliably disabled.
- **Familiar section** → "(Familiars disabled on this server)" +
  stats line "—" + bar hidden when Familiar reliably disabled.
- **Weapon section** → "(Weapon Expertise disabled on this server)"
  + stats / bonus-values / counter sub-rows hidden when Expertise
  reliably disabled.
- **Blood section** → "(Blood Legacy disabled on this server)" +
  stats / bonus-values / counter sub-rows hidden when Legacy
  reliably disabled.
- **Professions section** → "(Professions disabled on this server)"
  + both wrap-text and per-row layouts hidden when Profession
  reliably disabled.

**Standalone overlays**

- **XP overlay** → "(Leveling disabled)" + bar hidden when
  Leveling reliably disabled.
  - Weapon row → "Weapon (disabled on this server)" + stats /
    counter sub-rows hidden when Expertise reliably disabled.
  - Legacy row → "Legacy (disabled on this server)" + stats /
    counter sub-rows hidden when Legacy reliably disabled.
- **Familiar overlay** → "(Familiars disabled on this server)" +
  progress / stats / bar all neutralized when Familiar reliably
  disabled.
- **Familiar Browser overlay** → "(Familiars disabled on this
  server)" header + muted sub-line + Toggle/Unbind footer buttons
  disabled + list cleared when Familiar reliably disabled.
- **Professions overlay** → 8 per-profession rows hidden; the
  Enchanting row is reused as a single "(Professions disabled on
  this server)" hint line when Profession reliably disabled.

**Intentionally not touched in 0.15.1**

- **Shift Spell overlay** — reads from V Rising's ability-slot
  state directly (not from the Eclipse protocol broadcast), so a
  disabled-detection signal here would be misleading. The overlay
  will just show whichever ability is in the slot, regardless of
  Bloodcraft's ShiftSlot config.
- **Tab content** — the inside of each BLOODCRAFT tab (Familiars,
  Boxes, Class, Weapon Expertise, Blood Legacy, Levels, Prestige,
  etc.) still renders forms and form fields normally. The forms
  remain usable for issuing chat commands manually even when the
  backing system is disabled (the server-side handler will respond
  with a "not enabled" message that lands in the **Last server
  response** panel). Adding per-tab "(this system is disabled on
  the server)" banners is deferred to v0.16 to keep the v0.15.1
  hotfix scope manageable.

### Fixed: Familiar overlay false-positive "disabled" at login

Follow-up friend-test on the inverse scenario above: server with
Familiars **enabled** but Quests **disabled**, user logs in without
a familiar bound. The Familiar overlay incorrectly flagged the
system as "disabled" until the user summoned a familiar, at which
point it self-corrected.

Root cause: the Familiar detection signal was `Familiar.HasActive`
— which only fires when a familiar is **currently bound + summoned**
at the moment of the protocol broadcast. A player on a Familiars-
enabled server who simply hasn't summoned since login looks
identical to "FamiliarSystem disabled" via that signal. Cross-
corroboration kicks in after the 30-second settling window (other
systems are flowing data, Familiar hasn't shown anything yet) and
triggers the false-positive disabled UI.

Bloodcraft's Quest signal had a similar shape but doesn't hit this
in practice because the server auto-assigns daily/weekly quests
within minutes of login (`Quest.Goal > 0` fires). Familiar has no
such auto-trigger.

Two-part fix:

1. **`PlayerStateService._everReceivedFamiliarBoxList`** — new
   per-session flag set in `UpdateBoxList`. Arriving in
   `UpdateBoxList` at all is proof the server's `.fam boxes`
   handler accepted our request, which only happens when
   `ConfigService.FamiliarSystem=true`. The signal joins
   `Familiar.HasActive` as an "enabled" trigger in
   `RecomputeFeatureFlagsFromLatest`, and `UpdateBoxList` now
   re-runs the detection inline so UIs flip back from disabled
   immediately.

2. **`EclipseProtocolService.ScheduleFamiliarSystemProbe`** —
   schedules a silent `.fam boxes` probe via
   `MessageService.EnqueueMessageSilent` on the first ConfigsToClient
   ACK so the signal lands even when the user never opens the
   Familiar Browser overlay or the Boxes tab. The probe is one-shot
   per session — `EclipseProtocolService.Reset` clears the flag so
   the next world entry probes again on the new server.

`FamiliarOverlayPanel` now also subscribes to `FeatureFlagsChanged`
in addition to `FamiliarChanged` so the overlay re-renders the
moment the probe response flips the flag, without waiting for the
next ProgressToClient broadcast tick.

Friend confirmed in chat: once the probe lands, the Familiar
overlay updates from "(Familiars disabled on this server)" back to
"(no familiar bound)" — the correct empty-state placeholder for a
Familiars-enabled server with no current binding.



## 0.15.0 — Bloodcraft availability diagnostic + per-feature degradation + tab strip overflow + checkbox visibility + controller-A fix + familiar overlay min-height + .fam reset relabel + opt-in hotkeys + diagnostic mode + main-panel drag fix

Friend-test feedback bundle on top of v0.14.0. Eleven fixes spanning UX
redundancy, polish, a controller regression, a panel-locked-by-stale-
save regression, and two new opt-in user-experience tools (configurable
hotkeys + three-state diagnostic logging).

### New: Bloodcraft "Handshake failed" diagnostic panel

When BCH's Eclipse-protocol RegisterUser handshake times out after the
15-second retry window AND the user is still on the default
`BloodcraftAvailability=Auto` setting, the BLOODCRAFT tab group in the
left rail now expands to an in-panel diagnostic instead of just
greying out. The diagnostic:

- Stays brightly-labeled (no longer grayed) so the user notices it.
- Names the three likely root causes: server doesn't run Bloodcraft;
  server runs Bloodcraft but only `QuestSystem` / `ProfessionSystem`
  is enabled (which doesn't satisfy Bloodcraft's `Core.Eclipsed` gate);
  older Bloodcraft + server config `Eclipsed=false`.
- Surfaces a one-click **Force-enable tabs** button. Clicking it
  flips `BloodcraftAvailability=On` for the rest of the session and
  rebuilds the tab strip in place — sub-tab buttons replace the
  diagnostic, the user can navigate into any Bloodcraft tab and
  drive the chat-regex pipeline (`.fam boxes` / `.quest p` / `.bl get`
  / `.wep get` / etc.) with replies surfacing in the **Last server
  response** docked panel.
- Notes in a footnote that the override is session-only; to persist
  across launches, edit `BloodcraftAvailability=On` in
  `kdpen.BloodCraftHub.cfg`.

The override doesn't make the structured ProgressToClient broadcast
start working (that gate lives entirely on the server side), so live
HUD overlays still won't tick — but at least the user can issue
commands and read replies. README's new **Server-side Bloodcraft
compatibility** section explains the protocol-gating relationship
between the five core systems and the broadcast.

Friend-test 0.14.0 root cause: Bloodcraft v1.13.21 only initializes
its `EclipseService` (the thing that broadcasts ProgressToClient and
processes the RegisterUser handshake) when at least one of
`LevelingSystem` / `LegacySystem` / `ExpertiseSystem` / `ClassSystem` /
`FamiliarSystem` is enabled. Quests-only / Professions-only servers
silently drop our handshake entirely, leaving BCH staring at an
unaccountably empty tab group. Pre-0.15.0 the tab group went grey
with no explanation; users had to know about the `BloodcraftAvailability`
.cfg override to recover.

### New: per-feature degradation infrastructure (visual gating reverted)

Per-system availability detection is wired up under the hood:
`PlayerStateService.ServerFeatureFlags` tracks sticky-Enabled booleans
per Bloodcraft system, populated heuristically from each
ProgressToClient broadcast. Diagnostic mode logs every transition so
the data is observable.

**However**, the in-UI visual treatment (dimming tabs with "(off)"
suffix, auto-hiding standalone overlays, collapsing combined-overlay
sections) is **disabled in 0.15.0**. Friend-test on a fully-enabled
Bloodcraft server surfaced false positives because the only
detection signals available from Bloodcraft's protocol are
"is the user currently engaged with system X" (e.g. Familiar.HasActive
= true ONLY when a familiar is bound + summoned right now; ShiftSpell.
SpellIndex > 0 = true ONLY when a shift spell is currently slotted).
A real user who logs in and hasn't summoned a familiar in 30 seconds
falsely tripped Familiar=Disabled even though every familiar tab
worked fine the moment they tried it.

ConfigsToClient's max-level fields don't help either — they're
config defaults that come through identically whether the system is
enabled or disabled on the server.

Restoring v0.14 unconditional tab/overlay rendering until a reliable
probe lands. The two paths under consideration for v0.16:

- chat-regex probes (issue `.fam boxes`, parse "Familiars are not
  enabled." vs. any other reply, do the same for `.quest p` /
  `.prof l` / etc.) — accurate but adds outbound startup commands.
- Manual per-system "Show in UI" toggles in Settings → Display, all
  defaulting to on. Users who know their server is partial flip
  what they don't want to see. Cheap + 100% accurate but requires
  user knowledge.

The infrastructure stays in place so when the probe lands the UI
plumbing is already there.

### Original "per-feature graceful degradation" (kept for reference)

Even when the handshake succeeds, individual Bloodcraft systems can be
disabled server-side independently (e.g., a server runs LevelingSystem +
QuestSystem only, with Legacy/Expertise/Familiars/Class/Profession off).
Bloodcraft's `EclipseService.GetXxxData` returns zeros for disabled
systems, so ProgressToClient broadcasts still arrive with all 46 fields
but disabled features come through as 0s. Pre-0.15.0 BCH rendered the
zero data without any indication the feature was disabled, leaving
users staring at "Level 0 / 0%" indefinitely.

0.15.0 adds heuristic per-system availability detection:

- `PlayerStateService.ServerFeatureFlags` — sticky-Enabled bools for
  Leveling / Legacy / Expertise / Familiar / Class / Profession /
  Quest / ShiftSlot. Once a system shows non-zero data, the flag flips
  Enabled and stays Enabled for the session.
- 30-second settling window after the first ProgressToClient broadcast.
  During settling every system reads as Enabled so the UI doesn't
  briefly hide things at world entry. After settling, any system that
  never showed non-zero data gets marked Disabled.
- New `FeatureFlagsChanged` event — UI subscribers (tab strip,
  BCHubUIManager, combined overlay) refresh visibility on transition.

UI surface area gated by feature flags:

- **Tab strip** — sub-tabs whose backing system shows Disabled get
  italicized + grayed text + an " (off)" suffix. Still clickable so
  users can navigate in if they want.
- **Standalone overlays** — XP / Familiar / Familiar Browser / Daily
  Quest / Professions / Shift Spell each hide when their backing
  system is Disabled. The user's `Show*Overlay` setting is NOT mutated
  — moving to a server where the system IS enabled brings the overlay
  back at its original visibility.
- **Combined overlay sections** — XP / Familiar / Weapon / Blood /
  Professions / Quests sections collapse when their backing system is
  Disabled, so the panel shrinks to exactly the enabled systems.

Detection signals per system (each is a "system enabled iff this is
nonzero/non-empty at any point" check):
- Leveling: `Experience.Level > 0`
- Legacy: `Legacy.Level > 0` OR Prestige > 0 OR non-empty BonusStatsRaw
- Expertise: same shape as Legacy
- Familiar: `Familiar.HasActive` (non-empty Name field)
- Class: `Experience.Class != None`
- Profession: any of the 8 profession levels > 0
- Quest: `DailyQuest.Goal > 0` OR `WeeklyQuest.Goal > 0`
- ShiftSlot: `ShiftSpell.SpellIndex > 0`

### Fixed: tab strip overflow — Bloodcraft Admin button hidden by Kindred menu

Friend-test 0.14.0: with all three groups (BLOODCRAFT 12 tabs / KINDRED
6 / SETTINGS-AND-HELP 6) simultaneously expanded, the BLOODCRAFT group's
content GameObject was collapsing to its `minHeight: 0` because
Unity's VLG ran out of vertical space and fell back to minHeight when
total preferredHeight exceeded the strip's available height. With
zero height, the BLOODCRAFT sub-tab buttons rendered overlapping the
KINDRED group header, hiding the Admin button at the bottom of the
Bloodcraft list.

Two-part fix in `BuildTabStrip` + `BuildTabGroup`:

- `minHeight = preferredHeight` (`Tabs.Length * 30 + 4`) on each
  group's content GameObject. Children can no longer collapse below
  their natural height.
- ScrollRect wrapping the entire tab strip via `UIFactory.CreateScrollView`.
  When the strip's content exceeds the panel's left-rail height, the
  rail scrolls vertically instead of clipping. Belt-and-braces: the
  minHeight clamp handles the typical case while the ScrollRect handles
  any edge case where the panel is unusually short (Steam Deck, etc.).

### New: README heads-up additions

Added two new "Heads-up before you install" entries:

- **Controller / gamepad input under investigation** — calls out that
  V Rising controller input intersects with BCH UI in ways still being
  hardened (see "Fixed: controller A-press" below for the v0.15.0
  first-pass fix). Asks controller users to report repros via Discord.
- **Server-side Bloodcraft compatibility** — covers the five-core-systems
  gate plus a note on the modern `Eclipsed` config-key semantics
  (frequency knob on current Bloodcraft; potentially a hard kill on
  older builds). Pairs with the in-rail diagnostic added above.

Both bundled into the Thunderstore zip via `package-release.ps1`.

Also updated the existing Eclipse compatibility-table row to call out
the current conflict (previously listed Eclipse as "coexists, doesn't
conflict" — outdated since the client-crash issue was discovered).

### Improved: toggle / checkbox visibility on dark panels

Friend-test surfaced that the toggle checkboxes were literally
invisible on some laptop monitors — pre-0.15.0 the checkbox
background was pure `(0,0,0,Opacity)` sitting on a `(0.07,0.07,0.07)`
panel, which several screens couldn't differentiate. Two changes
in `UIFactory.CreateToggle` + `Theme`:

- `Theme.ToggleNormal` lifted from pure black to a dark slate
  `(0.18, 0.18, 0.21, Opacity)` so the checkbox reads as a tangible
  button rather than a void hole.
- **Anchored-stretch Frame border** (`Theme.ToggleOutline` painted at
  near-white `(0.92, 0.92, 0.95, 1.0)`) — 2-pixel ring on all four
  sides of every checkbox. Five iterations got here: (v1) Unity
  Outline component — invisible at 20×20 (four corner specks).
  (v2) 1-px HLG Frame — too subtle. (v3) 2-px HLG Frame — user
  reported still invisible. (v4) 3-px anchored-stretch Frame —
  finally visible but too thick. (v5 — shipped) 2-px anchored-stretch
  Frame: the Background is pinned to the Frame's edges with
  `offsetMin/Max = 2 px`, mathematically guaranteed to leave a 2-px
  ring of Frame visible regardless of any parent layout-group
  behavior. Outer footprint = inner checkbox size + 4 px total
  (e.g. 24×24 for the default 20×20 callers).
- **Custom toggle ColorBlock** (`(0.30, 0.30, 0.34, 1.0)` normal,
  `(0.45, 0.45, 0.50, 1.0)` highlighted). v0.14-and-earlier toggles
  inherited `Theme.SelectableNormal` which carries the panel's
  opacity — at 60% panel opacity the fill rendered at 60% alpha,
  ghosting the entire toggle into the dark panel chrome. v0.15.0's
  override keeps the toggle's fill opaque at every opacity setting
  while preserving the hover/press transitions (just from a brighter
  floor).

Affects every Toggle in BCH — form `BoolField`s, overlay-visibility
footer toggles, settings toggles, etc. — because they all flow
through the same `UIFactory.CreateToggle` factory.

### Fixed: "Reset all familiar entities" framing was misleading

`.fam reset` server-side (`Bloodcraft/Commands/FamiliarCommands.cs:962`)
clears stuck `FollowerBuffer` entities + active-familiar state, but
the box record and unlock data are completely untouched — familiars
can be re-summoned via `.fam b N` after running it. Pre-0.15.0 the
form's section title was `"Reset all familiar entities (.fam reset)
— DESTRUCTIVE"` and the confirm checkbox label was `"Yes, destroy
active follower entities"`. Friend reported testing it and being
surprised that the familiar came right back when re-summoned. Updated
to:

- Section title: `"Force-unbind stuck familiar (.fam reset)"`.
- Tooltip: emphasizes box records + unlocks are preserved, frames
  the command as a cleanup utility for stuck familiars, notes the
  server-side handler refuses to run while the active familiar is
  still alive (`.fam ub` first).
- Confirm checkbox: `"Yes, clear stuck familiar bind"`.

Also corrected the misleading `BCCOM_FAM_UNBIND` and `BCCOM_FAM_RESET`
comments in `MessageService_Processing.cs` that previously claimed
both commands were "DESTRUCTIVE: permanently destroys" — neither
actually destroys collection data.

### Fixed: controller A-press re-opens main panel after teleport

Friend-test 0.14.0 on Steam Deck / gamepad: pressing A on the
controller after using an in-world teleport waypoint would re-open
the BCH main panel because Unity's EventSystem retained focus on the
floating BCH button from the user's previous mouse click. The next A
press routed through the navigation graph to that focused Selectable
and re-clicked it.

Two-part fix on the floating BCH / OV buttons:

- `Navigation.Mode.None` on the floating button's `Selectable` so the
  buttons are excluded from the UI navigation graph entirely — A
  presses no longer route to them via controller navigation. Mouse
  clicks still work because they go through the pointer-event path,
  not the navigation path.
- `EventSystem.current.SetSelectedGameObject(null)` called in the
  click handler's `finally` block as belt-and-braces, mirroring the
  same pattern `FormBuilder` uses after form submit. Clears any
  stale selection so a transient focus elsewhere can't ghost-activate
  the floating buttons on the next A press.

Bug only manifested with controller input — keyboard `.teleport`
chat command was always fine because it doesn't route through Unity
UI navigation.

### New: opt-in configurable hotkeys for floating-button actions

Two new entries under Settings → Display → Hotkeys & diagnostics:

- **Open main panel** — keyboard shortcut to toggle the BCH main panel
  (same effect as clicking the floating BCH button).
- **Toggle all overlays** — keyboard shortcut for the master overlay
  hide/show (same effect as clicking the floating OV button).

Both unbound by default — the user clicks "Set..." in the rebind row,
then presses any key (or modifier+key combo) to bind. "Clear" removes
the binding. Modifier keys are automatically captured from whatever's
held during the bind keypress: Ctrl / Alt / Shift / Win all work,
either alone or combined. Examples that work:

- `Insert` (single key)
- `F3`
- `LeftControl+H` (combo)
- `LeftShift+F5`

Bindings persist via the .cfg file (`HotkeyToggleMainPanel` /
`HotkeyToggleAllOverlays` in `kdpen.BloodCraftHub.cfg`) and can also
be edited directly there if the user prefers — values are stored as
human-readable strings ("Insert", "Ctrl+H", etc.). The .cfg accepts
short modifier aliases (`ctrl`, `alt`, `shift`, `win`) in addition
to the full enum names.

Motivation: streamers and users with low-transparency floating
buttons sometimes lose the cursor target for the BCH/OV strip;
hotkeys give a guaranteed entry point that doesn't depend on
visibility or pointer focus. Also useful as a fallback if Unity's
UI navigation ever gets into a weird state (see the Item 4
controller-A fix in this same release).

Custom `BCHotkey` struct used in place of the older BepInEx 5
`KeyboardShortcut` — BepInEx 6 (V Rising's IL2CPP build) dropped
that type from its public surface, so a minimal in-namespace
replacement covers the parse / IsDown / serialize-to-cfg semantics
without depending on the upstream type.

### New: Diagnostic mode (three-state)

Three radio buttons under Settings → Display → Hotkeys & diagnostics:

- **Off** — no logging. Default.
- **This session** — verbose logging until the game restarts. The
  .cfg stays at Off so an experimental run doesn't accidentally
  leave logging on forever. The runtime override silently clears
  on next launch.
- **Always** — verbose logging every session until the user turns
  it off. Persists to .cfg.

The persisted value (`DiagnosticMode` in the .cfg) is always either
`Off` or `Always` — never `Session`, which is implemented as a
runtime-only override.

When active in any state other than Off, BCH emits `[DIAG]`-tagged
trace logs to BepInEx for:

- Floating BCH / OV button clicks (with prior EventSystem selection
  for diagnosing controller-input edge cases).
- Hotkey fires.
- `ToggleMainPanel` / `ToggleOverlay` transitions.
- Eclipse protocol registration state transitions (handshake-acked,
  give-up).
- Per-feature flag transitions (which Bloodcraft systems were detected
  enabled / disabled by 1B's heuristics).

The toggle lives in Settings → Display → Hotkeys & diagnostics. Off
by default. Cheap when off (one bool check + early return per log
point), so sprinkling at user-action paths is safe. Per-frame paths
are intentionally NOT instrumented to avoid log spam.

Workflow: user hits an issue → toggles diagnostic on → reproduces
the issue → shares the relevant section of `LogOutput.log` with the
maintainer. Replaces ad-hoc "add some LogInfos and ship a debug
build" cycles.

### Fixed: Familiar Browser overlay couldn't shrink past 440 px

`MinHeight` lowered from 440 to 220. Sum of the toolbar (28 px),
box-name row (20 px), status row (18 px), scroll-list minimum (80
px), and footer (26 px) only adds to 172 px even at default font
sizes, so 440 was very conservative. Shrinking the overlay now
takes ALL of the saved height out of the central scrollable familiar
list, because the toolbar / box-name / status / footer rows are
`flexibleHeight: 0` while the scroll view is `flexibleHeight: 1`.
Header buttons, box-name display, sort/view buttons, and the
Unbind/Toggle footer all stay at their natural heights at every
overlay size. Friend-test 0.14.0: users with Large text settings on
small monitors couldn't shrink the overlay tightly enough to fit
into a corner without overlapping other HUD elements.

### Fixed: main panel could end up locked in place from stale save data

Friend-test 0.15.0: user defaulted the main-panel size and reported
"the panel feels locked in place — I click on the borders and it
doesn't move." Root cause: `ResizablePanelBase.ApplySaveData` was
restoring the persisted `IsPinned` bit from the panel's serialized
save string regardless of which panel was loading. For overlays that
opt into `RespectsLockOverlays = true`, the Lock-overlays settings
toggle provides a clear path to un-pin. The main panel has
`RespectsLockOverlays = false`, so once `IsPinned=True` made it into
its save string (e.g. from a pre-0.11.2 fullscreen session that
triggered a save before the "fullscreen state is transient" logic
landed), there was no UI affordance to clear it — the main panel
loaded pinned every session and `PanelDragger.Update` early-returned
on the `IsPinned` check, blocking drag and resize.

Two-part fix:

- `ApplySaveData` now only restores `IsPinned` for panels that opt
  into `RespectsLockOverlays`. The main panel's stale `IsPinned=True`
  is ignored on load, and on the next save (any drag / resize / etc.)
  the current `IsPinned=false` overwrites the persisted `True`, so
  the .cfg self-heals after one session.
- The Settings → Display → Size & Positioning **Default** button on
  the main panel now defensively force-clears `IsPinned`, calls
  `EnsureValidPosition`, and saves — so users currently in the broken
  state can recover in their current session by clicking Default
  instead of needing to restart the game.

The button's tooltip was also reworded to flag the recovery behavior:
"Reset the main panel to its default size + unpin and re-center it.
Recovers from any 'panel feels stuck' state."



## 0.14.0 — Combined info overlay + main panel default-size bump + cleanup

The marquee v0.14.0 feature: a single combined info overlay that
replaces the four standalone info overlays (XP, Familiar, Daily Quest,
Profession) with one draggable / resizable panel containing six
configurable sections — XP, Familiar, Weapon Expertise, Blood Legacy,
Professions, Daily/Weekly Quest. The combined overlay is mutually
exclusive with the individual info overlays: enabling it auto-hides
the four it replaces; disabling it restores them per their own
Show*Overlay flags.

### New: Combined info overlay

Visible structure (when enabled and all sections on):

- **XP** — green section. Lv / progress % / Class / optional progress
  bar / optional `Exp: X / Y (P%)` counter.
- **Familiar** — warm amber. Name + Lv / Pr / HP-PP-SP / optional bar.
- **Weapon Expertise** — light grey. Weapon type + Lv / Pr / chosen
  stat names / decoded `Stats: …` line / optional bar / optional
  bonus-stat values (`+10.5% PhysicalPower …`) and `Exp: X / Y (P%)`
  counter when the relevant HUD-extras toggles are on.
- **Blood Legacy** — Bloodcraft red. Same shape as Weapon, with
  `Ess: X / Y (P%)` counter.
- **Professions** — gold. Two render modes:
  - Wrap-text (compact): `Enchanting 60   Alchemy 45   …` word-wrapped
    inside the panel width.
  - Per-row (when `ShowProgressBarProfessions` is on): eight rows,
    one per profession, each with label + amber bar. Hidden professions
    (`Settings.ShowProfession*` per-profession toggles from v0.13.0)
    drop both their label and their bar.
- **Daily / Weekly Quest** — cyan / gold headers. Target name +
  progress; renders "Complete!" when goal is reached.

Each section has its own bold colored heading (Eclipse-style color
vocabulary), the section background is fully transparent so the
panel's transparency slider correctly controls the entire visible
area, and sub-row heights scale with `Theme.ScaledOverlayHeight` so
the panel doesn't visually overlap rows at Large / X-Large overlay
text scale. The panel auto-fits its height to the configured sections
on construct, on section toggle (`RefreshSections` snaps to the
current `MinHeight`), and on text-scale rebuild — see "Dynamic sizing"
below.

### New: per-section + per-bar visibility controls

Settings → Display → Combined overlay:

- Master toggle: "Use combined overlay (hides individual info overlays)"
- Six per-section checkboxes: XP / Familiar / Weapon / Blood /
  Professions / Quests. XP / Familiar / Professions / Quests write the
  same `Settings.Show*Overlay` flags the footer toggles use, so the
  two surfaces stay in sync — whichever you click, the other reflects
  reality.

Settings → Display → HUD extras → "Show progress bars for":

- Five per-system bar toggles: XP / Familiar / Weapon / Blood /
  Professions. These apply to **both** the standalone overlays AND
  the combined overlay so toggling a bar has consistent effect
  regardless of overlay mode. The pre-v0.14 single global
  `Settings.ShowProgressBars` flag is kept for the Prestige info bar
  in the Prestige tab (its sole remaining consumer).

### New: footer Combined toggle + sync

A new "Combined" toggle leads the footer overlay-visibility row.
When checked, the four conflict toggles (XP / Familiar / Daily quest /
Professions) hide; Familiar Browser + Shift spell stay visible. The
toggle has a tooltip explaining the swap.

`MainPanel.RefreshAllOverlayToggleStates` pushes the current
`Settings.Show*Overlay` values onto every footer toggle AND the
Settings master toggle via `SetIsOnWithoutNotify` — eliminates the
desync where the toggle UI showed stale construct-time state after
the panel's actual visibility changed via mutual exclusion or
programmatic flip.

### New: Bonus stats + XP counter sub-rows on combined

Combined's Weapon and Blood sections render the same bonus-stat
values and numerical XP / Ess counter the standalone XP overlay
shows when `Settings.ShowOverlayBonusStats` / `ShowOverlayXpCounter`
are on. Data flows through:

- `ExperienceOverlayPanel.BonusStatsTick` (the existing `.wep get` +
  `.bl get` auto-fetch loop) — its gate widened to fire when EITHER
  the standalone XP overlay OR the combined overlay is enabled.
- `ApplyCombinedOverlayMutualExclusion` always `EnsureExperienceOverlay`
  even when the standalone is hidden, so the ticker has a host.
- Combined reads four new public accessors on `ExperienceOverlayPanel`
  (`WepGetHasData`, `WepGetRawExpertise`, `WepGetProgressPct`,
  `BuildCleanedWepGetStatsLines()`) for weapon, and reads
  `PlayerStateService.BloodInfoLatest` for blood.
- Combined subscribes to `LastResponseChanged` + `BloodInfoChanged`
  so sub-rows update live as fresh data arrives.

Toggling either HUD-extras checkbox pushes
`RefreshCombinedOverlaySections()` so the sub-rows show/hide
immediately without waiting for the next 10s data event.

### New: removed dead Bloodcraft battle-group commands

A Bloodcraft server admin confirmed in chat: the `.fam abg` /
`.fam cbg` / `.fam sbg` / `.fam dbg` / `.fam bgs` / `.fam bg` /
`.fam challenge` commands appear in the Bloodcraft README but were
never implemented in v1.1+. The entire "Battle Groups" card on the
Familiars tab is removed, the `BCCOM_FAM_BG_*` constants are
deleted, the intercept `StartsWith(".fam bgs")` / `.fam bg ` branches
are removed, and the Mod Help defaults block's
`FamiliarBattles = false` reference line is dropped.

### Dynamic sizing + visual scaling

- Main panel default size bumped 600×380 → 760×560 → **960×700** (two
  rounds of friend-test feedback that the default was "too small to
  read"). Existing users' saved sizes preserved.
- `Theme.ScaledOverlayHeight(N)` applied to all combined-overlay
  LayoutElement heights so labels reserve enough vertical space at
  Large / X-Large overlay text. Prevents text from overflowing into
  the progress bar below it.
- `CombinedOverlayPanel.RefreshSections` snaps panel height to the
  (dynamic) MinHeight after any section / bar toggle — disabled
  sections no longer leave empty space; enabled sections immediately
  get room.
- **Text-scale downshift now shrinks the panel**: `LateConstructUI`
  overridden in `CombinedOverlayPanel` to snap height to `MinHeight`
  after `base.LateConstructUI()` runs (which is what does
  `ApplySaveData` → restore-saved-sizeDelta). Took three iterations
  to land — v6 synchronous (lost to coroutine-deferred ApplySaveData),
  v7 deferred-next-frame (raced with same), v8 LateConstructUI
  override (runs in same call chain after ApplySaveData, no race).
- Section sub-containers built with `bgColor: Color(0, 0, 0, 0)` so
  the panel's transparency slider controls the entire visible area.
  Pre-fix, setting Combined transparency to 100% left an inner
  container visible at ~80% from `Theme.PanelBackground` default.

### Implementation notes (carry forward)

- `CombinedOverlayPanel.LateConstructUI` override: snaps height to
  MinHeight on every construct/rebuild. Side effect: manual height
  resizing of the combined panel does not persist across construct.
  Width + anchors + position persist normally.
- `ExperienceOverlayPanel` exposes 4 new public accessors and one
  helper method. The combined overlay depends on these; if anyone
  refactors that class, the combined overlay's bonus-stats and
  counter sub-rows depend on these stays-public.
- `BCHubUIManager.ApplyCombinedOverlayMutualExclusion` ALWAYS
  ensures `_experienceOverlay` is constructed (even when combined
  is the visible one) so the bonus-stats ticker has a host. The
  ticker's gate now checks "either Enabled OR
  Plugin.UIManager.CombinedOverlay.Enabled".
- `RebuildAllOverlaysNow` includes combined in the rebuild list and
  pushes `RefreshAllOverlayToggleStates` at the end so footer toggles
  match post-rebuild reality.
- New screenshots in `docs/screenshots/v0.13.0 Screenshots/`. README
  image URLs updated to reference them via raw GitHub.



## 0.13.1 — Hotfix: AwaitingBloodInfo timeout spam on Frailed / no-blood states

User-reported bug confirmed reproducible: when a player's blood type
drains to Frailed (or otherwise transitions to a non-bondable type like
VBlood / GateBoss), opening or leaving the BCH UI active starts spamming
the BepInEx log with paired `Intercept armed: AwaitingBloodInfo` /
`Intercept 'AwaitingBloodInfo' timed out with no server reply; resetting`
warnings every few seconds.

### Root cause

Two auto-refresh tickers — `MainPanel.FireBlInfoFetch` (Blood Legacy
tab's per-tab refresh) and `ExperienceOverlayPanel.TickBonusStatsRefresh`
(XP overlay's bonus-stats refresh) — fire `.bl get <Type>` against the
player's current `PlayerStateService.Legacy.Type`. Bloodcraft accepts
the 10 player-bondable blood types (Worker / Warrior / Scholar / Rogue /
Mutant / Draculin / Immortal / Creature / Brute / Corruption) but
rejects unit-category markers (Frailed / VBlood / GateBoss). Sending
`.bl get Frailed` against a Frailed-blood player produces no server
reply; the armed `AwaitingBloodInfo` intercept times out, gets
re-armed on the next tick, times out again — a cycle the user can't
exit short of closing the panel.

The existing guard was `if ((int)leg.Type != 0) ...` which was wrong
in both directions: it falsely SKIPPED `Worker` (enum value 0 — a valid
bondable blood) and let `Frailed` (value 6) through.

### Fix

- New `PlayerStateService.IsBondableBloodType(BloodType)` helper —
  single source of truth for "will Bloodcraft reply to `.bl get` for
  this type?" Returns true for the same 10 types listed in the
  `BloodTypeChoice` enum (the .bl cst / .bl get form picker subset).
- `MainPanel.FireBlInfoFetch` early-returns when the predicate is
  false. `Worker` blood now refreshes correctly; `Frailed` /
  `VBlood` / `GateBoss` are silent no-ops.
- `ExperienceOverlayPanel.TickBonusStatsRefresh` (the alternate-tick
  branch that handles `.bl get`) does the same.

No data-flow change otherwise — when the player drinks a normal blood
again, the next tick's auto-fetch resumes silently and the Blood Info
display + XP overlay bonus-stats update as before.



## 0.13.0 — Mod Help reference + per-profession toggles + class context cards

Information-architecture release. v0.12.x added quality-of-life UI
features (color theme, handshake retry, Game Guide tab). v0.13.0 turns
its attention to the OTHER half of usability: Bloodcraft is a deep mod
and prior versions made you swap to Quick Start or chat to remember
what each class / stat / prestige tier actually does. This release puts
that reference information exactly where the user needs it — both as a
comprehensive Mod Help tab and as inline context cards on the tabs the
user touches when making decisions.

### New "Mod Help" tab under Settings & Help

Section-by-section reference for every Bloodcraft system: XP leveling,
weapon expertise, blood legacies, the six classes (with full per-class
weapon + blood synergies + on-hit debuff school), prestige, Exo
prestige + Exoforms, familiars, professions, daily/weekly quests.

Each section uses the same three-part shape:

- **Bold gold section heading** (always visible)
- **Overview paragraph** (always visible — one-paragraph plain-English
  summary of the system)
- **Details collapsible** (collapsed by default — numeric specifics
  and non-obvious rules, e.g. rested-XP math, per-stat cap values,
  per-class on-hit debuff names, exoform duration formula)
- **Default settings collapsible** (collapsed by default — the
  `Bloodcraft.cfg` defaults the server admin can override; useful for
  understanding what your server's tuning departs from)

Tab opens compact (every detail/defaults block collapsed); the user
expands only what they care about. Content sourced from Bloodcraft
v1.13.21's README, source, and ConfigService — including the per-class
weapon-synergy + blood-synergy + on-hit-debuff mappings from
`Utilities/Classes.cs` and the exoform duration formula
`15 + (165 / 100) * exoLevel` from `Utilities/Shapeshifts.cs`.

### Inline class context cards on action tabs

The user explicitly asked for this after the first Mod Help pass:
"as users are actively using the Class / Weapon Expertise / Blood
Legacy / Prestige tabs to make their changes, they will have access to
the information relevant to the changes they are making." So we
duplicated the relevant Mod Help slice into each action tab.

- **Class tab** — Active Class card extends with a live class-details
  block (archetype + tagline + weapon synergies + blood synergies +
  on-hit debuff). A new "Compare all classes" collapsible inside the
  Change Class card shows all six classes side-by-side for picking.
- **Weapon Expertise tab** — new "Class synergies" card between
  Current Weapon Expertise and Actions. Top line: which weapon stats
  your CURRENT class amplifies (1.5× cap). Inside collapsible: every
  weapon stat's baseline cap at L100 plus the prestige math.
- **Blood Legacy tab** — mirror: which blood stats your class
  amplifies, plus every blood stat's baseline cap.
- **Prestige tab** — new "What each prestige tier gives you" card with
  three collapsibles:
  - Leveling-prestige per tier: −5% XP, +10% expertise/legacy rate,
    +1 class-spell unlock per tier
  - Weapon Expertise / Blood Legacy prestige per tier: −10% rate,
    +10% cap, max 10 tiers
  - Exo Prestige (endgame): 100 tiers, 500× Primal Stygian Shards per
    tier, exoform duration formula, `.fam echoes` cost scaling

Each context card carries a one-line italic disclaimer that the
defaults shown are subject to server-admin overrides in
`Bloodcraft.cfg`.

### Implementation: single source of truth

`ClassInfoByClass` static dictionary holds the per-class display
name + archetype + tagline + weapon synergies + blood synergies +
on-hit debuff + secondary self-buff for all six classes. Three
helpers (`FormatClassDetailsBlock`, `FormatClassWeaponSynergyHint`,
`FormatClassBloodSynergyHint`) feed the inline cards. The Mod Help
tab keeps its own inline text (for readability of the markdown-style
prose) but the data values are identical — touch the dictionary if
Bloodcraft 1.14+ changes any synergy and the four action tabs
auto-update; Mod Help requires the same parallel edit.

`RenderClass` (the existing class-change event handler) now also
rewrites `_classDetailsLabel`, `_wepClassSynergyLabel`, and
`_blClassSynergyLabel` in place so a class change on any tab updates
the context cards on the other three live.

### Per-profession overlay toggles

Settings → Display → "Professions tracked" — eight checkboxes
(Enchanting / Alchemy / Harvesting / Blacksmithing / Tailoring /
Woodcutting / Mining / Fishing) gate each profession's row + bar on
the Professions overlay. Default-on preserves the v0.12.x render
exactly so existing users see no change unless they uncheck.

Implementation: eight new `Settings.ShowProfession*` flags, gated at
render time in `ProfessionOverlayPanel.Render` via SetActive on each
label + bar. `BCHubUIManager.RefreshProfessionOverlay()` pushes the
re-render on toggle without rebuilding the overlay. Flag names chosen
to forward-feed the planned v0.14.0 combined overlay's per-section
toggles — the same checkboxes will eventually gate the combined
overlay's profession section.

### Visual refresh — gold section markers + 14pt body text

Friend-test on the v0.13.0 pre-release: "these pages contain a lot of
information, harder to read than other pages because the text is
small." Two coordinated changes:

- **Section markers.** `AddSectionHeading` now renders a thin 2-pixel
  warm-gold (`#E5B85C`) divider band immediately above every heading,
  and the heading text itself in gold + bold + italic at fontSize 16
  (was plain white-italic at 14). The gold band acts as a scannable
  "section starts here" cue when scrolling long Mod Help / Quick Start
  content. Gold was picked for high contrast against every panel
  background preset (dark + bright).
- **Body fontSize.** `AddGuideSection` body and the new
  `AddCollapsibleHelpDetail` body both bumped 12 → 14, making the
  prose on the Help-group tabs noticeably more readable. Layout
  unaffected because the TMP wrap + ContentSizeFitter pipeline handles
  the height growth.

Side effect: data-display tabs (Familiars, Prestige, Levels) use the
same `AddSectionHeading` so their section breaks get the gold-band
treatment too — judged net-positive (clearer visual hierarchy
everywhere).

Empty-title `AddSectionHeading` calls (Vanilla Admin uses these for
body-only paragraphs) now skip the heading + divider entirely
instead of rendering an empty stripe.

### README / Thunderstore description refresh

- Updated screenshot section with five v0.11.2 captures (Class /
  Logistics / Weapon Expertise / V-Bloods / Familiars) provided by a
  community user.
- Added a prominent "⚠ Heads-up before you install" section at the
  top of the README covering:
  - Eclipse mod incompatibility (workaround: disable Eclipse while
    using BloodCraftHub; fix forwarded to Eclipse's author).
  - Pre-1.0 testing status, with the The Shadow Realm Discord
    (https://discord.gg/usC9QgBrXK) as the primary bug-report channel.
- Status line refreshed to v0.13.0 with the v0.12 / v0.13 changes
  summarized.



## 0.12.1 — Bloodcraft availability retry + Game Guide tab + bright interior presets

Three focused additions on top of the v0.12.0 color-theme work.

### Bloodcraft availability — handshake retry + live UI refresh

Pre-0.12.1, the Bloodcraft tab group could render as "(unavailable)" on
servers that DO run Bloodcraft, simply because the user opened the panel
faster than the Eclipse-protocol handshake could ACK. Two compounding
problems caused this:

1. The handshake was sent ONCE — `SendRegistration` gated on
   `!UserRegistered && !RegistrationPending`, so once Pending flipped
   true no further attempts ever fired. If the server's first ACK was
   dropped (or the server hadn't loaded Bloodcraft at our handshake
   time), the user stayed stuck.
2. `IsTabGroupAvailable("Bloodcraft")` returned plain `UserRegistered`
   at panel construction time and the tab strip never refreshed —
   even when the handshake eventually succeeded, the panel kept
   showing "(unavailable)" until the user closed and reopened it.

Fixed by:

- `EclipseProtocolService.SendRegistration` now retries up to
  `REGISTRATION_MAX_ATTEMPTS` (3) times with
  `REGISTRATION_RETRY_AFTER_SECONDS` (5 s) between attempts. ~15 s
  total before giving up. A new `RegistrationGaveUp` flag latches
  true at the cap so we don't spam the server forever — and `Reset()`
  clears all retry state for the next session.
- New `EclipseProtocolService.AvailabilityChanged` event fires on
  successful ACK after a late retry, AND on give-up. `MainPanel`
  subscribes in `BuildTabStrip`, defers a frame via
  `CoreUpdateBehavior.Actions`, then walks `_groupHeaderText` /
  `_groupHeaderButton` to update header text, color, and
  interactability in place — no panel rebuild, no scroll-position
  loss, no flicker.
- `IsTabGroupAvailable("Bloodcraft")` now returns true during the
  retry window (`UserRegistered || !RegistrationGaveUp`). Users
  aren't locked out of the Bloodcraft tabs while detection is in
  flight; they only see "(unavailable)" once the retry cap has
  actually been hit.

### New "Game Guide" tab in the Settings & Help group

A new tab between Quick Start and Settings that surfaces V Rising
resources (the game itself, not BCH). Sections:

- **Official** — playvrising.com (Stunlock Studios homepage)
- **Community resources** — V Rising Wiki (Fandom), CaDrift
- **Discord** — V Rising official Discord invite

Each link uses an "Open" button that hands the URL to
`Application.OpenURL` so it launches in the user's default browser.
Closing note points to the BCH GitHub issues page for suggesting
additional resources in future versions.

### Bright interior color preset row + readability fix

The interior background-color picker now offers TWO preset rows
under separate sub-headings:

- **Dark variants** — the original 0.12.0 palette
  (Default / Black / Slate / Wine / Forest / Indigo / Crimson, all
  in the ~0.07–0.23 max-channel range).
- **Bright variants** — saturated twins of each, max channel
  ~0.40–0.65. Crimson Bright = `#A30000` matches `Theme.Level1`
  exactly, so one click restores the pre-0.12.0 framework red.

The section's help paragraph and "Dark variants / Bright variants"
sub-labels switched from the muted grey (`Theme.MutedBodyHex`) to
italic white. Muted-grey on the brighter presets failed the
readability bar; italic white stays hierarchically distinct from
the bold section heading while reading cleanly on every preset,
dark and bright.



## 0.12.0 — Split Toggle/Unbind buttons + two-zone panel color theme

First feedback-bundle release since the 0.11.2 hotfix. Two user-requested
quality-of-life improvements, both opt-in via the existing Settings UI.

### Familiar Browser footer: Toggle / Unbind active split

The single "Unbind active" button on the Familiar Browser overlay's footer
is replaced by two side-by-side buttons.

- **Toggle** (left, `.fam t`) — calls / dismisses the active familiar
  without changing the binding. The use case that drove the split:
  dominating an NPC auto-disables the familiar but does NOT auto-re-enable
  it on release (flying and teleporting do auto-re-enable). Toggle puts
  the familiar back into combat in one click.
- **Unbind active** (right, `.fam ub`) — removes the active binding. The
  familiar returns to your box and can be re-bound any time. NOT
  destructive — the box record is preserved. (Permanent box deletion is
  `.fam r N` on the main Familiars tab.)

Both buttons disable together when no familiar is bound. Tooltips describe
the distinction directly so the dominate-NPC workflow is discoverable from
the button itself.

### Two-zone color theme for every panel + overlay

Settings → Display now has two color-preset sections — one for the OUTER
panel chrome and one for the INTERIOR scroll area. Each section offers
seven curated presets (Default / Black / Slate / Wine / Forest / Indigo /
Crimson) plus a Reset button and a "Current: #hex" indicator. Power users
can hand-edit `BloodCraftHub.cfg` for any hex they want.

**Outer color** (`Settings.PanelBackgroundColorHex`, default `#121212`)
applies to every panel BCH builds — the main panel, Familiar Browser, and
all five info overlays (XP, Familiar, Daily Quest, Profession, Shift
Spell). Structural background Images recolor in one pass via a tree walker
in `UIFactory.ApplyBackgroundColorRgbToPanel`. Card-style mid-grey accents
(`Theme.CardBackground`) are detected and preserved so section grouping
inside the tabs survives the recolor.

**Interior color** (`Settings.InnerPanelBackgroundColorHex`, default
`#121212`) targets the scroll-view wrappers and viewports inside the main
panel (each tab's content scroll) and the Familiar Browser (the familiar
list scroll). The framework was painting the wrapper Image with
`Theme.Level1` = `(0.64, 0, 0)` — actual bright red. That's the strip of
red color that was visible inside both panels in every prior version. The
new default `#121212` masks it on construct so even users who never touch
the picker get a near-black look from session start.

Per-panel transparency is unchanged — the existing per-overlay
transparency sliders continue to own each panel's alpha independently of
either color picker.

### Implementation notes (for future maintenance)

- `PanelBase.ConstructUI` calls `RefreshBackgroundColor()` +
  `RefreshInnerBackgroundColor()` after `ConstructPanelContent()`. Panels
  opt in via the new virtual flags `UsesCustomBackgroundColor` (every
  panel returns true) and `UsesCustomInnerBackgroundColor` (only
  `MainPanel` + `FamiliarBrowserOverlayPanel` return true).
- `BCHubUIManager.RefreshAllPanelBackgrounds()` and
  `RefreshScopedInnerBackgrounds()` push live picks at runtime from the
  Settings UI click handlers.
- Color storage is hex string in `.cfg` for legibility. RGB parsing falls
  back to the documented default if a user breaks their `.cfg`, so a bad
  edit can't crash the UI.



## 0.11.2 — CRITICAL: panel can no longer grow larger than the screen

Friend-test (severity: stuck-can't-play): a player resized the main panel
into a fullscreen-stretched state, then either toggled Auto-resize OR
clicked the panel border to manually resize. The panel grew larger than
the screen, covering most of their display in red, and because they had
the overlay-lock active they couldn't drag, resize, or close it. Save
data persisted the bad state so reloading didn't recover. Visible BepInEx
log spam:

```
[Error  :BloodCraftHub] Exception loading panel save data:
System.ArgumentException: '3399.5' cannot be greater than -3399.5.
   at System.Math.ThrowMinMaxException[T](T min, T max)
   at PanelBase.EnsureValidPosition()
   at PanelBase.SetDefaultSizeAndPosition()
   at ResizeablePanelBase.ApplySaveData(String data)
```

### Root cause: stretched anchors invert sizeDelta semantics

`SetFullscreen(true)` sets `anchorMin=(0,0)/anchorMax=(1,1)` (stretched).
With stretched anchors, `RectTransform.sizeDelta` no longer represents
the panel's pixel size — it represents the OFFSET from the parent on
each axis. Setting `sizeDelta.y = 700` while in stretched mode means
"make me 700 pixels TALLER than the parent canvas," producing a panel
much larger than the screen.

Two code paths assigned `sizeDelta.y` directly after the anchors had
been stretched:

1. `MainPanel.AutoResizeIfEnabled` — when the user toggled the
   Auto-resize setting while already in fullscreen, the auto-resize
   path computed `desired = contentHeight + chrome`, clamped it to
   `Screen.height * 0.9` (a reasonable cap for centered anchors but
   meaningless for stretched), then assigned it as `sizeDelta.y`.
2. `PanelDragger`'s resize-drag handler — the moment the user clicked
   the panel border with intent to resize, the dragger started writing
   `Rect.sizeDelta = new Vector2(width, height)`. Same trap.

Once the panel exceeded the screen, `EnsureValidPosition`'s
`Math.Clamp(value, minPos, maxPos)` got bounds with `minPos > maxPos`
and threw `ArgumentException`. The exception propagated up through
`SetDefaultSizeAndPosition` → `ApplySaveData` → `LateConstructUI`, so
even the "restore to default" fallback path crashed and the panel was
left at whatever invalid state it landed in.

### Fix in five layers

1. **`PanelBase.EnsureValidSize` — hard cap to screen dimensions.** New
   `GetMaxAllowedSize()` helper returns `referenceResolution / uiScale
   - 10px margin`. Every panel now has a strict upper bound enforced
   via `Rect.SetSizeWithCurrentAnchors`, which works correctly with
   both centered AND stretched anchors. No panel can occupy more
   space than the canvas, regardless of MaxWidth settings, save-data
   corruption, or stretched-anchor sizeDelta misinterpretation.
2. **`PanelBase.EnsureValidPosition` — handle min > max without
   throwing.** When the panel is somehow larger than the screen on an
   axis (e.g. mid-cleanup of a bad save), the position bounds invert.
   Old code threw `ArgumentException`. New code returns position = 0
   (center) on that axis when bounds are inverted. Belt-and-suspenders
   alongside the size cap above.
3. **`PanelBase.SetDefaultSizeAndPosition` — reorder.** Pre-0.11.2 the
   sequence was `EnsureValidPosition()` then `EnsureValidSize()`. That
   order required `Rect.rect.width/height` to already be ≤ screen for
   the position clamp to work. Swapped: size first, position second.
4. **`MainPanel.AutoResizeIfEnabled` — bail when fullscreen.**
   Auto-resize doesn't make sense for a canvas-stretched panel; early-
   return when `_isFullscreen == true`.
5. **`MainPanel.SetFullscreen` — pin during fullscreen, don't
   persist.** Force `IsPinned = true` while fullscreen; restore prior
   pin state on exit. `PanelDragger.Update` early-returns when
   pinned, blocking BOTH drag and edge-resize — only the maximize
   button works. Also removed the trailing `OnFinishResize()` call so
   the fullscreen state doesn't persist to config; closing the game
   in fullscreen comes back in the pre-fullscreen layout.

### Stuck users self-heal on next launch

A player on 0.11.1 with the corrupted save state will, on next launch
with 0.11.2 installed: `LateConstructUI` runs `ApplySaveData`,
`EnsureValidSize` caps the restored panel to screen dimensions,
`EnsureValidPosition` clamps position with valid bounds (no throw),
panel comes up in a normal size at the center of the screen. The error
log entry `'3399.5' cannot be greater than -3399.5` is gone.

## 0.11.1 — Shift overlay fixes + V-Blood overlay row cleanup

Iterative friend-test fixes on top of 0.11.0. Five small commits worth of
work, bundled into one bump because 0.11.0 never made it past the local
machine — the v0.11.0 commit (35c0e6f) is tagged alongside 0.11.1 so the
history captures both, but only 0.11.1 ships to Thunderstore.

### Shift overlay finally tracks the cooldown

The 0.11.0 shipped overlay structurally rendered (square tile, SHIFT label,
radial-fill sprite) but failed to ever populate a cooldown. Three bugs
stacked on top of one another:

1. **Reading the wrong `LocalCharacter`.** `Services/ShiftCooldownService`
   pulled the local character from `Core.LocalCharacter` — a stub that
   was never wired up. `Core.Initialize(world)` lives commented-out at
   `Patches/GameManagerPatch.cs:12`, so `Core.HasInitialized` stays
   false forever and `Core.LocalCharacter` stays `Entity.Null`. The
   live character is at `Plugin.LocalCharacter`, set by
   `Patches/InitializationPatch.cs:67`. Same applies to
   `Core.EntityManager` / `Core.ClientWorld` — the working accessors
   are on `Plugin`. Service now reads `Plugin.LocalCharacter`,
   `Plugin.EntityManager`, and `Plugin.EntityManager.World`.

2. **Detection criterion was wrong for Bloodcraft overrides.** Earlier
   passes gated `HasShiftSpell` on
   `AbilityGroupSlotBuffer[3].BaseAbilityGroupOnSlot.GuidHash != 0`.
   That's the *base* prefab in the slot — but Bloodcraft uses
   `ReplaceAbilityOnSlotBuff` to overlay class spells onto the shift
   slot, leaving the base prefab empty even when the slot is fully
   usable. New detection: the slot's `GroupSlotEntity` being non-null
   is sufficient. The shift's actual prefab GUID is latched
   separately by watching `AbilityBar_Shared.CastGroup` for any cast
   with `SlotIndex == 3` — that fires the first time the user presses
   shift and persists for the session.

3. **Cooldown end-time was overwritten with 0 every poll.**
   `AbilityCooldownState` only lives on the cast-ability entity (the
   per-cast wrapper), and between casts `AbilityBar_Shared.CastAbility`
   points at a different ability's wrapper (typically the primary
   attack). The old code re-read fresh every poll and assigned
   directly — so any poll where the read missed wiped `_cooldownEnd`
   back to 0 and blanked the visible countdown. Fix: monotonic latch.
   `_latchedCooldownEnd` only ever advances; reads that fail leave
   the latch alone, reads that observe a later end-time refresh it.
   Local server-time then ticks the visible remaining seconds down
   without needing fresh game reads.

### Shift overlay redesigned as a square button

Friend-test: "look more like a button with a radial countdown, rather than
a bar." Previously a wide horizontal MiniBar; now an 80×80 outlined tile
with the cooldown text centered, a radial dark-overlay sweep clockwise
from 12 o'clock, and a SHIFT label below. Tile background brightens to a
cool-blue when ready, mutes to a darker tone while cooling down. Tile-
center cooldown text bumped to size 26 with a TMP outline so digits stay
readable against both the bright ready-state tile and the dark radial
sweep mid-cooldown.

Radial sprite uses `Texture2D.whiteTexture` as a 1×1 source stretched to
the tile's rect via `Image.type = Filled`, `fillMethod = Radial360`. The
white sprite was the simplest path that avoided procedural-texture
generation gotchas in IL2CPP.

### V-Blood overlay rows: drop the [box] suffix

Friend-test: "the rows in the V-Blood view should only show name, shiny,
attribute, and level — same as the normal familiar rows in box mode."
Stripped the trailing `[boxName]` segment from
`FamiliarBrowserOverlayPanel.RenderVBloodView` row labels. Format is now
exactly `<idx> — <name>  Lv X  Pn  ★ school` — identical to the BoxView
per-familiar format.

### Lock-overlays now covers the shift overlay

Wasn't actually broken — `ShiftSpellOverlayPanel` already inherited
`RespectsLockOverlays => true` from `ResizeablePanelBase` — but worth
calling out: flipping the global "Lock overlays" toggle now pins the
shift overlay alongside every other overlay, so accidental drags during
combat are no longer possible.

### Diagnostic line is opt-in

A small `pf / cg / si / end / srv` debug line under the SHIFT label
helped pinpoint the cooldown-read bugs (it surfaced the `no-char` state
that pointed straight at the dead Core stub). New config setting
`ShiftSpellOverlayShowDiagnostics` (default off) gates whether the line
renders. If we ever need to debug shift-state issues again, flip it on
in `kdpen.BloodCraftHub.cfg` under `[Overlays]` and re-open the overlay.

### Implementation notes

- `ShiftCooldownService` now exposes a `Diag*` block of public static
  fields used solely by the debug line. They stay zero-cost when the
  diagnostic toggle is off (the panel doesn't read them).
- The shift-prefab latch resets when `Plugin.LocalCharacter` changes
  identity (character swap) so a fresh-character session doesn't
  carry stale data.
- `ShiftSpellOverlayPanel.MinWidth/MinHeight` shrunk back to 140×120
  now that the default panel doesn't reserve diag-line space. Players
  who already dragged their overlay larger keep their saved size —
  these are floors, not the rendered size.

## 0.11.0 — Friend-test feedback bundle: Primals fix, All-Familiars tab, shift overlay, X-Large text, box dropdowns

This release bundles six items the friend-test group surfaced in 0.10.x.
Each is a discrete feature on its own; together they fill in the
remaining rough spots in the familiar workflow + accessibility story.

### Primal V-Bloods now appear in the V-Bloods collection

Friend-test: "Primals are not coming up in the V Blood List."

Root cause: the V-Blood scanner's `TryClassifyName` stripped the
`Primal ` prefix from incoming familiar names and looked up the
**remainder** against the full V-Blood registry. But Bloodcraft
server-side actually names primal variants as `Primal <FirstWord>`
(per Bloodcraft's own changelog example: `.fam sb "Primal Frostmaw"`
where the base V-Blood is *Frostmaw the Mountain Terror*). So the
classifier compared `"Frostmaw"` against the full registry list of
`"Frostmaw the Mountain Terror"` etc. and never matched.

Fix: `VBloodRegistry` now precomputes a stem map — for each registry
name, the substring before `" the X"` (or the full name when no
`" the "` separator is present, e.g. `"Putrid Rat"`). The scanner
now tries the original full-name match first (defensive — in case
Bloodcraft ever switches conventions), then falls back to the stem
match. `"Primal General Elena"` correctly routes to *General Elena
the Hollow*; `"Primal Adam"` routes to *Adam the Firstborn*; etc.

The underlying data model (`VBloodInstance` with `IsShiny + IsPrimal`)
was already in place from 0.10.9 — only the name classifier had the
bug. So all four variants (basic / shiny / primal / primal-shiny)
now populate correctly per V-Blood.

### V-Bloods overlay rows redesigned to show shiny / level / attribute

Friend-test: "The VBLOOD menu text in Familiar browser overlay needs
updating — doesn't all fit and looks messy. Can't see shiny status,
attribute, or level. It should resemble the normal familiars menu
in the overlay, but only show V Bloods."

Before: one compact row per V-Blood with `B S P Ps` chips showing
whether each variant was captured. Functional but lost the per-
instance level / prestige / shiny-school context.

After: one row per **captured variant**, formatted identically to
the BoxView's per-familiar row — `01 — Primal Adam  Lv 50  P2  ★ Storm  [box03]`.
A V-Blood with all four variants now occupies four rows; one with
only a basic capture occupies one. Uncaptured V-Bloods don't appear
in the overlay (they still appear in the main panel's V-Bloods tab
with the chip view + Summon for missing entries).

Sort modes work per-row: Default groups by box+index; Alphabetical
sorts by base name with basic before primal; Level descending; and
Location uses the region-page order from `VBloodRegistry`. Click any
row to smart-summon via `VBloodSummonService` (same flow as before).

### New "All Familiars" tab in the main panel

Friend-test: "Add a list similar to V Bloods but for All Familiars,
to search, filter, and sort by alpha, level, box, location. Include
an in-line button in the list to delete out unwanted familiars."

New top-level tab inside the Bloodcraft tab group (between V-Bloods
and Class). Lists every familiar across every box that the V-Blood
scanner walked — same data source as the V-Blood collection, so a
single Scan-all populates both views.

Per-row controls:
- **Bind** — switches to the row's box and binds the familiar
  (issues an unbind first if you have an active familiar).
- **Delete** — two-click confirm; `.fam cb <box>` then `.fam r <idx>`.
  First click changes the label to "Confirm?" for 3 seconds.

Header shows total familiars / total boxes / shiny count. Filter is
a free-text "contains" filter (case-insensitive). Sort cycles:
Box+index (default) → A→Z → Level descending → Shinies first.

### Box-mutation forms now use dropdowns of existing boxes

Friend-test: "In the Boxes forms (Delete box, rename box, move
familiar between boxes) load dropdown list of boxes names, so they
don't need to be typed in."

New `BoxNameDropdownField` form-field type. Populates from
`PlayerStateService.BoxList` at form-build time and stays in sync
when the box list changes (subscribes to `BoxListChanged`). If the
list is empty when the form opens, it auto-sends `.fam boxes` so the
dropdown self-populates as soon as the server replies. Wired into:

- **Delete empty box (.fam db)** — the `boxName` field is now a dropdown.
- **Rename box (.fam rb)** — the "Current name" field is now a dropdown
  (the "New name" stays a text input).
- **Move active familiar to box (.fam mb)** — the destination is a
  dropdown.

The free-text Create-new-box (`.fam ab`) form keeps its TextField —
that one needs you to type a brand-new name that doesn't exist yet.

### X-Large font scale option

Friend-test: "Some users still advised they struggled to read large
mode."

Added a fourth tier (1.50× multiplier) to both the UI and Overlay
text-scale rows in Display Settings — Small (0.85) / Standard (1.0)
/ Large (1.2) / **X-Large (1.5)**. Same picker pattern; same
"close and reopen the panel for new sizes to take effect" caveat.

Plus a layout-height plumbing pass — `Theme.ScaledHeight()` and
`Theme.ScaledOverlayHeight()` multiply layout heights in lockstep
with the font multiplier so labels don't clip at X-Large. Applied
inside the central helpers — `AddInfoLabel`, `AddSectionHeading`,
`AddBodyText`, `AddCard`, `AddStatRow`, `AddCommandButton`,
`FormBuilder` rows + submit button, `FormField` inputs / dropdowns /
toggles, `CollapsibleSection` headers. Scaling is gated to ≥ 1.0
so Small / Standard layouts are pixel-identical to pre-0.11.0;
only Large and X-Large stretch heights. Tab-local custom layouts
may still need per-tab tweaks — friend-test will surface them.

### Eclipse-style shift-spell cooldown overlay

Friend-test: "Add in shift spell button from Eclipse — a button you
could place on your window with a loading circle representing the
cooldown."

New draggable overlay (`ShiftSpellOverlayPanel`) with a "SHIFT"
label, a horizontal cooldown bar (ice-blue, matching MiniBar style),
and a remaining-cooldown countdown ("Ready" / "1.4s" / "2s"). When
the equipped shift spell has multiple charges (e.g. some class
shifts), the label shows the count: "SHIFT  2/3".

The overlay reads game state directly via a new
`ShiftCooldownService` — local character's `AbilityBar_Shared` →
shift slot's `AbilityGroupState` (slot 3) → `AbilityCooldownState`
+ `AbilityChargesState`. Polled at 10 Hz inside the service; the
overlay just renders the latest values. All IL2CPP access is
wrapped in a single try/catch so the overlay degrades gracefully
to "(unavailable)" if anything goes sideways rather than killing
the per-frame ticker.

Note (vs. Eclipse): clicking the BCH overlay does nothing. V Rising's
input bus doesn't accept "fire shift" from a UI click — pressing
your bound Shift key remains the cast trigger. The overlay is a
visual cooldown readout, not a chat-bound shortcut. If you've also
installed Eclipse, both overlays will work side-by-side (they read
the same game state, so the numbers agree); BCH's lives wherever
you drag it, Eclipse's stays anchored to the ability bar.

Toggle it on via the main panel's Display Settings (Show overlays
row), or persist it via `Settings.ShowShiftSpellOverlay`. Per-
overlay transparency setting available alongside the others.

### Implementation notes

- `VBloodRegistry.TryResolvePrimalStem(suffix, out canonical)` is
  the new lookup helper; the scanner calls it from
  `VBloodScannerService.TryClassifyName`.
- `MainPanel.AllFamiliars.cs` is a new partial-class file so the
  6000+ line `MainPanel.cs` doesn't grow further.
- `BoxNameDropdownField.OnBoxListChanged` mutates `Dropdown.options`
  directly instead of using `AddOptions(IL2CppList)`; the latter
  has bridge quirks on this Unity build, the former matches the
  upstream UIFactory.CreateDropdown pattern.
- `Theme.ScaledHeight()` clamps the multiplier at ≥ 1.0 so the
  scaling is additive — Small / Standard layouts are unchanged.
- `ShiftCooldownService.GetServerTimeOnServer()` resolves
  `ClientScriptMapper._ClientGameManager.ServerTime.TimeOnServer`
  at call time rather than caching, so a session reset doesn't
  leave us holding a stale reference.

## 0.10.14 — Drag/resize regression fix, Lock-overlays toggle

### Drag/resize regression fixed

Friend-test after 0.10.13: "I'm unable to click and expand the UI
and the overlays." Drag still worked; resize-by-edge did not.

Root cause: `PanelDragger` caches its 10-px-border resize hit-area
in `_resizeMask` at construction (and refreshes it in
`OnEndResize` — i.e. only after a manual drag-resize). When the
panel changes size *programmatically* — auto-resize on tab open,
collapsible expand, last-response panel filling, the 0.10.13
layout changes that resized the panel slightly — the cache pointed
at the OLD bottom border. The user clicked the visible bottom edge
of the panel and the cached mask still thought "bottom" was 60 px
higher, so resize hover/click didn't register. Pre-0.10.13 this
bug existed but the auto-resize delta was small enough that the
stale-cache offset stayed within the 10-px tolerance band; 0.10.13's
forceExpand fix made the delta larger and exposed the edge case.

Fix: `MainPanel.AutoResizeIfEnabled` now calls
`Dragger?.OnEndResize()` immediately after a programmatic
`Rect.sizeDelta` change, which refreshes `_resizeMask` against the
new rect. The same `OnEndResize` call already exists in
`SetFullscreen`, `SetDefaultSize`, and `AdjustSize` paths — this
patches the one remaining hole.

### Lock-overlays toggle

Friend-test: "as I ran into the resize issue I started accidentally
dragging overlays around — could you add a lock toggle on the main
panel beside Auto-resize so I don't accidentally click and drag an
overlay during play?"

Added `Settings.LockOverlays` (default off) plus a **Lock overlays**
toggle in the main panel's footer row 2 (right of Auto-resize panel).
When on:

- Every overlay's `IsPinned` flag is set to true.
- `PanelDragger.Update` early-returns when `IsPinned` is true, so
  there's no drag, no resize-by-edge, no resize-hover cursor on
  any overlay.
- Programmatic resize via `Rect.sizeDelta` (auto-resize on data
  arrival, Display Settings size nudges, V-Blood scan growing the
  list) is unaffected: `IsPinned` only short-circuits user mouse
  input, not direct rect mutations.

Setting persists across sessions. New overlays constructed after
the lock toggle is flipped (e.g. toggling an overlay off then back
on) automatically pick up the lock state via a new
`RespectsLockOverlays` virtual on `ResizeablePanelBase` —
`MainPanel` overrides it to false so the main panel itself never
gets pinned by the lock.

### Implementation notes

- `PanelBase.IsPinned` setter widened from `protected set` to
  public set so the main panel's lock toggle can drive it from
  outside the panel class.
- New `BCHubUIManager.ApplyOverlayLockState()` reads the setting
  and pins/unpins every live overlay in one call.
- `ResizeablePanelBase.LateConstructUI` reads
  `Settings.LockOverlays` after restoring save data and applies
  `IsPinned = true` if `RespectsLockOverlays && Settings.LockOverlays`.
  This handles the "lock was on at last session" case.

## 0.10.13 — Italic-text readability, vertical-scale layout fix, overlay-toggle reformat

### Italic body text replaced with muted normal-weight

Friend-test: "there is a good amount of text in italics, which in
standard mode text size is difficult to read." The 0.10.9 design
used italic styling at small sizes (typically 11 pt) to signal
"secondary content" — but italic glyphs render less crisply in V
Rising's TMPro fallback font, and the small size compounded the
issue.

0.10.13 drops italic from every body-text helper and prose-hint
label, and bumps default sizes 11 → 13:

- `AddBodyText` helper (all card-wrapped prose hints across Levels /
  Familiars / Boxes / Class / Expertise / Blood Legacy / Quests /
  Kindred / Logistics tabs)
- `AddAdminWarningIntro` (the three Kindred Admin intro paragraphs)
- Tooltip footer text (the bottom messages bar)
- Chat Logging settings help paragraph
- Size & Positioning settings help paragraph

The muted-grey color (`Theme.MutedBodyHex`) still does the "this is
secondary content" visual job. Italic was redundant emphasis that
hurt legibility without adding meaning.

### Main panel — bottom footers no longer grow vertically

Friend-test: "when you expand vertically [the main panel], the
lower portion of the UI where the messages display for the tooltips
grows unnecessarily when the area that's more important to grow is
the pane where the settings and the text are."

Cause: PanelBase creates ContentRoot's `VerticalLayoutGroup` with
`childForceExpandHeight=true`. Unity's vertical layout distributes
extra space EQUALLY among all children when force-expand is true,
regardless of per-child `flexibleHeight` — so the LastResponse
panel + OverlayFooter + TooltipFooter all absorbed extra space
alongside the tab content area when the user dragged the panel
taller. This is the vertical analog of the horizontal tab-strip
fix from 0.9.8 (where `forceExpandWidth=true` was making the tab
strip grow with panel width despite `flexibleWidth=0`).

Fix: `MainPanel.ConstructPanelContent` now sets the ContentRoot
VLG's `childForceExpandHeight = false` before building any children.
Only `body` (flex=1) absorbs extra vertical space — the footers
stay at their preferred heights.

Same fix applied to `FamiliarBrowserOverlayPanel.ConstructPanelContent`:
the box-name row, status line, and Unbind footer no longer grow
when the overlay is resized — extra space goes to the scrollable
familiar list (also flex=1). Friend-test: "the box label container
is still unnecessarily large" was the same Unity-quirk symptom.

### Overlay-toggle footer reformatted as a labeled container

Friend-test: "the overlay toggle buttons look like loose buttons —
reformat it so it's clear they're a container to manage overlay
visibility."

The bottom-of-panel toggle row now reads as a single grouped
container:

- Wrapped in a `Theme.CardBackground` inset box (10 px padding) so
  the toggles visually belong together.
- Added a bold **"Show overlays:"** label on the left of the toggle
  row that frames the group as overlay-visibility management.
- Auto-resize panel toggle moved to its own row below so it doesn't
  get mistaken for an overlay toggle.
- Toggle labels shortened ("XP overlay" → "XP", "Familiar overlay"
  → "Familiar") so the row fits comfortably alongside the new
  label prefix.

### Known limitations not addressed

The `MainPanel.ConstructPanelContent` fix and the FamiliarBrowser
overlay fix both flip the VLG's `childForceExpandHeight` on
ContentRoot. The other overlay panels (Experience, Familiar,
Daily Quest, Profession) keep the inherited PanelBase default
(force-expand=true), so their rows still grow when over-resized.
None reported friction; can be flipped per-overlay if needed in a
follow-up.

## 0.10.12 — Admin / Kindred / Logistics tab polish, more form replies wired to UI

### Tab polish — remaining unaddressed surfaces

0.10.9 polished Levels / Prestige / Familiars / Weapon Expertise /
Blood Legacy. 0.10.10 covered V-Bloods. 0.10.11 covered Boxes /
Class / Unarmed Shift / Daily Quest. 0.10.12 extends the card-wrap
pattern to the previously-unaddressed tabs:

- **Bloodcraft Admin** — admin info note + diagnostics row wrapped
  in cards. Trailing note converted to `AddBodyText` for muted styling.
- **Kindred Logistics** — Intro (with inline `.l` / `.lg` Mono
  command-name accents), Personal Toggles, Utility wrapped in cards.
- **Kindred Logistics Admin** — admin info, Admin Globals, Admin Item
  Spawn each in their own card.
- **KindredCommands Player** — Intro, Self, Server Info, Lookups
  wrapped in cards.
- **Kindred Admin (Players / Server / World)** — `AddAdminWarningIntro`
  helper now produces a card-wrapped italic-muted intro paragraph,
  which cascades across all three tabs.

Settings, Vanilla Admin, QuickStart, and About were already
sufficiently structured by their existing helpers (`AddGuideSection`,
`AddTextScaleRow`, etc.) — those receive no per-tab card-wrap but
benefit from the 0.10.11 `AddCard` padding bump (6 → 10).

### More form-reply commands surface in the UI

The 0.10.11 fix routed `.fam s` results into a dedicated panel on the
Familiars tab. 0.10.12 extends that principle to several more chat
commands whose replies were previously chat-only:

- `.fam sb "<name>"` (smart-bind) — single-match bind confirmation,
  multi-match clarification list, or no-match error now all land in
  the global LastResponse panel.
- `.misc sct <type>` (toggle scrolling combat text) — new state reply.
- `.lvl log` / `.quest log` / `.prof log` (per-system progress
  logging toggles) — new state reply.
- `.misc silence` (reset stuck combat music) — confirmation reply.

These were added to `ShouldArmGenericCapture` in
`MessageService_Processing.cs` AND classified with
`HasBchUIDisplay = true` in `CommandClassifier.cs`, so:

1. The chat reply is now captured + mirrored to
   `PlayerStateService.LastResponse`, which the global "Last server
   response" panel at the bottom of the main panel renders.
2. The Bloodcraft chat-logging toggle can suppress the chat copy
   (since the UI now shows it).

Friend-test gap from 0.10.11: "ensure that, in cases like this where
I'm specifically using a form to query information, that information
is returned to the user." This pass closes the gap for the most
common form-driven info commands.

### Known limitations / deferred to a later release

- **Settings tab** still uses its bespoke layout helpers
  (`AddTextScaleRow`, `AddTransparencyRow`, toggle rows) and isn't
  card-wrapped. It's already well-organized; nothing user-actionable
  is hidden by the current structure.
- **QuickStart / About** use `AddGuideSection` which renders headings
  + wrapped prose cleanly. Not touched for 0.10.12.
- **Vanilla Admin** has the 0.10.7 `AddCommandTableRow` + 0.10.8
  ContentSizeFitter wrap-fix; visually fine. The 0.10.7 intro
  `AddGuideSection` calls aren't card-wrapped — minor follow-up if
  the user wants it.
- A few outbound commands still go to chat only (battle-group
  challenge replies, .clan list pagination details, KindredCommands
  /KindredLogistics admin actions, vanilla console-style commands BCH
  can't trigger). Most are pure action confirmations; surfacing the
  rest case-by-case as users report friction is the right path.

## 0.10.11 — Search-result UI surface, missing-glyph cleanup, overlay compression, more tab polish

### Form-search results now render in the UI

Friend-testing 0.10.10: "I used the Search boxes by name form on the
Familiars tab and no results show up — they must be in chat but I
have BCH chat suppression on." Correct diagnosis. The parser was
already firing `MessageService.FamSearchCompleted` for every `.fam s`
reply (the same event the box-sweep scanner consumed in 0.10.8 and
earlier), but no UI subscriber was rendering the result.

0.10.11 adds an in-panel **Last search result** card at the bottom
of the Familiars tab's More Actions section. On every `.fam s` reply
it shows:

- Header: `Search: "name"  →  N box(es)` or `→ no matches`
- One row per matching box, with a `★ shiny` indicator when the
  server included the pink-star marker for that box

So now the result is visible in the UI regardless of the Chat Logging
toggle state.

### Missing-glyph cleanup

Friend-testing 0.10.9/0.10.10: "there's a square or shape that's
shown, almost like a placeholder, with no color or visual." V
Rising's TMPro fallback font is partial — only `←`, `→`, `★`, and
`•` are confirmed to render reliably. 0.10.9 added action-button
prefixes (`⚔ ⚒ ⚙ ℹ ⊘ 🔒 🎁 ♪ ↻ ↺ ▾`) and a V-Bloods shiny chip
(`✦`) that all degraded to blank squares.

0.10.11 strips every unreliable glyph from button labels and section
headings, plus the Blood Legacy heading drop (`🩸`). The V-Bloods
shiny column now uses `★` (confirmed working) instead of `✦`. The
visual identity stays intact via the existing card tints (XP cyan,
Legacy crimson, Expertise copper, Familiar violet, Profession green,
Quest gold) — those do the section-coloring job that the glyphs were
meant to reinforce.

### Familiar Browser overlay — vertical-space compression

Friend-testing: "there's extra space around the box label and below
Unbind — could you reformat the heights so more of the familiar
list shows?" Three changes:

- Box-name row tightened: minHeight 24/preferred 26 → 20/22, zero
  padding (was 6 px sides), nameLbl 22/24 → 18/20.
- Status line word-wrap turned off and overflow mode set to Ellipsis,
  so a long familiar name truncates instead of growing the row into
  a 2-line block that steals scroll-list space.
- Footer tightened: minHeight 30/32 → 26/28, unbind button 26/28 →
  24/26.

Net: about 12 px more familiar-list height in the default overlay
size.

### Divider overlap fix

The 0.10.9 `AddDivider` helper passed a Vector4 padding in the wrong
axis order (this codebase's Vector4 maps to `(top, bottom, left,
right)` per `UIFactory.cs:254` — easy to get backwards). The 12-px
"side padding" landed on top/bottom inside a 7-px wrap, which
collapsed the inner layout and caused the divider line to render
on top of the body text that followed it. The friend-test report
was the Familiars tab's "Switch to the Boxes tab" line. AddDivider
is now built from spacers + a 1-px Image at the parent level instead
of a HorizontalLayoutGroup wrap, eliminating the padding-order trap.

### More tab polish (Boxes, Class, Unarmed Shift, Daily Quest)

The 0.10.9 polish covered Levels / Prestige / Familiars / Weapon
Expertise / Blood Legacy, and 0.10.10 covered V-Bloods. 0.10.11
extends the card-wrap pattern to the next batch:

- **Boxes** — active-box header + tip wrapped in a Familiar-tinted
  card so the label is no longer flush with the panel border.
- **Class** — Active / Actions / Change Class as three cards.
  Expertise-tinted current state.
- **Unarmed Shift** — Shift Spell / Unarmed Expertise / Actions as
  three cards.
- **Daily Quest** — Daily / Weekly / Settings as three quest-tinted
  cards.

Default `AddCard` inner padding bumped 6 → 10 px so labels inside
every card (across the whole panel) have more left/right breathing
room. Friend-test wording: "ensure that within containers, down to
the lowest level where the text itself is, there is at least a
little left padding." This single default change cascades.

### Known limitations not addressed in 0.10.11

- Kindred admin tabs (Players / Server / World), Kindred Logistics
  tabs, Vanilla Admin, Settings, QuickStart, and the Bloodcraft Admin
  tab still use the un-carded layout. They'll get the same card-wrap
  pattern in a follow-up. The bumped default `AddCard` padding helps
  every existing card across the whole panel, but tabs that don't
  use AddCard yet won't see the change.
- Other forms that emit info-replies to chat (`.fam sb` smart-bind
  list, `.fam bg <group>` battle-group details, `.class l` /
  `.quest l` listings, etc.) don't yet have dedicated UI surfaces.
  The `LastResponse` generic-capture pipeline already mirrors many
  of these to per-tab "Last server response" panels, but a few
  still need explicit wiring.

## 0.10.10 — Scan opt-in by default, chat noise eliminated, overlay rebuilt, tabular V-Blood rows

### V-Blood scan now opt-in by default

Pre-0.10.10 the V-Bloods tab fired `VBloodScannerService.StartScan()`
the first time the user opened it with an empty collection. The
0.10.9 box-sweep takes ~30-60 seconds and walks every box — fast
enough that an unannounced auto-scan was the dominant friend-testing
annoyance. The behavior is now manual: open the tab, click **Scan all**.

Added `Settings.AutoScanVBloodsOnTabOpen` (default `false`) plus a
Display Settings toggle ("Auto-scan V-Bloods when the V-Bloods tab
opens") for users who liked the old behavior and want to opt back in.

### Scan chat noise eliminated

The 0.10.9 box-sweep used `EnqueueMessageSilent`, but the silent flag
only propagated to the BchAuto category for STRUCTURED intercepts —
the `.fam cb` action-confirmation suppression branch and the
`AwaitingBoxList` / `AwaitingBoxContent` legacy "ClearServerMessages"
gate didn't honor it. Result: every box switch the scanner made
leaked a `Box Selected - <color=white>{box}</color>` line into chat,
plus all the per-entry `.fam l` rows for users who hadn't turned
`ClearServerMessages` on.

Two fixes:

1. `NoteOutboundForIntercept` now arms a parallel `_actionForceSuppressUntil`
   window whenever a familiar-action command is sent via the silent
   path. The action-suppression branch in `HandleInboundChat` honors
   either the user's `SuppressFamiliarActionChatter` setting OR the
   new force window — so scanner-issued `.fam cb` confirmations are
   eaten regardless of user setting.
2. The `.fam boxes` and `.fam l` intercept-arming branches now call
   `ClassifyAndStoreCategory` (matching the pattern used by the prestige
   / blood-info / fam-search branches), and the BoxList / BoxContent
   receive handlers consult `ShouldSuppressByCategory()` alongside the
   legacy ClearServerMessages gate. The scanner classifies as BchAuto
   (default hidden), so all of its replies stay out of chat.

### V-Bloods tab — tabular row layout + header padding

Friend-testing 0.10.9: "the attributes are showing out of place
between rows — they're all the way across and adjusted differently
per row." Root cause was the shiny-school column being OMITTED for
non-shiny rows, which collapsed every column to its right.

Strict fixed-width columns now: **Type** (36) | **Name** (flex) |
**Lv** (70) | **Shiny** (96, with `—` placeholder when not shiny) |
**Box** (100) | **Summon** (78). A column-header row above the
list labels each column. The shiny column always renders even when
empty, so Name / Box / Summon stay column-aligned across all rows.
Missing rows use the same column slots with `—` placeholders so they
align with captured rows.

Card padding cascades through the tab — Header, Progress, Filter,
and Rows are each wrapped in their own `AddCard` so labels and
buttons no longer sit flush with the panel border. The "X / Y
captured" progress label gets proper inner padding now.

### Familiar Browser overlay — rebuilt layout + Scan button

Friend-testing: "the box name and count overlap with the left and
right buttons sometimes." Old layout packed ← BoxName → Reload into
one row and View / Sort into another. The new layout has FIVE rows:

1. **Toolbar** — every button: `← → Reload Scan View Sort`
2. **Box name + count** on its own line ("Name (X / N)")
3. **Mode / active-familiar / scan-progress status**
4. **Scrollable list** (built unchanged)
5. **Unbind footer**

Box name now has its own line and can't be crowded by buttons. Adding
the **Scan** button alongside Reload gives users a way to trigger a
full V-Blood scan without switching back to the main UI's V-Bloods
tab. The button cycles to **Cancel** while in-flight, mirroring the
main-tab Scan all behavior; the overlay's status line shows live
scan progress (`Scanning… box 3 / 14 — RoyalLineageBox`).

### Cleanup

- `OnScanStateChanged` subscription is now properly unregistered in
  the overlay's `Reset()` path alongside `VBloodCollectionChanged`.
- `FlushBoxList` / `FlushBoxContent` now call `ResetCaptureCategory()`
  after parsing — the new classify-on-arm for those intercepts means
  the receive-side category state must be reset so the next regular
  EnqueueMessage doesn't inherit BchAuto by accident.

## 0.10.9 — V-Bloods rebuilt (box-sweep, per-variant rows), cross-cutting visual polish

### V-Bloods: box-sweep scanner + per-variant rows

The 0.10.0–0.10.8 V-Blood path used 130 `.fam s` search queries
(basename + "Primal " + basename for each of 64 V-Bloods) and aggregated
per-name flags from the lossy reply (`box-name, anyShinyInBox`). Two
unfixable problems with that approach surfaced during friend-testing:

1. The reply doesn't distinguish basic vs shiny within a box — a box
   holding BOTH a basic and a shiny Alpha came back as one row, so
   the chip view couldn't show both.
2. The 4-minute scan window let any other `.fam` command the user typed
   clobber the shared `_intercept` state — summons were inconsistent
   afterward because the scanner's "best box" was sometimes stale or
   wrong.

0.10.9 replaces the search-based scanner with a box-sweep:

```
.fam boxes  → for each box, .fam cb <box> + .fam l  → snapshot per-entry
              (index, name, level, prestige, shiny, shiny-school, primal)
```

This is strictly more information (Bloodcraft's `.fam l` reply already
carries per-entry shiny + color-hex-derived school + primal-prefix
detection), and it runs in ~30–60 seconds across a typical 5–15-box
inventory instead of ~4 minutes. The scanner snapshots
`PlayerStateService.ActiveBox` at scan start and restores it at the
end, so the user's box-tab navigation isn't disrupted.

Data-model rewrite: `VBloodCaptureStatus` now stores a
`List<VBloodInstance>` — one entry per *captured variant* (basic /
shiny / primal / primal-shiny) with its exact (box, index, level,
prestige, shiny-school). The four `HasBasic / HasShiny / HasPrimal /
HasPrimalShiny` flags and `BestBox` / `BestIndex` are derived from
that list for backwards compat with the Familiar Browser overlay's
name-only smart-summon path.

The V-Bloods tab is no longer a chip-vs-instance toggle — it's a
single list where each row is one captured variant. Each row carries
a color-coded variant tag (`[B]` green / `[S]` cyan / `[P]` gold /
`[PS]` pink), the name, level/prestige, shiny school (when shiny), the
box, and a Summon button. Un-captured V-Blood names render as muted
"(not captured)" rows under All / Missing filters. The new
`VBloodSummonService.SummonVariant(name, isShiny, isPrimal)` targets
the exact captured instance — no more guessing, no more "which Alpha
do I get?" ambiguity. Summon refuses with a status message if a scan
is currently running, so the two flows can no longer race each other.

### Theme additions

- `Theme.CardBackground` — slightly lighter than `PanelBackground`,
  used by the new `AddCard` helper to give a section a subtle inset
  surface.
- `Theme.MutedBody`, `Theme.MutedBodyHex` — dim grey for muted
  explanatory prose. Replaces the pattern of stacking italic-white
  helper text on the same background as primary labels.
- `Theme.AccentMono`, `Theme.AccentMonoHex` — muted-cyan accent for
  inline command-name spans (`.wep cst`, `.fam l`, …). TMPro can't
  swap to a monospace font without us shipping one, so color +
  bold-weight does the visual emphasis.
- `Theme.DividerLine` — 55% grey, used by `AddDivider` for hairline
  breaks between logical groups inside a card.
- `Theme.SystemTintXP / Legacy / Expertise / Familiar / Profession /
  Quest` — 6%-alpha background washes tied to the six progression
  systems, used to color-code cards in Levels and Prestige.

### Layout helpers (in MainPanel.cs)

- `AddCard(parent, name, tint?, padding, innerSpacing)` — padded
  inset container with `Theme.CardBackground`; optional tint draws an
  ignore-layout `Image` BEHIND the card's children so the wash
  modulates the background without affecting layout.
- `AddStatRow(parent, label, value)` — left-label / right-value
  table row. Replaces stacked single labels in dense data sections.
- `AddDivider(parent, sidePadding)` — 1-px hairline between groups.
- `AddBodyText(parent, text)` — muted italic prose label. ContentSize-
  Fitted so it grows with wrapped text.
- `Mono(s)` — wrap a string in the accent-color rich-text span for
  inline-command emphasis.

### Tab polish (Levels, Prestige, Familiars, Expertise, Blood Legacy)

Pre-0.10.9 these tabs were stacked walls of labels on a single panel
background — the audit called this "basic HTML on a red background."
Polish applied:

- **Levels** — every progression system (Player XP, Blood Legacy,
  Weapon Expertise, Familiar, Professions) sits in its own
  system-tinted card. Profession Tools and Player Tools cards
  organize the action buttons and collapsibles. Action buttons get
  Unicode glyph prefixes (`↻`, `⚙`, `ℹ`, `★`, `⚔`, `⊘`, `🔒`, `🎁`,
  `♪`) so a row scans faster. Muted-prose `AddBodyText` replaces the
  italic helper labels and embeds `Mono(".fam t")` / `Mono(".lvl log")`
  spans.
- **Prestige** — the four prestige summary lines (XP / Legacy /
  Expertise / Familiar) become four tinted cards instead of a padded
  VLG of four labels. Each card carries the system tint matching its
  Levels-tab counterpart so the visual coding is consistent.
- **Familiars** — Active Familiar / Actions / Emote Bindings / More
  Actions / Battle Groups each become explicit cards with section
  headings inside. Collapsibles now nest into the More Actions and
  Battle Groups cards (visually grouped instead of floating loose).
  Action-row buttons get Unicode glyphs.
- **Weapon Expertise** & **Blood Legacy** — current state / Actions
  / Choose Bonus Stat each become cards with tints
  (Expertise / Legacy). Dividers separate the collapsibles from the
  trailing body text, and prose hints use `AddBodyText` with `Mono`
  command-name spans.

### Minor V-Blood progress display refinements

Header progress text now reports total instances captured alongside the
unique-name count: `12 / 65 captured  ·  18 instances  ·  3 primal  ·
4 shiny`. Scan status surfaces the current box being read in real
time (`Scanning… box 4 / 14 — RoyalLineageBox`) instead of a generic
query counter.

### Cleanup / removed code

- `VBloodCollection` no longer keys off the `.fam s` reply path; the
  old `_famSearchSuccessRegex` / `_famSearchBoxTokenRegex` / scanner
  infrastructure stays in place to support the manual user-triggered
  `.fam s` form on the Familiars tab, but `FamSearchCompleted` no
  longer has any subscribers.
- The V-Bloods tab's instance-view toggle and BoxContents-walking
  fallback are gone — one view, one source of truth.

## 0.10.8 — V-Blood scan/summon correctness, XP-overlay scale fixes, overlay edge padding, About cleanup

### V-Bloods: shiny is no longer double-counted as basic

Pre-0.10.8 the scanner set BOTH `HasBasic=true` AND `HasShiny=true` for a
single shiny-only capture. The chip view then drew both `B` and `S` chips
even though shiny is a *buff on* a basic familiar — not a separate
capture. Friend-testing surfaced this as "I only have a shiny Alpha but
the UI shows me as having both."

Root cause: the search reply tells us per-box `(name matches, at least
one of those matches has the shiny star)`. Pre-0.10.8 we ratcheted
`HasBasic=true` on ANY match and `HasShiny=true` only when the star was
present — so a shiny-only capture flagged both. Fixed interpretation:

- box returned WITHOUT the star → `HasBasic = true`
- box returned WITH the star    → `HasShiny = true`

Same logic for `HasPrimal` / `HasPrimalShiny`.

Caveat: a box that contains BOTH a non-shiny and a shiny of the same
name still returns a single star-marked row in the `.fam s` reply, so
the basic-of-that-name will show as missing in chip view until the user
opens that box's `.fam l`. The Instance view (0.10.7) already
reconciles that case from BoxContents and is the precise tool when the
same name has multiple captures.

### V-Bloods Summon: no more spurious .fam ub when nothing is bound

Pre-0.10.8 the Summon button (both chip-view and per-instance) always
pre-issued `.fam ub`. With no familiar bound, Bloodcraft replied
*"Couldn't find familiar to unbind! If this doesn't seem right try
using .fam reset"* — and that reply leaked into chat even with the
chat-suppression toggles on. Two underlying bugs:

1. The "is a familiar active?" check used `Familiar.Name != "" ||
   Level > 0`. `EclipseProtocolService` masks an empty server name with
   the placeholder `"Familiar"` and floors level to 1, so the check
   was ALWAYS true.
2. The unbind-failure reply wasn't in
   `IsKnownFamiliarActionConfirmation`, so action-chat suppression
   skipped it.

Fixes:

- Added `FamiliarState.HasActive`, sourced from the raw protocol name
  field (set before the placeholder mask), and switched every "is a
  familiar bound?" callsite to use it (Summon, Levels tab,
  FamiliarOverlay, MainPanel `RenderFamiliar`). Side effect: the Levels
  tab and Familiar overlay now correctly show "(no familiar bound)" /
  "Lv —" when nothing is bound, instead of "Familiar Lv 1".
- Added `"Couldn't find familiar to unbind"`, `"Couldn't find familiar
  actives"`, and `"Active familiar doesn't exist"` to the action-chat
  suppression patterns so any future code path that speculatively
  unbinds doesn't leak the failure message either.

### V-Bloods Summon: bind step now actually runs after a box switch

Pre-0.10.8 the Summon flow enqueued `.fam cb <box>` + `.fam l` to load
the target box's contents and bind by index — but never called
`PlayerStateService.SetActiveBox`. `FlushBoxContent` keys the parsed
entries by `PlayerStateService.ActiveBox`, so the entries got either
dropped (when ActiveBox was null, common case — user hadn't visited
the Boxes tab) or written under the previously-active box's key. Either
way, `BoxContentsChanged` either didn't fire OR fired with the wrong
key, so `VBloodSummonService.OnBoxContentsChanged` never resolved the
index and `.fam b N` never went out. Symptom: Summon issued an unbind
+ box switch, then sat silent.

Both summon paths (the shared `VBloodSummonService` and the per-
instance MainPanel path) now call `PlayerStateService.SetActiveBox`
before the `.fam cb` so `FlushBoxContent` keys the entries under the
right box, `BoxContentsChanged` fires, and the index lookup resolves.

### XP overlay: no row/bar overlap at Large text scale

At Standard scale (1.0×) rows looked correct. At Large (1.2×) the
weapon's "Bonus Stats" sub-label and the blood-legacy "Bonus Stats"
sub-label overflowed their fixed 32 px LayoutElement-preferredHeight
and drew up into the progress bar above them; main rows similarly
overflowed the fixed 20 px allocation. Two fixes:

- `AddRow` now scales the row's preferredHeight with the row's font
  size (`max(20, round(fontSize * 1.45))`). At any scale a 13 pt line
  gets a 19 px row, a 16 pt line gets a 24 px row.
- `ConfigureBonusStatsLabel` clears `LayoutElement.preferredHeight`
  (sets it to `-1`) so the ContentSizeFitter resolves to TMP's actual
  wrapped pixel height instead of being pinned at 32. Bonus rows now
  match their rendered glyphs at every scale.

`MinHeight` was also rebuilt to compose its floor from the same
`ResolveRowHeight` formula, so a Large-scale overlay opens at a size
that fits its own content instead of starting cramped.

### Vanilla Admin reference: rows now grow with wrapped descriptions

The 0.10.7 tabular layout wrapped the description column correctly but
the row's LayoutElement hard-coded `preferredHeight=22`, so the wrapped
second/third line drew into the row below. Added a ContentSizeFitter
on the row itself and set the row's `preferredHeight=-1` so the row
expands to whichever description is the tallest.

### Overlays: configurable left/right edge padding

Pre-0.10.8 every overlay's text sat flush with the panel border, and
the Familiar Browser's V-Blood list specifically butted up against the
scrollbar's left edge with no gutter. Added a Display Settings control
(`Overlay edge padding`, default 6 px, range 0..32) that applies
left/right padding to every overlay's `ContentRoot` VerticalLayoutGroup
plus the Familiar Browser's scroll-content VLG (so the scrollbar
gutter is symmetric with the panel edges). Wired into the
`RequestRebuildAllOverlays` lifecycle so +/- nudges take effect live.

### About tab cleanup

Reorganized into four explicitly-spaced regions — Header (version +
description), Mods (Bloodcraft / KindredCommands credits), About the
author (Discord links + support), Project (GitHub / Thunderstore /
license). Spacers between regions and section headings give the page
the visual breathing room friend-testing said it was missing.

## 0.10.7 — V-Blood scanner fix, overlay polish, per-instance V-Blood view, bump-process hardening

### V-Blood scanner: quoted query args (CRITICAL bug fix)

Pre-0.10.7 the scanner sent `.fam s Alpha the White Wolf` (no quotes). VCF
only consumes the first whitespace-delimited word as a positional arg, so
it failed to match Bloodcraft's `.fam s` command signature and instead
echoed Bloodcraft's `usage:` template — the literal string `.fam s [Name]`.
What you saw in chat: `[SYSTEM] [VCF] .familiar search .fam s [Name]`
repeating every 2s for the entire 60+ V-Blood scan. The `[Name]` was a
literal placeholder from the usage string, not a substituted value.

Two problems caused by this:

1. The scanner returned no results for any multi-word V-Blood (~60 of 64
   names contain spaces). The V-Bloods tab stayed empty.
2. The VCF usage echo didn't match any of the AwaitingFamSearch reply
   patterns, so the silent-suppression flag never fired on it. Replies
   leaked to chat regardless of the BCH-auto Chat Logging toggle.

Fix: wrap every `.fam s` / `.fam sb` / `.fam echoes` arg in double quotes
(`.fam s "Alpha the White Wolf"`). Updated both the format constants and
the user form templates so manual UI searches work for multi-word names
too. The intercept-arming Substring parse strips the wrapping quotes so
the scanner correlation (`string.Equals(captured, expected)`) still
matches the unquoted form.

Defense in depth: also added an explicit VCF-usage-echo recognizer in
the AwaitingFamSearch handler. If any future BCH path sends a malformed
`.fam s` and trips the usage echo, the scanner now advances as no-match
and the chat line gets suppressed unconditionally rather than leaking.

### XP overlay polish

- **Progress bar overlap fix.** Pre-0.10.7 the label rows had
  `minHeight: 18` while the rendered glyph height at default text scale
  is ~20 px. When the overlay was sized at its previous MinHeight (180)
  with progress bars on, the cumulative preferredHeight exceeded the
  panel and the VerticalLayoutGroup compressed rows below their
  glyph-render height — adjacent labels visually overlapped the 12px
  bar above. Tightened `minHeight = preferredHeight = 20` so the
  layout group can't compress below the rendered text height.
- **Computed MinHeight.** Replaces the static 180/300 floor with a
  formula derived from what's actually rendered:
  `25 (title) + 6 always-on rows × 20 + bars × (height+2) + bonus stats × 36 + counter × 20`.
  Toggling bars / bonus stats / XP counter on grows the floor enough
  to fit the new content without forcing the user to drag-resize.
- **Weapon stats: preamble filter.** The cached `.wep get` reply included
  the redundant "Your weapon expertise is X, prestige Y, and you have Z
  expertise with W!" preamble — the same data the Weapon row title
  already shows. The bonus-stats sub-label now filters out the
  preamble and the "no bonuses" / "haven't gained any expertise"
  placeholders, leaving only the actual "TypeName Stats: ..." bonus
  lines.
- **Optional XP counter row** (Settings → HUD extras → "Show numerical
  Exp / Ess counter on the XP overlay"). Renders
  `Exp: 123 / 4500 (2.7%)` under the Weapon row and the equivalent
  `Ess: ...` under the Legacy row. The threshold is derived from the
  raw count + percentage that Bloodcraft prints in the `.wep get` /
  `.bl get` chat reply, so it's accurate to within ±1. Off by default.
- **Progress bar height settings** (Settings → HUD extras). Absolute
  pixel height with +/- nudge buttons (clamped 4..24, default 8) and a
  companion "Scale bar height with overlay" toggle (off by default —
  pre-0.10.7 the bars stretched with the overlay, which read as
  inconsistent when users enlarged the overlay for new info rows).
- **Optional prestige sub-line in progress bars** (Settings → HUD
  extras). Eclipse-style: a slim 30%-height inset fill at the bottom
  of each main bar reflecting `Level / MaxLevel` (progress toward the
  next prestige tier). Applies to all three bars (XP / Weapon /
  Legacy). Off by default.

### V-Blood per-instance view (item 8)

The chip view (1 row per V-Blood NAME with B/S/P/Ps capture chips)
is great for "do I have this one?" but doesn't reflect that you might
have multiple captures of the same V-Blood at different levels, and
sort-by-level was meaningless against the chip aggregation.

New "View" toggle button on the V-Bloods tab cycles between **Chips**
(default, 0.10.0..0.10.6 behavior) and **Instances** (one row per
captured familiar with explicit Lv / Pr / Shiny school / Primal /
Box / Summon button). Sort-by-level now does what users expect.
Summon targets the specific row's exact box + index rather than the
"first match" guess — so a user with four Alpha the White Wolfs at
different levels can pick which one to summon.

Caveat (phase-1 implementation): instance view uses cached
`PlayerStateService.BoxContents`, populated by manual box navigation
(Familiars → Boxes) or the Familiar Browser overlay. Boxes you
haven't visited won't appear yet. An automatic "Deep Scan" that
cycles every box silently (with active-box restore) is on the roadmap
for a follow-up.

### Vanilla Admin reference: tabular layout

The Vanilla Admin tab's per-section command listings used a single
multi-line label with manually tab-padded alignment. Proportional fonts
misaligned the columns based on the longest command in each section.
New `AddCommandTable` helper renders each entry as a fixed-width bold
command column + a wrap-enabled description column, so every section
now has crisp alignment regardless of entry length.

### Build-then-bump process hardening

v0.10.6 shipped with a DLL embedding `PLUGIN_VERSION=0.10.5` because
the csproj bump happened AFTER the last `dotnet build`. The About tab
showed 0.10.5 in-game even though every text file in the repo said
0.10.6 — wasted debugging cycles.

Three fixes:

1. **`tools/bump-version.ps1` now auto-runs `dotnet build -c Release`
   at the end** (skip with `-NoBuild`). The freshly-bumped DLL is the
   one that lands in `bin/Release` ready for deploy.
2. **Preflight release check verifies DLL AssemblyVersion matches
   csproj `<Version>`.** Reads the metadata via
   `AssemblyName.GetAssemblyName(...)` so it doesn't load the assembly
   into the PS AppDomain. Catches the failure mode if anyone edits
   code after bump and forgets to rebuild.
3. **CLAUDE.md updated** with the new bump-then-build invariant so
   future sessions don't repeat the v0.10.6 mistake.

### Minor

- `MiniBar.CreateWithSubLine` is the new bar constructor — returns
  both the main fill RT and an optional sub-line fill RT. The existing
  `MiniBar.Create` is now a forwarder that discards the sub RT.
- `MiniBar.SetHeight` lets a consumer live-resize an existing bar's
  container height; used by the overlay's per-render
  `ApplyBarChrome()` so toggling Settings.ProgressBarHeight takes
  effect without rebuilding the overlay panel.

## 0.10.6 — Chat Logging diagnostic section + Vanilla Admin tab audit

Two features landing together.

### Chat Logging

New section at the bottom of the Settings tab. Three per-category
toggles for chat visibility plus master Show-all / Hide-all buttons.
Designed and implemented per the design discussion: suppression
applies ONLY to commands whose data BCH already mirrors to its own UI
surfaces — anything BCH doesn't structurally parse (action confirmations,
admin replies, Kindred commands BCH hasn't wired structurally yet)
stays visible regardless of the toggles. This guarantees no user ever
loses the ONLY visibility of a server reply.

**Categories** (and defaults):

- **BCH internal auto-fires** — default OFF (hidden). Replies to BCH's
  own background commands (V-Blood scanner `.fam s`, overlay bonus-stats
  ticker `.wep get` / `.bl get`, tab auto-refresh). Toggle ON for
  diagnostic visibility when troubleshooting BCH itself.
- **Bloodcraft command replies** — default ON (visible). Replies to
  user-initiated Bloodcraft commands BCH structurally parses
  (`.fam boxes` / `.fam l` / `.fam s` / `.fam gl` / `.bl get` / `.wep get` /
  `.prestige get`). Off = the BCH UI is the only place the data shows.
- **Kindred command replies** — default ON. Same shape as Bloodcraft
  but for KindredCommands / KindredLogistics. BCH doesn't structurally
  parse any Kindred replies in 0.10.6, so the toggle is currently a
  no-op — wired in advance for future structured Kindred parsing.

**Master buttons:**

- `Show all mod chat` — flip all three categories on (useful during
  diagnosis).
- `Hide all mod chat` — flip all three off (maximum chat quiet). Does
  NOT touch the global `ClearServerMessages` admin setting — those
  are independent.

**Spontaneous notifications stay visible.** Game-system messages that
aren't replies to BCH commands — quest progress, level-up notifications,
familiar capture events, server announcements, player join/leave — flow
through the inbound chat path with `_intercept = Idle`, so none of the
chat-logging suppression touches them. They remain in chat regardless
of category settings.

**Implementation:**

- New `Services/CommandClassifier.cs` — classifies outbound commands
  into `(CommandCategory, HasBchUIDisplay)` pairs. Prefix-based for
  user-fired commands; `EnqueueMessageSilent` forces BchAuto.
- `MessageService_Processing` — the 0.10.2 `_suppressCurrentCaptureChat`
  per-intercept bool is replaced by `_currentCaptureCategory` +
  `_currentCaptureHasBchUI`. Receive-side handlers route through
  `ShouldSuppressByCategory()` which reads the appropriate setting.
- Settings — three new bools (`ShowChatBchAuto` / `ShowChatBloodcraft` /
  `ShowChatKindred`) plus `ShowAllChat()` / `HideAllChat()` master
  helpers. Persisted across sessions.

**Eclipse compatibility:** unchanged from 0.10.2/0.10.4. The
suppression only destroys plain colored chat entities Eclipse already
ignores (Eclipse's prefix gates on the MAC-signed `[ECLIPSE]` pattern).

### Vanilla Admin tab audit

The Help → Vanilla Admin tab was a reference list of vanilla V Rising
console commands. Audit added the commonly-missed entries:

- **New Character actions section**: Suicide, KillPlayer, RevivePlayer,
  HealPlayer, DamagePlayer, ResetCharacter, KillUnit, HealUnit,
  DamageUnit, Despawn.
- **Player management expanded** with: PlayerInfo, UserList,
  WhoIsOnline, ForceConnectInfo.
- **Item / character spawning expanded** with: SpawnCastle, FillStorage,
  ClearAllInventories, DespawnAll.
- **New Teleportation section** (split from spawning): teleporttowaypoint,
  TeleportToPlayer, TeleportToBoss, TeleportToHorse, TeleportToOwner,
  TeleportToWorld, UnlockAllPlayerWaypoints, MapMarker.
- **New Time, world & difficulty section**: Time, ChangeMapTime,
  SetTimeOfDay, weather, GameDifficulty, Lockdown, alllockdown.
- **Server administration expanded** with: AutoSave, StopAutoSave,
  ReloadServerSettings, Restart, ShowVersion, ShowAdminCommands.
- **New Debugging / display section**: DebugHud, ShowDebugUI, ShowFPS,
  ShowInputBindings, BlockUserInput, Console.SetCheats.
- **Authoritative list note** at the bottom: points users at the in-game
  `List` command as the live source of truth, since V Rising's command
  set drifts between patches and any reference list can go stale.

These remain documentation-only entries — vanilla console commands
require the in-game F1 console and can't be triggered by chat-based
client mods like BCH. Chat-command equivalents for the common admin
actions are still wired in the KINDRED → Admin tabs.


## 0.10.5 — V-Blood overlay view + .wep get first-line suppression

**V-Blood view toggle in the Familiar Browser overlay.** New `View: Box`
/ `View: V-Bloods` cycle button on the overlay's second header row.
Box view is the existing box-by-box browser; V-Blood view replaces the
list with the captured V-Blood collection sourced from
`PlayerStateService.VBloodCollection` (populated by the scanner running
off the main UI's V-Bloods tab).

V-Blood rows are compact one-liners: `Name  [B][S][P][Ps]  box03`.
Chip colors mirror the main tab's convention (green = captured, gray =
missing). Click a row → triggers the smart summon via the new shared
`Services/VBloodSummonService` (unbind active → switch box → fetch
contents → bind by index). The header row's status text reports
summon progress (`Summoning Alpha the White Wolf from box01 (slot 3)…`).

Header restructured into two rows so the new toggle fits inside the
default overlay width:
- Row 1: `← BoxName → [Reload]` (hidden in V-Blood mode)
- Row 2: `[View: …] [Sort: …]` (visible in both modes)

Sort settings apply to both views — Region/Location mode works in
V-Blood view because every entry has a canonical region; in Box view
the Region mode is collapsed to Default (a single mixed-region box
doesn't benefit from regional ordering).

**`.wep get` first-line chat suppression fix.** v0.10.4 silenced the
stat lines (those start with `<color=#c0c0c0>`) but the FIRST line of
each Bloodcraft `.wep get` reply starts with plain text — `"Your weapon
expertise is..."` — and slipped past the generic-capture `<color`-prefix
filter. So even with silent enqueue + auto-fire suppression, the
most-info-bearing line of every refresh still surfaced in chat.

Fix: new `_genericReplyPlainHeaders` table maps each command's known
plain-leading reply prefixes (`.wep get` lists three: `"Your weapon
expertise is"`, `"No bonuses from currently equipped"`, `"You haven't
gained any expertise for"`). The generic-capture handler now matches
either `<color`-prefix OR a known plain header for the current
command. With both checks, silent-mode replies are fully destroyed.

**Shared smart-summon service.** `Services/VBloodSummonService` lifts
the pending-summon state machine + BoxContents subscription out of
MainPanel. Both the main UI V-Bloods tab and the overlay V-Blood rows
now call `VBloodSummonService.SummonVBlood(name)`. State is global —
one summon in flight at a time across the whole UI — and status text
fans out via `VBloodSummonService.StatusChanged` so the UI surface
that initiated the call can show progress.

### Deploy fix

If you were stuck on 0.10.3 with the V-Blood scanner mis-detecting
captures and chat spam: a duplicate top-level `BloodCraftHub.dll`
under `…/plugins/BloodCraftHub.dll` was loading instead of the
DEV-subfolder DLL my deploys had been writing to. 0.10.5 deploys to
both paths. Once you verify the About tab shows 0.10.5, the V-Blood
scanner should run with zero chat noise and detect captures correctly.

### Deferred: Chat Logging diagnostic section

User asked for a "Chat Logging" settings section that exposes per-mod
visibility toggles (BCH-auto / Bloodcraft / Kindred) with a master
show-all / hide-all button. Planned as 0.10.6 — design is in the
release summary so the user can review before implementation.


## 0.10.4 — V-Blood scanner chat-spam fix + overlay layout polish

Four user-reported issues from 0.10.3 friend-testing. The V-Blood
overlay-view item from the same report is deferred to 0.10.5 (substantial
UI work — separate from these regression fixes).

**V-Blood scanner no longer floods chat (and no longer mis-scans).**
Three bugs were stacked:

1. The scanner used `EnqueueMessage` instead of the silent variant added
   in 0.10.2 for the bonus-stats refresh — so every `.fam s "<name>"`
   reply (130 per scan) landed in the player's chat box.
2. `AwaitingFamSearch` arming didn't read `_suppressNextCaptureChat`,
   so even if I'd switched the scanner to `EnqueueMessageSilent` the
   per-intercept suppress flag would still be false.
3. Bloodcraft's "You don't have any unlocked familiars yet." reply
   (FamiliarCommands.cs:1096, fires when the player has zero captured
   familiars at all) wasn't matched by the no-match regex — so each
   search waited the full 0.6s timeout before moving on, instead of
   completing instantly. New players saw constant `.fam s` traffic
   while the scanner ground through the full 4-minute timeout loop.

All three fixed together: scanner uses `EnqueueMessageSilent`,
arming wires the suppress flag through to the AwaitingFamSearch
parser, and the no-match regex now catches the "no unlocks" line.
Result: scans on a fresh character complete in seconds (every search
immediately resolves as no-match), and there's zero scanner chat
chatter while the silent flag is in effect.

**Overlay sort button no longer overhangs the edge.** Pre-0.10.4 the
sort cycle button was 88-96 px wide with the longest label "Sort:
Region" — combined with the other header elements (←/→/box-name/
Reload) the row totaled ~366 px, which spilled past the 280 px
overlay minWidth and looked broken. Two fixes: dropped the "Region"
mode from the overlay's cycle (region grouping only reads naturally
on the V-Bloods tab which spans all 64 entries — a single mixed-region
box doesn't benefit from that ordering), and tightened the button
to 60 px with compact labels ("A→Z", "Lv↓", "Box"). The V-Bloods tab
keeps the full 4-mode cycle including Region. If the saved setting is
Region (e.g. cycled from the V-Bloods tab), the overlay treats it as
Default for rendering until the user cycles the overlay button.

**XP overlay default size tightened.** Pre-0.10.4 default sizes were
220 px (bonus stats off) / 360 px (on) with conservative per-row
padding — produced ~30-40 px of unused vertical margin most users
didn't want. New floors: 180 / 300, with row preferredHeight trimmed
from 24 → 20 and bonus-stat label preferredHeight from 44 → 32. Users
who want even more compactness can still drag the panel smaller
manually; the new floors are just the Default-button reset point.

**Weapon/Blood expertise auto-fire suppression (was already in 0.10.2,
re-verified)**. The bonus-stats ticker and the per-tab auto-refresh
both call `EnqueueMessageSilent` — replies get destroyed before
reaching the chat window. If users still see chat noise from these,
likely they're on an older DLL than 0.10.4.

### Deferred to 0.10.5

V-Blood view toggle in the Familiar Browser overlay. The user
requested a way to see V-Blood captures inline in the overlay (not
just the box-cycled familiar list). Implementation needs a "View:
Box / V-Bloods" cycle button + a second header row to fit it +
extraction of the smart-summon logic from MainPanel into a
reusable service so the overlay's V-Blood rows can summon directly.
~150 lines of UI; held back so the chat-noise fix in 0.10.4 ships
without delay.


## 0.10.3 — Critical fix: plugin load NRE introduced in 0.10.0

**Hotfix. 0.10.0 / 0.10.1 / 0.10.2 all fail to load** with the following
exception at plugin startup:

```
System.TypeInitializationException: The type initializer for
'BloodCraftHub.Services.MessageService' threw an exception.
---> Il2CppInterop.Runtime.Il2CppException: System.NullReferenceException
  at Unity.Entities.TypeManager.FindTypeIndex (System.Type type)
  at Unity.Entities.ComponentType.ReadOnly (System.Type type)
  at BloodCraftHub.Services.MessageService..cctor()
```

**Root cause**: 0.10.0 added `VBloodScannerService.Initialize()` to
`Plugin.Load`, which subscribed to `MessageService.FamSearchCompleted`.
Subscribing to a static event reads its backing field — and that is the
first MessageService static-field access ANY code path made at Plugin.Load
time. Reading a static field triggers the type's static constructor, which
ran the `NetworkEventComponents` initializer with
`ComponentType.ReadOnly(Il2CppType.Of<FromCharacter>())` etc. Those calls
route into `Unity.Entities.TypeManager.FindTypeIndex`, which NREs when
V Rising's ECS World hasn't been built yet. Plugin.Load runs BEFORE the
World exists; the cctor died; plugin load aborted.

Pre-0.10.0 the cctor only fired during the first frame's
`CoreUpdateBehavior` tick when `ProcessAllMessages` ran, by which point
the World was up. Nothing in `Plugin.Load` touched any
MessageService static field before 0.10.0 introduced the scanner.

This is the exact landmine called out in the NOTE block at the top of
`Services/EclipseProtocolService.cs` — "do NOT add static
ComponentType[] fields here" because Plugin.Load triggers the cctor
before TypeManager is built. The 0.10.x V-Blood work tripped it from a
different angle (event subscription instead of property setter).

**Fix, in two parts:**

1. `MessageService.NetworkEventComponents` is now a **lazy property**.
   The `ComponentType.ReadOnly(...)` calls resolve at SendMessage time
   (gameplay, ECS up) instead of cctor time (plugin load, ECS not up).
   This permanently neutralizes the bug class — any future early access
   to MessageService statics will no longer crash plugin load.
2. `VBloodScannerService.Initialize()` is **deferred to
   `Plugin.UIOnInitialize`** (called from `InitializationPatch` once
   LocalCharacter is bound). Same pattern Eclipse-main uses for its
   own ECS-dependent init. Belt-and-suspenders with the lazy refactor.

The per-frame `VBloodScannerService.Tick` is still registered in
`Plugin.Load` because the tick body bails out via `MessageService.IsInitialized`
checks until the scanner's `Initialize()` has run.

No feature changes. If 0.10.2 had loaded for you, all its features
(Region sort, fast type-switch refresh, chat suppression, tab-strip
width, dynamic overlay default, alignment toggle) remain present in
0.10.3.


## 0.10.2 — V-Blood region sort, fast type-switch refresh, overlay polish, chat-noise suppression

Six items addressing 0.10.1 friend-testing feedback plus the deferred
Location sort mode.

**Location sort now works for V-Bloods + Familiar Browser.** Ported
FamBook's `vbloods.json` page layout into `VBloodRegistry` as a region
mapping (page 1 = Farbane Woods through page 7 = Endgame). The Sort
cycle in both UIs now includes `Region` as the fourth mode, ordering
V-Bloods by their original encounter region (alpha within each region
for stable order). Non-V-Blood familiars (regular drops) fall into a
"unknown" bucket and sink to the bottom alphabetically. `Primal &lt;name&gt;`
entries match their base form's region.

**Faster overlay/tab refresh when you switch weapons or blood types.**
Pre-0.10.2, the bonus-stats display lagged 15-20s behind a weapon
swap because the auto-fetch was on a fixed cadence. The XP overlay
and the Weapon Expertise / Blood Legacy tabs now track the last-seen
`WeaponType` / `BloodType`; when an Eclipse stream tick reports a
delta (typically within 1-3s of equipping), the next auto-fetch timer
fires immediately rather than waiting for the next 10s tick. Old
cached stat values get hidden while the new reply is in flight so
the user doesn't see stale data for the unequipped weapon.

**Auto-fired `.wep get` / `.bl get` chat replies are now suppressed**
when the trigger is the overlay's bonus-stats ticker or the tab's
auto-refresh. New `MessageService.EnqueueMessageSilent(text)` sets a
one-shot flag on the intercept arming so the receive-side handler
destroys the chat copy. Manual user clicks (the Refresh button in
either tab, or any `.fam s` from the search form) keep using the
regular `EnqueueMessage` path — those replies still appear in chat as
before. **Eclipse compatibility unchanged**: Eclipse only consumes
MAC-signed `[ECLIPSE]` entities; the plain colored `.wep get` / `.bl
get` lines BCH suppresses are already ignored by Eclipse's prefix per
its `CheckMAC` gate.

**Tab strip width bumped 180 → 220.** The v0.9.8 "Help" → "SETTINGS AND
HELP" rename pushed the longest group header past the 180px cap,
overlapping the rail edge at Standard text scale and truncating at
Large. 220 covers Large with margin; the right content area still
absorbs all extra horizontal space as the main panel widens.

**Dynamic Default size for the XP overlay.** `MinHeight` is now
conditional on `Settings.ShowOverlayBonusStats`: 220 when the bonus-
stats display is off (compact view), 360 when on (room for the wrapped
weapon + legacy stat sub-lines). Clicking `[Default]` in the Size &
Positioning section now picks the right floor for the current toggle
state — pre-0.10.2 the floor was always 220 and the stat lines
overlapped the EXO row at default size.

**Overlay text alignment setting (Left default / Right).** New
`OverlayTextAlignment` setting + cycle button in Display Settings →
HUD extras. Applies to every overlay (XP, Familiar, Familiar Browser,
Daily Quest, Professions). Toggle triggers `RequestRebuildAllOverlays`
so labels pick up the new alignment immediately. Right is useful when
you've pinned an overlay to the right edge of the screen and want the
values closer to the panel border instead of left-floating in a wide
overlay.

### Deferred to future versions

The user flagged three more substantial items for future work that did
NOT land in 0.10.2:

- **Asset-ID database for admin forms** (`.give`, `.spawnnpc`, gems,
  familiar reset) — currently a free-text `TextField`; future version
  could render a searchable dropdown over a curated PrefabGUID list.
- **Periodic active+offline player query for admin dropdowns** —
  research whether KindredCommands exposes a parseable player-list
  reply, then surface as a dropdown alongside the existing
  `PlayerNameField`.
- **Server-side companion mod (2.0)** — admin-installed variant that
  auto-deploys to clients. Architecturally a different mod; would mirror
  Bloodcraft + Eclipse's signed protocol pattern.

These are tracked in memory (`project_bloodcrafthub_roadmap.md`) so
future sessions can pick them up without context loss.


## 0.10.1 — V-Blood tracker follow-ups: smart summon, re-scan subtraction, sort options

Three additions on top of the v0.10.0 V-Blood tracker.

**Smart V-Blood summon chain.** The summon button now composes the full
bind sequence instead of just switching boxes and asking the user to
finish manually:

1. **Unbind** any currently-active familiar (server tolerates a no-op
   reply when none is bound).
2. **Switch box** (`.fam cb &lt;box&gt;`) if the V-Blood's box differs from
   the active one, and trigger `.fam l` so the box-content intercept
   fills `BoxContents[BestBox]` with per-entry indices.
3. **Resolve the V-Blood entry** by name (prefer exact-basename match;
   fall back to `Primal &lt;name&gt;` if only the primal variant is present).
4. **Bind** via `.fam b &lt;index&gt;`.

If box contents are already cached for the target box (the user has
visited it previously), the bind fires immediately. Otherwise a
pending-summon waiter listens on `BoxContentsChanged` and triggers
once the data arrives, with a 12-second timeout safeguard so a slow
server doesn't leave a zombie waiter. Status text in the V-Bloods
tab reports each phase ("Summoning Alpha the White Wolf from box01
(slot 3)…").

**Re-scan now subtracts deleted V-Bloods.** Pre-0.10.1, the scanner
accumulated results per-name without clearing prior state — a V-Blood
deleted via `.fam r` between scans kept showing as captured. v0.10.1
clears `VBloodCollection` at `StartScan` so the scan begins from a
clean slate; deletions and box-moves now reflect on the next scan.
The grid renders the cleared (all-gray) state briefly while the scan
streams results back in.

**Sort options shared across V-Bloods tab + Familiar Browser overlay.**
New `FamiliarSortOrder` setting cycles between:
- **Default**: server / registry order (Familiar Browser: box order
  per `.fam l`; V-Bloods tab: alphabetical from `VBloodRegistry`)
- **Alphabetical**: A→Z by familiar name
- **Level**: descending by max captured level (looked up from
  `BoxContents` per row; uncaptured rows sink to the bottom)
- **Location**: reserved for 0.10.2. Falls back to alphabetical for
  now — needs a static map-region table for each V-Blood, which the
  user has offered to supply.

Two cycle buttons surface the setting:
- In the V-Bloods tab filter row: `Sort: Default / Alpha / Level`
- In the Familiar Browser overlay header: `Sort: Box / A→Z / Lv↓`

Changing the setting from either UI updates both. The overlay
re-renders its list immediately; the V-Bloods tab reorders its 65
rows via `SetSiblingIndex` (no rebuild, scroll position preserved).


## 0.10.0 — V-Blood collection tracker

New feature category. A "V-Bloods" tab (peer of Familiars / Boxes inside
the BLOODCRAFT group) tracks which of the 65 Bloodcraft-recognized
V-Bloods you've captured across all your boxes, with status chips for
each of the four possible variants per V-Blood:

- **B**  — basic variant captured
- **S**  — basic + shiny captured (Bloodcraft applies one of 6 schools
  to a random fraction of captures)
- **P**  — Primal variant captured (server prefab uses the "Primal "
  name prefix; e.g. "Primal Angram the Purifier")
- **PS** — Primal + shiny captured

Each row shows the V-Blood's name, the four chips (green when captured,
gray when not), the first known box that contains it, and a one-click
Summon button. Filter buttons across the top: All / Captured / Missing /
Shiny so you can focus on what's left to collect.

**Scanner — silent and non-disruptive.** When you open the tab for the
first time, the scanner auto-starts and walks all 65 names (basic + primal,
130 queries total) using `.fam s "<name>"`. The search is read-only on
the server side — it never switches your active box, so the scanner can
run in the background while you play. The outbound queue throttles
between sends, so a full scan completes in roughly 4 minutes. Partial
results stream into the grid as they arrive.

The scan progress is shown next to the header counter
(`23 / 65 captured  ·  4 primal  ·  6 shiny`) and there's an explicit
`Cancel` button if you want to stop early. You can rerun the scan any
time to pick up new captures.

**Why .fam s instead of walking boxes:** an earlier draft used `.fam cb`
+ `.fam l` to enumerate every box's contents directly. That gives richer
data (per-entry level / prestige / shiny school) but switches the
player's active box during the walk, which is jarring while playing.
The `.fam s` approach trades that data fidelity for invisibility — at
the cost of not being able to surface shiny SCHOOL (the search reply
only carries a per-box shiny bit, not which school the shiny is). Future
0.10.x versions can opt into a deep-scan mode that calls `.fam cb` +
`.fam l` to resolve schools, gated behind an explicit opt-in.

**Summon flow** reuses the existing outbound commands:
`.fam cb <box>` switches to the V-Blood's box and `.fam b <index>` binds.
The 2s queue serializes them. When the index is unknown (default for
fresh scans), a status note tells you to open the Boxes tab to pick a
specific familiar after the box switch.

**Implementation files:**

- `Resources/VBloodRegistry.cs` (new) — canonical 65-name list mirrored
  from Bloodcraft's `Utilities/Familiars.cs::VBloodNamePrefabGuidMap`.
- `Services/VBloodScannerService.cs` (new) — queue + tick + result
  fold. Initializes once in `Plugin.Load`; tick registered on
  `CoreUpdateBehavior.Actions`.
- `Services/PlayerStateService.cs` — new `VBloodCaptureStatus` struct,
  `VBloodCollection` dictionary, `VBloodCollectionChanged` event.
- `Services/MessageService_Processing.cs` — new
  `InterceptFlag.AwaitingFamSearch` with regex parsing of the two reply
  shapes (`"Matching/VBlood familiar(s) found in: <colored boxes>"` and
  `"Couldn't find..."`). New public `FamSearchCompleted` event and
  `FamSearchResult` payload type.
- `UI/ModContent/MainPanel.cs` — `BuildVBloodsTab` builder + per-row
  refresh path. Tab inserted in the BLOODCRAFT group between Boxes and
  Class so it's discoverable but not interrupting the existing
  Familiars / Boxes flow.
- `UI/ModContent/Data/PanelType.cs` — new `VBloodsTab` enum value.

**Nothing existing was rewired.** The Boxes tab, Familiar Browser
overlay, summon/unbind/auto-swap logic, and outbound queue all behave
exactly as in 0.9.9. The V-Blood tab is purely additive; if you never
open it, the scanner never runs and there's zero impact.


## 0.9.9 — XP overlay bonus-stats: weapon stats now display, lines now wrap

Two follow-on fixes for the `ShowOverlayBonusStats` setting added in 0.9.6:

**Weapon expertise stats now actually appear** (pre-0.9.9 only blood
showed). Root cause: `MessageService.EnqueueMessage` sends commands
immediately and arms the regex intercept right away. The bonus-stats
ticker fired `.wep get` and `.bl get <CurrentBlood>` back-to-back; the
second `NoteOutboundForIntercept` call overwrote the first's
`AwaitingGenericResponse` with `AwaitingBloodInfo` before the wep reply
arrived from the server, so the wep stat lines hit the receiver with
the flag already pointing at the blood-info state machine and got
silently dropped. Fix: alternate the two fetches per tick via a toggle
flag. Each command now refreshes every ~20 seconds (toggle period =
2 × `OVERLAY_BONUS_REFRESH_SECONDS`), which is fine because bonus
values only change on level-up or when the player picks a new stat
via `.wep cst` / `.bl cst`.

**Stat lines now wrap inside the overlay width** instead of overflowing
past the right edge. `AddRow` defaults to `enableWordWrapping=false`
(correct for the single-line value rows like Level / Class / XP%), so
the new `ConfigureBonusStatsLabel` helper overrides those settings on
the two stat sub-labels: enables word-wrap, adds a vertical-axis
`ContentSizeFitter` so the label auto-grows, and switches the rendering
to newline-join. Also strips TMPro `<color=...>` tags from the captured
`.wep get` lines — each tag chews ~16 chars of label width without
rendering glyphs, so removing them buys back a lot of usable line space
inside a narrow overlay (BloodInfo.StatLines already had tags stripped
upstream by the regex intercept).

The race between competing intercept consumers (overlay's
bonus-stats ticker + main panel's tab auto-refresh on the Expertise
tab) still exists in theory but is much less likely to hit in practice:
both consumers fire on independent ~10s timers, so a collision requires
their fire windows to overlap within the server's reply-round-trip
window (a few hundred ms). A proper fix would give each consumer its
own intercept state, but that's a refactor — flagged in
`docs/LESSONS_LEARNED.md`-worthy territory and deferred until it
actually surfaces as a problem in testing.


## 0.9.8 — Discord DM link; "Settings and Help" rename; three Size & Positioning fixes

Visibility + correctness round-up from 0.9.7 friend-testing:

**About tab: "DM me on Discord" row** linking directly to
`https://discord.com/users/PerpetualChaos`. Friend-testing: users wanted
a one-on-one channel for mod feedback / bug reports without joining the
server Discord first. Slots in alongside the existing Server Discord
row in the "About me" section.

**Left-rail group renamed: "HELP" → "SETTINGS AND HELP".** Multiple
friend-testers didn't realize there was a Settings page tucked under a
"Help" group — the rename makes the actionable child visible from the
collapsed state. No structural change; same four tabs (Quick Start,
Settings, Vanilla Admin, About) live under it.

**Tab-strip width cap now actually holds.** 0.9.7 set the strip's
`preferredWidth=180` and `flexibleWidth=0`, but the parent body's
`HorizontalLayoutGroup.childForceExpandWidth=true` overrode it — Unity
gives every child an equal share of extra space when `forceExpandWidth`
is on, regardless of individual `flexibleWidth=0` settings. Switched the
body's `forceExpandWidth` to `false`; the content area's
`flexibleWidth=1` still absorbs all the extra horizontal space when the
panel widens, so the visible result is just "left rail stops growing,
right side keeps growing". Which is what 0.9.7 was supposed to do.

**Size & Positioning [Default] button now resets size only — not
position.** Pre-0.9.8 the button called `SetDefaultSizeAndPosition()`
which re-anchored each overlay to its `DefaultPosition`. For the XP
overlay that's the top-left corner of the screen, so clicking [Default]
sent overlays flying. New `ResizeablePanelBase.SetDefaultSize()` only
resets `sizeDelta` to `MinWidth × MinHeight`; anchors/pivot/position
are untouched. Drag the overlay if you also want to re-center.

**Size readouts now update during manual drag-resize.** Pre-0.9.8 the
`Width: 720 px` / `Height: 480 px` labels in the Settings section only
refreshed inside the +/- click handlers, so dragging a panel by its
edge left the readout stale until the user clicked +/-. Added a per-
frame refresher gated on `ActiveTab == SettingsTab` — zero cost when
the user is on any other tab; live sync when they're on Settings.


## 0.9.7 — Title-bar maximize, Size & Positioning section, About-tab version block, tab-strip width cap

Friend-testing feedback batch:

**Title-bar maximize/restore button on the Main UI.** New `[ ]` button to
the left of the existing `[—]` close button on the panel title bar.
Clicking toggles the panel between its current size+position and a
fullscreen stretch (with a 20 px inset on each edge so the resize handles
stay grabbable). Clicking again restores the prior layout pixel-for-pixel.
Toggle is transient — not persisted across logouts. Fullscreen-toggle is
intentionally Primary-UI only; overlays are size-only.

**New Size & Positioning section in Settings.** Per-component subsections
(Primary UI + each of the 5 overlays). Each subsection exposes:

- `[Default]` button: calls `SetDefaultSizeAndPosition()` on that panel.
- `Width:  [-]  N px  [+]` and `Height:  [-]  N px  [+]` rows. Each click
  adjusts by 20 px; hold Shift while clicking for 100 px steps. Clamps to
  the panel's MinWidth / MinHeight / MaxWidth. The displayed value updates
  after each click so the user can see the current size.
- Primary UI additionally has `[Auto-size]` (mirrors the footer toggle)
  and `[Fullscreen]` (mirrors the title-bar button).

Manual drag-from-edge resize is unaffected — the new section just gives a
click-driven alternative for users who didn't realize the panels were
resizable. Friend-testing: "users had provided feedback that they were
not aware that you could resize them."

**Tab-strip max width.** The left rail (BLOODCRAFT / KINDRED / HELP) is
now capped at 180 px regardless of how wide the main panel grows. Pre-
0.9.7 the rail scaled proportionally with the panel — friend-testing
feedback: "The main reason that people change the size of the UI is to
enable them to see more information in the right side panel." Extra
width now flows entirely to the content area on the right.

**About tab — version block + Thunderstore link.** Bottom of the About
tab now shows `BloodCraftHub vX.Y.Z` with a paragraph mirroring the
Thunderstore listing description. Added a "BloodCraftHub on Thunderstore"
link row alongside the existing GitHub link. Useful for users who got
the DLL directly (Discord, friend hand-off, etc.) and wouldn't have
seen the Thunderstore page.

**Quick Start: drag/resize note.** Welcome section now explicitly calls
out that both the main panel AND every overlay are draggable AND
resizable from their edges, and points users at the new maximize button
+ Settings tab size controls. Friend-testing: "some users had provided
feedback that they were not aware that you could resize them."

**Display settings: stale "(0.9.0)" version annotation removed** from
the section heading. Version info now lives only in the About tab.

Implementation notes:

- `ResizeablePanelBase.AdjustSize(int dWidth, int dHeight)` is the new
  public API. Applies the delta, runs the existing `MinWidth` / `MaxWidth`
  / `MinHeight` clamps via `EnsureValidSize()`, and persists the new size
  to config via `SaveInternalData()` so it survives a session.
- `MainPanel.SetFullscreen(bool)` snapshots `sizeDelta`, `anchoredPosition`,
  `anchorMin`, `anchorMax`, and `pivot` before swapping to a stretch
  layout — restore reverts each field to its captured value.
- Setting `MainPanel.ToggleFullscreen` flips the title-bar button text
  between `[ ]` and `[X]` so the user sees current state at a glance.
  Plain-text glyphs (vs Unicode maximize icons) per the TMPro fallback-
  font lessons in `docs/LESSONS_LEARNED.md`.


## 0.9.6 — XP overlay Blood Legacy row + per-row stat values; tab headers carry full current-state info

Friend-testing feedback addressed in one batch:

**Blood Legacy row added to the XP overlay.** Mirrors the Weapon row shape:
`Legacy: Warrior  Lv 50 (45.3%)  Pr 1` plus an optional crimson progress
bar (controlled by the same `Settings.ShowProgressBars` toggle as the XP
and weapon bars). Data feeds from `PlayerStateService.Legacy` which the
Eclipse `ProgressToClient` stream already populates at indices 4..8.

**XP overlay height fix.** Friend-testing surfaced that the EXO Prestige
row (added in 0.8.3) was rendering BELOW the panel background border on
cold-installs, because the panel's `MinHeight = 90` predated the
Weapon (0.9.4) and now Legacy (this version) rows. Bumped to 220 so all
rows fit comfortably; `ResizeablePanelBase.EnsureValidSize` will grow
existing users' saved panels up to the new minimum on first load.

**New `Settings.ShowOverlayBonusStats` toggle (default OFF).** When on,
the overlay shows an italicized sub-line under the Weapon and Legacy
rows carrying the chosen bonus-stat names with their current numeric
values — matching what Eclipse displays. Friend-testing: "in the
original eclipse mod ... the Weapon Expertise and Blood Legacy stats
show within the overlay. Both the selected expertise and legacy
attributes, as well as the percentage of them." Default OFF preserves
the compact overlay for users who want a minimal HUD; toggle in
Display settings.

Data plumbing for the new line:
- Weapon values: subscribe to `LastResponseChanged`; cache the line
  buffer when `Command == ".wep get"` (snapshots survive subsequent
  unrelated generic captures like `.lvl get`).
- Legacy values: read `PlayerStateService.BloodInfoLatest.StatLines`
  populated by the existing `AwaitingBloodInfo` intercept.
- A 10s ticker on `CoreUpdateBehavior` auto-fires `.wep get` and
  `.bl get <CurrentBlood>` while the overlay is visible AND the setting
  is on. Stops cleanly when the setting toggles off or the overlay
  closes; no chat traffic when nobody's looking.

**Tab headers now auto-populate with current stat values.** Friend-
testing: "add in the current weapon expertise and sub stats and
attributes and the blood legacy attributes and percentages into the
header of their respective tabs in the main UI ... separate from the
search features within these tabs".

- Weapon Expertise tab: new italicized "stat values" line below the
  existing bonus-name line, populated from the cached `.wep get`
  reply. The full Bloodcraft response (raw color-tagged lines) is
  rendered with bullet separators.
- Blood Legacy tab: new italicized "stat values" line below the
  existing bonus-name line, populated from `BloodInfoLatest.StatLines`
  when the queried blood matches the currently-equipped blood. The
  existing full Blood Info display further down the page still serves
  the "query ANY blood" form unchanged.
- Both tabs auto-fire their respective queries on tab open AND every
  10s while the tab is the active page (per-frame `TickTabAutoRefresh`
  ticker on `MainPanel`). Self-gates on `Enabled` + `ActiveTab` so
  it's a no-op when the panel is closed or the user is on a different
  tab.


## 0.9.5 — Eclipse-mod coexistence fix

Friend-testing report: installing BloodCraftHub alongside the standalone
Eclipse client mod caused Eclipse's overlay to render with all progress
bars and numbers blanked/zeroed. Uninstalling BCH restored Eclipse.

Root cause: Bloodcraft's server-side mod broadcasts a single MAC-signed
`[N]:csv;mac<hash>` stream per player. Both BCH (`EclipseProtocolService`)
and Eclipse (`Eclipse.Patches.ClientChatSystemPatch`) independently
Harmony-prefix `ClientChatSystem.OnUpdate` and consume that stream. BCH
destroyed the chat entity after parsing (to keep the protocol noise out
of the chat window), so Eclipse's prefix saw a dead entity and rendered
zero-filled bars.

Fix lives in two places:

- `EclipseProtocolService.IsEclipseModLoaded()` looks up
  `io.zfolmt.Eclipse` in BepInEx's `IL2CPPChainloader.Instance.Plugins`
  the first time it's called and caches the result. The lookup has to be
  lazy because BCH's `Plugin.Load` runs before BepInEx has finished
  loading the rest of the plugin set (alphabetical: BCH < Eclipse).
- `Patches/ClientChatPatch.OnUpdate_Prefix` only calls
  `EntityManager.DestroyEntity` on a MAC-verified entity when
  `IsEclipseModLoaded()` returns false. When Eclipse is present we let
  Eclipse's own prefix destroy the entity after Eclipse parses it — the
  chat-window noise is still suppressed, just by Eclipse instead of BCH.
- `OnUpdate_Prefix` is now `[HarmonyPriority(Priority.High)]` so BCH
  reliably runs *before* Eclipse's normal-priority prefix. Without this,
  Eclipse could win the race, destroy the entity, and starve BCH's
  overlays instead — same bug, opposite direction.

No effect when Eclipse is not installed: detection returns false on the
first call and the destroy-after-parse behavior is unchanged. The legacy
`MessageService.HandleInboundChat` regex pipeline is untouched — its
intercept flags are already only-destroy-on-match so non-protocol traffic
(real Bloodcraft chat replies, KindredCommands output, etc.) is unaffected.



## 0.9.4 — Equipped-weapon expertise row on the XP overlay

Friend-testing follow-up: "I didn't see in any of the experience overlays
where it would show the current weapon and its experience and prestige
level. This could be added into the general experience overlay."

Added a weapon row between the Class row and the EXO Prestige row on
`ExperienceOverlayPanel`. Data source:
`PlayerStateService.Expertise` (`Type`, `Level`, `Prestige`, `Progress`) —
populated from Bloodcraft's signed Eclipse `ProgressToClient` stream at
indices 9..13. Bloodcraft only streams the currently-EQUIPPED weapon's
expertise (per the upstream design), so switching weapons in-game updates
this row on the next stream tick (~1s cadence).

Format mirrors the main level/prestige line:
`Weapon: Sword  Lv 25 (12.3%)   Pr 1`

If no weapon is equipped (Bloodcraft writes Type=0/Unarmed + Level=0 at
startup before the first equip), the row shows `Weapon —` placeholder and
the bar hides, rather than rendering "Unarmed Lv 0" which would read as
broken data.

Optional copper-red progress bar paired with the row, tied to the
existing `Settings.ShowProgressBars` toggle. Color chosen to be visually
distinct from the cyan XP bar above it.

## 0.9.3 — Wrap tooltip; bar visibility + Familiar/Professions bars; chat suppress actually fires

Four follow-ups from v0.9.2 friend-testing.

**Tooltip footer now wraps.** `BuildTooltipFooter` had `enableWordWrapping = false`
so long tooltips (notably the new master-overlay "OV" button's multi-sentence
description) overflowed the right edge of the panel as one un-wrapped line.
Wrap is on now; footer height bumped 22 → 56 px (room for ~3 lines of 12pt
italic) with `TextOverflowModes.Ellipsis` so very long tooltips get capped
instead of pushing the panel taller.

**Progress bar — opaque container with outlined border.** Friend-testing:
"If they are a bar that crosses the transparent background, it's hard to
distinguish where they're expected to end." Root cause was the bar's bg
alpha was 0.85, which against a low-opacity overlay backdrop made the
container nearly invisible. Now the bg is fully opaque dark
(`0.07, 0.07, 0.07, 1.0`), a 1px gray Outline draws the container's
border, and the colored fill is inset by 1px so it sits inside the
outline cleanly instead of crashing through. Default bar height bumped
8 → 12 px for clearer visual.

**Progress bars added to Familiar overlay + Professions overlay.** Pre-0.9.3
the toggle only affected the XP overlay and the Prestige info display.
- Familiar overlay: one warm-orange bar below the "Lv X (Y.Y%)" row,
  driven by `FamiliarState.Progress`. Hidden when no familiar is bound
  (otherwise it'd render a zero-fill bar that looks broken).
- Professions overlay: 8 amber bars, one paired with each profession's
  label row. Color chosen to be distinct from the XP-overlay cyan and
  the Familiar-overlay orange so all three overlays remain visually
  separable when visible together.

**Chat suppression now fires during box switching.** Friend-testing: the
toggle was on but "Box Selected" still appeared in chat every box click.
Root cause confirmed via exploration of `MainPanel.OnBoxClicked` (line
1395-1408) and `FamiliarBrowserOverlayPanel` (line 257-265): clicking a
box enqueues TWO commands back to back:
1. `.fam cb {name}` — arms `_actionSuppressUntil` (1.5s window).
2. `.fam l` — arms `InterceptFlag.AwaitingBoxContent`.

When Bloodcraft replies `"Box Selected - <color=white>{name}</color>!"`,
intercept was already AwaitingBoxContent (overwritten by the second
command's arm). The old suppress check required `_intercept == Idle`,
so the action-confirmation skipped suppress and landed in chat.

Fix: replace the broad "contains color tag during action window" filter
with `IsKnownFamiliarActionConfirmation(text)` — pattern-matches the
exact literal strings Bloodcraft uses (from `Commands/FamiliarCommands.cs`
and `Utilities/Familiars.cs`):
- `.fam cb` → starts with "Box Selected"
- `.fam ub` → contains "unbound</color>!" or "</color> unbound!"
- `.fam mb` → contains "</color> moved -"
- `.fam r`  → contains "</color> removed from "
- `.fam t`  → contains "Familiar</color> <color=" and "enabled!"/"disabled!"
- `.fam b`  → contains "now bound!" or "now active!"

These patterns are distinct from the structured intercept patterns
(`<color=yellow>\d+</color>|` for box content; literal "Familiar Boxes"
header for box list; etc.) so the suppress can fire alongside an active
intercept without eating legitimate list/info data. Net result: clicking
a box switches the box without spamming chat.

## 0.9.2 — Live settings, real transparency, dedicated Settings tab, progress bars, fixes

Seven follow-ups from v0.9.1 friend-testing.

**Per-overlay transparency now actually changes anything.** Root cause: the
`opacity` parameter on `UIFactory.CreatePanel` had been accepted but never
applied to the panel's background `Image` — the alpha came packed into
`Theme.DarkBackground` (the static global, ~0.8) and the per-panel `Opacity`
override was a no-op. Fixed by setting `Image.color.a = opacity` at
construct time. Also added `UIFactory.ApplyOpacityToPanel(...)` +
`PanelBase.RefreshOpacity()` so runtime Settings changes push the new
alpha to existing panels' Image components — no rebuild required. Each
transparency segmented-button click now calls
`BCHubUIManager.RefreshAllOpacities()` and the change is visible
immediately.

**UI text size changes now actually re-render.** Pre-0.9.2 the
`Theme.UIFontMultiplier` was updated correctly but existing labels kept
their construct-time fontSize. Added `BCHubUIManager.RequestRebuildMainPanel()`
and `RequestRebuildAllOverlays()` — both defer the destroy+recreate to
the next frame (via `CoreUpdateBehavior.Actions`) so the click handler that
triggered the rebuild can finish before the panel hosting it disappears.
ActiveTab is preserved across the rebuild. Overlays are only rebuilt when
their per-overlay `Settings.Show*Overlay` is true — disabled overlays
stay un-constructed.

**Settings moved to a dedicated tab.** Display settings + Chat noise +
HUD extras are no longer a section on the About tab. New
`PanelType.SettingsTab` lives in the HELP left-rail group between Quick
Start and Vanilla Admin. The About tab is now purely acknowledgements +
community links, as it was originally intended.

**Chat suppression filter — fixed for Bloodcraft's actual reply format.**
Pre-0.9.2 the filter was `text.StartsWith("<color")` — but Bloodcraft's
`.fam cb` reply is `"Box Selected - <color=white>name</color>"` with the
color tag in the middle. Changed to `text.Contains("<color=")` so both
prefix-color and mid-color patterns match. False-positive risk minimal:
human players don't type `<color=` literal in chat, and the suppress
window is only 1.5s wide after a deliberate action click.

**Weekly Quest accent — switched from pink/magenta to gold.** v0.9.1's
brightened-magenta `(1, 0.55, 1)` still read as pink against red in-game
backdrops. Changed to gold `(1, 0.85, 0.3)` which is well outside the red
wavelength — contrast survives any backdrop and still visually distinct
from the cyan daily-quest target. Applied to both the Daily Quest tab and
the Daily Quest overlay.

**Boxes tab — heading text no longer overlaps the panel border.**
`AddSectionHeading` reserved only `minHeight: 20 / preferredHeight: 22`, too
tight for the bolded italic 14pt text + line metrics, and very wrong at
Large scale where the text grew to 17pt. Bumped to 26/30. Also bumped the
box list container's top padding 2px → 6px so the first row has breathing
room below the heading. Section headings throughout the panel are
slightly taller now as a side effect — a small visual change but no
content shifts.

**Progress-bar toggle for XP and Prestige.** New
`Settings.ShowProgressBars` config + Settings-tab toggle. When on:
- XP overlay renders a horizontal cyan progress bar below the "XP X%"
  label (XP% number stays visible).
- Prestige info display renders a green progress bar below the "Current
  Prestige Level: X / Y" line (only when MaxLevel > 0; otherwise the bar
  hides since there's no normalized progress).
Off by default so existing layouts are unchanged. New `MiniBar` control
under `UI/Framework/CustomLib/Controls/` — minimal anchor-stretched
two-image setup, pure proportional scaling (no need to query parent
width). Reused the existing heavier `ProgressBar.cs` would have over-spec'd
the feature — that class includes flash animations, fade timers, change
deltas, and alert tooltips, none of which a HUD progress bar needs.

**Outside this release** (still queued):
- PlayerNameField autocomplete dropdown.
- `.class csp` shift-spell-picker dropdown.
- Suspend-typing redesign on a different patch target.
- Per-profession prestige row in the Professions overlay if Bloodcraft
  ever streams it.

## 0.9.1 — Accent-text legibility + opt-in chat suppression for familiar actions

Two follow-ups from v0.9.0 friend-testing.

**Pink-on-red contrast fix.** Friend-testing flagged that the Daily Quest tab's
weekly-quest label and the Blood Legacy tab's "Bloodcraft red" headings read as
"pink text on red background" when the panel transparency lets a red in-game
backdrop bleed through. Two changes:

- New `ApplyStrongAccentOutline` helper bumps the per-character TMP outline
  from the default 0.15 → 0.25 (still black) for the accent-recolored labels:
  `_blTypeLabel`, `_blInfoTitleLabel`, `_blInfoStatsLabel` (which preserves
  server `<color=red>` tags), `_dqDailyTargetLabel`, `_dqWeeklyTargetLabel`,
  and the docked "Last server response" body. Same helper in
  `DailyQuestOverlayPanel` for the cyan + magenta titles. Outlines apply
  globally to the TMP element so rich-text segments with mid-string color
  tags also get the dark border.
- Weekly-quest accent color brightened from Bloodcraft's #BF40BF
  (0.75/0.25/0.75) to a lighter magenta (1.0/0.55/1.0) on both the Daily
  Quest tab and the Daily Quest overlay. The darker reference value really
  did blend into red backdrops; the lighter shade keeps the weekly target
  visually distinct from the cyan daily target without sacrificing
  readability.

**Opt-in suppression of familiar-action chat confirmations.** Friend-testing
question: "is it possible to suppress all of the chat messages that come
through as you were switching boxes and familiars". Yes — and the UI keeps
working because the data feeds the UI uses (`.fam boxes` / `.fam l`
structured intercepts + Bloodcraft's signed Eclipse `ProgressToClient`
stream) are completely independent of the human-readable confirmation
lines.

New `Settings.SuppressFamiliarActionChatter` config flag (default off so
existing users see no change). Wiring:

- In `MessageService_Processing.NoteOutboundForIntercept`, alongside the
  existing structured-intercept arming, an additional `IsFamiliarActionCommand`
  check arms a 1.5-second `_actionSuppressUntil` window for these
  prefixes: `.fam b ` (bind), `.fam ub`, `.fam t`, `.fam cb ` (switch
  box), `.fam mb ` (move box), `.fam sb ` (smartbind), `.fam r `
  (permanent remove).
- In `HandleInboundChat`, BEFORE the intercept state-machine switch, a
  pre-check: if the intercept is Idle, the action-suppress window is
  active, the user has enabled `SuppressFamiliarActionChatter`, AND the
  line is color-tagged (Bloodcraft confirmations always are) → return
  `true` to consume the line from chat.
- Only consumes color-tagged lines, so unrelated chatter (player joins,
  world events, broadcast messages) still passes through.
- Only fires when intercept is Idle, so it never clobbers a structured
  capture mid-flight (e.g., a `.fam l` query running concurrently).

Toggle lives on the Help → About tab's Display settings section under a
new "Chat noise" subsection. Off by default; turning it on takes effect
immediately for the next action.

**Outside this release** (still queued):
- Live text-scale re-render without close/reopen.
- PlayerNameField autocomplete dropdown.
- `.class csp` shift-spell-picker dropdown.
- Suspend-typing redesign on a different patch target.

## 0.9.0 — Accessibility & customization: text scales, overlay transparency, master toggle, Professions overlay

Three friend-testing requests rolled together with one missed overlay. v0.9.0
is a polish release focused on accessibility (text scaling) and on giving
power users finer control over each overlay's appearance.

**Dual text-size toggle.** Two independent multiplier axes on `Theme`:
`UIFontMultiplier` (main panel + forms) and `OverlayFontMultiplier` (the five
secondary overlays). Three preset values per axis — Small (0.85×), Standard
(1.0×, default), Large (1.2×). New segmented controls live on the Help →
About tab under a "Display settings" section. Every hardcoded `fontSize` call
across `MainPanel`, `MainPanel.KindredAdmin`, `FormBuilder`, `CollapsibleSection`,
`FormField`, and the four (now five) overlay panels was rewrapped in
`Theme.ScaledUI(...)` or `Theme.ScaledOverlay(...)` accordingly via a
case-sensitive regex sweep.

**Live-vs-deferred caveat:** scale changes do NOT retroactively resize labels
that are already built. The user has to close and reopen the panel (and toggle
each overlay off/back-on) for the new scale to take effect. The "current: X"
hint in the segmented control + the explanatory paragraph in the section
header keep this discoverable.

**Per-overlay background transparency.** Five new BepInEx config floats:
`XPOverlayTransparency`, `FamiliarOverlayTransparency`,
`FamiliarBrowserTransparency`, `DailyQuestTransparency`,
`ProfessionOverlayTransparency`. Each gets a five-button row on the About
tab's Display settings section (0% / 25% / 50% / 75% / 100%). Each overlay's
`Opacity` property getter routes its setting through
`Settings.TransparencyToAlpha(...)` which:
- inverts the user convention (0% transparency = solid; 100% = invisible) to
  Unity's `alpha` (1.0 = opaque; 0.0 = transparent)
- applies a 0.95 floor — at the user's "100% transparent" the panel chrome
  / drag handle still has alpha 0.05 so the user can grab it. Without the
  floor, the user could lose the panel entirely with no way to find it on
  screen.

Text and any non-background elements stay fully opaque regardless of
transparency setting (it only affects the panel's `Image.color.a`). Per
user direction: this is purely background transparency, not overall element
transparency.

**Master overlay show/hide button.** New "OV" 40×40 button next to the BCH
floating button (the host panel grew from 56×56 → 104×56 to fit both). Click
toggles a session-only `BCHubUIManager._overlaysSuppressedByUser` flag:
- when suppressed, every overlay is hidden regardless of its per-overlay
  state
- when un-suppressed, overlays are re-shown ONLY for the per-overlay
  `Settings.Show*` flags that are true — never re-shows overlays the user
  has disabled via the panel footer
- session-only: the flag is **not** persisted across game restarts, so the
  user can't accidentally hide everything and forget how to get it back.

This was the friend-testing request "beside the B, C, H button, there was
a toggle to show and hide all active overlays so that this way a person can
toggle them on and off as they need" — common on smaller screens where the
overlays conflict with the in-game menus.

**New Professions overlay** (`UI/ModContent/ProfessionOverlayPanel.cs`).
Displays all eight Bloodcraft professions (Enchanting / Alchemy / Harvesting
/ Blacksmithing / Tailoring / Woodcutting / Mining / Fishing) with level + XP
percentage. Data is already streamed via Bloodcraft's signed
`ProgressToClient` Eclipse channel — `EclipseProtocolService` unpacks it into
`PlayerStateService.ProfessionState` at fields 19..34 — so the overlay just
subscribes to `ProfessionChanged` and renders. Per-profession prestige is NOT
shown because Bloodcraft doesn't stream it on the Eclipse channel (only
levels); we don't fake data we don't have. Visibility persists across
sessions via the new `Settings.ShowProfessionOverlay` config flag, same
mechanism as the other four overlays. Toggle in the panel footer row 1.

**Outside this release** (queued for later):
- PlayerNameField autocomplete dropdown (the name cache fills passively but
  the field is still plain text).
- `.class csp` shift-spell-picker dropdown (would need `.class lsp` reply
  parsing + new state slot for spell names per class).
- Per-row Move in Boxes Edit mode.
- Live text-scale re-render without close/reopen (requires a panel-rebuild
  pipeline; not in scope for this release).
- Profession-prestige row in the overlay if Bloodcraft ever adds it to the
  signed stream.
- Suspend-typing redesign on a different patch target (still deferred from
  v0.8.2 — removal stays in place until a non-locking mechanism is proven).

## 0.8.3 — Read-command replies land in UI + EXO prestige row

Friend-testing pointed out that many `.X get` / `.X list` chat-command replies (e.g.
weapon expertise info, class lists, prestige lists, user stats, clan rosters, boss /
region / staff / time lookups) landed only in the chat box. If the user was browsing
the UI panel they could easily miss the reply. This release fixes that for ~25 commands
and also adds the long-missing EXO prestige row to the experience overlay.

**Generic server-response capture.** New `ServerResponseCapture` machinery in
`Services/MessageService_Processing.cs`: a new `InterceptFlag` pair
(`AwaitingGenericResponse` / `ReceivingGenericResponse`) and a `ShouldArmGenericCapture`
helper that detects ~25 known read-data command prefixes. When one of those commands
is sent via `MessageService.EnqueueMessage`, the capture arms; the next batch of
server-colored chat lines is buffered for ~0.6s then flushed into a new
`PlayerStateService.LastResponse` slot. The capture is **additive** — the chat
copy of each line is still delivered so familiar chat readers don't lose their
flow.

Commands wired (their replies are now mirrored to the UI):
- `.fam pr`, `.fam actions`, `.fam bgs`, `.fam bg <name>`
- `.prestige l`, `.prestige lb <type>`
- `.bl l`, `.bl lst`
- `.wep get`, `.wep l`, `.wep lst`
- `.lvl get` (chat side; Eclipse stream still drives live overlay data)
- `.class l`, `.class lsp`, `.class lst`
- `.prof l`, `.prof get <type>`
- `.misc userstats`, `.misc health`, `.misc remindme`
- `.quest p <type>`, `.quest t <type>`
- `.checklevel <player>`
- `.clan list`, `.clan members <clan>`
- `.boss list`, `.region list`, `.openplots`, `.staff`, `.time`, `.gear soulshardstatus`
- `.fc <chest>`
- `.search item <q>`, `.search npc <q>`

Specific structured intercepts (`.fam boxes`, `.fam l`, `.prestige get <type>`,
`.bl get <type>`) still take priority — those continue routing to their dedicated
state slots (`BoxList` / `BoxContents` / `PrestigeInfoLatest` / `BloodInfoLatest`).

**"Last server response" docked panel.** A new collapsible section docked just
above the overlay-toggle footer in the main panel (`BuildLastResponsePanel` in
`UI/ModContent/MainPanel.cs`). Hidden until a captured response arrives, then
shows a header (`Last server response — .wep get  (5 lines)`) and the raw
color-tagged response body. TMP `richText` is on so server color tags render
the same way they would in chat. Click the header to collapse/expand. Auto-
expands when a fresh response lands. The panel is panel-level (not per-tab),
so users see the latest response regardless of which tab they're on — easier
to follow when clicking a command on Tab A then switching to Tab B.

**EXO Prestige row in the Experience overlay.** New 4th row on
`ExperienceOverlayPanel`. EXO data is not part of Bloodcraft's signed
`ProgressToClient` stream (the stream tops out at the shift-spell PrefabGUID at
field 45, no EXO fields), so we get it via the existing prestige-info intercept
by firing `.prestige get Exo` once on overlay first-show. The reply populates
`PlayerStateService.PrestigeInfoLatest` with `TypeName="Exo"`; the overlay
subscribes to `PrestigeInfoChanged`, filters to TypeName == "Exo", and renders
`"EXO Prestige: X / Y"` (or `"EXO Prestige: X"` if max isn't reported).

The fetch is deferred via a `CoreUpdateBehavior.Actions` ticker until
`MessageService.IsInitialized` flips true, mirroring the 0.8.1 fix for the
Familiar Browser's first-load auto-pull (IL2CPP gotcha #3). One-shot per panel
instance — re-toggling the overlay doesn't re-fire the query. Manual refresh
is still available via the Prestige tab's "Show prestige info" form (pick Exo,
Submit).

**Outside this release** (queued for v0.9.0):
- Dual text-size toggle (UI + overlays, separate). Small / Standard / Large.
- Per-overlay background transparency (0/25/50/75/100% — floor at 95% so panel
  handles remain visible/grabbable).
- Master overlay show/hide button next to the floating BCH button (session-only
  state; never makes hidden-by-config overlays visible).
- New Professions overlay (data already streamed via Eclipse `ProgressToClient`).

## 0.8.2 — Friend-testing hotfix: safety + dropdown + admin gate + docs

Friend-testing of v0.8.1 surfaced five issues — one critical, four ergonomics.
This release fixes all of them. The bigger feature work (text-size toggles,
per-overlay transparency, master overlay toggle, professions overlay, EXO
prestige row) is queued for v0.9.0; the chat-routing audit for `.wep get` /
`.lvl get` / `.class l` / `.misc userstats` etc. is queued for v0.8.3.

**Suspend-typing toggle REMOVED.** The `SuspendGameInputWhileTyping` footer
toggle has been removed entirely along with `Patches/InputActionSystemPatch.cs`,
the Settings accessor, and the config init. The Harmony prefix returning false
on `InputActionSystem.OnUpdate` was inherently incompatible with V Rising's UI
input pipeline — even with all the scope guards added across 0.1.1 → 0.1.3,
testers still got fully locked games and had to force-quit. Eclipse-main's
reference patch (a postfix observer that never blocks) confirms the prefix-
return-false approach is fundamentally wrong; a proper fix needs a different
patch target (filtering InputState components after the system writes them,
not skipping the system update). Until that redesign happens, the feature is
gone. Stale `SuspendGameInputWhileTyping` entries in user `.cfg` files are
inert. Updated `docs/LESSONS_LEARNED.md` § "InputActionSystem.OnUpdate Harmony
prefix returning false wedges UI input" to reflect the removal and document the
constraint for any future re-implementation.

**Dropdown popup no longer closes when you grab the scrollbar.** Root cause was
in `FormDropdownRegistry.TickCloseOnOutsideClick` (`UI/Forms/FormField.cs`) —
the outside-click check used `RectangleContainsScreenPoint` against the "Dropdown
List" rect, which doesn't reliably include the scrollbar across all canvas modes.
Clicks on the scrollbar handle counted as "outside" and dismissed the dropdown
mid-scroll, so users couldn't actually scroll through long enum lists. Switched
to `EventSystem.RaycastAll` + ancestry-walk: if any UI raycast hit is a descendant
of the dropdown's transform tree, the dropdown stays open. This naturally covers
the scrollbar, the handle, the item rows, and any future child widgets. Kept the
rect check as a fallback for the rare case where the canvas isn't raycast-registered.

**Dropdown popup taller — 150 → 250 px.** `UI/Framework/UniverseLib/UI/UIFactory.cs`
template `sizeDelta.y` bumped so long enum dropdowns (blood types, the 1-12 stat
indices for `.wep cst` / `.bl cst`, KindredCommands player lookups) show ~10
items before requiring scroll instead of ~6.

**Admin gate removed; replaced with an info note at the top of each admin tab.**
The previous `RenderAdminGate` flow had a "I am a server admin" toggle that flipped
`Settings.IsAdmin`. The toggle's rebuild path called `ShowTab(ActiveTab)` on the
same tab, which didn't fully rebuild the page in every code path — users had to
fully relaunch the game for admin tabs to surface. The gate added no security
either, since the server enforces permissions; non-admins clicking commands just
get rejection messages. So: gate removed across `BuildAdminTab`,
`BuildKindredLogisticsAdminTab`, `BuildKindredAdminPlayersTab`,
`BuildKindredAdminServerTab`, `BuildKindredAdminWorldTab`. Each now renders a
colored info note (`RenderAdminInfoNote`) at the top explaining the commands
require server-admin permission and that non-admin clicks fail safely.
`Settings.IsAdmin` / `Settings.SetIsAdmin` removed; stale `.cfg` entries are inert.

**Prestige tab — info rows + parsed-prestige box no longer crammed.** The four
"Current Prestige" rows (XP / Blood Legacy / Weapon Expertise / Familiar) used to
stack directly under the section heading with no padding; wrapped them in a
dedicated `PrestigeSummary` VerticalGroup with `spacing: 6` and `padding: 8/8/6/6`,
and bumped row font size 13 → 14. The parsed-prestige info box
(`BuildPrestigeInfoDisplay`) had `spacing: 2, padding: 6` — bumped to
`spacing: 8, padding: 12/12/10/10` and increased title font 14 → 16, body 12 → 13.
The effect-lines wrap noticeably less crowded now.

**Docs cleanup (README + LICENSE + Thunderstore notes):**

- Removed the "Screenshots go here" placeholder block. If screenshots are
  added in the future, they can land in `docs/screenshots/` with a proper
  reference.
- Removed the "Source-of-truth references (read-only)" section that listed
  the workspace's `LearningMods/` folders — that's internal-dev context, not
  end-user / Thunderstore-reader context.
- Removed the "Why combine BloodCraftUI + Eclipse?" historical-motivation
  section — at this point the mod stands on its own and the motivation is
  better captured in the `Acknowledgements` section.
- Removed `InputActionSystemPatch` from the layout diagram (file no longer
  exists) and the "Game-input suspension" bullet from the feature list.
- **Added a real "Acknowledgements" section** crediting Bloodcraft (zfolmt),
  KindredCommands (odjit), and KindredLogistics (odjit) explicitly with
  Thunderstore links. Ported the "About me" content from the in-UI About tab
  (Chaos / The Shadow Realm / Discord / PayPal / SkillEra.IO).
- `LICENSE.txt` third-party attribution updated to credit the upstream peer
  authors with their links, even though no code is bundled from those mods.
- Updated `Status:` line to v0.8.2 in the README.

**Outside this release** (queued for v0.8.3 / v0.9.0):
- v0.8.3: chat-routing audit. Every `.X get` / `.X list` reply that today
  lands in chat instead of the UI panel (`.wep get`, `.wep l`, `.bl l`,
  `.prestige l`, `.class l`, `.class lsp`, `.class lst`, `.misc userstats`,
  `.clan list`, etc. — ~22 commands total). Plus EXO prestige row in the
  Experience overlay.
- v0.9.0: dual text-size toggle (UI + overlays, separate); per-overlay
  transparency (0/25/50/75/95% — floor at 95% so handles stay grabbable);
  master overlay show/hide button next to the BCH floating button;
  Professions overlay (data already streamed via Eclipse).

## 0.8.1 — Familiar Browser glyph fix + deferred auto-pull

**Square-glyph buttons replaced with rendering-safe alternatives.** The header buttons used `◄` (BLACK LEFT-POINTING POINTER, U+25C4), `►` (U+25BA), and `↻` (CLOCKWISE OPEN CIRCLE ARROW, U+21BB) — V Rising's TMPro fallback font lacks all three glyphs, so they rendered as featureless squares with no indication of what they did. Replaced with `←` / `→` (LEFTWARDS / RIGHTWARDS ARROW, U+2190 / U+2192) which we already use successfully for the main Boxes tab Back button, and the literal word `Reload` for the refresh button. Buttons widened from 32→36px (arrows) / 32→64px (Reload) to fit the new labels at fontSize 18 (arrows) / 12 (Reload). Tooltips kept. Also updated the empty-list hint strings ("click ↻ to refresh" / "use ◄ ► to pick a box") to match the new glyphs.

**Deferred auto-pull fixes "boxes not loading on login".** The first-load `.fam boxes` auto-pull in `ConstructPanelContent` had an inline `if (MessageService.IsInitialized && BoxList.Count == 0)` check. With 0.6.0's overlay-restore-on-init feature, the panel now constructs DURING `Plugin.UIOnInitialize`, which runs from `CharacterHUDEntry.Awake` BEFORE `CommonClientDataSystem.OnUpdate` has a chance to call `MessageService.SetUser` / `SetCharacter`. The inline check saw `IsInitialized=false` and silently skipped, so a user with the overlay set to auto-restore opened it to an empty box list with no fetch ever happening. Replaced the inline check with `TickDeferredAutoPull` — a per-frame ticker registered with `CoreUpdateBehavior` that fires the auto-pull as soon as `MessageService.IsInitialized` flips true, then unregisters. Reset() removes the ticker so it doesn't keep firing after panel destruction.

## 0.8.0 — Browser sizing + Lookups + Vanilla Admin reference + Thunderstore links

**Familiar Browser overlay — taller default.** MinHeight bumped 360 → 440 so the default panel size shows ~12 familiar rows comfortably (full 10-fam box plus extras) without scrolling. Also tightened a couple of internal row heights 24→22.

**Kindred Admin: World — Lookups section promoted.** The `.search item` / `.search npc` forms already existed but were buried in a "Search" section easy to miss. Renamed to "Lookups (find IDs for spawn / give)" with a prominent italic intro explaining the workflow (search → copy prefab name → paste into Spawn / Give forms), better placeholders, richer tooltips, and a "List All Bosses" quick button for `.boss list`. KindredCommands does the heavy lifting server-side; replies appear in chat.

**About tab — Thunderstore links for backing mods.** Added "Open" buttons for:
- Bloodcraft on Thunderstore (zfolmt's mod page)
- KindredCommands on Thunderstore (odjit's mod page)

So if a player wants to research the underlying commands or check for updates, the page is one click away.

**New "Vanilla Admin" reference tab under HELP.** V Rising's vanilla admin commands (`adminauth` / `BanUser` / `Kick` / `give` / `giveset` / `SpawnUnit` / `Banhammer` / `Unban` / `BanList` / `Connectinfo` / `Save` / `List` / `Help` / etc.) are CONSOLE commands, not chat commands — they're typed into the in-game console (default key F1). BCH is a CLIENT mod that sends CHAT messages, so it can't trigger console commands directly. Added a documentation tab under HELP that:
- Explains the console-vs-chat distinction up front
- Documents the common vanilla admin commands organized by purpose (auth / players / spawning / server)
- Points each one at the existing chat-command equivalent in the Kindred admin tabs (so users know they don't have to drop into the console for routine tasks like kick/ban/give/spawn)
- Includes a short "how to use the console" section (F1 key, adminauth gate, command-line `-console` flag fallback)

**Outside this release** (still queued):
- PlayerNameField autocomplete dropdown
- `.class csp` shift-spell-picker dropdown
- Per-row Move in Boxes Edit mode
- Suspend-typing lockup root cause
- In-UI parsing of `.search item` / `.search npc` / `.boss list` replies (replies currently land in chat; future iteration could parse + click-to-fill into spawn forms)

## 0.7.0 — Final Bloodcraft audit gaps + scrollbar click + About-tab links

**Bloodcraft re-audit found 4 commands + 1 entire group missing.** Earlier "complete coverage" claims for `.prestige` / `.class` / `.quest` were over-stated. Wired:
- `.prestige ignore [Player]` — admin: toggle a player's leaderboard exclusion (paired with the leveling `.lvl ignore` form). Form on Bloodcraft Admin tab.
- `.prestige iacknowledge…` (the spelled-out global purge) — admin: globally remove every player's prestige buffs so config-changed buffs can be re-applied. Form on Admin tab with required confirm.
- `.quest c [Player] [Type]` — admin: force-complete a player's Daily/Weekly quest. Form on Admin tab.
- `.prof` (`.profession`) **whole group** — 4 commands. Player-facing log/get/list on a new "Profession Tools" section of the Levels tab; admin set on the Bloodcraft Admin tab. New `BloodcraftProfession` enum (8 professions) drives the dropdown.

Plus new `BloodcraftQuestType` enum (Daily/Weekly) for the .quest c form's type picker.

**About-tab URLs are now openable.** Each external link (Discord / PayPal / SkillEra.IO / GitHub repo) gets an inline "Open" button that calls `Application.OpenURL` to launch your default browser. Cleaner than wiring TMPro `<link>` handlers under IL2CPP. New `AddLinkRow` helper.

**Scrollbar click-to-drag works.** Unity's Slider.OnPointerDown should jump the value when you click the bar, but in our canvas hierarchy it only fires on the handle. Added `SliderClickRegistry` — a per-frame mouse-down handler (registered with `CoreUpdateBehavior` in `Plugin.Load`) that detects clicks anywhere on a registered slider's track and snaps the value. Skips when the click was on the handle so the existing Unity drag still owns that gesture. Sliders self-register from `UIFactory.CreateSliderScrollbar`.

**Outside this release** (still queued):
- PlayerNameField autocomplete dropdown (cache fills passively but no UI yet) — needs a custom suggestion widget
- `.class csp` shift-spell-picker dropdown — would need `.class lsp` parsing + new state slot for spell names per class
- Per-row Move in Boxes Edit mode — global form covers it; per-row workflow is multi-step
- Suspend-typing lockup root cause — safe default (off + force-disabled on load) is in place; needs a different input-suspension layer entirely

## 0.6.0 — Overlay sizing & persistence + .fam audit gaps

**Familiar Browser overlay — sizing fix.** The dedicated swap-warning slot was reserving ~36px at the top regardless of state. Combined it with the active-familiar label into a single dual-purpose status line: shows "Active: {name}" when idle, the swap-confirm warning when armed. Recovers the wasted vertical space — full 10-familiar boxes now fit comfortably without scrolling at the default panel height.

**Overlay visibility persists across sessions.** New bug found: every overlay (XP / Familiar / Familiar Browser / Daily Quest) defaulted to off on every login regardless of what the user had toggled. Root cause: the `Settings.Show*Overlay` config entries existed but were never read on init AND `BCHubUIManager.ToggleOverlay` never wrote to them.
- `BCHubUIManager.ToggleOverlay` now persists the new state via `Settings.SetShow*` after every flip.
- New `BCHubUIManager.RestoreOverlaysFromSettings()` runs from `Plugin.UIOnInitialize` after `SetupAndShowUI` to bring back any overlay that was visible at last logout.
- New settings: `ShowFamiliarBrowser`, `ShowDailyQuestOverlay` (the older `ShowExperienceOverlay` / `ShowFamiliarOverlay` are now actually wired).

**Server-side toggle "stickiness" caveat.** `Toggle Emotes` / `Toggle Combat` / `Toggle Shift` / `Toggle XP Log` / etc. are all **server-side flags** — Bloodcraft flips a value on the server and reports the new state in chat. The client never sees the underlying value, so we can't "remember" it in the .cfg the same way. A new italic note on the Levels tab explains this so the behavior isn't confusing.

**Bloodcraft `.fam` audit re-run — 13 commands wired** (the earlier "complete coverage" claim was wrong):

*More Familiar Actions section on the Familiars tab:*
- `.fam s [Name]` — search boxes by familiar name
- `.fam sb [Name]` — smartbind (search + bind in one step)
- `.fam shiny [SpellSchool]` — spend vampiric dust to make active familiar shiny (new `FamiliarShinySchoolChoice` enum: Blood / Storm / Unholy / Chaos / Frost / Illusion)
- `.fam option [Setting]` — toggle per-player familiar settings
- `.fam echoes [VBloodName]` — buy V-Blood exo reward
- `.fam reset` — destroy all entities in follower buffer (DESTRUCTIVE; required confirm)

*Battle Groups section on the Familiars tab — full PvP-grouping system, 7 commands:*
- `.fam bgs` (list) / `.fam bg [group]` (show)
- `.fam cbg [group]` (choose active) / `.fam abg [group]` (create) / `.fam dbg [group]` (delete, with required confirm)
- `.fam sbg [group] [slot]` (assign active familiar to slot)
- `.fam challenge [player]` (initiate / queue PvP)

**Bloodcraft `.misc` audit — 6 player-facing commands wired** (Levels tab → new Player Tools section):
- `.misc userstats`, `.misc remindme`, `.misc silence`, `.misc kitme`, `.misc prepare`
- `.misc sct [Type]` (collapsible form with type field)

**Bloodcraft `.lvl` audit — 2 commands wired:**
- `.lvl log` — toggle XP-gain logging (Player Tools)
- `.lvl ignore [Player]` — admin: toggle shared-XP exclusion (Bloodcraft Admin tab)

**Outside this release** (still queued from the earlier polish list):
- Scrollbar click-to-drag investigation
- PlayerNameField autocomplete dropdown (cache fills passively but no UI yet)
- Clickable About-tab URLs (TMPro `<link>` + click handler)
- `.class csp` shift-spell-picker dropdown (currently IntField)
- Per-row Move in Boxes Edit mode (per-row Delete works)

## 0.5.0 — Audit gaps + UX polish + per-row edit mode

**Top-right "—" close button now actually closes.** `MainPanel` skips `ResizeablePanelBase.ConstructPanelContent()` (which would hide the title bar entirely), so the inherited title bar with its dead "—" button stayed visible. Wired `OnClosePanelClicked` to `SetActive(false)` so the button hides the panel; the floating button stays so you can reopen.

**Footer toggles wrap to 2 rows.** Adding the Familiar Browser toggle in 0.4.1 + the long "Suspend game input when typing" label pushed toggles off the right edge at the panel's MinWidth=600. Footer is now a vertical wrapper containing two horizontal rows: row 1 holds the four overlay toggles (XP / Familiar / Familiar Browser / Daily quest), row 2 holds the two behavior toggles (Auto-resize / Suspend-input). Reduced per-toggle min-width 200→130 and shortened the suspend label to "Suspend game input on type (experimental)".

**Familiar Browser polish.**
- Tooltips on ◄ / ► / ↻ buttons explaining what each does (was non-descript squares).
- Box-name header now reads `BoxName  (X / N)` so you can see your position in the cycle.
- Familiar list wrapped in a ScrollView so a full box of 10+ familiars no longer overlaps the Unbind footer. Also bumped MinHeight 240→360 so the default size shows ~10 rows comfortably.

**Input-field + dropdown contrast.** `CreateInputField` and `CreateDropdown` were using `Theme.DarkBackground` (matches the panel background, ~0.07 brightness), so fields nearly invisibly blended. Now: lighter slate background (0.18, 0.18, 0.21) plus a subtle ~0.55 gray outline. Visible at any panel opacity.

**Class tab — `.class csp` shift-spell-picker form.** New collapsible "Choose class shift spell" form: IntField 1-32 + Submit. Use 'List Spells' (already there) to see the numbered options before submitting.

**Blood Legacy tab — in-UI `.bl get [Type]` parser.** Mirrors the 0.3.0 prestige-info pattern. Submit the "Show info for a specific blood" form, the multi-line reply is parsed into `BloodInfo` (BloodType / Level / Prestige / Essence / ProgressPct / StatLines) and rendered in a panel below: title in Bloodcraft red, level summary, bulleted stat lines. New `InterceptFlag.AwaitingBloodInfo` / `ReceivingBloodInfo` + `_bloodHeaderRegex` / `_bloodStatLineRegex` parsers. The bare `.bl get` (no arg) is intentionally NOT intercepted — the live Eclipse stream already keeps the equipped blood current and intercepting would clobber the structured display on every Refresh click.

**Boxes tab — per-row edit mode.** New "Edit mode" toggle next to the Reload button on the box content view. When ON, each familiar row sprouts a red Delete button: first click changes its label to "Confirm?", second click within 3 seconds fires `.fam r {index}` and auto-refreshes the list. Off by default so accidental clicks can't trigger destruction. Move-from-row stays as the existing global form (Bloodcraft's `.fam mb` is multi-step and doesn't fit a one-click row UX).

**KindredCommands audit gaps wired** — 13 commands that existed in KindredCommands but weren't surfaced anywhere in BCH:
- *Admin: Players* — `.playerinfo`, `.idcheck`, `.assignsteamID`, `.showhair`, `.gruelsettings`, `.feedsettings`, `.unbindall` (with required confirm)
- *Admin: Server* — `.wipe` (queue), `.commencewipe` (with required confirm), `.cancelwipe`
- *Admin: World* — `.longestofflinecastles`, `.clan castles`, `.clan fix`, `.bloodbound add`, `.bloodbound remove`

Each gets the same form-with-tooltip pattern as the existing admin sub-tabs; destructive ones get the `RequireTrue` confirm checkbox.

**Deferred:** scrollbar click-to-drag (mouse wheel works, click-on-bar doesn't move the handle). Needs deeper investigation into Unity's Slider raycast / event flow vs the parent ScrollRect — saving for a future pass.

## 0.4.1 — Familiar Browser overlay

**New `FamiliarBrowserOverlayPanel`** — an overlay-sized version of the Boxes tab so you can switch boxes and bind/unbind familiars without opening the main panel. Independent draggable / resizable like the other overlays.

Layout:
- Header row: ◄ {ActiveBoxName} ► [↻] — prev/next cycles through `PlayerStateService.BoxList` and fires `.fam cb {box}` + `.fam l`; the refresh button re-pulls boxes + contents
- Active-familiar indicator: `Active: {Name} Lv N` (or "(none bound)")
- Reserved warning slot for the auto-swap confirm (matches the main Boxes tab's no-shift behavior)
- Familiar list: clickable rows formatted `01 — Name Lv N P{prestige} ★ {shiny}` — click to bind, with the same two-click destruction-confirm as the main Boxes tab when one is already bound
- Footer: "Unbind active" button (disabled when no familiar is bound)

On first show, if `PlayerStateService.BoxList` is empty, sends `.fam boxes` automatically — so the overlay is useful even if the user never opens the main Boxes tab.

Auto-swap state is local to the overlay, so arming a swap here doesn't carry into the main panel. Subscribes to BoxList / BoxContents / ActiveBox / Familiar changed events; Reset() unsubscribes.

Toggle from the panel footer alongside the existing "XP overlay" / "Familiar overlay" / "Daily quest" checkboxes. New label "**Familiar Browser**" — distinct from the existing "Familiar overlay" (which still shows just the active familiar's stats).

New `PanelType.FamiliarBrowserOverlay` enum value; wired through `BCHubUIManager` (Reset / SetActive / ToggleOverlay / IsOverlayOpen / EnsureFamiliarBrowserOverlay).

## 0.4.0 — Weapon Expertise stat picker + Blood Legacy tab

**`EnumIndexField<T>`** — new `FormField` subclass; same dropdown UX as `EnumField<T>` but emits the 1-based dropdown index ("1".."12") instead of the enum NAME ("PhysicalPower"). Bloodcraft's `.wep cst <Weapon> <StatIndex>` and `.bl cst <Blood> <StatIndex>` use a 1-based int (the command body does `--statType` to re-zero-base it server-side). Wrapping in a typed dropdown lets the user pick a NAMED stat while the form still substitutes the integer the parser expects.

**Three new picker enums** in `PlayerStateService`, mirroring Bloodcraft's enum order and excluding the sentinel/non-pickable values:
- `WeaponBonusStat` (12 values: MaxHealth → SpellCritDamage)
- `BloodBonusStat` (12 values: HealingReceived → CorruptionDamageReduction)
- `BloodTypeChoice` (10 valid bloods: Worker / Warrior / Scholar / Rogue / Mutant / Draculin / Immortal / Creature / Brute / Corruption — excludes None / VBlood / Frailed / GateBoss which Bloodcraft rejects as legacy choices)

**Weapon Expertise tab — bonus-stat picker.** Replaced the dead-end "use chat" note with a collapsible "Set bonus stat for a weapon (.wep cst)" form: WeaponType dropdown + EnumIndexField<WeaponBonusStat> dropdown. Submit auto-refreshes via `.wep get`. New `BCCOM_WEP_CHOOSE_STAT_FORMAT` constant. The "show all weapons' settings in one view" the user asked for in earlier feedback **is not possible** — Bloodcraft's `.wep get` only ever returns the equipped weapon's data, no command queries non-equipped weapons. Surfaced this limitation in a new italic note on the tab so the user isn't left wondering.

**New Blood Legacy tab** under BLOODCRAFT (between Weapon Expertise and Unarmed + Shift). Mirrors the Weapon Expertise tab's structure:
- Live "Current Blood Legacy" labels (type / level + progress + prestige / chosen bonus stats) fed by `PlayerStateService.Legacy` via the existing Eclipse stream
- Action row: Refresh / List Bloods / List Stats / Reset Stats (`.bl get` / `.bl l` / `.bl lst` / `.bl rst`)
- Collapsible: **Set bonus stat for a blood type** (`.bl cst <Blood> <StatIndex>`) — BloodTypeChoice + EnumIndexField<BloodBonusStat>; submit auto-refreshes via `.bl get`
- Collapsible: **Show info for a specific blood** (`.bl get <Blood>`) — unlike `.wep get`, Bloodcraft's `.bl get` accepts a blood-type argument so you CAN inspect a non-current blood's level + chosen stats. Reply still goes to chat (in-UI parsing is a future iteration)
- Italic note explaining the difference from Weapon Expertise

New BCCOM constants: `BCCOM_BL_GET_FORMAT`, `BCCOM_BL_LIST`, `BCCOM_BL_LIST_STATS`, `BCCOM_BL_RESET_STATS`, `BCCOM_BL_CHOOSE_STAT_FORMAT`.

Outside this release: per-row Move/Delete in box contents, in-UI parsing of `.bl get` reply (analogous to the `.prestige get` parser added in 0.3.0).

## 0.3.1 — Form post-submit hook + About tab

**FormBuilder gained an `onSubmitted` callback overload.** Lets a form chain a follow-up command after the primary one is enqueued. Used to:
- Auto-refresh the box-contents list (`.fam l`) after Permanently Delete familiar (`.fam r`) — the user's previous view stayed stale until they manually clicked Reload.
- Auto-refresh after Move active familiar (`.fam mb`) for the same reason.
- Auto-refresh the box list (`.fam boxes`) after Create / Delete-empty / Rename box.

Existing `FormBuilder.Build(parent, title, template, params fields)` stays — overload resolution picks the new `Build(parent, title, template, Action onSubmitted, params fields)` only when a callback is passed.

**About tab under HELP.** New tab next to Quick Start with credits + community links:
- Bloodcraft credit (zfolmt) — "Leveling, expertise, legacies, professions, familiars, classes, quests!"
- KindredCommands credit (odjit) — "Commands to expand administration efforts and provide information"
- About me: player Chaos, V Rising server "The Shadow Realm" (Brutal, PvE), Discord link, PayPal support link, personal website
- About this UI: open source pointer to the GitHub repo

## 0.3.0 — Class apply + in-UI Prestige info

**Class tab — apply a class from the UI.** New collapsible "Select / change your class (.class s)" form on the Class tab. Dropdown picks from `BloodcraftClassChoice` (BloodKnight / DemonHunter / VampireLord / ShadowBlade / ArcaneSorcerer / DeathMage — Bloodcraft's six built-in classes; the new enum is a subset of `PlayerClass` minus the `None` sentinel so users can't pick an invalid value). Submit sends `.class s {Class}`; Bloodcraft replies in chat with success or rejection. New `BCCOM_CLASS_SELECT_FORMAT` / `BCCOM_CLASS_CHANGE_FORMAT` constants for the two equivalent aliases.

**Prestige tab — in-UI display of `.prestige get` results.** The "Show prestige info" form already existed but only echoed to chat. Now its multi-line reply is parsed into a structured `PrestigeInfo` (TypeName, Level, MaxLevel, EffectLines) and rendered in a dedicated panel below the form:
- Title in Bloodcraft green (`#90EE90`): `{Type} Prestige Info`
- Level line: `Current Prestige Level: {N} / {Max}`
- Effect lines: bulleted list of growth-rate / stat-bonus / total-effect text Bloodcraft sends, color tags stripped

Implementation: new `InterceptFlag.AwaitingPrestigeInfo` / `ReceivingPrestigeInfo` states, regex match on the `<color=#90EE90>Type</color> Prestige Info:` header to enter receive mode, a level-line regex (`<color=yellow>{N}</color>/{Max}`) to capture the bracket, then any further line in receive mode is treated as an "effect" line (color tags stripped via `<[^>]+>` replace). Existing 600 ms timeout flush handles end-of-reply. `NoteOutboundForIntercept` arms when the outgoing command starts with `.prestige get ` (matches all PrestigeType variants without enumerating them).

`PlayerStateService` gained `PrestigeInfo` struct + `PrestigeInfoLatest` snapshot + `PrestigeInfoChanged` event. The Prestige tab subscribes; Reset() unsubscribes.

Outside this release: Weapon Expertise per-weapon view, Blood attributes tab, per-row Move/Delete in box contents — still queued for 0.3.x.

## 0.2.1 — Relabel destruction, add real Move/Delete, both quests in overlay, server availability

**Familiars tab — `.fam ub` is "Unbind", not "Destroy".** Tracing Bloodcraft source confirmed `.fam ub` destroys the in-world familiar entity but **preserves the box record** (level/prestige/shiny intact); the familiar can be re-bound from the box at any time. So calling it "Destroy (Permanent)" was misleading. Renamed the red two-click button to plain **Unbind** (no red color, no confirm) and updated the tooltip to explain the in-world-only effect plus point at the new Permanently Delete form for actual destruction.

**Boxes tab — auto-swap warning rewritten + layout no longer shifts.** The 0.2.0 warning ("DESTROY current and bind it") was wrong for the same reason as above. New text reads "Active: FamX. Click {target} again within 5s to unbind current and bind it. The current familiar returns to its box (level/prestige preserved); use Permanently Delete below to actually remove it from your collection." The warning label now reserves space (~38px) even when empty, so showing/hiding it doesn't push the familiar list down and yank the click target out from under the cursor.

**Boxes tab — Move + Permanently Delete forms.** Two new collapsible forms at the bottom of the box content view:
- **Move active familiar to box (.fam mb)** — moves the currently-bound familiar into the named box. User has to bind first; explained in the form tooltip.
- **Permanently delete familiar from box (.fam r)** — IntField for index + a `RequireTrue` BoolField "Yes, permanently delete" that gates submission. The form refuses to submit unless the confirm box is checked, so accidental deletion needs both an unchecked-by-default confirm AND a Submit click.

`BoolField` gained an optional `RequireTrue` flag for confirmation gates on destructive forms.

**Daily Quest overlay — both quests at once.** `DailyQuestOverlayPanel` now renders Daily AND Weekly stacked, each with its own header (cyan `#00FFFF` for Daily, magenta `#BF40BF` for Weekly), target line, and progress line. The reroll hint adapts (`.quest r d` vs `.quest r w`).

**Server availability detection — collapses tab groups when the backing mod is absent.**
- New `Settings.ModAvailability` (Auto / On / Off) for both Bloodcraft and Kindred.
- Auto for Bloodcraft = present iff `EclipseProtocolService.UserRegistered` is true (server ACK'd our Eclipse handshake).
- Auto for Kindred = currently always-on (no protocol indicator yet — set to Off manually if your server doesn't have it).
- When unavailable, the left-rail group header shows `–  GROUPNAME  (unavailable)` in gray, becomes non-interactable, and starts collapsed. Tab buttons within stay rendered so the user can still see what BCH supports.
- Override via the .cfg: `BloodcraftAvailability` / `KindredAvailability` keys under `[GeneralOptions]`, values `Auto` / `On` / `Off`.

## 0.2.0 — Shiny labels, safe auto-swap, Daily Quests, admin-tab cleanup

**Boxes tab — shiny element labels.** `FamiliarBoxEntry` now exposes `ShinySchool` derived from the captured shiny color hex (Bloodcraft v1.13.x mapping in `FamiliarShinySchools`: `#A020F0`→Chaos, `#FFD700`→Storm, `#FF0000`→Blood, `#008080`→Illusion, `#00FFFF`→Frost, `#00FF00`→Unholy). Box rows now render as `01  —  RoyalRavager   Lv 12  P3  ★ Storm`.

**Boxes tab — safe two-click auto-swap.** Clicking a familiar while another is bound now arms a destruction-confirm: a warm-orange banner appears explaining "Active: FamX. Click {target} again within 5s to DESTROY current and bind it. Bloodcraft has no non-destructive switch." Second click on the SAME familiar within the window fires `.fam ub` (destroy) → `.fam b N` (bind new) sequentially. State clears on box change, Back, timeout, or any other familiar click. Bloodcraft's bind strictly errors if a familiar is bound and `.fam t` (toggle/dismiss) doesn't free the slot — `HasActiveFamiliar()` only returns false once the entity is *destroyed*. So the destructive path is the only one Bloodcraft offers; the UI surfaces it explicitly instead of silently destroying.

**`.castle openplots` → `.openplots`.** The KindredCommands command isn't in the `castle` group; it's a top-level `.openplots` (alias `.op`). Old constant returned "command not found"; fixed and updated the tooltip.

**Kindred Logistics admin split into its own tab.** The "Admin Globals (.lg)" CollapsibleSection that used to live at the bottom of the Logistics tab is now a sibling tab `Logistics: Admin` under KINDRED, gated behind `RenderAdminGate("Kindred Logistics admin")`. Non-admins see the placeholder + "I am a server admin" toggle they're already used to; admins see the full set of `.lg` toggles + `.adminstash` form. Player-side Logistics (personal toggles, utility commands) stays on the original Logistics tab.

**Daily Quests tab + overlay (Bloodcraft `.quest`).** New tab under BLOODCRAFT showing both Daily and Weekly quest state from `PlayerStateService.DailyQuest` / `WeeklyQuest` (already streamed by the Eclipse protocol). Each section has Refresh / Track / Reroll buttons (`.quest p|t|r d|w`) plus a Toggle Quest Log button (`.quest log`). The new `DailyQuestOverlay` is a small movable HUD identical in style to the XP and Familiar overlays — toggle from the panel footer (third overlay checkbox). Shows target name, V-Blood marker, and progress / completion state.

**Quick Start Guide formatting.** Replaced the line-count-based `EstimateHeight` (consistently overshot by a couple of px per line, producing visible gaps between sections) with `ContentSizeFitter.PreferredSize` so each section sizes itself to the actual rendered TMP text. Sections now sit flush against each other.

Outside this release: per-row Move/Delete edit mode in box contents, Class-apply UI, Weapon Expertise per-weapon view, Blood attributes tab, in-UI Prestige display — still queued for 0.2.x.

## 0.1.3 — Safety + Boxes polish

**Suspend-typing toggle defaulted OFF + force-disabled on load.** Same root cause as the 0.1.1 lockup is still present: returning `false` from `InputActionSystem.OnUpdate` also wedges UI input. The 0.1.2 scoping fix only narrowed *when* the patch fires (BCH fields only), not the underlying mechanism — and as the user discovered, clicking into a Kindred Logistics text field with the toggle on still freezes the panel. Until a non-locking suspension mechanism is in place: default off; force-disable on `Plugin.Load` if the user has it on (one-time write, logged); footer toggle now carries an EXPERIMENTAL warning + tooltip explaining the lockup. Anyone who genuinely wants the feature can opt in each session.

**Auto-resize: chrome budget bumped 76 → 110px.** The earlier number missed the panel's title-bar height; manifested as the panel coming up a row or two short when switching to a long box-content view.

**Familiars tab — Emote Bindings reference.** Bloodcraft has no chat command to *trigger* an emote programmatically, so a "Beckon" button isn't possible — the player must perform the emote in-world. Added a small reference card so the bindings (Wave→recall, Salute→combat, Clap→bind, Beckon→interact/inventory) are visible without having to remember them.

**Boxes tab — richer per-familiar display.** Extended `BOX_CONTENT_ENTRY_REGEX` to capture level, prestige tier, and shiny indicator (Bloodcraft sends them all in the `.fam l` reply already; we just weren't parsing them). `FamiliarBoxEntry` now has `Level`, `Prestige`, `IsShiny`, `ShinyColorHex` fields. List rows now render as `01  —  RoyalRavager   Lv 12  P3  ★`.

**Boxes tab — box management.** New collapsible forms in the picker view: Create new box (`.fam ab`), Delete empty box (`.fam db`), Rename box (`.fam rb`). Move-familiar-between-boxes is intentionally deferred to 0.2.0 because Bloodcraft's `.fam mb` only acts on the currently-bound familiar — the workflow needs binding first, then the move command, which is a multi-step UX that fits better with the planned "edit mode" toggle.

Outside this release: `.fam mb` workflow, per-row Move/Delete in box contents, Class-apply UI, Weapon Expertise per-weapon view, Blood attributes tab, in-UI Prestige display — all queued for 0.2.x.

## 0.1.2 — Hotfix for two 0.1.1 regressions

**Removed `SuspendGameInputWhileUIOpen`.** The 0.1.1 implementation returned `false` from `InputActionSystem.OnUpdate` whenever the BCH panel was open — but that also wedged Unity's UI input pipeline, so any user with the toggle on couldn't click the panel, couldn't escape, and couldn't disable the setting. Worse, the value persisted in the .cfg, so they were locked out across sessions the moment they opened BCH again. The setting and footer toggle are gone; the value in the user's existing `.cfg` is now an inert orphan (BepInEx ignores keys it doesn't bind).

**V Rising native chat — Enter no longer fires gameplay.** Scoped `InputActionSystemPatch` to fields that are descendants of `Plugin.UIManager.UIRoot`. Previously the patch suppressed input for any focused TMP_InputField — including V Rising's own chat — and the suppression was apparently being layered on top of V Rising's own input gating, so the closing Enter keystroke bled through into gameplay (attack/etc.) the same frame. With the descendant check, V Rising's chat is no longer touched, vanilla input handling applies, no bleed-through. Removed the 100ms focus-release grace from 0.1.1 — it was the wrong layer to fix this at and unneeded with the scoping fix.

## 0.1.1 — Bugfix sweep (post-0.1.0 in-game testing)

Driven by a comprehensive bug report after the first 0.1.0 in-game session.

**Familiars tab — wrong Bloodcraft commands.** Audit against current Bloodcraft v1.13.x sources caught three command aliases that no longer match:
- `BCCOM_FAM_UNBIND` was `.fam u` → `.fam ub`
- `BCCOM_FAM_COMBAT` was `.fam combat` → `.fam c`
- `BCCOM_FAM_TOGGLE`  was `.fam toggle` → `.fam t`
- Removed dead `BCCOM_FAM_RESET_STATS` (`.fam rs` doesn't exist server-side)
- New: `BCCOM_FAM_TOGGLE_EMOTES` (`.fam e`), `BCCOM_FAM_LIST_EMOTES` (`.fam actions`)

The Familiars action row is now two rows. Toggle is re-labeled **Recall / Dismiss** (recallable) and Unbind is re-labeled **Destroy (Permanent)** with red tint and a two-click confirm — these were previously identical-looking and identical-named, and a single click on the wrong button permanently destroyed a familiar. The old Combat button now sends the correct `.fam c`. New Toggle/List Emotes buttons surface the emote-binding system that ties clap (or any emote) to "open familiar inventory" — previously a player had no in-UI way to know clap was bound.

**Tooltips never ticked.** Log diagnostic showed `ticking=False, bindings=415` — bindings registered fine, but `EnsureTicking()` was bailing on a load-order race against `Plugin.CoreUpdateBehavior`. Moved `TooltipHover.TickAll` registration to `Plugin.Load()` directly (alongside `MessageService.ProcessAllMessages`); removed the lazy `EnsureTicking` indirection that was failing.

**Boxes tab fixes:**
- Box-content regex didn't match Bloodcraft v1.13.x format (server adds a space after `|`); added `\s*` so both old and new formats match. `.fam l` was returning zero entries → UI sat on "Loading familiars for X…" forever.
- The state machine waited for a "first non-color line" terminator that frequently never arrived; replaced with a timeout-based flush registered in `Plugin.Load()` (`TickInterceptTimeouts`, 600ms grace). Also stops mid-list system messages from prematurely flushing partial state.
- Auto-pull `.fam boxes` on first open of the Boxes tab so the user doesn't have to click Refresh on every cold open.
- Moved the "click Refresh / click a box" hint to the top of the tab so it can never be visually overlapped by a long box list.
- Dropped fixed `flexibleHeight: 1` on picker/content sections so the box list takes its natural height; AutoResize grows the panel and the tab-page ScrollView (Phase 5j) handles overflow when the panel hits 0.9*screen.
- Logging on intercept transitions (arm + flush) for future debugging.

**Form / collapsible-section sizing.** Two recurring "fixed preferredHeight that's wrong" bugs were causing forms to overlap each other and the submit button to render off-screen:
- `CollapsibleSection`'s content container had `preferredHeight: 80` regardless of contents → form rendered on top of the next sibling.
- `FormBuilder` predicted form height as `70 + fields*34` → wrong for any form with an `EnumField`/`PlayerNameField`, submit button got cut off.

Both now drop fixed `preferredHeight` and let the inner `VerticalLayoutGroup` auto-compute; `AutoResize` sees the correct preferred-height and grows the panel to fit. Multiple expanded forms now stack instead of overlapping.

**Dropdown couldn't be closed without selecting.** TMP_Dropdown's own click-outside Blocker doesn't fire reliably in our canvas setup. Added `FormDropdownRegistry.TickCloseOnOutsideClick` registered in `Plugin.Load` — closes any open dropdown when the user clicks outside its rect.

**Input handling — three related fixes:**
- Critical lockup on text-field click (game keeps acting + UI unresponsive): added focus-change diagnostic logging to `InputActionSystemPatch` so a future repro produces actionable log evidence.
- Native chat conflict (Enter-to-send in V Rising chat fired a gameplay action the same frame): added a 100ms focus-release grace window; the closing keystroke can no longer bleed through.
- New `Settings.SuspendGameInputWhileUIOpen` toggle (default off) + footer checkbox: when on, **all** game input is suspended whenever the main BCH panel is visible, not just while typing. Solves "my character attacks/moves on every UI click."

**Levels tab.** Added a "List Weapon Types (chat)" button and a one-line note explaining that the Eclipse protocol only streams the active weapon's expertise — per-weapon snapshots aren't available without server-side support, so the in-panel "all weapons" view requested isn't possible yet. Surfacing the limitation so the user isn't left wondering.

**Admin gating.** New `Settings.IsAdmin` flag (default off). When off, the Bloodcraft Admin tab and the three Kindred admin sub-tabs (Players / Server / World) display a placeholder explaining the gate plus a "I am a server admin" toggle. Flipping the toggle re-shows the current tab with full admin commands. Keeps non-admin players from being shown commands the server would reject anyway.

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

### Phase 5i — KindredCommands admin sub-tabs

The largest single phase by command count. KindredCommands' admin surface was inventoried at **~146 commands** and split into three sibling tabs under KINDRED.

- `Services/MessageService_Processing.cs` — new BCCOM_KCA_* region (`KCA` = KindredCommands Admin). Constants are grouped per sub-tab (Players / Server / World) and per CommandGroup within, with `_FORMAT` templates for arg-taking commands.
- `UI/ModContent/Data/PanelType.cs` — three new enum values: `KindredAdminPlayersTab`, `KindredAdminServerTab`, `KindredAdminWorldTab`. KINDRED rail group now hosts 5 tabs (Logistics, Commands, Admin: Players, Admin: Server, Admin: World).
- `MainPanel` made `partial`. The admin Build methods live in a new file `UI/ModContent/MainPanel.KindredAdmin.cs` so the main `MainPanel.cs` stays digestible. Dispatch in `BuildContentArea` switches to the three new methods.
- **Admin: Players (~59 commands)** — General, Identity (rename / unbind / swap), Fly (toggle / up / down / level / height / obstacle), Buffs & items (.buff, .debuff, .give, .bloodpotion, .bloodpotionmix), Boost (.bst, wrapped in its own outer collapsible — 22 commands), Gear (player), Clan (player). Most commands take an optional player arg with placeholder "blank for self".
- **Admin: Server (~53 commands)** — Global toggles, Time / respawn, Announcements (4), Dropped items (8), Regions (10), Boss locks (4), Server-side Gear (6), Clan rename, Prisoner config (4), Staff (6).
- **Admin: World (~34 commands)** — Position (whereami / horse teleport), Search (item / npc), Spawn (NPC / custom / coords / despawn / horse / spawn-ban), Boss modify+teleport, Castle (10 — claim, decay reports, freeze/thaw, plot info), Servant (7), Gear range-based (2).
- Implementation patterns: zero-arg commands appear as `AddCommandButton` rows; arg-taking commands are each their own `CollapsibleSection` (collapsed by default) wrapping a `FormBuilder` form. Common shapes are extracted into helpers (`AddPlayerCollapse` for optional-player commands, `AddBstValueForm` / `AddBstTogglePlayer` for the boost wall, `AddSimpleTextCollapse` for one-string commands).
- VCF custom types (`FoundUnit`, `FoundItem`, `FoundRegion`, `FoundVBlood`, `FoundPrimal`, `BuffInput`, etc.) are surfaced as `TextField` — the server resolves the string. `BloodType` uses `EnumField<PlayerStateService.BloodType>` since we already have that enum.

Player-facing equivalents from Phase 5h remain on the "Commands" tab. Phase 5h's 13 commands are not duplicated into the admin tabs.

### Phase 5j — Polish

Four targeted polish items before release-prep starts.

- **ScrollRect on every tab page.** `CreateTabPage` now returns the `CreateScrollView` wrapper (the visible tab GameObject) and out-params the inner content where `BuildXxxTab` adds children. A parallel `_tabInnerContent[PanelType]` dict feeds `AutoResizeIfEnabled` the true children-sum height; the panel still grows to fit short content and caps at 90% screen, but tall admin tabs scroll instead of clipping. Scroll wheel is permanently-visible vertical, sensitivity 35.
- **Passive player-name harvesting from chat.** `PlayerNameCacheService.TryHarvestNames` runs a conservative regex (`<color=...>([A-Za-z][A-Za-z0-9_]{2,19})</color>`) against every inbound chat line that Eclipse didn't consume. A small denylist filters recurring non-name tokens (Bloodcraft / Server / Online / Joined / etc.). `ClientChatPatch` calls it between the Eclipse handler and the regex pipeline.
- **Shift-spell prefab name lookup.** New `Resources/PrefabNameResolver.cs` lazily builds a `Dictionary<int, string>` by inverting `PrefabCollectionSystem.SpawnableNameToPrefabGuidDictionary` on first call. `RenderUnarmedShift` now shows `Equipped: <name>` when the resolver knows the spell and falls back to `Equipped: PrefabGUID <hash>` when not yet built or unknown. Pattern ported from `LearningMods/Eclipse-main/Services/LocalizationService.cs`.
- **`.clan list` pagination widget.** The static "Clan List" button on the Commands tab is replaced with a stateful pager: `[<]  Clan List p1  [>]`. Click Prev/Next to fire `.clan list <page>` with the new page number; the label tracks the page the next press will request. State (`_clanListPage`) lives on the panel instance.

### Phase 6 — Release prep (v0.1.0)

First public release prep — everything needed before the Thunderstore upload.

- **LICENSE.txt** replaced with MIT (copyright 2026 KDavidP1987) plus a third-party attribution block noting the ported portions from BloodCraftUI (panthernet, unlicensed in-repo — author contact required for redistribution beyond MIT) and Eclipse (zfolmt, has LICENSE.md — comply for derivative use).
- **Thunderstore metadata** filled in: `thunderstore.toml` and `BloodCraftHub.csproj` `<Description>` now mention KindredCommands + KindredLogistics. `<PackageProjectUrl>` set to the GitHub repo so the generated `manifest.json` populates `website_url`.
- **README** restructured for end users: lead with status + screenshots placeholder, installation instructions (TMM / r2modman / manual), compatibility matrix (BepInEx 1.733.2, Bloodcraft v1.13.21, KindredCommands v2.5.8, KindredLogistics v1.6.0, optional Eclipse v1.3.13 + BloodCraftUI v1.1.0), known-issues section. Dev docs moved to a "For developers" section at the bottom.
- **`docs/screenshots/` placeholder** with a README listing the suggested shots for the first release (floating button, main panel, admin tabs, overlays, Quick Start).
- **Release ZIP** built at `dist/BloodCraftHub-0.1.0.zip` (130 KB) containing `BloodCraftHub.dll` + `manifest.json` + `icon.png` (256×256, verified) + `README.md` + `LICENSE.txt` + `CHANGELOG.md` at zip root. Ready for drag-and-drop to thunderstore.io/c/v-rising/create/.
- **Publication is the user's manual step** — Thunderstore uploads are public + one-way, so the assistant stops at "ZIP is ready" rather than calling `tcli publish` autonomously.

## Phases remaining

- Nothing scheduled. v0.1.0 ships once the user drops the ZIP into thunderstore.io.
