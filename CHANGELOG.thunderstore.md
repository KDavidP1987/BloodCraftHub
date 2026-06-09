# Changelog

> **Full per-version history with technical detail lives at**
> https://github.com/KDavidP1987/BloodCraftHub/blob/main/CHANGELOG.md
>
> Thunderstore caps embedded CHANGELOG.md at 100,000 characters, so this
> bundled copy summarizes earlier versions and reproduces the most
> recent release in full.

## 0.29.2 — Spawn overlay page selector + the real stuck-attack fix

- **Fixed the stuck auto-attack after closing the panel.** Diagnostics confirmed the closing click's
  button-release is eaten by the Close button, leaving the game's Primary-attack input latched. BCH now
  cancels a Primary attack that fires while the mouse button is physically up (the phantom) after a close;
  a real held attack passes through normally.
- **Spawn overlay: page selector instead of scroll.** Scrolling zoomed the game camera (V Rising captures
  the wheel), so the list uses Prev/Next pages again (the page-growth bug stays fixed).
- **Spawn overlay: fixed the red background** (it was the scroll view's inner background).

## 0.29.1 — Spawn overlay scroll fix + tester feedback

- **Fix:** the spawn overlay's object list now **scrolls** inside the panel (it was rendering rows in the
  wrong container, so the list overflowed the overlay and grew on each page/category change). Pager buttons
  replaced with a scrollbar.
