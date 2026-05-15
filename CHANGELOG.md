# Changelog

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
