# Changelog

> **Full per-version history with technical detail lives at**
> https://github.com/KDavidP1987/BloodCraftHub/blob/main/CHANGELOG.md
>
> Thunderstore caps embedded CHANGELOG.md at 100,000 characters, so this
> bundled copy summarizes earlier versions and reproduces the most
> recent release in full.

## 0.13.1 — Hotfix: AwaitingBloodInfo timeout spam on Frailed / no-blood states

User-reported bug: when a player's blood drains to Frailed (or
transitions to a non-bondable type like VBlood / GateBoss), the BCH UI
starts spamming the BepInEx log with paired
`Intercept armed: AwaitingBloodInfo` /
`Intercept 'AwaitingBloodInfo' timed out` warnings every few seconds.

**Root cause:** two auto-refresh tickers — the Blood Legacy tab's
per-tab refresh and the XP overlay's bonus-stats refresh — fire
`.bl get <CurrentBlood>` against the player's current blood type.
Bloodcraft accepts 10 player-bondable types (Worker / Warrior /
Scholar / Rogue / Mutant / Draculin / Immortal / Creature / Brute /
Corruption) but rejects unit-category markers (Frailed / VBlood /
GateBoss). Sending `.bl get Frailed` produces no reply; the armed
intercept times out and re-arms on the next tick. The existing guard
`(int)leg.Type != 0` was wrong in both directions — it falsely
skipped `Worker` (enum value 0, a valid bondable blood) and let
`Frailed` through.

**Fix:** new `PlayerStateService.IsBondableBloodType(BloodType)`
predicate gates both auto-fire sites. Worker blood now refreshes
correctly; Frailed / VBlood / GateBoss are silent no-ops until the
player drinks a normal blood again.

## 0.13.0 — Mod Help reference + per-profession toggles + class context cards

Information-architecture release. Bloodcraft is a deep mod and BCH used
to make you swap to the Quick Start tab (or chat) to remember what each
class / stat / prestige tier does. v0.13 puts that information where you
need it.

**Mod Help tab (new under Settings & Help).** Section-by-section
reference for every Bloodcraft system — XP leveling, weapon expertise,
blood legacies, the six classes (with full per-class weapon + blood
synergies and on-hit debuff school), prestige, Exo prestige + Exoforms,
familiars, professions, daily/weekly quests. Each section has a one-
paragraph overview always visible plus two collapsible blocks: a
**Details** block with numeric specifics and non-obvious rules, and a
**Default settings** block listing the Bloodcraft.cfg defaults the server
admin can override. Sourced from Bloodcraft v1.13.21 — README, source,
and ConfigService.

**Inline class context cards on action tabs.** No more swapping to Mod
Help while making picks:
- **Class tab** — Active Class card now shows your live class's
  archetype + tagline + weapon synergies + blood synergies + on-hit
  debuff. A "Compare all classes" collapsible inside Change Class shows
  all six classes side-by-side for picking.
- **Weapon Expertise tab** — new Class synergies card listing which
  weapon stats your CURRENT class amplifies (1.5× cap). Plus a
  collapsible reference for every weapon stat's baseline cap.
- **Blood Legacy tab** — mirror addition: which blood stats your class
  amplifies + full baseline-cap reference.
- **Prestige tab** — new "What each prestige tier gives you" card with
  three collapsibles: leveling-prestige per tier (XP slowed, expertise
  rate boosted, class spells unlocked), weapon/blood prestige per tier
  (rate −10%, cap +10%), and Exo-prestige math (form duration formula,
  shard rewards, .fam echoes cost scaling).

All four context cards carry a disclaimer that the defaults shown can be
server-overridden in Bloodcraft.cfg. The class data feeding them is a
single source of truth, so the Mod Help tab and the inline cards never
drift apart.

**Per-profession overlay toggles.** Settings → Display → Professions
tracked. Eight checkboxes (Enchanting / Alchemy / Harvesting /
Blacksmithing / Tailoring / Woodcutting / Mining / Fishing) hide
individual rows + bars on the Professions overlay live, without
rebuilding. Default-on preserves the v0.12.x render.

**Visual refresh — gold section markers + 14pt help text.**
- Every section heading across the panel now gets a thin 2-pixel warm-
  gold divider band immediately above it plus the heading text itself
  in gold + bold-italic at 16pt (was plain white-italic at 14pt). Makes
  long Mod Help / Quick Start scrolling actually navigable — section
  starts are unmistakable. Side benefit: data-display tabs (Familiars,
  Prestige, Levels) get cleaner section breaks too.
