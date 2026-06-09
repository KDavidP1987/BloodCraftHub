using BloodCraftHub.Services;
using BloodCraftHub.UI.Forms;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using TMPro;
using UnityEngine;
using BloodCraftHub.UI.Framework.CustomLib.Util;

namespace BloodCraftHub.UI.ModContent;

// Phase 5i — KindredCommands admin command surface (146 commands), split into
// three sub-tabs (Players / Server / World). Each Build method on the
// MainPanel partial below adds an intro warning, then walks the BCCOM_KCA_*
// constants organized by command group, surfacing zero-arg commands as
// AddCommandButton rows and arg-taking commands as collapsible FormBuilder
// forms.
//
// The PlayerNameField default placeholder is "Player name". Many admin
// commands accept an optional player argument that defaults to self when
// blank, so this file uses plain TextField with placeholder "blank for self"
// rather than PlayerNameField in those slots - the type is the same widget
// either way, just with a more accurate hint.
public partial class MainPanel
{
    // -----------------------------------------------------------------------
    // Admin: Players sub-tab
    // -----------------------------------------------------------------------

    private void BuildKindredAdminPlayersTab(GameObject page)
    {
        page = BeginAdminGate(page);   // gray out + disable the admin controls below for non-admins
        RenderAdminInfoNote(page, "Kindred admin (Players)");
        AddAdminWarningIntro(page,
            "Player-targeting admin commands. Most accept a player name; leave " +
            "the player field blank to target yourself. Requires KindredCommands " +
            "+ server admin permission.");

        // ---- General (no group) ------------------------------------------
        AddSpacer(page, 4);
        AddSectionHeading(page, "General");

        var genRow = AddKLRow(page, "KCAGenRow1");
        AddCommandButton(genRow, "Revive Target", MessageService.BCCOM_KCA_REVIVE_TARGET,
            "Revive the dead player you are currently targeting (.revivetarget).");

        AddPlayerCollapse(page, "Revive (.revive)",
            "Revive a player from death (defaults to self).",
            ".revive {player}", optional: true);
        AddPlayerCollapse(page, "Kill player (.killplayer)",
            "Instantly kill the specified player.",
            ".killplayer {player}", optional: false);
        AddPlayerCollapse(page, "Stay down (.staydown)",
            "Down the target player until revived.",
            ".staydown {player}", optional: false);

        CollapsibleSection.Build(page,
            title: "Set heart count (.playerheartcount)",
            startExpanded: false,
            tooltip: "Set how many soul-shard 'hearts' a player counts as on the server.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set heart count",
                commandTemplate: ".playerheartcount {amount} {player}",
                new IntField("amount", "Heart count", min: 0, max: 99,
                    tooltip: "Number of soul-shard hearts."),
                new PlayerNameField("player", "Player",
                    tooltip: "Player to apply to. Required for this command.")));

        CollapsibleSection.Build(page,
            title: "Teleport player to coords (.teleport)",
            startExpanded: false,
            tooltip: "Teleport a player to specific X/Y/Z world coordinates (defaults to self).",
            buildContent: c => FormBuilder.Build(c,
                title: "Teleport to coords",
                commandTemplate: ".teleport {x} {y} {z} {player}",
                new TextField("x", "X", tooltip: "World X coordinate."),
                new TextField("y", "Y", tooltip: "World Y coordinate."),
                new TextField("z", "Z", tooltip: "World Z coordinate."),
                new TextField("player", "Player (blank for self)",
                    tooltip: "Target player. Leave blank to teleport yourself.")));

        AddPlayerCollapse(page, "Reveal map (.revealmap)",
            "Reveal the entire map for a player.",
            ".revealmap {player}", optional: true);
        AddPlayerCollapse(page, "Unlock skills/journal (.unlock)",
            "Unlock all skills, journal entries, and abilities for a player.",
            ".unlock {player}", optional: true);
        AddPlayerCollapse(page, "God mode on (.god)",
            "Enable full god mode for the target.",
            ".god {player}", optional: true);
        AddPlayerCollapse(page, "Mortal (god mode off) (.mortal)",
            "Remove god mode and admin boosts from the target.",
            ".mortal {player}", optional: true);
        AddPlayerCollapse(page, "Reset cooldowns (.resetcooldown)",
            "Instantly reset all ability cooldowns for the target.",
            ".resetcooldown {player}", optional: true);
        AddPlayerCollapse(page, "List active buffs (.listbuffs)",
            "List the buffs currently applied to the target.",
            ".listbuffs {player}", optional: true);

        CollapsibleSection.Build(page,
            title: "Spectate (.spectate)",
            startExpanded: false,
            tooltip: "Toggle spectate mode following a player.",
            buildContent: c => FormBuilder.Build(c,
                title: "Spectate",
                commandTemplate: ".spectate {player} {returnToStart}",
                new TextField("player", "Player (blank for self)",
                    tooltip: "Player to spectate. Blank reverts to self."),
                new BoolField("returnToStart", "Return to start when done",
                    defaultValue: true,
                    tooltip: "If true, snaps you back to your starting position when spectate ends.")));

        // ---- Rename / Unbind / Swap -----------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Identity");

        CollapsibleSection.Build(page,
            title: "Rename self (.rename)",
            startExpanded: false,
            tooltip: "Rename your own character.",
            buildContent: c => FormBuilder.Build(c,
                title: "Rename self",
                commandTemplate: ".rename {newName}",
                new TextField("newName", "New name",
                    tooltip: "Your new character name.")));
        CollapsibleSection.Build(page,
            title: "Rename player (.rename)",
            startExpanded: false,
            tooltip: "Rename another player's character.",
            buildContent: c => FormBuilder.Build(c,
                title: "Rename player",
                commandTemplate: ".rename {player} {newName}",
                new PlayerNameField("player", "Player"),
                new TextField("newName", "New name")));
        CollapsibleSection.Build(page,
            title: "Unbind player save (.unbindplayer)",
            startExpanded: false,
            tooltip: "Unbinds a SteamID from a player save so the slot can be re-used.",
            buildContent: c => FormBuilder.Build(c,
                title: "Unbind player",
                commandTemplate: ".unbindplayer {player}",
                new PlayerNameField("player", "Player")));
        CollapsibleSection.Build(page,
            title: "Swap two players' SteamIDs (.swapplayers)",
            startExpanded: false,
            tooltip: "Switches the SteamIDs of two players. Useful for recovering lost characters.",
            buildContent: c => FormBuilder.Build(c,
                title: "Swap players",
                commandTemplate: ".swapplayers {player1} {player2}",
                new PlayerNameField("player1", "Player 1"),
                new PlayerNameField("player2", "Player 2")));

        // ---- Fly group ----------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Fly");

        AddPlayerCollapse(page, "Toggle fly (.fly)",
            "Toggle fly mode for a player (defaults to self).",
            ".fly {player}", optional: true);
        AddPlayerCollapse(page, "Fly up one floor (.flyup)",
            "Increase fly height by one floor.",
            ".flyup {player}", optional: true);
        AddPlayerCollapse(page, "Fly down one floor (.flydown)",
            "Decrease fly height by one floor.",
            ".flydown {player}", optional: true);

