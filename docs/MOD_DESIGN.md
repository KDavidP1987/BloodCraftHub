# BloodCraftHub — Mod Design

What the mod is, who it's for, and what it does. This is the **user-facing** spec; for code structure see [`ARCHITECTURE.md`](ARCHITECTURE.md), for publishing see [`THUNDERSTORE.md`](THUNDERSTORE.md).

## In one sentence

**BloodCraftHub is a client-side V Rising mod that gives players and admins a unified UI for the Bloodcraft server mod, replacing the brittle text-command workflow with discoverable panels and live HUD overlays.**

## Audience

- **Players** on Bloodcraft servers — want to see their progress, manage familiars, and interact with Bloodcraft features without memorizing chat commands.
- **Server admins** — want better diagnostics (live state inspection) and feature toggles surfaced in-game.

Not in scope: a server-side mod, a generic V Rising overlay, support for non-Bloodcraft servers.

## What it replaces

| Existing mod | What it does | What BloodCraftHub keeps | What BloodCraftHub fixes |
|---|---|---|---|
| **BloodCraftUI** v1.1.0 | Panel UI focused on familiars (`.fam`) | The panel framework and familiar UX | Replaces fragile regex chat parsing with Eclipse's structured protocol; expands beyond familiars |
| **Eclipse** v1.3.13 | In-place HUD overlays (XP bar, prestige, legacy, expertise, professions, quests, shift slot) | The structured event protocol and HUD overlays | Adds discoverable panel UI on top so features have more than a thin overlay strip |

Both upstream mods continue to exist; this project is a fork/merge intended to obsolete the need to run both side by side.

## Feature surface

Driven by what Bloodcraft (server v1.13.21) exposes — currently 9 command groups, ~97 subcommands. The UI groups them into themed panels:

### Tier 1 — must-have for v1.0

- **Progress overlay**: leveling XP bar, prestige tag, class indicator. Live-updating via Eclipse structured protocol. (Eclipse parity.)
- **Legacy overlay**: blood legacy bar with active legacy name. (Eclipse parity.)
- **Expertise overlay**: weapon expertise bar tracking the equipped weapon. (Eclipse parity.)
- **Familiar panel**: list of familiar boxes, contents, active familiar, bind/unbind buttons, stats readout. (BloodCraftUI parity.)
- **Quest tracker**: daily/weekly quest list, progress, targets. (Eclipse parity, but as a draggable panel rather than fixed overlay.)
- **Shift slot indicator**: shows the equipped shift spell. (Eclipse parity.)

### Tier 2 — soon after

- **Profession panel**: each profession's level, recent gathering yield boosts, perks.
- **Prestige panel**: prestige levels per system, requirements for next tier, stat bonuses.
- **Class panel**: current class, abilities granted, switching UX (subject to server config).

### Tier 3 — eventually / nice-to-have

- **Admin diagnostics panel** (gated on Bloodcraft admin role): show server-side feature toggles snapshot, current player state dump, last-N-messages debugger.
- **Macro / loadout system**: save and replay sequences of `.fam` / `.weapon` commands (e.g. a "switch to mage" hotkey).
- **Notification feed**: structured surface for Bloodcraft events that today only appear in chat.

### Out of scope

- Combat overlays / DPS meters / generic V Rising QoL features. There are other mods for that; BloodCraftHub is the **Bloodcraft companion**, not a swiss army knife.
- Anything that bypasses server authority (no client-side stat injection, no command spoofing for actions the player isn't authorized for).

## UX principles

1. **Discoverability over economy.** Every Bloodcraft command the user needs should be reachable from the UI in ≤2 clicks. The chat command is a fallback, not the primary path.
2. **Don't fight the game's HUD.** In-place overlays sit on the existing UI like Eclipse — they don't replace the game's UI, they annotate it.
3. **Panels are draggable, resizeable, and toggleable.** Players have wildly different screen sizes; defaults work but lock-in doesn't.
4. **Server-respecting.** Feature gates (`IsLegacyEnabled`, etc.) are honored. If the server disables a Bloodcraft system, the corresponding UI hides itself rather than showing a broken empty bar.
5. **Fail soft.** A malformed inbound message (whether regex or structured) logs a warning and is skipped — the UI never crashes or freezes because the server format drifted.

## Versions of dependency it works against

| Layer | Version | Notes |
|---|---|---|
| V Rising | (TBD — pin once tested) | Production game; whatever Stunlock is currently shipping |
| BepInEx | `6.0.0-be.733` | IL2CPP build from `nuget.bepinex.dev` |
| `VampireReferenceAssemblies` | `1.1.11-r96495-b8` | Newer replacement for `VRising.Unhollowed.Client` |
| Bloodcraft (server) | `v1.13.21` (target) | Eclipse upstream targets the same; protocol may drift, pin tested version per release |

## Operating modes

- **Solo / single-player:** mod still loads; structured protocol returns nothing because there's no Bloodcraft server. UI gracefully shows "not connected" rather than empty bars. (Tier 1 nicety.)
- **Connected to a Bloodcraft server:** primary supported mode. UI populates via Eclipse protocol + chat injection.
- **Connected to a non-Bloodcraft server:** mod loads, sends no commands, hides all panels. No error spew.

## Success criteria (for v1.0)

- A new player can join a Bloodcraft server, see their XP/legacy/expertise update in real time, and manage their first familiar **without ever typing a chat command.**
- Mod loads cleanly on a vanilla BepInEx install with no manual file editing required beyond installing through the Thunderstore mod manager.
- Total package size under 5 MB.
- No regression vs Eclipse for the features Eclipse already covers (same data, same update cadence).