- Body fontSize bumped 12 → 14 in AddGuideSection + AddCollapsibleHelpDetail
  so the help-group tabs (Quick Start / Mod Help / Game Guide / Settings
  descriptions) are noticeably more readable.

## 0.12.1 — Bloodcraft handshake retry + Game Guide tab + bright interior presets

Three focused additions on top of the v0.12.0 color-theme work.

**Bloodcraft availability — handshake retry + live UI refresh.**
Pre-0.12.1, the Bloodcraft tab group could render as "(unavailable)"
on servers that DO run Bloodcraft, simply because the user opened the
panel faster than the Eclipse-protocol handshake could ACK. Two
compounding bugs: `SendRegistration` had a single-attempt gate (once
`Pending` flipped true, no further sends ever fired), and
`IsTabGroupAvailable` returned plain `UserRegistered` at construct time
and never refreshed. Fixed by retrying up to 3 times with 5-second
backoff (~15s total) before latching a new `RegistrationGaveUp` flag,
plus a `AvailabilityChanged` event that lets `MainPanel` refresh the
tab strip in place when the handshake ACKs late — no panel rebuild,
no scroll-position loss.

**New Game Guide tab** under Settings & Help, between Quick Start and
Settings. Surfaces V Rising resources (the game itself, not BCH) —
official Stunlock homepage, V Rising Fandom wiki, CaDrift guides, and
the official V Rising Discord. Each row has an "Open" button that
launches the URL in your default browser.

**Bright interior color preset row + readability fix.**
The interior background-color picker (added in 0.12.0) now offers TWO
preset rows: Dark variants (the original v0.12.0 palette) and Bright
variants (saturated twins, max channel ~0.40–0.65). Crimson Bright =
`#A30000` matches `Theme.Level1` exactly — the pre-0.12.0 framework
default red — so one click restores the old look. The section's help
paragraphs switched from muted grey to italic white so they read
cleanly on every preset, dark and bright.

## 0.12.0 — Split Toggle/Unbind buttons + two-zone panel color theme

First feedback-bundle release since the 0.11.2 hotfix. Two
user-requested quality-of-life improvements, both opt-in via the
existing Settings UI.

**Familiar Browser footer: Toggle / Unbind active split.** The single
"Unbind active" button on the overlay's footer is now two side-by-side
buttons. **Toggle** (left, `.fam t`) calls / dismisses the active
familiar without changing the binding — the use case that drove the
split: dominating an NPC auto-disables the familiar but doesn't auto-re-
enable on release (flying / teleporting do). **Unbind active** (right,
`.fam ub`) removes the binding so the familiar returns to your box;
it's NOT destructive — the box record is preserved, and you can re-bind
any time. Permanent box deletion is `.fam r N`, which is intentionally
NOT exposed on the overlay footer.

**Two-zone color theme.** Settings → Display has two color-preset
sections. The outer picker applies to every panel BCH builds (main +
all six overlays). The interior picker targets the scroll-view
wrappers + viewports inside the main panel + Familiar Browser — those
surfaces were bright red by framework default (`UIFactory.CreateScrollView`
painted the wrapper `Theme.Level1`). Default `#121212` masks the red
on construct so even users who never touch the picker get a near-black
look from session start. Per-panel transparency sliders are unchanged
— they continue to own each panel's alpha independently of either
color picker.

## 0.11.2 — CRITICAL: panel can no longer grow larger than the screen

