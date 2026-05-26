# BloodCraftHub — Internal Roadmap & Backlog

Engineering-facing roadmap. The *user-facing* roadmap (short, marketing-tone)
lives in `README.md` → "Roadmap". This file is the detailed version: enough
context to hit the ground running when we pick an item up.

Status legend: **Planned** (agreed, not started) · **Spike** (needs a
de-risking prototype first) · **In progress** · **Deferred**.

---

## Localization / i18n (German + Spanish first) — **Planned**

**Goal:** translate the BCH UI into additional languages, starting with
**German + Spanish**, then French + Swedish as cheap follow-ons, with **Russian
last** (pending the Cyrillic font check). Driven by a real DE user base.

**Recommendation in one line:** worth doing, but it's a *dedicated track* (not a
drop-in for a feature release) because the dominant cost is externalizing ~600
hardcoded UI strings — not the translation or the fonts.

### Current state (as of v0.16.0)
- `Services/LocalizationService.cs` — **stub** (constructor commented out; does nothing).
- `Resources/Localization/English.json` — **empty** (`{"strings":{}}`), already
  embedded via `BloodCraftHub.csproj`.
- **No `GetString`/lookup layer.** Every user-facing string is a hardcoded C#
  literal: ~230 `.text =`, ~210 form `tooltip:` params, ~100 `AddSectionHeading(...)`.

### The font question (narrower than it first sounds)
- BCH UI text renders through V Rising's TMPro stack. `docs/LESSONS_LEARNED.md`
  notes a few *symbol* glyphs render as boxes (`◄ ► ↻`) — but those are obscure
  symbols, **not letters**.
- **German/Spanish/French/Swedish are Latin-script**; their accented characters
  (ä ö ü ß, ñ ¿ ¡, é è ç, å) are common Latin-1 glyphs that are very likely
  already renderable. Strong corroborating evidence: **Bloodcraft itself ships
  Spanish / French / Turkish localization in this same game** (see
  `LearningMods/Bloodcraft-main/Resources/Localization/`), so the game's text
  stack clearly handles accented Latin.
- **Russian (Cyrillic) is the only real font risk.** It may render as boxes
  through BCH's UI font and need routing to one of the game's own multilingual
  `TMP_FontAsset`s (V Rising is officially localized in ~15 languages, so the
  glyphs exist in the game's assets). Defer RU behind a specific Cyrillic test.

### Scope lever: tier the strings
Don't translate everything equally. Localize the **interactive chrome first**
(tab labels, buttons, settings labels, status messages, tooltips) and **defer
the long help prose** (Quick Start / Mod Help / Game Guide — hundreds of lines,
lowest ROI, most likely to drift). This cuts the real translation burden a lot
while giving translated users ~90% of the value.

### Design notes
- BCH's need is **simpler than Bloodcraft's**. Bloodcraft registers strings with
  `Stunlock.Localization` so the *server* can emit localized chat. BCH owns its
  own UI labels, so it just needs an internal key→string dictionary plus a
  `GetString(key)` that picks the active-language JSON. No Stunlock.Localization
  registration required.
- Language selection: a Settings dropdown (Auto-detect from the game's locale,
  or manual override), persisted to `.cfg`.
- **Layout caveat:** German strings run ~30% longer and overflow tight
  buttons/labels — the externalization pass should add layout give (auto-size /
  wrapping) where text is cramped.

### Pre-commit checks (do these BEFORE the big refactor)
1. **Font glyph test (~½ day):** drop German + Spanish (+ a Cyrillic sample)
   onto a BCH label and look. Confirms accented Latin renders (expected yes) and
   gives a definitive yes/no on Russian. No point externalizing 600 strings if
   they render as boxes.
2. **Reviewer availability:** machine translation handles the bulk, but mod
   jargon (familiar, expertise, legacy, prestige, exoform, stash) needs
   native-speaker polish. Recruit 1–2 DE/ES reviewers from the Discord (fits the
   friend-test culture). Confirm before investing.

### Sequencing (hit-the-ground-running order)
1. Font glyph spike → decides whether DE/ES (expected green) and RU (uncertain)
   are on the table.
2. Build the lookup layer: finish `LocalizationService` + a `GetString(key)`
   helper + populate `English.json` as the source-of-truth keyset.
3. Externalize strings **incrementally** (chrome first, defer help-prose) —
   route each `.text =` / `tooltip:` / heading through `GetString`. Low-risk but
   mechanical; do it file-by-file, not one giant PR.
4. Ship **German + Spanish** with Discord native-speaker review.
5. **French + Swedish** become cheap follow-ons once the infra exists (translated
   JSON + review).
6. **Russian** last, pending the Cyrillic font answer (may need a game
   `TMP_FontAsset` routed into BCH's text).

---

## Other planned items (summary — see memory / discussions for detail)

- **Standalone UI-enhancements tab group** — *In progress (v0.17).* A 4th
  left-rail group for client-side features that work with no server mod. The
  launcher + panel already appear unconditionally (no gating fix needed); the
  group falls through to "always available." Hosts settings/controls for the
  client features below.
- **Tabbed chat** — *Planned (v0.17 headline).* Per-channel chat
  (Global / Local / Clan / System / per-person Whisper), built on the data the
  client already receives (`ChatMessageServerEvent`: type + sender + timestamp).
  Open design decision: integrate into the *native* game chat vs. a docked
  BCH-styled surface (see discussion).
- **Resource-node overlays (accessibility)** — *Spike.* Screen-space markers for
  nearby resource nodes, colorblind-friendly (shape/label, not color-only).
  Prototype to settle entity-identification + per-frame perf before committing.
- **Map: castle-heart timers + plot availability/size** — *Deferred (v0.18+,
  dual-component).* Static plot sizes (`CastleTerritory.WorldBounds`) are
  map-fixed → client-renderable/scrapeable; live availability + heart timers
  (`CastleHeart.FuelEndTime`) are NOT replicated to the client → need a
  companion **server-side mod** feeding BCH via the chat-command-shape protocol.
- **Eclipse coexistence** — *Planned.* Re-test against the latest Eclipse; was a
  client crash with both installed. Resolve so they can run side by side.