        CollapsibleSection.Build(page,
            title: "Fly level (.flylevel)",
            startExpanded: false,
            tooltip: "Set fly height to a specific floor level.",
            buildContent: c => FormBuilder.Build(c,
                title: "Fly to level",
                commandTemplate: ".flylevel {floor} {player}",
                new IntField("floor", "Floor", min: 0, max: 99,
                    tooltip: "Floor number to fly to."),
                new TextField("player", "Player (blank for self)")));
        CollapsibleSection.Build(page,
            title: "Set fly height (.flyheight)",
            startExpanded: false,
            tooltip: "Set the absolute fly height for yourself (default 30).",
            buildContent: c => FormBuilder.Build(c,
                title: "Set fly height",
                commandTemplate: ".flyheight {height}",
                new IntField("height", "Height", min: 1, max: 500,
                    tooltip: "Fly height in world units. Default 30.")));
        CollapsibleSection.Build(page,
            title: "Set fly obstacle height (.flyobstacleheight)",
            startExpanded: false,
            tooltip: "Set the height to fly above any obstacles (default 7).",
            buildContent: c => FormBuilder.Build(c,
                title: "Set obstacle height",
                commandTemplate: ".flyobstacleheight {height}",
                new IntField("height", "Height", min: 0, max: 100,
                    tooltip: "Auto-rise height when an obstacle is below. Default 7.")));

        // ---- Buffs & items -----------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Buffs & items");

        CollapsibleSection.Build(page,
            title: "Apply buff (.buff)",
            startExpanded: false,
            tooltip: "Apply a named buff to a player. Buff names match the server's prefab lookup.",
            buildContent: c => FormBuilder.Build(c,
                title: "Apply buff",
                commandTemplate: ".buff {buff} {player} {duration} {immortal}",
                new TextField("buff", "Buff prefab",
                    tooltip: "Buff prefab name or ID. Use .search npc on the server for hints."),
                new TextField("player", "Player (blank for self)"),
                new IntField("duration", "Duration (seconds, 0=infinite)", min: 0, max: 86400),
                new BoolField("immortal", "Immortal flag",
                    tooltip: "If true the buff cannot be cleansed.")));

        CollapsibleSection.Build(page,
            title: "Remove buff (.debuff)",
            startExpanded: false,
            tooltip: "Remove a specific buff from a player.",
            buildContent: c => FormBuilder.Build(c,
                title: "Remove buff",
                commandTemplate: ".debuff {buff} {player}",
                new TextField("buff", "Buff prefab"),
                new TextField("player", "Player (blank for self)")));

        CollapsibleSection.Build(page,
            title: "Give item (.give)",
            startExpanded: false,
            tooltip: "Spawn an item directly into your inventory.",
            buildContent: c => FormBuilder.Build(c,
                title: "Give item",
                commandTemplate: ".give {item} {quantity}",
                new TextField("item", "Item",
                    tooltip: "Item prefab/name. Use .search item to look up names."),
                new IntField("quantity", "Quantity", min: 1, max: 9999)));

        CollapsibleSection.Build(page,
            title: "Create blood potion (.bloodpotion)",
            startExpanded: false,
            tooltip: "Spawn a blood potion of the given blood type and quality.",
            buildContent: c => FormBuilder.Build(c,
                title: "Create blood potion",
                commandTemplate: ".bloodpotion {type} {quality} {quantity}",
                new EnumField<PlayerStateService.BloodType>("type", "Blood type",
                    defaultValue: PlayerStateService.BloodType.Warrior),
                new IntField("quality", "Quality (1-100)", min: 1, max: 100),
                new IntField("quantity", "Quantity", min: 1, max: 99)));

        CollapsibleSection.Build(page,
            title: "Create mixed blood potion (.bloodpotionmix)",
            startExpanded: false,
            tooltip: "Spawn a mixed blood potion blending two blood types.",
            buildContent: c => FormBuilder.Build(c,
                title: "Create mixed potion",
                commandTemplate: ".bloodpotionmix {primaryType} {primaryQuality} {secondaryType} {secondaryQuality} {secondaryTrait} {quantity}",
                new EnumField<PlayerStateService.BloodType>("primaryType", "Primary type",
                    defaultValue: PlayerStateService.BloodType.Warrior),
                new IntField("primaryQuality", "Primary quality", min: 1, max: 100),
                new EnumField<PlayerStateService.BloodType>("secondaryType", "Secondary type",
                    defaultValue: PlayerStateService.BloodType.Scholar),
                new IntField("secondaryQuality", "Secondary quality", min: 1, max: 100),
                new IntField("secondaryTrait", "Secondary trait #", min: 1, max: 4),
                new IntField("quantity", "Quantity", min: 1, max: 99)));

        // ---- Boost group (.bst) -------------------------------------------
        AddSpacer(page, 6);
        CollapsibleSection.Build(page,
            title: "Boost (.bst) - 22 commands",
            startExpanded: false,
            tooltip: "Per-player stat boosts and admin toggles. Each accepts an optional player (blank for self).",
            buildContent: BuildBoostGroup);