Friend-test (severity: stuck-can't-play): a player resized the main panel
into a fullscreen-stretched state, then either toggled Auto-resize OR
clicked the border to resize manually. The panel grew larger than the
screen — covering their display in red — and because they had overlay-
lock active, they couldn't drag, resize, or close it. Save data
persisted the bad state. BepInEx logged a recurring
`'3399.5' cannot be greater than -3399.5` exception from
`EnsureValidPosition`'s `Math.Clamp`.

**Root cause:** `SetFullscreen(true)` sets stretched anchors, which
inverts the semantics of `sizeDelta` — assigning `sizeDelta.y = 700`
makes the panel 700 pixels TALLER than the canvas, not 700 pixels tall.
Both `AutoResizeIfEnabled` and `PanelDragger`'s resize-drag handler hit
this trap.

**Fix in five layers:**

- `PanelBase.EnsureValidSize` hard-caps every panel to canvas
  dimensions via `SetSizeWithCurrentAnchors`, which works for both
  centered and stretched anchors.
- `PanelBase.EnsureValidPosition` returns position = 0 (center) when
  the clamp bounds invert, instead of throwing `ArgumentException`.
- `SetDefaultSizeAndPosition` reordered to call `EnsureValidSize`
  before `EnsureValidPosition` so the position clamp always has
  valid bounds.
- `MainPanel.AutoResizeIfEnabled` early-returns when `_isFullscreen`.
- `MainPanel.SetFullscreen` force-pins the panel while fullscreen
  (`PanelDragger.Update` blocks drag/resize when pinned) and stops
  persisting fullscreen state to config — fullscreen is now actually
  transient as the original comment claimed.

**Stuck users on 0.11.0 / 0.11.1 self-heal on next launch** once
0.11.2 is installed — `EnsureValidSize` shrinks the restored panel
to fit and `EnsureValidPosition` no longer throws. No manual config
edit required.

## 0.11.1 — Shift overlay fixes + V-Blood row cleanup

Iterative friend-test fixes on top of 0.11.0 (which never made it past
local — both versions land on Thunderstore via this release).

- **Shift overlay now actually tracks the cooldown.** Three stacked bugs
  fixed: (1) service was reading `Core.LocalCharacter`, a stub never
  wired up — `Core.Initialize(world)` is commented out in
  `Patches/GameManagerPatch.cs:12`. The live character lives at
  `Plugin.LocalCharacter`; service updated. (2) Detection gated on a
  buffer field that's empty when Bloodcraft's `ReplaceAbilityOnSlotBuff`
  overlays a class spell on the shift slot — switched to "slot entity
  exists" + latch the actual prefab GUID from the first observed
  `CastGroup` event with `SlotIndex == 3`. (3) `_latchedCooldownEnd`
  got overwritten with 0 every poll between casts (the cooldown state
  lives on the cast-ability entity, which moves to whatever you cast
  most recently). Latch is now monotonic — only ever advances; missed
  reads leave it alone.
- **Shift overlay redesigned as a square button.** Friend-test ask:
  "look more like a button with a radial countdown, rather than a bar."
  80×80 outlined tile, radial dark-overlay sweep clockwise from
  12 o'clock, centered countdown text with a TMP glyph outline for
  contrast against both the bright ready-state tile and the dark
  radial sweep mid-cooldown. Tile brightens to cool-blue when ready,
  mutes to a darker tone while cooling down.
- **V-Blood overlay rows drop the `[box]` suffix.** Friend-test ask:
  "should only show name, shiny, attribute, and level — same as the
  normal familiar rows in box mode." Row format now exactly matches
  the BoxView per-familiar layout.
- **Lock overlays now covers the shift overlay too** (was already
  wired through `RespectsLockOverlays` — worth confirming).
- **Diagnostic line is opt-in.** New `ShiftSpellOverlayShowDiagnostics`
  config setting (default off) controls the small italic
  `pf / cg / si / end / srv` debug line under the SHIFT label. Flip
  on in `kdpen.BloodCraftHub.cfg` if you ever need to debug
  shift-state issues.

## 0.11.0 — Friend-test feedback bundle

Six items from the 0.10.x friend-testing round:

- **Primal V-Bloods now appear in the V-Bloods collection.** The scanner
  was stripping the `Primal ` prefix and looking up the full registry
  name, but Bloodcraft actually names primals as `Primal <FirstWord>`
  (e.g. `Primal Frostmaw` for *Frostmaw the Mountain Terror*). Fixed
  with a precomputed stem map in `VBloodRegistry`.
- **V-Bloods overlay rows redesigned** to one-row-per-captured-variant,
  formatted like the BoxView rows — shows level, prestige, shiny school,
  and box on every entry. Uncaptured V-Bloods stay in the main tab.
- **New "All Familiars" tab** in the main panel. Lists every familiar
  across every box (same data as the V-Blood scanner) with search
  filter, sort cycle (Box+# / A→Z / Level desc / Shinies first), and
  inline Bind + two-click Delete buttons per row.
- **Box-mutation forms use dropdowns** of existing boxes (Delete box,
  Rename box's current-name field, Move-familiar destination). New
  `BoxNameDropdownField` auto-syncs when boxes are added / renamed /
  deleted.
- **X-Large font scale** (1.5×) added to both UI and Overlay text-scale
  pickers in Display Settings. Plus a layout-height plumbing pass so
  rows don't clip at the new tier.
- **Eclipse-style shift-spell cooldown overlay.** Draggable widget with
  a "SHIFT" label, cooldown bar, and remaining-time countdown. Reads
  game state directly (`AbilityCooldownState` + `AbilityChargesState`)
  at 10 Hz. Visual readout only — clicking does nothing; press your
  bound Shift key to cast. Coexists with Eclipse.

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

## 0.10.13 — Italic-text readability, vertical-scale layout fix, overlay-toggle reformat

Dropped italic styling and bumped font size 11 → 13 on every body-
text and prose-hint label across the panel (italic glyphs were hard
to read at standard scale in V Rising's TMPro fallback font). The
muted-grey color still signals "secondary content"; italic was
redundant emphasis. Cascades through the `AddBodyText` helper, the
Kindred Admin intro paragraphs, tooltip footer, Chat Logging help,
and Size & Positioning help.

Vertical-scale layout fix: when dragging the main panel taller, all
extra space now goes to the tab content area instead of being
distributed among the bottom footers. Same fix applied to the
Familiar Browser overlay — the box-name row and Unbind footer no
longer absorb extra height; the scroll list gets it. Root cause was
the same Unity quirk as the 0.9.8 horizontal tab-strip fix.

Overlay-toggle footer reformatted as a labeled container with a bold
"Show overlays:" prefix, Auto-resize moved to its own row, toggle
labels shortened.

## 0.10.12 — Admin / Kindred / Logistics tab polish, more form replies wired to UI

Extended the card-wrap pattern to Bloodcraft Admin, Kindred Logistics,
Kindred Logistics Admin, KindredCommands Player, and (via the shared
`AddAdminWarningIntro` helper) all three Kindred Admin tabs (Players /
Server / World).

Wired more form-reply commands through the global "Last server
response" panel: `.fam sb` (smart-bind: single-match confirm,
multi-match clarification, no-match error), `.misc sct`, `.lvl log`,
`.quest log`, `.prof log`, `.misc silence`. All tagged
HasBchUIDisplay=true in the classifier so the Bloodcraft chat-logging
toggle can suppress the chat copy when the UI shows it.

## 0.10.11 — Search-result UI surface, missing-glyph cleanup, overlay compression, more tab polish

Added an in-panel "Last search result" card at the bottom of the
Familiars tab's More Actions section. On every `.fam s` reply it
shows the search query, match count, and one row per matching box
with a shiny indicator. Works regardless of Chat Logging toggle state.

Stripped every unreliable Unicode glyph from button labels and section
headings — V Rising's TMPro fallback only reliably renders
`← → ★ •`. The visual identity stays intact via card tints. V-Bloods
shiny column now uses `★` instead of `✦`.

Compressed the Familiar Browser overlay vertical space (~12 px more
list height). Fixed an `AddDivider` Vector4 padding-axis bug that
made the divider line render on top of the body text below it. More
tab polish (Boxes, Class, Unarmed Shift, Daily Quest cards) and a
default-padding bump in `AddCard` (6 → 10) for more left/right
breathing room across every previously-polished card.

## 0.10.10 — Scan opt-in by default, chat noise eliminated, overlay rebuilt, tabular V-Blood rows

V-Blood scan is now opt-in by default — open the V-Bloods tab and
click "Scan all" to start. New `Settings.AutoScanVBloodsOnTabOpen`
setting lets users opt back into auto-fire.

Scan chat noise eliminated. Two fixes: a force-suppress window for
silent-fired familiar-action commands (catches the "Box Selected"
line), and the `.fam boxes` / `.fam l` intercepts now classify as
BchAuto so their replies are filtered via `ShouldSuppressByCategory`.

V-Bloods tab now uses strict fixed-width columns (Type | Name | Lv |
Shiny | Box | Summon) with a column-header row. The shiny column
always renders with `—` placeholder when not shiny, so columns align
across rows. Header / Progress / Filter / Rows sections wrapped in
cards for proper padding.

Familiar Browser overlay rebuilt with a 5-row layout: toolbar
(`← → Reload Scan View Sort`), box name on its own line, mode/
filter status, scroll list, Unbind footer. New Scan button in the
overlay triggers the V-Blood scan without switching to the main tab.

## 0.10.9 — V-Bloods rebuilt (box-sweep, per-variant rows), cross-cutting visual polish

Replaced the 130-query `.fam s` V-Blood scan with a box-sweep
(`.fam boxes` → for each box, `.fam cb` + `.fam l`). Runs in
~30-60 seconds instead of ~4 minutes, and gives strictly more
information per reply (per-entry index, shiny flag, shiny color →
school, primal prefix). Scan restores your active box at the end.

`VBloodCaptureStatus` data model rewritten — now stores a
`List<VBloodInstance>` (one per actual capture) with exact box +
index. The V-Bloods tab renders one row per captured variant with a
color-coded variant tag, per-row Summon button targeting the exact
(box, index), and a `Settings.LockOverlays` precursor that refuses
Summon during scan.

New theme colors (CardBackground, MutedBody, AccentMono, DividerLine,
SystemTint* for the six progression systems) plus layout helpers
(AddCard, AddStatRow, AddDivider, AddBodyText, Mono). Card-wrapped
sections with system-tinted backgrounds applied across Levels,
Prestige, Familiars, Weapon Expertise, and Blood Legacy tabs.

## 0.10.8 — V-Blood scan/summon correctness, XP-overlay scale fixes, overlay edge padding, About cleanup

V-Bloods chip view no longer double-counts shiny as basic — a single
shiny-only capture now correctly shows only the S chip, not both B
and S. Search reply interpretation: boxes returned WITHOUT the
pink-star marker flip HasBasic; boxes WITH the marker flip HasShiny.

V-Bloods Summon flow finally works end-to-end. Two underlying bugs
fixed: added `FamiliarState.HasActive` (sourced from the raw Eclipse
protocol name field BEFORE the empty→"Familiar" mask), so the Summon
button stops sending unnecessary `.fam ub` when no familiar is bound;
and both summon paths now call `PlayerStateService.SetActiveBox`
before `.fam cb` so the `.fam l` reply gets keyed correctly.

XP overlay row heights now scale with the overlay text multiplier
so glyphs don't visually overlap the progress bar at Large scale.
Vanilla Admin row heights grow with wrapped descriptions. New
`Settings.OverlayEdgePadding` setting + UI control applies left/right
padding to every overlay's content. About tab reorganized into four
spaced regions (Header, Mods, About author, Project).

## 0.10.7 — V-Blood scanner fix, overlay polish, per-instance V-Blood view, bump-process hardening

Critical V-Blood scanner fix: pre-0.10.7 the scanner sent
`.fam s Alpha the White Wolf` without quotes, so VCF only consumed
"Alpha" and Bloodcraft echoed its `usage: .fam s [Name]` template
literally — repeating every 2s for the entire ~60-V-Blood scan.
Fixed by wrapping every `.fam s` argument in double quotes.

XP overlay polish: progress-bar-overlap fix (rows now stay ≥20 px
high so adjacent labels can't compress into the bar), MinHeight
computed from actual rendered content, weapon-stats sub-row filters
out the redundant preamble, optional XP counter row, configurable
progress bar height setting, optional prestige sub-line in bars.

V-Blood per-instance view (toggleable from the Chips view) — one
row per captured familiar with explicit Lv / Pr / Shiny school /
Primal / Box / Summon button. Vanilla Admin tab switched to a proper
2-column tabular layout. `bump-version.ps1` now auto-runs
`dotnet build` so the DLL never embeds the previous version.

## 0.10.0 – 0.10.6 — V-Blood collection tracker arc

0.10.0 introduced the V-Blood collection tracker (search-based scan,
chip view with B/S/P/Ps capture chips, smart Summon).
0.10.1 added re-scan-subtraction so `.fam r` deletions get accounted
for, plus alphabetical / level / location sort options.
0.10.2 added region-based sort (Farbane → Endgame), fast type-switch
refresh for overlay bonus stats, and per-overlay chat-noise
suppression.
0.10.3 fixed a critical NRE at plugin load caused by 0.10.0's
`ComponentType.ReadOnly` calls firing before IL2CPP's `TypeManager`
was built.
0.10.4 silenced scanner chat spam (130 silent searches in 4 minutes)
and tightened overlay row spacing.
0.10.5 added a V-Blood collection view to the Familiar Browser
overlay + suppressed the duplicate first-line of `.wep get` replies.
0.10.6 added the Chat Logging diagnostic section with three category
toggles (BCH-auto / Bloodcraft / Kindred) plus master Show All /
Hide All buttons, and rebuilt the Vanilla Admin tab with every
console command documented.

## 0.9.x — Accessibility, transparency, progress bars, friend-test polish

The 0.9.x line was a polish + accessibility pass:

- 0.9.0: text-scale options (Small / Standard / Large for both main
  panel and overlays), per-overlay transparency control,
  master-overlay suppress button, new Professions overlay.
- 0.9.1: accent-text legibility (wider black outline for colored
  labels on red backdrops) + opt-in chat suppression for familiar
  actions.
- 0.9.2: live-applying settings (text scale + transparency take
  effect without a panel rebuild), dedicated Settings tab,
  optional XP/legacy/expertise progress bars, BloodInfo display
  in the Blood Legacy tab.
- 0.9.3: bar visibility fixes (opaque bg + outline so the right edge
  shows on transparent overlays), Familiar/Professions bars,
  chat-suppression actually fires regardless of intercept state.
- 0.9.4: equipped-weapon expertise row on the XP overlay.
- 0.9.5: Eclipse-mod coexistence — when both BCH and Eclipse are
  installed, BCH yields the MAC-verified protocol entity to Eclipse
  so its overlay still populates.
- 0.9.6: XP overlay Blood Legacy row + per-row stat values; tab
  headers carry full current-state info.
- 0.9.7: title-bar maximize/restore button, Size & Positioning
  settings section with +/- nudge buttons per panel, About-tab
  version block, tab-strip width cap.
- 0.9.8: Discord DM link in the About tab, "Help" → "Settings and
  Help" rename, three Size & Positioning fixes including the
  horizontal-tab-strip force-expand fix.
- 0.9.9: XP overlay bonus-stats — weapon stats now display correctly,
  long lines wrap to multiple lines.

## 0.8.x — Generic capture, read-command replies in UI, friend-testing prep

- 0.8.0: Familiar Browser sizing controls + Lookups section + Vanilla
  Admin reference tab + Thunderstore listing links.
- 0.8.1: First public Thunderstore release. Familiar Browser glyph
  fix + deferred auto-pull (the overlay's auto-fetch had been
  silently no-op because MessageService wasn't bound yet during
  the original eager fetch).
- 0.8.2: Friend-testing hotfix — safety fixes for crashes, dropdown
  improvements, removed self-asserted admin gate (server enforces
  permissions anyway), docs refresh.
- 0.8.3: Read-command replies now land in the UI — generic-capture
  pipeline added so `.wep get` / `.bl l` / `.class l` etc. show
  their replies in the dedicated "Last server response" panel
  instead of only in chat. EXO prestige row added to the XP overlay.

## 0.1.0 – 0.7.0 — Initial feature build-out

- 0.1.0: First feature build — main panel, familiar list, box browser,
  basic tabs (Familiars / Boxes / Levels / Class), chat-command
  routing.
- 0.1.1 – 0.1.3: Bugfix sweeps after early in-game testing — safety
  guards, Boxes tab polish, regression hotfixes.
- 0.2.0 – 0.2.1: Shiny labels, safe auto-swap (two-click confirm
  before binding a different familiar), Daily Quest tab, admin-tab
  cleanup, real Move/Delete instead of confusing labels, server
  availability detection.
- 0.3.0 – 0.3.1: Class selection apply, in-UI Prestige info display
  with progress bar, post-submit form hook, About tab.
- 0.4.0 – 0.4.1: Weapon Expertise stat picker + Blood Legacy tab +
  Familiar Browser overlay.
- 0.5.0: Audit-gap fill for Bloodcraft chat commands, UX polish,
  per-row edit mode on box content.
- 0.6.0: Overlay sizing & persistence + .fam audit gaps (Battle
  Groups, smart-bind, shiny application).
- 0.7.0: Final Bloodcraft audit-gap pass + scrollbar click + About-
  tab links.

---

For the full per-version commit log, technical detail, and per-issue
friend-test context, see
https://github.com/KDavidP1987/BloodCraftHub/blob/main/CHANGELOG.md
