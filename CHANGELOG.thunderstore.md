# Changelog

> **Full per-version history with technical detail lives at**
> https://github.com/KDavidP1987/BloodCraftHub/blob/main/CHANGELOG.md
>
> Thunderstore caps embedded CHANGELOG.md at 100,000 characters, so this
> bundled copy summarizes earlier versions and reproduces the most
> recent release in full.

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