        // ---- Gear (player-targeting) --------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Gear");
        AddPlayerCollapse(page, "Repair all gear (.gear repair)",
            "Repair every piece of gear the target is wearing.",
            ".gear repair {player}", optional: true);
        AddPlayerCollapse(page, "Break all gear (.gear break)",
            "Drop the target's gear durability to zero.",
            ".gear break {player}", optional: true);
        CollapsibleSection.Build(page,
            title: "Set soulshard durability (.gear soulsharddurability)",
            startExpanded: false,
            tooltip: "Set the soulshard durability for a player.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set soulshard durability",
                commandTemplate: ".gear soulsharddurability {durability} {player}",
                new IntField("durability", "Durability (0-1)", min: 0, max: 1,
                    tooltip: "Use a fractional value via the chat command for non-integer durability. UI uses int."),
                new TextField("player", "Player (blank for self)")));

        // ---- Clan (player ops) --------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Clan");
        CollapsibleSection.Build(page,
            title: "Add player to clan (.clan add)",
            startExpanded: false,
            tooltip: "Force-add a player to a clan.",
            buildContent: c => FormBuilder.Build(c,
                title: "Add to clan",
                commandTemplate: ".clan add {player} {clanName}",
                new PlayerNameField("player", "Player"),
                new TextField("clanName", "Clan name")));
        CollapsibleSection.Build(page,
            title: "Kick player from clan (.clan kick)",
            startExpanded: false,
            tooltip: "Remove a player from their current clan.",
            buildContent: c => FormBuilder.Build(c,
                title: "Kick from clan",
                commandTemplate: ".clan kick {player}",
                new PlayerNameField("player", "Player")));
        CollapsibleSection.Build(page,
            title: "Change clan role (.clan changerole)",
            startExpanded: false,
            tooltip: "Change a player's role within their clan (Member / Officer / Leader).",
            buildContent: c => FormBuilder.Build(c,
                title: "Change clan role",
                commandTemplate: ".clan changerole {player} {role}",
                new PlayerNameField("player", "Player"),
                new TextField("role", "Role",
                    tooltip: "Role name (e.g. Member, Officer, Leader). Match what KindredCommands expects.")));

        // ---- Player info / lookup (0.5.0 audit gaps) ---------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Lookup");
        CollapsibleSection.Build(page,
            title: "Player info (.playerinfo)",
            startExpanded: false,
            tooltip: "Display detailed info about a specific player. Reply appears in chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Player info",
                commandTemplate: ".playerinfo {player}",
                new PlayerNameField("player", "Player")));
        CollapsibleSection.Build(page,
            title: "Search player by SteamID (.idcheck)",
            startExpanded: false,
            tooltip: "Look up which character is associated with a given SteamID.",
            buildContent: c => FormBuilder.Build(c,
                title: "ID check",
                commandTemplate: ".idcheck {steamId}",
                new TextField("steamId", "SteamID", placeholder: "76561198…")));
        CollapsibleSection.Build(page,
            title: "Assign SteamID to player (.assignsteamID)",
            startExpanded: false,
            tooltip: "Link a player record to a different SteamID. Recovery use — most servers will never need this.",
            buildContent: c => FormBuilder.Build(c,
                title: "Assign SteamID",
                commandTemplate: ".assignsteamID {player} {steamId}",
                new PlayerNameField("player", "Player"),
                new TextField("steamId", "SteamID", placeholder: "76561198…")));

        // ---- Cosmetic / convenience -------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Cosmetic / Convenience");
        CollapsibleSection.Build(page,
            title: "Toggle hair visibility (.showhair)",
            startExpanded: false,
            tooltip: "Toggle hair rendering on a player (or yourself if blank).",
            buildContent: c => FormBuilder.Build(c,
                title: "Show hair",
                commandTemplate: ".showhair {player}",
                new PlayerNameField("player", "Player (blank for self)")));

        // ---- Prisoner config readouts (paired with the .prisoner setters) -
        AddSpacer(page, 6);
        AddSectionHeading(page, "Prisoner config (read)");
        var diagRow = UIFactory.CreateHorizontalGroup(page, "PrisonerDiag",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(diagRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(diagRow, "Gruel Settings", MessageService.BCCOM_KCA_GRUEL_SETTINGS,
            "Print current gruel-conversion config (.gruelsettings).");
        AddCommandButton(diagRow, "Feed Settings",  MessageService.BCCOM_KCA_FEED_SETTINGS,
            "Print current feed-prisoner config (.feedsettings).");

        // ---- Mass destructive (extra-careful) ---------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Mass action (DESTRUCTIVE)");
        CollapsibleSection.Build(page,
            title: "Unbind every player (.unbindall) — DESTRUCTIVE",
            startExpanded: false,
            tooltip: "Renames EVERY player to OLD##### and unbinds them from their SteamIDs. There is no undo. Used for fresh-start prep. Requires the confirm checkbox.",
            buildContent: c => FormBuilder.Build(c,
                title: "Unbind ALL players",
                commandTemplate: ".unbindall",
                new BoolField("confirm", "Yes, rename + unbind everyone",
                    tooltip: "Required. Even with this checked, double-check before clicking Submit.",
                    requireTrue: true)));
    }

    // Helper: build a CollapsibleSection wrapping a 1-field form for the very
    // common "optional player target" command pattern.
    private static void AddPlayerCollapse(GameObject page, string title, string tooltip, string template, bool optional)
    {
        CollapsibleSection.Build(page,
            title: title,
            startExpanded: false,
            tooltip: tooltip,
            buildContent: c => FormBuilder.Build(c,
                title: title,
                commandTemplate: template,
                optional
                    ? (FormField)new TextField("player", "Player (blank for self)",
                        tooltip: "Leave blank to target yourself.")
                    : new PlayerNameField("player", "Player")));
    }

    // The boost group has 22 closely-related commands; rather than 22 top-level
    // CollapsibleSections, wrap them all in one outer collapsible and build a
    // tighter list inside.
    private void BuildBoostGroup(GameObject c)
    {
        var row = AddKLRow(c, "BstZeroArg");
        AddCommandButton(row, "List Boosted", MessageService.BCCOM_KCA_BST_PLAYERS,
            "List all currently boosted players (.bst players).");

        AddBstValueForm(c, "Attack speed (.bst attackspeed)",
            ".bst attackspeed {value} {player}", "Attack speed multiplier (default 10).", 10);
        AddBstTogglePlayer(c, "Remove attack speed (.bst removeattackspeed)",
            ".bst removeattackspeed {player}");
        AddBstValueForm(c, "Damage (.bst damage)",
            ".bst damage {value} {player}", "Bonus damage value (default 10000).", 10000);
        AddBstTogglePlayer(c, "Remove damage (.bst removedamage)",
            ".bst removedamage {player}");
        AddBstValueForm(c, "Health (.bst health)",
            ".bst health {value} {player}", "Bonus health value (default 100000).", 100000);
        AddBstTogglePlayer(c, "Remove health (.bst removehealth)",
            ".bst removehealth {player}");
        AddBstValueForm(c, "Movement speed (.bst speed)",
            ".bst speed {value} {player}", "Movement speed multiplier (default 15).", 15);
        AddBstTogglePlayer(c, "Remove speed (.bst removespeed)",
            ".bst removespeed {player}");
        AddBstValueForm(c, "Yield (.bst yield)",
            ".bst yield {value} {player}", "Yield multiplier on harvest/loot (default 10).", 10);
        AddBstTogglePlayer(c, "Remove yield (.bst removeyield)",
            ".bst removeyield {player}");

        AddBstTogglePlayer(c, "Bat vision (.bst batvision)",         ".bst batvision {player}");
        AddBstTogglePlayer(c, "Fly (.bst fly)",                       ".bst fly {player}");
        AddBstTogglePlayer(c, "No aggro (.bst noaggro)",              ".bst noaggro {player}");
        AddBstTogglePlayer(c, "No blood drain (.bst noblooddrain)",   ".bst noblooddrain {player}");
        AddBstTogglePlayer(c, "No cooldown (.bst nocooldown)",        ".bst nocooldown {player}");
        AddBstTogglePlayer(c, "No durability loss (.bst nodurability)", ".bst nodurability {player}");
        AddBstTogglePlayer(c, "Immaterial (.bst immaterial)",         ".bst immaterial {player}");
        AddBstTogglePlayer(c, "Invincible (.bst invincible)",         ".bst invincible {player}");
        AddBstTogglePlayer(c, "Shrouded (.bst shrouded)",             ".bst shrouded {player}");
        AddBstTogglePlayer(c, "Sun invulnerable (.bst suninvulnerable)", ".bst suninvulnerable {player}");
        AddBstTogglePlayer(c, "Show state (.bst state)",              ".bst state {player}");
    }

    private static void AddBstValueForm(GameObject c, string title, string template, string valueTooltip, int defaultValue)
    {
        CollapsibleSection.Build(c,
            title: title, startExpanded: false, tooltip: valueTooltip,
            buildContent: cc => FormBuilder.Build(cc,
                title: title, commandTemplate: template,
                new IntField("value", "Value", min: 0, max: 1_000_000,
                    tooltip: valueTooltip + $" Try {defaultValue} for parity with the chat default."),
                new TextField("player", "Player (blank for self)")));
    }

    private static void AddBstTogglePlayer(GameObject c, string title, string template)
    {
        CollapsibleSection.Build(c,
            title: title, startExpanded: false, tooltip: "Toggle on the target (blank for self).",
            buildContent: cc => FormBuilder.Build(cc,
                title: title, commandTemplate: template,
                new TextField("player", "Player (blank for self)")));
    }

    // -----------------------------------------------------------------------
    // Admin: Server sub-tab
    // -----------------------------------------------------------------------

    private void BuildKindredAdminServerTab(GameObject page)
    {
        RenderAdminInfoNote(page, "Kindred admin (Server)");
        AddAdminWarningIntro(page,
            "Server-wide admin commands. These affect global config, all " +
            "players, or persistent server state. Requires KindredCommands + " +
            "server admin permission.");

        // ---- General ------------------------------------------------------
        AddSpacer(page, 4);
        AddSectionHeading(page, "Global");

        var gr1 = AddKLRow(page, "KCAServerGen1");
        AddCommandButton(gr1, "Reveal map (all)", MessageService.BCCOM_KCA_REVEALMAP_ALL,
            "Reveal the map for every player on the server (.revealmapforallplayers).");
        AddCommandButton(gr1, "Clean lost shards",  MessageService.BCCOM_KCA_CLEAN_CONTAINERLESS_SHARDS,
            "Destroy all items that aren't currently in containers (.cleancontainerlessshards).");
        AddCommandButton(gr1, "Daywalker (all)",    MessageService.BCCOM_KCA_EVERYONE_DAYWALKER,
            "Toggle daywalker (sun immunity) for every player (.everyonedaywalker).");
        AddCommandButton(gr1, "Bat vision (all)",   MessageService.BCCOM_KCA_GLOBAL_BAT_VISION,
            "Toggle bat vision for every player (.globalbatvision).");

        CollapsibleSection.Build(page,
            title: "Set time of day (.settime)",
            startExpanded: false,
            tooltip: "Set the game world's time to a specific day and hour.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set time",
                commandTemplate: ".settime {day} {hour}",
                new IntField("day",  "Day",  min: 0, max: 9999),
                new IntField("hour", "Hour", min: 0, max: 23)));
        CollapsibleSection.Build(page,
            title: "Force respawn (.forcerespawn)",
            startExpanded: false,
            tooltip: "Force spawn chains (NPCs, resources) to respawn in a radius around you.",
            buildContent: c => FormBuilder.Build(c,
                title: "Force respawn",
                commandTemplate: ".forcerespawn {range}",
                new IntField("range", "Range", min: 1, max: 200,
                    tooltip: "Radius in world units. Default 10.")));

        // ---- Announce -----------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Announcements");

        var anRow = AddKLRow(page, "KCAAnnounce");
        AddCommandButton(anRow, "List", MessageService.BCCOM_KCA_ANNOUNCE_LIST,
            "List scheduled announcements (.announce list).");

        CollapsibleSection.Build(page,
            title: "Add announcement (.announce add)",
            startExpanded: false,
            tooltip: "Schedule a new chat announcement.",
            buildContent: c => FormBuilder.Build(c,
                title: "Add announcement",
                commandTemplate: ".announce add {name} {message} {time} {oneTime}",
                new TextField("name", "Name", tooltip: "Unique announcement key."),
                new TextField("message", "Message", tooltip: "Message text to post."),
                new TextField("time", "Time/interval",
                    tooltip: "Schedule expression - see KindredCommands docs."),
                new BoolField("oneTime", "One time only", defaultValue: false)));
        CollapsibleSection.Build(page,
            title: "Change announcement (.announce change)",
            startExpanded: false,
            tooltip: "Update an existing announcement by name.",
            buildContent: c => FormBuilder.Build(c,
                title: "Change announcement",
                commandTemplate: ".announce change {name} {message} {time} {oneTime}",
                new TextField("name", "Name"),
                new TextField("message", "Message"),
                new TextField("time", "Time/interval"),
                new BoolField("oneTime", "One time only", defaultValue: false)));
        CollapsibleSection.Build(page,
            title: "Remove announcement (.announce remove)",
            startExpanded: false,
            tooltip: "Remove an announcement by name.",
            buildContent: c => FormBuilder.Build(c,
                title: "Remove announcement",
                commandTemplate: ".announce remove {name}",
                new TextField("name", "Name")));

        // ---- Drop items ---------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Dropped items");

        var drRow = AddKLRow(page, "KCADrops");
        AddCommandButton(drRow, "Clear All",          MessageService.BCCOM_KCA_DROP_CLEAR_ALL,
            "Clear every dropped item in the world (.dropitems clearall).");
        AddCommandButton(drRow, "Clear All Shards",   MessageService.BCCOM_KCA_DROP_CLEAR_ALL_SHARDS,
            "Clear every dropped soulshard in the world (.dropitems clearallshards).");
        AddCommandButton(drRow, "Remove Lifetime",    MessageService.BCCOM_KCA_DROP_REMOVE_LIFETIME,
            "Remove the lifetime cap on dropped items (.dropitems removelifetime).");

        CollapsibleSection.Build(page,
            title: "Set drop lifetime (.dropitems lifetime)",
            startExpanded: false,
            tooltip: "Set how long dropped items persist (in seconds).",
            buildContent: c => FormBuilder.Build(c,
                title: "Set lifetime",
                commandTemplate: ".dropitems lifetime {seconds}",
                new IntField("seconds", "Seconds", min: 0, max: 86400)));
        CollapsibleSection.Build(page,
            title: "Set drop lifetime when disabled (.dropitems lifetimewhendisabled)",
            startExpanded: false,
            tooltip: "Lifetime applied while the dropped-item system is disabled.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set fallback lifetime",
                commandTemplate: ".dropitems lifetimewhendisabled {seconds}",
                new IntField("seconds", "Seconds", min: 0, max: 86400,
                    tooltip: "Default 300.")));
        CollapsibleSection.Build(page,
            title: "Set shard lifetime (.dropitems shardlifetime)",
            startExpanded: false,
            tooltip: "Set how long dropped soulshards persist (default 3600).",
            buildContent: c => FormBuilder.Build(c,
                title: "Set shard lifetime",
                commandTemplate: ".dropitems shardlifetime {seconds}",
                new IntField("seconds", "Seconds", min: 0, max: 86400)));
        CollapsibleSection.Build(page,
            title: "Clear drops in radius (.dropitems clear)",
            startExpanded: false,
            tooltip: "Clear dropped items within a radius around you.",
            buildContent: c => FormBuilder.Build(c,
                title: "Clear radius",
                commandTemplate: ".dropitems clear {radius}",
                new IntField("radius", "Radius", min: 1, max: 500)));
        CollapsibleSection.Build(page,
            title: "Clear shards in radius (.dropitems clearshards)",
            startExpanded: false,
            tooltip: "Clear dropped soulshards within a radius around you.",
            buildContent: c => FormBuilder.Build(c,
                title: "Clear shards radius",
                commandTemplate: ".dropitems clearshards {radius}",
                new IntField("radius", "Radius", min: 1, max: 500)));

        // ---- Region -------------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Regions");

        var rgRow = AddKLRow(page, "KCARegion");
        AddCommandButton(rgRow, "List Players", MessageService.BCCOM_KCA_REGION_LIST_PLAYERS,
            "List all players currently allowed to enter gated regions (.region listplayers).");

        AddSimpleTextCollapse(page, "Lock region (.region lock)",      ".region lock {region}",   "region", "Region name");
        AddSimpleTextCollapse(page, "Unlock region (.region unlock)",  ".region unlock {region}", "region", "Region name");
        CollapsibleSection.Build(page,
            title: "Gate region (.region gate)",
            startExpanded: false,
            tooltip: "Gate a region behind a player level requirement.",
            buildContent: c => FormBuilder.Build(c,
                title: "Gate region",
                commandTemplate: ".region gate {region} {level}",
                new TextField("region", "Region"),
                new IntField("level", "Level", min: 0, max: 100)));
        AddSimpleTextCollapse(page, "Ungate region (.region ungate)",  ".region ungate {region}", "region", "Region name");
        AddSimpleTextCollapse(page, "Allow player (.region allow)",    ".region allow {player}",  "player", "Player name");
        CollapsibleSection.Build(page,
            title: "Ban player from region (.region ban)",
            startExpanded: false,
            tooltip: "Ban a player from a specific region.",
            buildContent: c => FormBuilder.Build(c,
                title: "Ban from region",
                commandTemplate: ".region ban {player} {region}",
                new PlayerNameField("player", "Player"),
                new TextField("region", "Region")));
        CollapsibleSection.Build(page,
            title: "Unban player from region (.region unban)",
            startExpanded: false,
            tooltip: "Lift a region ban on a player.",
            buildContent: c => FormBuilder.Build(c,
                title: "Unban from region",
                commandTemplate: ".region unban {player} {region}",
                new PlayerNameField("player", "Player"),
                new TextField("region", "Region")));
        AddSimpleTextCollapse(page, "List region bans (.region listbans)", ".region listbans {region}", "region", "Region name");
        AddSimpleTextCollapse(page, "Remove from allowlist (.region remove)", ".region remove {player}", "player", "Player name");

        // ---- Boss locks ---------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Bosses (lock)");
        AddSimpleTextCollapse(page, "Lock boss (.boss lock)",            ".boss lock {boss}",           "boss", "V Blood boss name");
        AddSimpleTextCollapse(page, "Unlock boss (.boss unlock)",        ".boss unlock {boss}",         "boss", "V Blood boss name");
        AddSimpleTextCollapse(page, "Lock primal (.boss lockprimal)",    ".boss lockprimal {boss}",     "boss", "Primal boss name");
        AddSimpleTextCollapse(page, "Unlock primal (.boss unlockprimal)", ".boss unlockprimal {boss}", "boss", "Primal boss name");

        // ---- Gear (server) ------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Gear (server)");

        var grRow1 = AddKLRow(page, "KCAGearServer1");
        AddCommandButton(grRow1, "Headgear loss",   MessageService.BCCOM_KCA_GEAR_HEADGEAR,
            "Toggle whether players lose headgear on death (.gear headgear).");
        AddCommandButton(grRow1, "Shard flight",    MessageService.BCCOM_KCA_GEAR_SS_FLIGHT,
            "Toggle soulshard flight restrictions (.gear soulshardflight).");
        AddCommandButton(grRow1, "Shard drop mgmt", MessageService.BCCOM_KCA_GEAR_SS_DROP_MGMT,
            "Toggle KC-managed soulshard drops (.gear togglesoulsharddropmanagement).");
        AddCommandButton(grRow1, "Destroy shards",  MessageService.BCCOM_KCA_GEAR_DESTROY_ALL_SHARDS,
            "Destroy every soulshard currently in the world (.gear destroyallshards).");

        CollapsibleSection.Build(page,
            title: "Shard drop limit (.gear soulshardlimit)",
            startExpanded: false,
            tooltip: "Set how many soulshards of a given type can exist before drops stop.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set shard limit",
                commandTemplate: ".gear soulshardlimit {limit} {shardType}",
                new IntField("limit", "Limit", min: 0, max: 99),
                new TextField("shardType", "Shard type",
                    tooltip: "Type filter - blank or 'None' for all types.")));
        CollapsibleSection.Build(page,
            title: "Shard durability time (.gear soulsharddurabilitytime)",
            startExpanded: false,
            tooltip: "Set how long a soulshard lasts before its durability ticks down.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set shard durability time",
                commandTemplate: ".gear soulsharddurabilitytime {seconds}",
                new IntField("seconds", "Seconds", min: 1, max: 86400)));

        // ---- Clan rename --------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Clans");
        CollapsibleSection.Build(page,
            title: "Rename clan (.clan rename)",
            startExpanded: false,
            tooltip: "Admin-rename a clan, optionally specifying the leader.",
            buildContent: c => FormBuilder.Build(c,
                title: "Rename clan",
                commandTemplate: ".clan rename {oldClanName} {newClanName} {leaderName}",
                new TextField("oldClanName", "Old clan name"),
                new TextField("newClanName", "New clan name"),
                new TextField("leaderName", "Leader name (optional)",
                    tooltip: "Leave blank if not changing leadership.")));

        // ---- Prisoner -----------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Prisoners");
        CollapsibleSection.Build(page,
            title: "Set gruel transform chance (.prisoner gruel)",
            startExpanded: false,
            tooltip: "Adjust the chance and rate window of gruel transformations.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set gruel",
                commandTemplate: ".prisoner gruel {chance} {min} {max}",
                new TextField("chance", "Chance (0-1)"),
                new TextField("min", "Min value"),
                new TextField("max", "Max value")));
        CollapsibleSection.Build(page,
            title: "Set gruel transform prefab (.prisoner grueltransform)",
            startExpanded: false,
            tooltip: "Set the unit prefab a gruel-transformed prisoner becomes.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set gruel transform",
                commandTemplate: ".prisoner grueltransform {prefab}",
                new TextField("prefab", "Unit prefab")));
        CollapsibleSection.Build(page,
            title: "Set prisoner feed settings (.prisoner feed)",
            startExpanded: false,
            tooltip: "Adjust the per-feed ranges of health / misery / blood-quality changes.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set feed",
                commandTemplate: ".prisoner feed {feed} {healthMin} {healthMax} {miseryMin} {miseryMax} {qualityMin} {qualityMax}",
                new TextField("feed", "Feed name"),
                new TextField("healthMin", "Health min"),
                new TextField("healthMax", "Health max"),
                new TextField("miseryMin", "Misery min"),
                new TextField("miseryMax", "Misery max"),
                new TextField("qualityMin", "Quality min"),
                new TextField("qualityMax", "Quality max")));
        CollapsibleSection.Build(page,
            title: "Restore feed defaults (.prisoner feeddefault)",
            startExpanded: false,
            tooltip: "Reset a feed to its default config.",
            buildContent: c => FormBuilder.Build(c,
                title: "Restore feed defaults",
                commandTemplate: ".prisoner feeddefault {feed}",
                new TextField("feed", "Feed name")));

        // ---- Staff --------------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Staff");

        var stRow = AddKLRow(page, "KCAStaff");
        AddCommandButton(stRow, "Reload staff",      MessageService.BCCOM_KCA_STAFF_RELOAD_STAFF,
            "Reload the staff config from disk (.staff reloadstaff).");
        AddCommandButton(stRow, "Reload admin list", MessageService.BCCOM_KCA_STAFF_RELOAD_ADMIN,
            "Reload the admin list (.staff reloadadmin).");
        AddCommandButton(stRow, "Auto admin auth",   MessageService.BCCOM_KCA_STAFF_AUTO_ADMIN_AUTH,
            "Toggle whether you are on the auto-admin-auth list (.staff autoadminauth).");

        CollapsibleSection.Build(page,
            title: "Set staff rank (.staff setstaff)",
            startExpanded: false,
            tooltip: "Assign a staff rank to a player.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set staff",
                commandTemplate: ".staff setstaff {player} {rank}",
                new PlayerNameField("player", "Player"),
                new TextField("rank", "Rank")));
        CollapsibleSection.Build(page,
            title: "Remove staff rank (.staff removestaff)",
            startExpanded: false,
            tooltip: "Remove a player's staff rank.",
            buildContent: c => FormBuilder.Build(c,
                title: "Remove staff",
                commandTemplate: ".staff removestaff {player}",
                new PlayerNameField("player", "Player")));
        CollapsibleSection.Build(page,
            title: "Toggle admin (.staff toggleadmin)",
            startExpanded: false,
            tooltip: "Add or remove a player from the admin list.",
            buildContent: c => FormBuilder.Build(c,
                title: "Toggle admin",
                commandTemplate: ".staff toggleadmin {player}",
                new PlayerNameField("player", "Player")));

        // ---- Server wipe (DESTRUCTIVE 3-step flow) ----------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Server wipe (DESTRUCTIVE)");
        var wipeNote = UIFactory.CreateLabel(page, "WipeNote",
            "Wipe is a 3-step flow: (1) .wipe sets up the exclusion list (territories that survive), (2) .commencewipe ACTUALLY wipes, (3) .cancelwipe aborts before commence. There is no undo once you commence.",
            TMPro.TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(wipeNote.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 36, preferredHeight: 48, flexibleHeight: 0);
        wipeNote.TextMesh.fontStyle = TMPro.FontStyles.Italic;
        wipeNote.TextMesh.enableWordWrapping = true;
        wipeNote.TextMesh.overflowMode = TMPro.TextOverflowModes.Overflow;

        CollapsibleSection.Build(page,
            title: "Step 1: prepare wipe (.wipe)",
            startExpanded: false,
            tooltip: "Queue the wipe and tell the server which territory IDs to EXCLUDE (those keep their owners). Run .commencewipe afterwards to actually fire.",
            buildContent: c => FormBuilder.Build(c,
                title: "Prepare wipe",
                commandTemplate: ".wipe {territories}",
                new TextField("territories", "Excluded territory IDs", placeholder: "12,34,56",
                    tooltip: "Comma-separated territory IDs that should NOT be wiped.")));
        CollapsibleSection.Build(page,
            title: "Step 2: commence wipe (.commencewipe) — DESTRUCTIVE",
            startExpanded: false,
            tooltip: "Actually performs the wipe queued by .wipe. There is no undo. Requires the confirm checkbox.",
            buildContent: c => FormBuilder.Build(c,
                title: "Commence wipe",
                commandTemplate: ".commencewipe",
                new BoolField("confirm", "Yes, perform the wipe NOW",
                    tooltip: "Required. The confirm checkbox + Submit click is the only safety between you and a wiped server.",
                    requireTrue: true)));
        var wipeCancelRow = UIFactory.CreateHorizontalGroup(page, "WipeCancelRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(wipeCancelRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(wipeCancelRow, "Cancel queued wipe", MessageService.BCCOM_KCA_CANCEL_WIPE,
            "Aborts a wipe queued via .wipe before .commencewipe is called.");
    }

    // -----------------------------------------------------------------------
    // Admin: World sub-tab
    // -----------------------------------------------------------------------

    private void BuildKindredAdminWorldTab(GameObject page)
    {
        RenderAdminInfoNote(page, "Kindred admin (World)");
        AddAdminWarningIntro(page,
            "World manipulation commands - spawn units, teleport, search " +
            "prefabs, manage servants and castles. Acts on the world around " +
            "you (or supplied coordinates). Requires KindredCommands + server " +
            "admin permission.");

        AddSpacer(page, 4);
        AddSectionHeading(page, "Position");

        var posRow = AddKLRow(page, "KCAWorldPos");
        AddCommandButton(posRow, "Where am I",     MessageService.BCCOM_KCA_WHERE_AM_I,
            "Print your current position and territory (.whereami).");

        CollapsibleSection.Build(page,
            title: "Teleport horses to me (.teleporthorse)",
            startExpanded: false,
            tooltip: "Teleport all horses within radius to your position.",
            buildContent: c => FormBuilder.Build(c,
                title: "Teleport horses",
                commandTemplate: ".teleporthorse {radius}",
                new IntField("radius", "Radius", min: 1, max: 500,
                    tooltip: "Default 5.")));

        // ---- Lookups (find prefab IDs for spawn/give commands) -----------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Lookups (find IDs for spawn / give)");
        var lookupsHint = UIFactory.CreateLabel(page, "LookupsHint",
            "Use these to find the prefab name / ID for an item, NPC, or boss before using the Spawn or Give forms below. Replies appear in chat — V Rising's prefab registry isn't available client-side, so the search runs on the server and KindredCommands lists matching results.",
            TMPro.TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(lookupsHint.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 40, preferredHeight: 56, flexibleHeight: 0);
        lookupsHint.TextMesh.fontStyle = TMPro.FontStyles.Italic;
        lookupsHint.TextMesh.enableWordWrapping = true;
        lookupsHint.TextMesh.overflowMode = TMPro.TextOverflowModes.Overflow;

        // Quick boss-list button (no args). Same data feed as the player-facing
        // Boss List on the Kindred Commands tab; surfaced here so admins
        // doing world-spawning have the lookup at hand.
        var lookupsRow = UIFactory.CreateHorizontalGroup(page, "LookupsRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(lookupsRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(lookupsRow, "List All Bosses", MessageService.BCCOM_KC_BOSS_LIST,
            "List every boss prefab the server knows about (.boss list). Reply in chat. Use one of the names with the boss-modify / lock / teleportto forms or the spawn forms below.");

        CollapsibleSection.Build(page,
            title: "Search items (.search item)",
            startExpanded: false,
            tooltip: "Search items by name fragment. Reply lists every match with its prefab name — copy the name (e.g. 'Item_Ingredient_Iron') and paste into the .give form on the Players admin tab to spawn it. Page defaults to 1; KindredCommands paginates if there are many matches.",
            buildContent: c => FormBuilder.Build(c,
                title: "Search items",
                commandTemplate: ".search item {query} {page}",
                new TextField("query", "Search text", placeholder: "iron",
                    tooltip: "Substring of the item name. Case-insensitive."),
                new IntField("page", "Page", min: 1, max: 99,
                    tooltip: "Pagination — start at 1, increment if results say there's more.")));
        CollapsibleSection.Build(page,
            title: "Search NPCs / creatures (.search npc)",
            startExpanded: false,
            tooltip: "Search NPC + creature prefabs by name fragment. Reply lists every match — copy a 'CHAR_'-prefixed name and use it with .spawnnpc / .customspawn / .customspawnat / .servant change / .despawnnpc.",
            buildContent: c => FormBuilder.Build(c,
                title: "Search NPCs",
                commandTemplate: ".search npc {query} {page}",
                new TextField("query", "Search text", placeholder: "wolf",
                    tooltip: "Substring of the unit name. Case-insensitive."),
                new IntField("page", "Page", min: 1, max: 99)));

        // ---- Spawn --------------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Spawn");
        CollapsibleSection.Build(page,
            title: "Spawn NPC (.spawnnpc)",
            startExpanded: false,
            tooltip: "Spawn a number of NPCs at your position.",
            buildContent: c => FormBuilder.Build(c,
                title: "Spawn NPC",
                commandTemplate: ".spawnnpc {unit} {count} {level}",
                new TextField("unit", "Unit prefab",
                    tooltip: "CHAR_-prefixed prefab name. Use .search npc to find one."),
                new IntField("count", "Count", min: 1, max: 50),
                new IntField("level", "Level (-1 default)", min: -1, max: 200)));
        CollapsibleSection.Build(page,
            title: "Custom spawn at me (.customspawn)",
            startExpanded: false,
            tooltip: "Spawn an NPC at your position with full customization.",
            buildContent: c => FormBuilder.Build(c,
                title: "Custom spawn",
                commandTemplate: ".customspawn {unit} {type} {quality} {consumable} {duration} {level}",
                new TextField("unit", "Unit prefab"),
                new EnumField<PlayerStateService.BloodType>("type", "Blood type",
                    defaultValue: PlayerStateService.BloodType.Warrior),
                new IntField("quality",  "Blood quality", min: 0, max: 100),
                new BoolField("consumable", "Consumable", defaultValue: true),
                new IntField("duration", "Duration (-1 infinite)", min: -1, max: 86400),
                new IntField("level",    "Level (0 default)", min: 0, max: 200)));
        CollapsibleSection.Build(page,
            title: "Custom spawn at coords (.customspawnat)",
            startExpanded: false,
            tooltip: "Spawn an NPC at specific world coordinates with full customization.",
            buildContent: c => FormBuilder.Build(c,
                title: "Custom spawn at coords",
                commandTemplate: ".customspawnat {unit} {x} {y} {z} {type} {quality} {consumable} {duration} {level}",
                new TextField("unit", "Unit prefab"),
                new TextField("x", "X"),
                new TextField("y", "Y"),
                new TextField("z", "Z"),
                new EnumField<PlayerStateService.BloodType>("type", "Blood type",
                    defaultValue: PlayerStateService.BloodType.Warrior),
                new IntField("quality",  "Blood quality", min: 0, max: 100),
                new BoolField("consumable", "Consumable", defaultValue: true),
                new IntField("duration", "Duration (-1 infinite)", min: -1, max: 86400),
                new IntField("level",    "Level",  min: 0, max: 200)));
        CollapsibleSection.Build(page,
            title: "Despawn NPCs (.despawnnpc)",
            startExpanded: false,
            tooltip: "Despawn NPCs of a given prefab within a radius.",
            buildContent: c => FormBuilder.Build(c,
                title: "Despawn NPC",
                commandTemplate: ".despawnnpc {unit} {radius}",
                new TextField("unit", "Unit prefab"),
                new IntField("radius", "Radius", min: 1, max: 200,
                    tooltip: "Default 25.")));
        CollapsibleSection.Build(page,
            title: "Spawn horse (.spawnhorse)",
            startExpanded: false,
            tooltip: "Spawn one or more horses with custom stats.",
            buildContent: c => FormBuilder.Build(c,
                title: "Spawn horse",
                commandTemplate: ".spawnhorse {speed} {acceleration} {rotation} {num}",
                new TextField("speed", "Speed"),
                new TextField("acceleration", "Acceleration"),
                new TextField("rotation", "Rotation speed"),
                new IntField("num", "Count", min: 1, max: 20)));
        CollapsibleSection.Build(page,
            title: "Ban unit from spawning (.spawnban)",
            startExpanded: false,
            tooltip: "Prevent a unit prefab from spawning naturally, with a reason logged.",
            buildContent: c => FormBuilder.Build(c,
                title: "Spawn ban",
                commandTemplate: ".spawnban {unit} {reason}",
                new TextField("unit", "Unit prefab"),
                new TextField("reason", "Reason")));

        // ---- Boss (world: modify+teleport) -------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Bosses (world)");
        CollapsibleSection.Build(page,
            title: "Modify boss level (.boss modify)",
            startExpanded: false,
            tooltip: "Set the level of the nearest V Blood boss with the given name.",
            buildContent: c => FormBuilder.Build(c,
                title: "Modify boss",
                commandTemplate: ".boss modify {boss} {level}",
                new TextField("boss", "Boss name"),
                new IntField("level", "Level", min: 1, max: 200)));
        CollapsibleSection.Build(page,
            title: "Modify primal boss level (.boss modifyprimal)",
            startExpanded: false,
            tooltip: "Set the level of the nearest primal boss with the given name.",
            buildContent: c => FormBuilder.Build(c,
                title: "Modify primal boss",
                commandTemplate: ".boss modifyprimal {boss} {level}",
                new TextField("boss", "Boss name"),
                new IntField("level", "Level", min: 1, max: 200)));
        CollapsibleSection.Build(page,
            title: "Teleport to boss (.boss teleportto)",
            startExpanded: false,
            tooltip: "Teleport yourself to a named boss.",
            buildContent: c => FormBuilder.Build(c,
                title: "Teleport to boss",
                commandTemplate: ".boss teleportto {boss} {whichOne}",
                new TextField("boss", "Boss name"),
                new IntField("whichOne", "Index (0 default)", min: 0, max: 50)));

        // ---- Castle -------------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Castles");

        var castleRow = AddKLRow(page, "KCACastle");
        AddCommandButton(castleRow, "Relocate Reset",   MessageService.BCCOM_KCA_CASTLE_RELOCATE_RESET,
            "Clear the relocation cooldown on the current castle (.relocatereset).");
        AddCommandButton(castleRow, "Incoming Decay",   MessageService.BCCOM_KCA_CASTLE_INCOMING_DECAY,
            "List territories with the least castle time remaining (.castle incomingdecay).");
        AddCommandButton(castleRow, "Freeze Heart",     MessageService.BCCOM_KCA_CASTLE_FREEZE_HEART,
            "Freeze the castle heart you are targeting (stops decay) (.castle freezeheart).");
        AddCommandButton(castleRow, "Thaw Heart",       MessageService.BCCOM_KCA_CASTLE_THAW_HEART,
            "Resume decay on the castle heart you are targeting (.castle thawheart).");

        CollapsibleSection.Build(page,
            title: "Claim castle (.claim)",
            startExpanded: false,
            tooltip: "Claim the castle heart you are targeting for the specified player (self if blank).",
            buildContent: c => FormBuilder.Build(c,
                title: "Claim castle",
                commandTemplate: ".claim {player}",
                new TextField("player", "Player (blank for self)")));
        CollapsibleSection.Build(page,
            title: "List plots owned (.castle plotsowned)",
            startExpanded: false,
            tooltip: "List castle plots owned by each player.",
            buildContent: c => FormBuilder.Build(c,
                title: "Plots owned",
                commandTemplate: ".castle plotsowned {page}",
                new IntField("page", "Page", min: 1, max: 99)));
        CollapsibleSection.Build(page,
            title: "Frozen hearts (.castle frozenhearts)",
            startExpanded: false,
            tooltip: "List frozen (non-decaying) castle hearts.",
            buildContent: c => FormBuilder.Build(c,
                title: "Frozen hearts",
                commandTemplate: ".castle frozenhearts {page}",
                new IntField("page", "Page", min: 1, max: 99)));
        CollapsibleSection.Build(page,
            title: "Clan plots owned (.castle clanplotsowned)",
            startExpanded: false,
            tooltip: "List castle plots owned by each clan.",
            buildContent: c => FormBuilder.Build(c,
                title: "Clan plots owned",
                commandTemplate: ".castle clanplotsowned {page}",
                new IntField("page", "Page", min: 1, max: 99)));
        CollapsibleSection.Build(page,
            title: "Teleport to territory plot (.castle teleporttoplot)",
            startExpanded: false,
            tooltip: "Teleport to the castle heart of a specific territory.",
            buildContent: c => FormBuilder.Build(c,
                title: "Teleport to plot",
                commandTemplate: ".castle teleporttoplot {territoryIndex}",
                new IntField("territoryIndex", "Territory index", min: 0, max: 9999)));
        CollapsibleSection.Build(page,
            title: "Plot info (.castle plotinfo)",
            startExpanded: false,
            tooltip: "Print detailed info about a territory's plot.",
            buildContent: c => FormBuilder.Build(c,
                title: "Plot info",
                commandTemplate: ".castle plotinfo {territoryIndex}",
                new IntField("territoryIndex", "Territory index", min: 0, max: 9999)));

        // ---- Servant ------------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Servants");

        var srRow1 = AddKLRow(page, "KCAServant1");
        AddCommandButton(srRow1, "Convert",          MessageService.BCCOM_KCA_SERVANT_CONVERT,
            "Instantly convert the servant in the targeted coffin (.servant convert).");
        AddCommandButton(srRow1, "Perfect",          MessageService.BCCOM_KCA_SERVANT_PERFECT,
            "Make the targeted servant perfect (max expertise) (.servant perfect).");
        AddCommandButton(srRow1, "Heal",             MessageService.BCCOM_KCA_SERVANT_HEAL,
            "Cure the targeted servant of injuries (.servant heal).");
        AddCommandButton(srRow1, "Revive",           MessageService.BCCOM_KCA_SERVANT_REVIVE,
            "Revive the servant in the targeted coffin (.servant revive).");

        var srRow2 = AddKLRow(page, "KCAServant2");
        AddCommandButton(srRow2, "Complete Mission", MessageService.BCCOM_KCA_SERVANT_COMPLETE_MISSION,
            "Complete the active mission on the targeted servant (.servant completemission).");

        CollapsibleSection.Build(page,
            title: "Change servant in coffin (.servant change)",
            startExpanded: false,
            tooltip: "Replace the servant in the targeted coffin with a different unit prefab.",
            buildContent: c => FormBuilder.Build(c,
                title: "Change servant",
                commandTemplate: ".servant change {character}",
                new TextField("character", "Unit prefab")));
        CollapsibleSection.Build(page,
            title: "Add servant to coffin (.servant add)",
            startExpanded: false,
            tooltip: "Add a servant to the targeted empty coffin.",
            buildContent: c => FormBuilder.Build(c,
                title: "Add servant",
                commandTemplate: ".servant add {character}",
                new TextField("character", "Unit prefab")));

        // ---- Gear (world: range-based) -----------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Gear (radius)");
        CollapsibleSection.Build(page,
            title: "Repair all gear in radius (.gear repairall)",
            startExpanded: false,
            tooltip: "Repair every piece of gear worn by any player in the radius.",
            buildContent: c => FormBuilder.Build(c,
                title: "Repair all in radius",
                commandTemplate: ".gear repairall {range}",
                new IntField("range", "Range", min: 1, max: 200,
                    tooltip: "Default 10.")));
        CollapsibleSection.Build(page,
            title: "Break all gear in radius (.gear breakall)",
            startExpanded: false,
            tooltip: "Drop the gear durability of all players in the radius to zero.",
            buildContent: c => FormBuilder.Build(c,
                title: "Break all in radius",
                commandTemplate: ".gear breakall {range}",
                new IntField("range", "Range", min: 1, max: 200,
                    tooltip: "Default 10.")));

        // ---- Castle / Clan world queries (0.5.0 audit gaps) -------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Castle & Clan");
        var castleClanRow = UIFactory.CreateHorizontalGroup(page, "CastleClanRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(castleClanRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(castleClanRow, "Longest offline",  MessageService.BCCOM_KCA_LONGEST_OFFLINE_CASTLES,
            "List castles ranked by owner-last-online (.longestofflinecastles). Helps clean up dead bases.");
        AddCommandButton(castleClanRow, "Repair clan data", MessageService.BCCOM_KCA_CLAN_FIX,
            "Repair clan-data inconsistencies (.clan fix). Use if a player shows in a clan they're not actually in.");

        CollapsibleSection.Build(page,
            title: "List castles owned by a clan (.clan castles)",
            startExpanded: false,
            tooltip: "List the territories a specific clan owns. Reply appears in chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Clan castles",
                commandTemplate: ".clan castles {clan}",
                new TextField("clan", "Clan name")));

        // ---- Bloodbound items -------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Bloodbound items");
        CollapsibleSection.Build(page,
            title: "Add Bloodbound attribute (.bloodbound add)",
            startExpanded: false,
            tooltip: "Marks the named/prefab item as Bloodbound (drops only to its owner, etc.).",
            buildContent: c => FormBuilder.Build(c,
                title: "Add bloodbound",
                commandTemplate: ".bloodbound add {item}",
                new TextField("item", "Item prefab/name")));
        CollapsibleSection.Build(page,
            title: "Remove Bloodbound attribute (.bloodbound remove)",
            startExpanded: false,
            tooltip: "Removes the Bloodbound flag from the item.",
            buildContent: c => FormBuilder.Build(c,
                title: "Remove bloodbound",
                commandTemplate: ".bloodbound remove {item}",
                new TextField("item", "Item prefab/name")));
    }

    // -----------------------------------------------------------------------
    // Shared helpers for this file
    // -----------------------------------------------------------------------

    private static void AddAdminWarningIntro(GameObject page, string body)
    {
        // 0.10.12: wrap the intro paragraph in a card so it doesn't sit
        // flush with the panel border. Italic-muted styling pulls it back
        // visually so the forms below are the primary focus.
        var card = UIFactory.CreateVerticalGroup(page, "AdminIntroCard",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(8, 8, 10, 10),
            bgColor: Theme.CardBackground);
        UIFactory.SetLayoutElement(card,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, flexibleHeight: 0);

        // 0.10.13: dropped italic and bumped font size 12 → 13 for legibility.
        var intro = UIFactory.CreateLabel(card, "AdminIntro",
            $"<color={Theme.MutedBodyHex}>{body}</color>",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(intro.GameObject,
            minWidth: 320, preferredWidth: 380, flexibleWidth: 1,
            minHeight: 36, preferredHeight: 44, flexibleHeight: 0);
        intro.TextMesh.enableWordWrapping = true;
        intro.TextMesh.overflowMode = TextOverflowModes.Overflow;
        intro.TextMesh.fontStyle = FontStyles.Normal;
    }

    // For "fill in one string, fire command" cases.
    private static void AddSimpleTextCollapse(GameObject page, string title, string template,
                                              string fieldName, string fieldLabel)
    {
        CollapsibleSection.Build(page,
            title: title, startExpanded: false, tooltip: title,
            buildContent: c => FormBuilder.Build(c,
                title: title,
                commandTemplate: template,
                new TextField(fieldName, fieldLabel)));
    }
}