- **Removed Durability / Respawn from the Object Catalog tab** (it's informational; those spawn settings
  live on the Object Spawning tab, which the catalog's Spawn buttons still read).
- **Stuck-attack-after-close:** added a Diagnostic-mode log (`[CloseAttackDiag]`) capturing the
  character's input/cast state for ~1.5s after each panel close, to pin down the remaining cause.

## 0.29.0 — Uriel spawn palette overlay, object search, and overlay/Shift fixes

- **New Spawn Objects overlay (Uriel).** A draggable palette of your unlocked objects — cycle a category
  with **← / →** and click **Spawn**, so you can build while walking around without the big main panel
  turning your camera aside. Header has Despawn / Rotate / Refresh + its own durability/respawn selector;
  remembers open/closed across logout. Toggle it from **Show spawn overlay** on the Object Spawning tab.
- **Text search (name or ID)** added to both the Object Spawning and Object Catalog tables (live filter,
  combines with the category chips).
- **Building hotkeys moved to the top** of the Object Spawning tab; **Despawn + Rotate** moved there too
  (from the Catalog tab) — they're spawning actions.
- **Fix:** *Abbreviate stat names* now shortens the "Stats: …" chosen-stat line in the Combined overlay,
  not just the bonus-values sub-row.
- **Fix:** stuck auto-attack after closing the main panel now clears on **every** close path (X button,
  toggle hotkey, launcher), not only when the cursor was over the panel.
- **Fix:** Shift overlay now also reads the cooldown from the slot's persistent ability group, so
  recast/charge abilities can start their timer after the cast window (plus new `ch`/`slotcd` diagnostics).

## 0.28.0 — Overlay Visibility options (timed hide, hide the buttons, cleaner chat)

New **Settings → Display → Overlay Visibility** section for the upper-right **OV** button / the
**Toggle all overlays** hotkey:
- **Hide mode — Toggle or Timed.** Timed hides everything for a countdown (10s / 30s / 1m / 2m / 5m /
  10m, default 25s) then auto-reappears — long enough for a timed video capture; Toggle (default) stays
  hidden until pressed again.
- **Hide BCH/OV buttons too** for a fully clean screen — safety-gated so it only applies when there's a
  way back (Timed hide on, or a *Toggle all overlays* hotkey bound). A relog always restores the
  launcher, so you can't end up stranded.
- **Keep game chat hidden while hidden** (default on) — fixes the native chat popping back up when the
  OV toggle hid the BCH chat.

## 0.27.1 — Uriel spawn durability + respawn options

- The Object Spawning / Object Catalog spawn-mode control now offers **Indestructible → Breakable →
  Smashable** durability plus a **Respawn** toggle (matches Uriel's new `.uriel spawn` lifecycle flags).

## 0.27.0 — Uriel admin tools fully built out

Every Uriel admin command now has a button (was read-only before); destructive actions use a two-click
confirm and the server still enforces admin status.
- **Admin: Sharing** — player target field (autocomplete): per-player share list + Unshare player, plus
  a Danger-zone Unshare ALL.
- **Admin: Objects** — Block/Unblock (prefab autocomplete), per-player Grant/Revoke + Grant-ALL
  (all/destructible/indestructible), View unlocks, Purge plot / Force-purge plot, and Arm→Confirm
  force-despawn. Fixed the Boss map button (now `bossmap list`).
- **Stairs** — admin Stair purge (5m) + Refresh (diag).

## 0.26.6 — Uriel object lists paginated + breakable spawns; stronger close-attack fix

- Object Spawning + Object Catalog lists are now **paginated** (50/page, ← Prev / Next →) so big
  collections and the ~2,000-item catalog are fully browsable.
- New **Spawn mode** toggle — spawn objects as Invulnerable (default) or Breakable.
- Stronger fix for the "character keeps attacking after closing the panel" bug: the attack is now
  cancelled for a brief window after the panel closes regardless of button state (the 0.26.5 fix
  missed quick clicks that released the button first).

## 0.26.5 — Fix: character kept attacking after closing the panel

- Closing the main panel by clicking its X no longer leaves your character stuck repeating a primary
  attack. Suppression now keeps releasing the attack until the mouse button is released after the panel
  closes, so the closing click can't carry into the world. Ends the instant you let go (1s safety cap).

## 0.26.4 — Uriel Object Catalog: own tab + columns

- The full spawnable-object catalog moved to its own **Object Catalog** tab (with Load / Re-scan +
  category filter), so the Object Spawning tab stays focused on your unlocked items.
- Both lists are now a proper table — **Name · Category · ID · action** — with aligned column headers
  instead of one cramped line. Owned objects show **Spawn**, un-owned show **locked**.
- New **Despawn nearest** button on the Object Catalog tab (one-click `.uriel despawn`, separate from
  the Remove build-hotkey).

## 0.26.3 — Uriel spawn-placement fix

- Spawning from the Object Spawning palette now places the object at your location
  (`.uriel spawn <guid> 0 here`) so it lands in your plot; nudge it into place with the Move
  build-hotkey. Pairs with Uriel's server-side fix that makes the unlocked list + full catalog populate.

## 0.26.2 — Uriel Object Spawning UI fixes

- Fix: long status / diagnostic messages (Object Spawning progress + scan notes, Storage nearby list,
  Uriel Settings readout) now wrap and grow instead of being clipped to one line.
- Fix: the Object Spawning category filter chips now wrap onto multiple rows instead of overflowing.

## 0.26.1 — Uriel build-mode hotkeys + object-spawn diagnostics

- **Building hotkeys** — bind keys to move / rotate / remove the nearest spawned Uriel object, behind a
  **temporary Build Mode** toggle that's always OFF at login (turn on while building, off when done).
- **Object-spawn diagnostics** — the Object Spawning tab now explains an empty unlocked list / catalog
  ("awaiting reply", per-page loading, or a "no reply / disabled" note after a timeout) instead of
  doing nothing silently.
- Fix: `.uriel despawn`/`move`/`rotate` relays no longer append a `nearest` token.

## 0.26.0 — Uriel integration (storage sharing, prisons, stairs, object spawning)

New client UI for the sibling server-side mod **Uriel, Lord of Hosts**. The whole **URIEL** tab
group only appears when the server runs Uriel (handshake-gated, same as Beelzebub).

- **Storage Sharing** — quick-share the nearest chest (take/give/give+take) or build a custom share
  (permission + withdrawal limits + cost, with item-id lookup); pay-chest, unshare, my-shares.
- **Nearby Public Storage** — BCH detects Uriel-shared containers/cells around you client-side (no
  server query) and lists them in the Storage tab + a new draggable overlay (name · kind · distance).
- **Object Spawning** — spawn your unlocked prefabs, collection progress, category filter, per-player
  unlock notifications, and an optional full-catalog browse cached per Uriel version.
- **Prisons & Stairs** — share cells + Take-prisoner relay; live stair restyling (six styles) +
  remove/next/show.
- **Quick Start + Help** guides, a Uriel **Settings** tab, admin tabs (Sharing/Objects/Config), a
  **Connection → Re-detect Uriel** card, and an inline "not detected" diagnostic with Re-check /
  Force-enable.
- New config: `UrielAvailability`, `UrielDiagnostics`, `ShowUrielSharedOverlay`,
  `UrielSharedOverlayTransparency`.

## 0.25.0 — The typing keyboard lock, done the way the game does it

- Game keybinds can no longer fire while you type into BCH fields or use the main panel. Menus
  (B/M/K/J/P, wheels), abilities, action-bar hotkeys, and admin keybinds triggering mid-typing in the
  chat window or main-panel forms is fixed at the source: while a BCH text field has focus (or the panel
  is open with `SuppressGameInputWhileUIOpen`), BCH places the game's OWN native-chat typing-lock
  context into V Rising's internal input-context stack — the exact object the native chat uses to lock
  the keyboard — so every game action is consumed before any game system sees it, and the blocked
  key-set is identical to native chat typing by construction.
- ADMINS: console keybindings (hotkeys assigned with the console's `keybinding create` command) no
  longer fire while you type in a BCH field or have the panel open with `SuppressGameInputWhileUIOpen`.
  These binds are read outside the game's input pipeline (even the native chat can't block them); BCH
  now disables them the same way the game's own UI text fields do, and the game re-enables them the
  instant you stop typing. New General setting `SuppressConsoleKeybindsWhileTyping` (default ON).
- Beelzebub ability keybinds now also pause while the main panel is open with
  `SuppressGameInputWhileUIOpen` (previously they only paused while typing).
- No new crash surface: zero new Harmony detours and no injected callbacks — the context object is the
  game's own (native code end-to-end), added/removed through the game's public input API exactly the
  way the native chat gates it. The older protections stay as belt-and-suspenders.
- New Compatibility kill-switch `EnableNativeTypingLock` (default ON) for diagnosis.

## 0.24.8 — Loadout first + large-text button fix

- Loadout is now the first tab in the BEELZEBUB group (above Bestiary) — it's the tab players actually
  live in. Opening the panel to the Beelzebub mod now lands on Loadout too.
- Fixed slot-bind buttons word-wrapping vertically (one letter per line) at Large+ text size: compact
  Beelzebub button widths/heights now scale with the font multiplier, the table headers scale in lockstep
  so columns stay aligned, and button captions can never word-wrap. Small/Standard layouts are unchanged.
  Reopen the panel after changing the text-size setting for it to apply.

## 0.24.7 — Fix: Beelzebub tab group could stay "unavailable" while connected

- Fixed the BEELZEBUB tab group showing greyed/"unavailable" even though the Connection tab reported
  Beelzebub Connected. The group availability only re-evaluated on the one-shot presence-resolved event; if
  that refresh missed the live panel during the post-login handshake-settle window, the group stayed greyed
  all session while the Connection readout (which watches a broader set of presence updates) showed Connected.
  The group now reconciles on those same updates — including the subscribe ACK right after detection — so it
  lights up reliably without a manual Re-detect.

## 0.24.6 — Fix: non-admins no longer see "[denied] broadcast-msgs" on login

- Fixed admin-command denial spam in chat for non-admin players. The Beelzebub announcements editor (Admin:
  Config) auto-read the broadcast message pool with the admin-only `api broadcast-msgs` while building its
  rows; since the panel reopens to your last-active mod tab on login, a non-admin who'd left off on that tab
  got a red `[vcf] [denied] broadcast-msgs` reply every login. BCH now gates that automatic read behind the
  local player's admin state. No change for admins; the broadcast feature itself was never affected.

## 0.24.5 — Admin "Cleanse stuck buffs" (Beelzebub v0.131)

- **NEW "Cleanse stuck buffs"** in Admin: Players → Recovery wires Beelzebub v0.131's `.beelz admin cleanse
  <player>` — strips stuck STATE buffs (invisible / phased / immaterial) that survive respawn + relog. Fixes a
  stuck/invisible PLAYER (distinct from a stuck action bar); non-destructive, no confirm.
- Beelzebub v0.127–v0.130 catalog data changes need nothing from BCH (version-keyed cache re-scans the new
  state).

## 0.24.4 — Admin ability config: changes now apply + show up

- Fixed the Admin: Abilities editor where a saved field change didn't seem to take effect or update in the
  table. Saves (and Reset to defaults) now send `.beelz admin reload` to apply the change live, then re-scan
  that ability **after a short delay** — the old confirm re-scan fired the same frame as the save and raced
  the write, reading back the old value. The "now:" column updates once the re-scan completes. (If a value
  still won't change, the server's `Abilities_ApplyConfig` master switch may be off.)

## 0.24.3 — Beelzebub v0.125 catch-up (leap-height tuning)

- **New `Leap height` ability-config field** (Beelzebub v0.125) in Admin: Abilities — tames a boss leap that
  flings the caster sky-high (try ~20-40 vs vanilla ~250). Write-only, applies on Save via
  `.beelz admin ability <id> leapheight <v>`; global prefab edit.
- Beelzebub v0.124's "Arctic Leap" name fix (52 abilities) shows up automatically — BCH's version-keyed
  catalog cache invalidates and re-scans the corrected names. (v0.123/v0.125 server-side changes need nothing
  from BCH.)

## 0.24.2 — Admin action-bar recovery: Purge + Rebuild bar

- **NEW "Purge bar" (Beelzebub v0.121)** in Admin: Players → Recovery — the last-resort fix for a creature
  kit jammed on a player's bar that survives relog/Respawn/Rebuild slots (an engine modification leak). Wipes
  all Beelzebub bar integration + removes the leaked engine slot mods, keeping captures + unlocks. Sends
  `.beelz admin purge <player> CONFIRM`; two-click confirmed.
- **NEW "Rebuild bar"** wires the missing `.beelz admin rebuildbar` recovery command (two-click confirmed).
- Admin help now shows the full stuck-bar escalation ladder (rebuildslots → rebuildbar → clearslotmods →
  respawn → purge).

## 0.24.1 — Stuck-bar recovery uses Beelzebub v0.120's authoritative reset

- **"Unstick bar"** now uses `.beelz resetbar` on Beelzebub v0.120+ (clears the engine's deeply-cached slot
  values and re-applies your saved loadout — which `.beelz refresh` alone couldn't), reverting any active
  transform first and falling back to `refresh` on older servers. Admin **Rebuild slots** / **Respawn**
  recovery tooltips updated to match (Respawn = the guaranteed cure for a bar that survives a relog).

## 0.24.0 — Secondary chat window, loadout fixes, bar recovery

- **NEW view-only secondary chat window** — a second draggable overlay (no input) mirroring only the
  channels you pick, so you can watch two streams at once. Enable + choose channels in **Game UI →
  Secondary chat window (view-only)**.
- **Beelzebub Loadout:** slot bind buttons now update instantly when you switch sets (Mounted's 3/6/7
  restriction no longer needs a manual Refresh); slot buttons **default to key labels** (LM/Q/Sp/Sh/E/R/C/T,
  numbers are the alternative); and a new **Unstick bar** button reverts + refreshes to recover a bar stuck
  on a transformation's abilities.
- **Dual-mod tip:** the Admin: Config coexistence note now names the contested slots (3 = Shift, 1+4 =
  Unarmed) and both fixes (`Interop_SlotInjectionPriority=1` or Bloodcraft's `ShiftSlot`/`UnarmedSlots`
  off). Verified BCH itself introduces no Bloodcraft/Beelzebub slot conflict.

## 0.23.0 — Beelzebub v0.120 / ApiVersion 28 catch-up

- **`Mounted` loadout form** added to the per-form picker + quick-scan (slots 3/6/7 only — the horse owns
  the rest). Gated by the server's `Forms_CustomAbilities_Enabled`.
- **`!`-blacklist form/weapon restrictions** (Beelz v0.101+) are now understood: a "usable everywhere
  except X" ability is no longer mis-filtered as weapon/form-bound, and `forms`/`weapons` are editable in
  the Admin: Abilities editor (comma-separated; `!` to block).
- **New `Power window (s)` ability-config field** (`powerwindow`, Beelz v0.120).
- **Transform-switch safety** matched to Beelz v0.120 (which now refuses a direct transform→transform):
  BCH auto-reverts first; tooltips explain it; admin Force notes you may need Clear first.
- **Dual-mod tip:** the Admin: Config tab points to `Interop_SlotInjectionPriority` for contested slots.
- **Foreign-language names/blocked basics** from Beelz v0.120 are handled automatically — the version-keyed
  catalog cache invalidates and re-scans fresh.

## 0.22.0 — Onboarding, UI tidy-up, and a transform safety guard

- **First-run welcome:** the panel's first-ever open routes you to the **Quick Start** tab (one-time),
  and the left rail now opens to the **active** mod — a non-Bloodcraft server no longer force-opens the
  Bloodcraft "handshake failed" panel (Bloodcraft still wins when both mods are present).
- **Tidier forms:** dropdowns and numeric inputs across the whole panel no longer stretch — only
  free-form / multi-line text fields stay wide.
- **Beelzebub Bestiary:** stats + scan status moved to their own lines so they no longer overlap the
  buttons before a scan.
- **Beelzebub Loadout:** *Assign* controls split into a search line, a **Group:** row and a **Filter:**
  row; the abilities table is taller.
- **Beelzebub quick-scan:** the picker now covers every ability category plus per-form and per-weapon
  slices (not just Summons / Spells / V-Bloods).
- **Beelzebub Admin: Abilities:** the expanded editor shows each field's **current value**, and **Save**
  re-scans to confirm the change applied.
- **Transform safety guard:** switching transform→transform could corrupt your action bar; BCH now
  auto-reverts to vampire form first (client-side mitigation).

## 0.20.0 — Beelzebub v0.100 catch-up (ApiVersion 22)

Brings the Beelzebub integration up to date with Beelzebub v0.100.0 (ApiVersion 22).

- **Admin: Abilities table lists every ability** (enabled + disabled), via Beelzebub's new admin catalog
  scope, so you can configure non-collectible abilities too. Uncaptured abilities now show their unit +
  numeric ID (Beelzebub now streams those on the scan).
- **Custom transform loadouts** — a new Transforms-tab editor: pick a form + phase to see **each slot's
  current bind** (with per-slot Clear), bind any ability from that form's kit, or reset to defaults. (Falls
  back to build-and-apply on a Beelzebub too old to report current binds.)
- **New Transforms overlay** — a draggable on-screen list of your forms; **double-click to transform**, with
  **Phase 1 / Phase 2 / Revert** buttons. Toggle from the Transforms tab or the "Beelz transforms" footer.
- **More transforms** — Werewolf, Golem, Gargoyle (+ a basic werewolf) join Dracula & Morgana, each
  multi-phase; the UI no longer assumes only two forms.
- **Per-transformation duration / cooldown** in the admin transform-tuning editor (with `inherit`), plus
  the new **Transform_Enabled** master switch and **Transform_CooldownScope** in the config tab.
- **Rebuilt announcement editor** — manage the 100%-collection / leaderboard message pools one message at
  a time (add / edit / remove) via Beelzebub's `broadcast-msg`, reading the live list back from the server
  (fixes the garbled text the old editor showed).
- New **Reset loadouts** admin button (clears a player's binds/loadouts/active transform, keeps their
  collection); clearing a single primary/ultimate bind now removes just that slot.
- **"Show overlays" footer reflowed** — the label is on its own line and the 10+ overlay toggles wrap onto
  as many rows as the window width allows (they used to squish / overlap the border when narrow).

## 0.19.1 — Testing & polish (player-feedback rounds)

A polish pass over 0.19.0 from in-game testing. No protocol change; still current with Beelzebub
v0.94.0 (ApiVersion 20).

- **Summons** — management moved to the Hotkeys tab (Stash / Restore / Recall / Clear), plus a new
  draggable **Summons overlay** with a one-click Stash ⟷ Restore toggle. Show/hide it from the Hotkeys
  tab or the "Beelz summons" footer toggle; it auto-hides when Beelzebub isn't detected.
- **Admin tabs gate to admins** — non-admins see the controls grayed-out (not hidden) with a **Re-check
  admin** button (also re-checks on page switch). Applies to the Beelzebub, Bloodcraft, and Kindred
  admin tabs.
- **Admin: Abilities table** — slimmer rows with aligned **Ability / ID / Unit / Enabled** columns;
  Enabled applies immediately; expanded editor gained **Cancel/revert** + **per-field tooltips**; new
  **Export config → clipboard**; Unit/ID load on a single Scan-all, and captured abilities show their
  numeric ID. Broadcast message pools are now a clean **multi-line editor**.
- **Input fields** — the text caret is visible in every field (including pre-filled ones), and selecting
  text no longer spams errors.
- **Overlays** — transparency is a live slider (0–100%) reaching fully-invisible (handle no longer
  vanishes); overlays stay properly **behind game menus** (no flicker/click-steal); the Shift-spell
  overlay works on any server.
- **Smaller fixes** — form fields no longer cramped against labels; longer chat history; "command not
  found" handshake noise filtered; vampiric-red title.

## 0.19.0 — Beelzebub catch-up (current with Beelzebub v0.94.0 / ApiVersion 20)

Brings the Beelzebub integration fully up to date with the latest Beelzebub.

- **Fixed:** ability tooltips & the Bestiary catalog were truncated against current servers — Beelzebub
  now splits long replies into parts, and BCH reassembles them. Complete tooltips/catalog again.
- **Fixed:** the "Clear bar" button (the old `.beelz resetbar` became a no-op; now uses `.beelz clearbar`).
- **Per-form ability sets** — a distinct loadout per shapeshift form (Wolf/Bear/Rat/Spider/Toad/Werewolf/
  Gargoyle), alongside the universal + per-weapon sets. Pick the form in the Loadout "Editing" dropdown.
- **Primary + ultimate slots** — bind to the left-click (primary) and T-key (ultimate) slots too; the
  Loadout now shows all eight slots for every set.
- **Server loadout presets** (save/load/list/delete); captured-ability lists use Beelzebub's friendly names.
- **Transforms tab** shows each category's mode / duration / live cooldown; added Recall + Clear-summons.
- Active-bar abilities resolve full tooltips by GUID (no more "No name"); **Leaderboard / My odds**
  buttons, a **mute repeat-devour** toggle, and a 100%-collection hook.
- **Admin:** a **per-ability shaping editor** (cooldown, range, charges, AoE, projectile speed, duration,
  healing, summon caps, cast modifiers, …) with reset-to-defaults, **per-unit transform tuning**,
  **capture-filter management** (deny/allow/GUID lists + reload), and **server-announcement** controls.
- **Test-notes fixes & UX:** Loadout shows all 8 slots; **Bestiary** now has tabular columns, group-by and
  pagination; clearer input fields; a new always-reachable **Connection** tab (re-detect both mods); a chat
  **Copy** button; Admin Config grouped into sections with enum **dropdowns** + hover descriptions;
  **auto-refresh the bar on grant** (default on); optional key labels (LM/Q/Sp/Sh/E/R/C/T) for slot buttons.
- **Round 2:** readable input fields; action-bar tooltips with descriptions; Bestiary collapsible groups +
  Captured/Missing axis + diagnostics IDs; copy-ability-ID buttons; tab groups auto-expand by what's
  detected; Quick Start split (general + Bloodcraft); **Admin: Players** searchable player/unit/ability
  fields + current-target banner + per-ability shaping "Load current settings"; a new **Admin: Abilities**
  mass-config table (editable Enabled/Cooldown/Range/Dmg×/CD× per row, per-row Save, search/filter/paging);
  Clear-set now clears primary/ultimate; no more Bestiary↔Loadout tab-switch lag.

## 0.18.4 — Server-switch stability, Shift-overlay redesign, customization

- **Fixed (crash):** opening BloodCraftHub shortly after switching servers (without fully quitting) could
  crash the client to desktop. BCH cached internal game-world handles that went stale when the world is
  rebuilt on a switch; they're now rebuilt for the new world on every switch.
- **Fixed (crash):** renaming a chest / storage box with the BCH chat window enabled could crash the
  client to desktop. The Enter key that confirms a rename was stolen by the chat window, arming the
  "block menu keys while typing" logic, which then destroyed a networked game entity. BCH now leaves
  Enter alone while a game text field is focused, and never destroys a networked entity.
- **Fixed:** Bloodcraft now re-detects when you switch servers. Going from a Bloodcraft server to one
  without it used to leave the Bloodcraft tab enabled and its overlays showing the old server's stats —
  a stuck handshake meant BCH stopped retrying and never realized the new server had no Bloodcraft. The
  handshake now retries and resolves correctly on every server.
- **Fixed:** the chat window resets on a server switch (no more carrying the previous server's scrollback
  or a half-typed message into the next one).
- **Fixed (freeze):** on a Beelzebub server with a large captured collection, opening BCH fetched details
  for *every* ability and froze the UI. It now only fetches the rows on screen; the rest load on demand.
- **Behavior change — "hidden until confirmed":** the **Bloodcraft** and **Beelzebub** tabs/overlays now
  stay greyed/hidden until *this* server confirms the mod, instead of showing during the detection window.
  Beelzebub no longer sits enabled through several probes and then suddenly greys out; it starts hidden and
  lights up the moment the server answers (restored if a later probe answers). On a real Bloodcraft/
  Beelzebub server the tab + overlays now appear a second or two after load-in rather than instantly.
- **Shift-spell overlay redesign:** now a compact, ability-button-style tile (dark beveled frame +
  spell icon + radial cooldown + "Shift" label + charges badge) instead of the old big panel. Much
  smaller and more thematic, and **resizing it now resizes the button itself** (it scales). The spell
  icon reads clearly on a dark slot — blue shows only before the icon is identified (on load-in), and
  the cooldown sweep is hidden until a cooldown is actually running. If you'd resized it before, drag it
  smaller or reset it in Settings → Size & Positioning.
- **The Beelzebub action-bar (hotkey) overlay shows each ability's icon** on its buttons (resolved from
  the cast ability), so it reads like the Shift overlay instead of a text label. On by default.
- **Testers:** Beelzebub → Loadout gets a **"Copy"** button per ability row (only when Beelzebub
  diagnostic details are on) that copies the ability's full details to the clipboard for the Discord docs.
  Also: Beelz hotkey names must be a single word (no spaces/symbols — they're sent as a chat command).
- **New: button color** (Settings → Display) — recolor BCH's buttons (BCH/OV launcher, Stash All,
  Familiar Browser, etc.) to any preset/hex. Deliberate-colored buttons (Danger/WIPE red) keep theirs.
- **New: launcher button size** (Settings → Display, 60%–120%) — shrink/grow the top-right BCH/OV
  buttons for displays where they render large.
- Renamed footer toggle **"Hide chat too" → "Hide chat with OV"**.

## 0.18.3 — Mod-detection on server-switch, chat-suppression fix, overlay tidy-ups

- **Fixed:** other mods' / system chat messages no longer disappear on load-in. BCH's login auto-queries
  (prestige / familiar boxes) could keep "listening" and eat unrelated colored system lines until you ran
  a BCH command; that listener is now hard-bounded to the brief reply burst.
- **Fixed:** Beelzebub re-detects reliably when switching servers without fully restarting the game (the
  detection handshake now waits for the heavy relog to settle and probes longer).
- **Overlays:** Bloodcraft overlays auto-hide on non-Bloodcraft servers, and the Beelz action-bar (hotkey)
  overlay auto-hides on non-Beelzebub servers — returning automatically when you're back on a server that
  runs them. New footer **"Beelz hotkeys"** quick-toggle. New **"Hide chat too"** option (default OFF) so
  the upper-right "hide all overlays" button can optionally include the chat window.
- Testers: the two fixes log verbose `[Beelz][diag]` / `[Intercept]` detail when **Diagnostic mode** is on.

## 0.18.2 — Typing in BCH forms no longer moves/casts (+ a known input gap)

- **Typing in a BCH form field (search/name/admin boxes) now stops your character moving and casting**,
  like the chat window already did. **Escape** frees the keyboard; new setting **"Lock keyboard in form
  fields"** (General, default ON). Safe — does NOT bring back the input patches behind the 0.16 crash.
- **Known limitation:** some menu/hotkey keys (map, build, weapon-swap number keys) can still register
  while you type in a *form*. The chat window blocks them fully (it uses V Rising's native chat-open
  input gate); matching that for forms needs deeper game-input work and is planned for a later update.
  The UI is mostly mouse-driven, and Escape always frees the keyboard.

## 0.18.1 — Logout crash fix + chat-noise fix & cleanup

- **Fixed:** logging out exited the game **to desktop** instead of returning to the main menu (BCH-only —
  a per-frame patch could throw while the world tore down on logout and crash the client). It now bails
  cleanly during disconnect, so logout returns you to the menu.
- **Fixed:** BCH's overlays/panel lingered over the **main menu** after logout — the UI now tears down when
  you leave the game and rebuilds cleanly on your next login (including reconnecting to a different server).
- **Fixed:** the **Beelzebub tab group** could stay "Unavailable" after switching servers without restarting
  the game (the detection result from a non-Beelzebub server stuck). BCH now resets Beelzebub + Bloodcraft
  detection when you leave a game, so each server is re-detected fresh.
- **Fixed:** other mods' / system messages could intermittently vanish from chat (until you ran a BCH
  command). BCH's reply capture could latch onto unrelated colored system messages and hide them; it's now
  strictly bounded to its own command's reply burst — **BCH never hides other mods' or players' chat.**
- **Consolidated** all chat-suppression into one **"Chat noise"** Settings section — four clear "Hide …"
  switches (background-query replies *on*; your BCH command replies *off*; familiar action confirmations
  *off*; command-framework/VCF errors *on*). Existing preferences carry over.

## 0.18.0 — Beelzebub: a full client UI for the ability-capture / transform mod

A complete client UI for **Beelzebub** ("Lord of Gluttony"), the sibling **server-side** mod —
collect defeated units' abilities, swap them onto your spell bar (per-weapon sets), cast extras
beyond the 6 slots, and transform into Dracula or Morgana. Chat-driven like BCH↔Bloodcraft; the
**BEELZEBUB** tab group only appears when the server runs Beelzebub.

- **Bestiary** — collector checklist with % toward 100%, Captured/Missing + category + kind
  filters, rich search; one-time **Scan all** loads the full collectible list. Captures outside
  the server's curated catalog are tagged *(off-catalog)*.
- **Loadout** — a separate ability set per weapon (+ Universal fallback) on the 6 slots; the
  server auto-switches sets on weapon swap. Per-slot/whole-set clear, copy-from-set, reset/fix bar.
- **Hotkeys + Action Bar overlay** — bind abilities beyond the 6 slots; each becomes a draggable
  on-screen tile with a cooldown ring + optional keyboard shortcut.
- **Transforms** — Dracula / Morgana: activate / revert, phase switch, signature summon /
  detonation, summon stashing.
- **Admin: Config & Players** — wrap Beelzebub's admin commands (server enforces permission).
- **Settings** — diagnostics toggle for testers (shows ability IDs + writes a copy-paste log
  trace), connection readout, re-detect, overlay toggle; plus an Auto/On/Off control for the tab
  group on the main Settings page.
- **Help** — full in-app guide + command reference, plus a Quick Start.
- Performance-tuned for fully-collected servers (visible-tab-gated rebuilds, opt-in catalog scan).

Requires the Beelzebub server mod for the tabs to appear; the rest of BloodCraftHub is unchanged.

## 0.17.3 — Chat overhaul: whisper anyone, cleaner columns, new-player hints

A big quality-of-life pass on the tabbed chat window, plus new-player guidance.

**Whispering**
- **Right-click → Whisper on the social page (P key) now opens in BCH's chat** instead of
  the hidden native chat (where it did nothing) — composed to that player, input focused.
  Only when BCH's chat window is on; otherwise the vanilla whisper is untouched.
- **Double-click a name in chat to whisper them.** Clickable names show underlined +
  link-blue; toggle off if you click names by accident.
- **Whisper anyone online** — the "+ Whisper…" picker and `\whisper <name>` now read the
  full connected-player roster (the same list the social page shows), not just people
  you've seen talk.
- **Backslash/slash chat commands:** `\g` `\local` `\clan`, `\whisper <name>`, etc. switch
  the send target inline as you type.

**Readability**
- **Tabular layout:** optional **separate channel + name columns**, and **auto-fit
  columns** so the **message column grows first** when you widen the window (instead of
  every column stretching). Both toggleable.
- **All-tab channel filter** (pick which channels show in All), **tab-switch hotkeys**
  (`Alt + 1–6` by default), long-name / unread-badge wrap fixes, and an **Eclipse-blue**
  background preset.

**New-player guidance**
- **"Missing element — free power" hints** on the Class / Weapon Expertise / Blood Legacy
  pages + overlays when you haven't picked a class or chosen expertise/legacy stats. Only
  for systems your server has enabled; fully toggleable (default on).

**Fixes**
- "Putrid Rat" V-Blood ("Nibbles the Putrid Rat") capture fixed; stuck basic-attack when
  clicking chat tabs/input fixed; Eclipse command-console mode no longer spams `.wep get`
  / `.bl get` replies; native-chat input-trap guard hardened.

**Known issue:** **Stash All** doesn't work while a game menu is open — with "Overlays
behind game menus" on (default), BCH overlays sit behind the menu so the button can't be
clicked. Close the menu first (or turn that setting off). A proper fix is planned.

## 0.17.2 — Fixes the load / V-Blood-tracking / waypoint-teleport crash

Fixes the 0.16.x crash that hit some players a few seconds after loading in, the
moment they started **tracking a V-Blood / boss**, or on a **waypoint teleport**
(and which then made the client crash on every load afterward).

- **Root cause:** BCH's "don't open menus while typing" feature patched three of the
  game's menu-input systems; detouring those during the HUD rebuild on login /
  tracking / teleport tipped a bug in the BepInEx IL2CPP interop layer (a
  garbage-collector finalizer fault) — a native crash with no error in BCH's log that
  also corrupted the interop cache.
- **Fix:** removed those three patches. Menu-open suppression while typing is now done
  safely (clearing the queued menu request) without hooking those systems. The "don't
  move / cast while typing" suppression is kept.
- **Known minor gap:** typing into a main-panel FORM field can still let a menu hotkey
  (M / B / I) open a menu — press Escape. (It works fine in the tabbed chat.) A safe
  fix is planned for later.
- **Also:** overlays now rebuild a few seconds after login on a quiet frame
  (`UiBuildDelaySeconds`, default 3); new `[Compatibility]` config switches let you
  disable individual BCH hooks to isolate a conflict; the layering update is throttled.
- **Reverted for stability:** an attempt to also suppress menus while typing in form
  fields (via Unity EventSystem focus) was backed out — it could leave your character
  looping an action when leaving chat.

**Crashing on every load from an earlier version?** Close the game, delete
`BepInEx/interop` + `BepInEx/cache` in your profile (they rebuild), and update your
BepInEx (V Rising) pack — that clears the corrupted interop cache.

## 0.17.1 — Run alongside Eclipse (command-console mode)

BloodCraftHub + [Eclipse](https://thunderstore.io/c/v-rising/p/zfolmt/Eclipse/)
used to crash the client on load together. The fault is in Eclipse's own HUD code
(a `BufferLookup` read from a background coroutine; BCH isn't in the crash stack
and can't fix it directly), so BCH now **auto-detects Eclipse and switches to
"command-console mode"** — they load together cleanly.

- With Eclipse present, BCH **stands down from its own live Bloodcraft readouts**
  (no data-stream registration) and **auto-hides its stat overlays** (XP / Familiar
  / Daily Quest / Professions / Shift / Combined). **Eclipse provides the live HUD.**
  Your overlay on/off prefs are preserved and come back automatically when you play
  without Eclipse.
- Everything non-overlapping keeps working: all **command buttons** (Bloodcraft +
  Kindred + KindredLogistics), the **chat window**, the **Familiar Browser** + **Quick
  Actions** overlays, full **familiar management** (click a familiar to switch;
  "Unbind active" to release), and **V-Bloods** (fills in passively as you browse
  boxes; **Scan all** still walks every box). Affected tabs show an in-app note.
- Prefer BCH's own overlays? Disable Eclipse — BCH covers the same readouts.
- Minor: the familiar-system probe defers off the busy login window. On any
  load crash, regenerating `BepInEx/interop` + `cache` (delete; they rebuild)
  usually clears it.

## 0.17.0 — Standalone "Game UI" group + tabbed chat window

A large client-side release — all of it works on **any** server (no Bloodcraft /
Kindred required).

- **New "Game UI" tab group** for client-side interface enhancements.
- **Tabbed chat window** — movable / resizable / persistent, with per-channel
  tabs (All / Global / Local / Clan / System / Whispers), every player's resolved
  name, and unread badges.
  - Send on the active channel; on the All tab a compact "send to" dropdown +
    **Tab to cycle** Global / Local / Clan / active whispers (like native chat).
  - **Whisper conversations**: per-person sub-tabs, reply, initiate from a
    dropdown of players seen in chat, and close ("x") a conversation.
  - **Optional native-chat takeover** (off by default) — hide the game's chat and
    type in the tabbed window; gameplay/menu input is suppressed while typing and
    Escape always frees you.
  - **Customization**: chat-only text size, newest-at-bottom/top, auto-scroll,
    word-wrapping input, timestamps, `[G]` vs `[Global]` labels, colored tabs, a
    configurable Global color, and the chat window's own transparency + theme
    color. Readable dark typing field.
- Folds in the **0.16.1 stability hardening** (custom recipes default-off +
  deferred, recipe shape-checks, gated SHIFT-icon read).
- **Eclipse:** coexistence not yet re-verified against Eclipse's latest — keep
  Eclipse disabled while using BloodCraftHub for now.

## 0.16.1 — Crash hotfix (intermittent load crash)

Fixes an **intermittent crash a few seconds after loading into a game** that
some players hit on 0.16.0 (others on the same build never saw it). It showed up
as a NullReferenceException deep inside Il2CppInterop's GC
(`GarbageCollector_RunFinalizer_Patch`) — a known-unstable piece of the interop
layer, not BCH code. BCH wasn't failing; it was *triggering* that latent bug by
doing too much GC-pressuring work in the busy login window — chiefly applying
custom recipes (a burst of ECS structural changes) right as login traffic
landed.

- **Custom recipes now default OFF and apply deferred.** `EnableCustomRecipes`
  now defaults to **false** (turn it on to opt in). When on, recipes are applied
  a few seconds after login on a quiet frame instead of inline, keeping the
  structural-change burst out of the volatile load window. The mutation is also
  hardened (isolated per-block, shape-checked) when enabled.
- **SHIFT-spell icon** lookup is now gated behind the SHIFT overlay being shown,
  `ShowShiftSpellIcon` is a real kill-switch, stale entities are skipped, and a
  circuit-breaker latches it off after repeated faults (cooldown unaffected).
- **Quick Actions "Stash All"** overlay starts smaller and can be shrunk further.

This is a probability reduction for a non-deterministic interop-layer race, not a
guaranteed fix — keeping your BepInEx (V Rising) pack up to date is recommended.
Existing configs that already set `EnableCustomRecipes = true` keep that value; if
you still crash, set it to `false`.

## 0.16.0 — Input suppression, custom recipes, SHIFT-spell icon, exoform fix, Quick Actions overlay, overlay layering + resize discoverability

A player-feedback release. Marquee addition: an optional setting that freezes
the character's in-game actions while the BCH UI is open.

**Added — optionally freeze character actions while the BCH panel is open.**
The most-requested fix: with the main panel open, your character kept acting in
the background — moving on WASD, swinging on mouse clicks, casting hotkeyed
abilities, opening game menus (B = build, M = map). New
`SuppressGameInputWhileUIOpen` option (default **off**), toggled in
Settings → Display ("Freeze character actions while the main panel is open").
When on, while the main panel is open: movement + aim stop; ability/hotkey casts
stop (and an attack started by the click that opened the panel is cancelled, so
it doesn't stick firing); and game-menu hotkeys no longer open menus behind the
panel (with no queued menus firing when you close it). It suppresses only the
gameplay-input systems, never the UI's own input — so the panel, cursor, and
form typing stay fully responsive and the client can't freeze. Off by default.

**Added — Quick Actions overlay (one-click Stash All).** A new draggable,
resizable overlay of one-click command buttons, shipping with a **Stash All**
button (KindredLogistics `.stash`) for the "just got back to base" moment.
Toggle from the footer or Settings → Display; persists across sessions. Built to
host more one-click actions in future releases.

**Added — Bloodcraft custom recipes in the crafting stations.** When the
server has Bloodcraft's extra recipes enabled (vampiric dust, copper
wires, charged battery, soul-shard extraction, primal jewel, blood
crystal, primal stygian, plus new salvage outputs), they now appear in
the vanilla crafting/refinement UI — the same surface the Eclipse client
mod provided. Client-side display only; the server still validates every
craft. Only applied when the server enables them (the `extraRecipes`
flag); automatically skipped when the Eclipse mod is also installed (it
does this itself); gated behind a new `EnableCustomRecipes` option
(default on).

**Added — SHIFT-spell overlay shows the real spell icon.** The shift
cooldown overlay now displays the actual icon of the slotted spell (like
Eclipse), with the cooldown sweep shading over it. New `ShowShiftSpellIcon`
option (default on); off falls back to the plain colored tile. (Known limitation:
for Bloodcraft class spells that override the shift slot, the icon resolves on
first cast — matching Eclipse — since the tooltip data isn't available
client-side until then.)

**Fixed — exoform (Exo) prestige now tracks.** The EXO prestige line
never populated because Exo's `.prestige get` reply has a different
format from every other prestige type and the parser never matched it.
Fixed; the Experience-overlay EXO line now fills, the combined info overlay's
XP section gained the EXO line, and a matching Exo prestige card was added to
the Prestige tab.

**Added — overlays can sit behind in-game menus.** New
`OverlaysBehindGameMenus` option (default on): while a vanilla menu
(inventory, character sheet, map, etc.) is open, BCH's overlays drop
behind it instead of floating over the top. Set false for the old
always-on-top behavior.

**Fixed — couldn't close the panel in fullscreen on smaller screens.**
The fullscreen panel's close/restore controls landed under BCH's floating
launcher button, which ate the click. The launcher now hides while the
panel is fullscreen and returns on exit.

**Improved — drag-to-resize is easier to grab.** The resize edge was hard
to find; the grab zone was widened and the border now highlights on hover.

## 0.15.1 — Hotfix: hotkey double-toggle + per-system disabled detection + familiar auto-probe

Hotfix on top of v0.15.0 covering four friend-test reports.

### Fixed: hotkey appears to work first time then stops responding

After binding a hotkey to "Open main panel", pressing it once
visibly closed the panel — but every press afterwards looked like
nothing happened. The `[DIAG]` log lines showed the state
correctly alternating between True and False, so the hotkey was
firing — just somehow not visibly opening the panel.

Root cause: a long-standing latent double-Setup of BCH's per-frame
host MonoBehaviour. `CoreUpdateBehavior.Setup()` was being called
twice (once from `Plugin.Load`, once from `UniversalUI.Init`), so
every registered per-frame action was running 2× per frame.
Pre-v0.15 actions were idempotent so this was invisible; v0.15.0's
hotkey listener calls `Input.GetKeyDown`, which returns true for
the entire frame after a key transitions Up→Down — so running it
twice per frame double-toggled the panel (open→close→open in one
frame, net visible effect = nothing). Setup now uses a static
host-object guard so the MonoBehaviour is created exactly once
regardless of how many places allocate a `CoreUpdateBehavior`
instance.

### Improved: Quest detection — "(none yet)" → "Quests disabled on this server"

Friend-test 0.15.0: server had every Bloodcraft system enabled
**except** Quests. The Daily Quest overlay + the Daily Quests tab
both showed the empty-state placeholder ("(none yet)")
indefinitely, with no signal that the feature was actually off on
that server.

v0.15.1 adds a stronger per-system "reliably disabled" check that
uses **cross-system corroboration**: if the settling window has
elapsed AND the target system has shown zero data the whole time
AND at least one OTHER Bloodcraft system has shown non-zero data,
the target system is reliably disabled. Condition (c) proves the
structured protocol is up — so an empty signal for the target
system reflects real server-side state rather than "data hasn't
arrived yet."

Three render sites updated to consult the helper:

- **Daily Quest overlay** — empty rows now show "(Quests disabled
  on this server)" + a muted sub-line ("The server admin has
  Bloodcraft's QuestSystem turned off") when reliably detected.
- **Daily Quests tab** — empty placeholder strings swap to the
  same message.
- **Combined info overlay** — QUEST section's empty rows show
  "Daily: (disabled on this server)" / "Weekly: (disabled on this
  server)" so users in combined-mode get the same signal.

The cross-corroboration approach is conservative: it only fires
when we have proof the protocol is working. New players who haven't
engaged with Quests yet on a Quest-enabled server still see the
neutral "(none yet)" placeholder until other systems prove the
broadcast is flowing.

### Improved: per-system disabled detection extended to XP / Familiar / Weapon / Blood / Professions

Follow-up friend report on the inverse scenario — a server with
**only** QuestSystem enabled and every other Bloodcraft feature
off. Quest itself worked (v0.15.0's diagnostic was the right call),
but every OTHER system rendered as "functional" with zeroed data
and no signal the feature was off server-side. Friend-test quote:
"the weapon here I'm actually unarmed but it doesn't register
because unarmed is part of weapon expertise which is off."

`IsSystemReliablyDisabled` is now consulted at every per-system
render site:

- **Combined info overlay** — XP / Familiar / Weapon / Blood /
  Professions sections each show "(<system> disabled on this
  server)" when reliably detected, with bars + sub-rows hidden.
- **Standalone XP overlay** — main XP line plus Weapon and Legacy
  rows each get the disabled treatment (replaces the misleading
  "Weapon —" / "Legacy —" placeholders).
- **Standalone Familiar overlay** — single "(Familiars disabled
  on this server)" line replaces name / progress / stats / bar
  when disabled.
- **Familiar Browser overlay** — header swaps to "(Familiars
  disabled on this server)" with a muted sub-line; Toggle and
  Unbind footer buttons disabled; list cleared.
- **Standalone Profession overlay** — all 8 per-profession rows
  hide; single "(Professions disabled on this server)" line
  takes their place.

Intentionally not touched in 0.15.1: the Shift Spell overlay
(reads V Rising's ability slot directly, not the Eclipse
broadcast, so a disabled signal here would be misleading) and the
inside of each Bloodcraft tab (forms still work for issuing
commands manually — per-tab "(disabled)" banners are deferred to
v0.16 to keep scope manageable).

### Fixed: Familiar overlay false-positive "disabled" at login

Follow-up report on the same Quest-only-disabled server: at login
without a familiar bound, the Familiar overlay incorrectly flagged
Familiars as "disabled" until the user summoned a familiar.

Root cause: the Familiar detection signal was `HasActive` (familiar
currently bound + summoned). A player who hasn't summoned since
login looks identical to "FamiliarSystem disabled" via that
signal. Bloodcraft's Quest signal had the same shape but doesn't
hit this in practice because the server auto-assigns quests within
minutes; Familiar has no such auto-trigger.

Fix: BCH now auto-issues a silent `.fam boxes` probe on the first
ConfigsToClient ACK. The probe is one-shot per session. The
server's response is unambiguous proof: a "Familiar Boxes" header
means FamiliarSystem is enabled (regardless of how many boxes the
user has), and the absence of a response (because Bloodcraft's
handler emits "Familiars are not enabled." instead, which doesn't
match BCH's box-list regex) means it's disabled. The Familiar
overlay also now subscribes to `FeatureFlagsChanged` so it updates
immediately when the probe response lands instead of waiting for
the next ProgressToClient broadcast tick.

## 0.15.0 — Bloodcraft availability diagnostic + visible toggle borders + opt-in hotkeys + diagnostic mode + drag-fix

Friend-test feedback bundle on top of v0.14.0. Eleven items across UX
redundancy, polish, a controller regression, a panel-locked-by-stale-
save regression, and two new opt-in user-experience tools (configurable
hotkeys + three-state diagnostic logging).

### Bloodcraft availability — diagnostic + override

- **In-rail diagnostic panel.** When BCH's Eclipse-protocol handshake
  times out after the 15-second retry window AND `BloodcraftAvailability`
  is still on the default `Auto`, the BLOODCRAFT tab group in the left
  rail expands to a bright-labeled diagnostic instead of just greying
  out. It names the three likely root causes (server doesn't run
  Bloodcraft; runs only `QuestSystem` / `ProfessionSystem`; older
  Bloodcraft with hard `Eclipsed=false`) and surfaces a one-click
  **Force-enable tabs** button. Clicking it flips
  `BloodcraftAvailability=On` for the session so the user can navigate
  into Bloodcraft tabs and drive the chat-regex pipeline (`.fam boxes`
  / `.quest p` / `.bl get` / etc.) with replies surfacing in the
  **Last server response** docked panel.
- **Per-feature degradation infrastructure** (visual treatment
  reverted). `PlayerStateService.ServerFeatureFlags` tracks per-system
  Bloodcraft availability from each ProgressToClient broadcast and
  exposes a `FeatureFlagsChanged` event. The in-UI tab dimming +
  overlay auto-hide that v0.15.0 originally added was reverted after
  friend-test surfaced false positives (Familiar/Shift detection
  signals only fire when the user is actively engaged with each
  system). Infrastructure stays for v0.16's chat-regex probes.

### UI polish

- **Toggle / checkbox borders are now genuinely visible** on every
  monitor. Five-iteration fix landing on an anchored-stretch 2-px
  Frame Image painted near-white plus a custom opaque ColorBlock so
  the inner fill no longer ghosts into the panel chrome at low
  panel-opacity settings. Affects every Toggle in BCH — form
  `BoolField`s, footer overlay toggles, Settings toggles, etc.
- **Tab strip overflow fix.** All three groups expanded
  simultaneously (BLOODCRAFT 12 / KINDRED 6 / SETTINGS-AND-HELP 6)
  no longer collapses the BLOODCRAFT content to zero height and
  hides the Admin button behind the KINDRED header. Two-part fix:
  `minHeight = preferredHeight` clamp on each group's content
  GameObject + ScrollRect wrapping the entire strip so it scrolls
  when content exceeds available vertical space.
- **Familiar Browser overlay min-height** dropped from 440 → 220 so
  users with Large text settings can fit it into small monitor
  corners. Shrinkage comes entirely out of the central scrollable
  list — toolbar, box-name, status, and Unbind/Toggle footer rows
  stay at their natural heights at every overlay size.

### Wording

- **"Reset all familiar entities" relabel.** Pre-0.15 the
  Familiars-tab section was titled `"Reset all familiar entities
  (.fam reset) — DESTRUCTIVE"` and the confirm checkbox read `"Yes,
  destroy active follower entities"`. A user-test showed `.fam reset`
  is actually non-destructive at the collection level — it clears
  stuck `FollowerBuffer` entities + the active-familiar record, but
  box records and unlocks are preserved (familiars can be re-summoned
  via `.fam b N` afterwards). Now titled `"Force-unbind stuck
  familiar (.fam reset)"` with a tooltip that emphasizes box records
  + unlocks are preserved.

### Regressions fixed

- **Controller A-press could re-open the main panel after a teleport
  waypoint.** First-pass fix: `Navigation.Mode.None` on the floating
  BCH / OV buttons so they're excluded from the UI navigation graph
  entirely, plus `EventSystem.SetSelectedGameObject(null)` after each
  click as belt-and-braces. Mouse clicks work unchanged. **Controller
  testing is ongoing** — the README's new "Controller / gamepad input
  under investigation" heads-up asks for repro reports via Discord.
- **Main panel could load locked against drag/resize** when a stale
  `IsPinned=True` had been persisted in the panel's save data (e.g.
  from a pre-0.11.2 fullscreen session that triggered a save before
  the "fullscreen state is transient" rule landed). `ApplySaveData`
  now skips the IsPinned bit for panels that don't opt into
  `RespectsLockOverlays` (the main panel doesn't), and the Settings
  → Display → Size & Positioning **Default** button defensively
  unpins so existing-broken-state users can recover without
  restarting the game.

### New opt-in tools

- **Keyboard hotkeys** for the floating-button actions. Two slots:
  Open main panel + Toggle all overlays. Both unbound by default;
  bind via Settings → Display → Hotkeys & diagnostics → click "Set..."
  → press any key (or modifier+key combo). Persists to `.cfg` as a
  human-readable string (`"Insert"`, `"Ctrl+H"`, etc.).
- **Three-state diagnostic mode** — Off / Session / Always. Off by
  default; Session-only logging never persists across game restarts,
  so a one-off bug repro can't leave verbose `[DIAG]` logging on
  forever. When active in any non-Off state, BCH emits `[DIAG]`-
  tagged trace logs for UI clicks, overlay toggles, protocol
  registration transitions, feature-flag transitions, and hotkey
  fires.

### Documentation

- README adds two new "Heads-up before you install" entries
  (controller-input under investigation; server-side Bloodcraft
  compatibility) and updates the Eclipse compatibility-table row to
  reflect the current client-crash conflict (previously listed Eclipse
  as "coexists, doesn't conflict" — outdated since the issue was
  discovered).
- New screenshot added (mid-combat capture showing BCH overlays
  running alongside the V Rising HUD).
- Tab counts, overlay counts, and group names refreshed throughout.

## 0.14.0 — Combined info overlay + default-size bump + dead-command cleanup

The marquee v0.14.0 feature: **one combined info overlay** that
replaces the four standalone info overlays (XP, Familiar, Daily
Quest, Profession) with a single draggable / resizable panel
containing six configurable sections — XP, Familiar, Weapon
Expertise, Blood Legacy, Professions, Daily/Weekly Quest. Mutually
exclusive with the individuals: turning Combined on hides them;
turning it off restores them.

### Combined overlay highlights

- **Six sections**, each with its own bold colored heading (Eclipse-
  style color vocabulary — XP green, Familiar amber, Weapon grey,
  Blood red, Professions gold, Quests cyan/gold).
- **Per-section visibility checkboxes** in Settings → Display →
  Combined overlay. XP / Familiar / Professions / Quests share the
  same flags the footer toggles use, so the two surfaces stay in sync.
- **Per-system progress bars** (Settings → Display → HUD extras →
  "Show progress bars for") apply to BOTH the standalone overlays
  AND the combined overlay so toggling one of these is consistent
  regardless of which mode you're in. Fifth flag for Professions
  was added in this release.
- **Bonus stats + XP counter sub-rows** on the Weapon and Blood
  sections respect the existing `ShowOverlayBonusStats` /
  `ShowOverlayXpCounter` toggles — combined picks up the same data
  feed the standalone XP overlay uses.
- **Auto-fit**: panel snaps to its content size on construct, on
  section toggle, and on overlay text-scale change (Standard ↔ Large
  ↔ X-Large). Text-scale downshift shrinks the panel; manual width
  resizing persists, manual height is reset on each text-scale rebuild.
- **Transparency works end-to-end**: section sub-containers are
  fully transparent so the panel's transparency slider controls the
  entire visible area (pre-fix only the outer chrome was affected).
- **Footer Combined toggle** with tooltip explains the mutual-
  exclusion swap. When Combined is on, the four conflict toggles
  (XP / Familiar / Daily quest / Professions) hide from the footer
  — Familiar Browser + Shift Spell stay visible since they're
  independent.

### Other v0.14.0 changes

- **Removed unimplemented Bloodcraft battle-group commands**
  (`.fam abg`, `.fam cbg`, `.fam sbg`, `.fam dbg`, `.fam bgs`,
  `.fam bg`, `.fam challenge`). A Bloodcraft server admin confirmed
  in chat that these never shipped functionally in Bloodcraft v1.1+.
  The entire Battle Groups card on the Familiars tab is removed;
  intercept paths cleaned up.
- **Main panel default size bumped from 600×380 → 960×700**. Two
  rounds of friend-test feedback that the post-v0.13 layout was
  cramped at the old default. Existing saved sizes preserved.
- **Updated screenshots** to v0.13.0 captures — class context cards
  on the Class / Weapon / Blood tabs, prestige progression reference,
  dual-zone color picker, V-Bloods tab in-world.

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
