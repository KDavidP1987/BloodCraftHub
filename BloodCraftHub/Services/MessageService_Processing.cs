using System.Collections.Generic;
using System.Text.RegularExpressions;
using BloodCraftHub.Utils;

namespace BloodCraftHub.Services;

// Companion partial to MessageService.cs.
//
// Holds:
//   1. BCCOM_* command-string constants (single source of truth - Bloodcraft
//      renames are a single-file edit).
//   2. The "legacy" inbound regex pipeline (Phase 3b): for chat replies
//      Bloodcraft doesn't ship via the structured Eclipse protocol -
//      .fam boxes lists, .fam l box-content lists, etc.
//      Eclipse-protocol messages are intercepted earlier by
//      EclipseProtocolService.TryHandleServerMessage. Anything that gets
//      here is plain colored chat from Bloodcraft.
//
// PORT REFERENCE for the regex code:
//   LearningMods/BloodCraftUI-master/BloodCraftUI/Services/MessageService_Processing.cs
public static partial class MessageService
{
    // =========================================================================
    // Command constants
    // =========================================================================

    // ---------- Familiar (.fam) ----------
    public const string BCCOM_FAM_BOXES                  = ".fam boxes";
    public const string BCCOM_FAM_LIST_CURRENT_BOX       = ".fam l";         // lists familiars in the currently-selected box
    public const string BCCOM_FAM_SWITCH_BOX_FORMAT      = ".fam cb {0}";    // {0} = box name
    public const string BCCOM_FAM_BIND_BY_INDEX_FORMAT   = ".fam b {0}";     // {0} = index within current box
    public const string BCCOM_FAM_ADDBOX_FORMAT          = ".fam ab {0}";    // {0} = new box name
    public const string BCCOM_FAM_DELETEBOX_FORMAT       = ".fam db {0}";    // {0} = box name (must be empty)
    public const string BCCOM_FAM_RENAMEBOX_FORMAT       = ".fam rb {0} {1}"; // {0} = current name, {1} = new name
    public const string BCCOM_FAM_MOVEBOX_FORMAT         = ".fam mb {0}";    // {0} = destination box; acts on currently bound familiar
    public const string BCCOM_FAM_REMOVE_FORMAT          = ".fam r {0}";     // {0} = index in current box; permanently deletes from collection
    public const string BCCOM_FAM_UNBIND                 = ".fam ub";       // DESTRUCTIVE: permanently destroys active familiar
    public const string BCCOM_FAM_TOGGLE                 = ".fam t";        // calls/dismisses (recallable)
    public const string BCCOM_FAM_COMBAT                 = ".fam c";        // toggle combat on/off
    public const string BCCOM_FAM_TOGGLE_EMOTES          = ".fam e";        // enable/disable emote-action bindings (e.g. clap = open inventory)
    public const string BCCOM_FAM_LIST_EMOTES            = ".fam actions";  // list current emote-action bindings
    public const string BCCOM_FAM_PRESTIGE               = ".fam pr";
    public const string BCCOM_FAM_GET_LEVEL              = ".fam gl";
    public const string BCCOM_FAM_ENABLE_EQUIP           = ".fam smartbind"; // legacy constant; smartbind takes a name (see BCCOM_FAM_SMARTBIND_FORMAT)

    // ---- 0.6.0 .fam audit additions ----
    public const string BCCOM_FAM_SEARCH_FORMAT          = ".fam s {0}";       // search boxes by name
    public const string BCCOM_FAM_SMARTBIND_FORMAT       = ".fam sb {0}";      // search and bind in one step
    public const string BCCOM_FAM_SHINY_FORMAT           = ".fam shiny {0}";   // make active familiar shiny ([SpellSchool] = blood/storm/unholy/chaos/frost/illusion)
    public const string BCCOM_FAM_OPTION_FORMAT          = ".fam option {0}";  // toggle a per-player familiar setting (e.g. shiny, vbloodemotes)
    public const string BCCOM_FAM_ECHOES_FORMAT          = ".fam echoes {0}";  // purchase exo reward via VBlood essence
    public const string BCCOM_FAM_RESET                  = ".fam reset";       // DESTRUCTIVE: destroy all entities in followerbuffer + clear active data

    // ---- 0.6.0 battle group commands ----
    public const string BCCOM_FAM_BG_LIST                = ".fam bgs";
    public const string BCCOM_FAM_BG_SHOW_FORMAT         = ".fam bg {0}";      // {0} = group name (optional)
    public const string BCCOM_FAM_BG_CHOOSE_FORMAT       = ".fam cbg {0}";     // {0} = group name
    public const string BCCOM_FAM_BG_ADD_FORMAT          = ".fam abg {0}";     // {0} = new group name
    public const string BCCOM_FAM_BG_SLOT_FORMAT         = ".fam sbg {0} {1}"; // {0} = group OR slot, {1} = slot if first arg was group
    public const string BCCOM_FAM_BG_DELETE_FORMAT       = ".fam dbg {0}";     // {0} = group name
    public const string BCCOM_FAM_BG_CHALLENGE_FORMAT    = ".fam challenge {0}"; // {0} = player name (optional)

    // ---------- Leveling (.lvl) ----------
    public const string BCCOM_LVL_GET            = ".lvl get";
    public const string BCCOM_LVL_LOG_TOGGLE     = ".lvl log";                  // toggle in-chat XP gain logging
    public const string BCCOM_LVL_IGNORE_FORMAT  = ".lvl ignore {0}";           // admin: add/remove player from shared-XP exclusion

    // ---------- Prestige (player-facing) ----------
    public const string BCCOM_PRESTIGE_LIST                = ".prestige l";
    public const string BCCOM_PRESTIGE_SYNC_BUFFS          = ".prestige sb";
    public const string BCCOM_PRESTIGE_TOGGLE_EXOFORM      = ".prestige exoform";
    public const string BCCOM_PRESTIGE_TOGGLE_SHROUD       = ".prestige shroud";
    // .prestige me/get/lb take a PrestigeType; .prestige sf takes an ExoformVariant.
    public const string BCCOM_PRESTIGE_ME_FORMAT           = ".prestige me {0}";
    public const string BCCOM_PRESTIGE_GET_FORMAT          = ".prestige get {0}";
    public const string BCCOM_PRESTIGE_LEADERBOARD_FORMAT  = ".prestige lb {0}";
    public const string BCCOM_PRESTIGE_SELECT_FORM_FORMAT  = ".prestige sf {0}";
    // ---- 0.7.0 prestige audit additions (admin-only) ----
    public const string BCCOM_PRESTIGE_IGNORE_LEADERBOARD_FORMAT = ".prestige ignore {0}"; // admin: toggle leaderboard exclusion for player
    // The "iacknowledge..." nuke is intentionally spelled out in full as a guard
    // against accidental copy/paste use. Only fires from the form with a confirm.
    public const string BCCOM_PRESTIGE_GLOBAL_BUFF_PURGE
        = ".prestige iacknowledgethiswillremoveallprestigebuffsfromplayersandwantthattohappen";

    // ---- 0.7.0 quest audit addition ----
    public const string BCCOM_QUEST_COMPLETE_FORMAT = ".quest c {0} {1}";  // admin: force-complete a quest for a player. {0}=player, {1}=Daily/Weekly

    // ---------- Blood legacy (.bl) ----------
    public const string BCCOM_BL_GET                 = ".bl get";
    public const string BCCOM_BL_GET_FORMAT          = ".bl get {0}";       // {0} = blood type name (queries any blood)
    public const string BCCOM_BL_LIST                = ".bl l";             // list legacy types
    public const string BCCOM_BL_LIST_STATS          = ".bl lst";           // list selectable bonus stats with indices
    public const string BCCOM_BL_RESET_STATS         = ".bl rst";           // reset chosen stats for current blood
    public const string BCCOM_BL_CHOOSE_STAT_FORMAT  = ".bl cst {0} {1}";   // {0} = blood type name, {1} = 1-based stat index

    // ---------- Profession (.prof) ----------
    // Player-facing 'log' / 'get' / 'list', admin 'set'. Use BloodcraftProfession
    // enum for the type argument so the dropdown emits valid names.
    public const string BCCOM_PROF_LOG_TOGGLE   = ".prof log";
    public const string BCCOM_PROF_GET_FORMAT   = ".prof get {0}";       // {0} = profession name (or blank)
    public const string BCCOM_PROF_LIST         = ".prof l";
    public const string BCCOM_PROF_SET_FORMAT   = ".prof set {0} {1} {2}"; // admin: {0}=player, {1}=profession, {2}=level

    // ---------- Misc (.misc) ----------
    public const string BCCOM_MISC_HEALTH         = ".misc health";
    // ---- 0.6.0 .misc audit additions (player-facing) ----
    public const string BCCOM_MISC_REMINDERS      = ".misc remindme";          // toggle general feature reminders
    public const string BCCOM_MISC_SCT_FORMAT     = ".misc sct {0}";           // toggle SCT element [Type]
    public const string BCCOM_MISC_KIT_ME         = ".misc kitme";             // claim starter kit
    public const string BCCOM_MISC_PREPARE        = ".misc prepare";           // complete GettingReadyForTheHunt
    public const string BCCOM_MISC_USER_STATS     = ".misc userstats";         // print neat player info
    public const string BCCOM_MISC_SILENCE        = ".misc silence";           // reset stuck combat music

    // ---------- Quests (.quest) ----------
    public const string BCCOM_QUEST_PROGRESS_DAILY  = ".quest p d";       // print daily quest objective
    public const string BCCOM_QUEST_PROGRESS_WEEKLY = ".quest p w";       // print weekly quest objective
    public const string BCCOM_QUEST_TRACK_DAILY     = ".quest t d";       // print location/direction to daily target
    public const string BCCOM_QUEST_TRACK_WEEKLY    = ".quest t w";       // print location/direction to weekly target
    public const string BCCOM_QUEST_REROLL_DAILY    = ".quest r d";       // reroll daily (costs configured item)
    public const string BCCOM_QUEST_REROLL_WEEKLY   = ".quest r w";       // reroll weekly (costs configured item)
    public const string BCCOM_QUEST_LOG_TOGGLE      = ".quest log";       // toggle in-chat progress logging

    // ---------- Class (.class) ----------
    public const string BCCOM_CLASS_LIST          = ".class l";
    public const string BCCOM_CLASS_LIST_SPELLS   = ".class lsp";
    public const string BCCOM_CLASS_LIST_STATS    = ".class lst";
    public const string BCCOM_CLASS_TOGGLE_SHIFT  = ".class shift";
    public const string BCCOM_CLASS_SELECT_FORMAT = ".class s {0}";   // {0} = enum name (BloodKnight, DemonHunter, ...)
    public const string BCCOM_CLASS_CHANGE_FORMAT = ".class c {0}";   // alias of select; some servers gate one or the other
    public const string BCCOM_CLASS_CHOOSE_SHIFT_FORMAT = ".class csp {0}"; // {0} = 1-based spell index from .class lsp output

    // ---------- Weapon expertise (.wep) ----------
    public const string BCCOM_WEP_GET                = ".wep get";
    public const string BCCOM_WEP_LIST               = ".wep l";
    public const string BCCOM_WEP_LIST_STATS         = ".wep lst";
    public const string BCCOM_WEP_RESET_STATS        = ".wep rst";
    public const string BCCOM_WEP_LOCK_SPELLS        = ".wep locksp";
    public const string BCCOM_WEP_CHOOSE_STAT_FORMAT = ".wep cst {0} {1}";  // {0} = weapon type name, {1} = 1-based stat index

    // =========================================================================
    // KindredLogistics commands (separate server mod, used in the KINDRED tab)
    // =========================================================================

    // ---------- Personal toggles (.l <flag>) — each toggles a per-player flag.
    public const string BCCOM_KL_SORT_STASH        = ".l ss";
    public const string BCCOM_KL_CRAFT_PULL        = ".l cr";
    public const string BCCOM_KL_DONT_PULL_LAST    = ".l dpl";
    public const string BCCOM_KL_AUTOSTASH_MISSION = ".l asm";
    public const string BCCOM_KL_CONVEYOR          = ".l co";
    public const string BCCOM_KL_SALVAGE           = ".l sal";
    public const string BCCOM_KL_UNIT_SPAWNER      = ".l us";
    public const string BCCOM_KL_BRAZIER           = ".l bz";
    public const string BCCOM_KL_SILENT_PULL       = ".l sp";
    public const string BCCOM_KL_SILENT_STASH      = ".l ssh";
    public const string BCCOM_KL_SETTINGS          = ".l s";

    // ---------- Utility commands (no group) — player-facing.
    public const string BCCOM_KL_STASH_ALL              = ".stash";
    public const string BCCOM_KL_PULL_ITEM_FORMAT       = ".pull {0} {1}";   // {0} = item name, {1} = qty
    public const string BCCOM_KL_FIND_ITEM_FORMAT       = ".fi {0}";         // {0} = item name
    public const string BCCOM_KL_FIND_CHEST_FORMAT      = ".fc {0}";         // {0} = chest name

    // ---------- Admin globals (.lg <flag>) — server-wide.
    public const string BCCOM_KL_ADMIN_SORT_STASH        = ".lg ss";
    public const string BCCOM_KL_ADMIN_PULL              = ".lg p";
    public const string BCCOM_KL_ADMIN_CRAFT_PULL        = ".lg cr";
    public const string BCCOM_KL_ADMIN_AUTOSTASH_MISSION = ".lg asm";
    public const string BCCOM_KL_ADMIN_CONVEYOR          = ".lg co";
    public const string BCCOM_KL_ADMIN_SALVAGE           = ".lg sal";
    public const string BCCOM_KL_ADMIN_UNIT_SPAWNER      = ".lg us";
    public const string BCCOM_KL_ADMIN_BRAZIER           = ".lg bz";
    public const string BCCOM_KL_ADMIN_NAMED_BRAZIER     = ".lg nam";
    public const string BCCOM_KL_ADMIN_TRASH             = ".lg trash";
    public const string BCCOM_KL_ADMIN_SETTINGS          = ".lg s";

    // ---------- Admin utility (no group) — admin-only.
    public const string BCCOM_KL_ADMIN_EMPTY_TRASH       = ".emptytrash";
    public const string BCCOM_KL_ADMIN_STASH_SPAWN_FORMAT = ".adminstash {0} {1}";

    // =========================================================================
    // KindredCommands - player-facing commands (Phase 5h)
    //
    // The admin command surface is huge (~120 commands) and lands in Phase 5i
    // under separate sub-tabs (Players / Server / World). This block covers
    // only the 13 commands a non-admin player will actually use.
    // =========================================================================

    // ---------- Self (no group) ----------
    public const string BCCOM_KC_AFK             = ".afk";
    public const string BCCOM_KC_PING            = ".ping";
    public const string BCCOM_KC_PACE            = ".pace";

    // ---------- Server info (zero-arg listings) ----------
    public const string BCCOM_KC_TIME            = ".time";
    public const string BCCOM_KC_STAFF           = ".staff";
    public const string BCCOM_KC_BOSS_LIST       = ".boss list";
    public const string BCCOM_KC_REGION_LIST     = ".region list";
    public const string BCCOM_KC_CASTLE_OPEN_PLOTS = ".openplots";  // not in castle group; top-level command (alias .op)
    public const string BCCOM_KC_GEAR_SOULSHARD_STATUS = ".gear soulshardstatus";
    public const string BCCOM_KC_CLAN_LIST       = ".clan list";

    // ---------- Lookups (forms) ----------
    public const string BCCOM_KC_CHECK_LEVEL_FORMAT   = ".checklevel {0}"; // {0} = player name
    public const string BCCOM_KC_CLAN_MEMBERS_FORMAT  = ".clan members {0}"; // {0} = clan name

    // =========================================================================
    // KindredCommands - admin commands (Phase 5i)
    //
    // ~146 admin commands surveyed and split across 3 sub-tabs (Players / Server
    // / World). Constants below are organized in the same shape so UI code can
    // walk them top-to-bottom.
    //
    // Many admin commands take an optional `player:OnlinePlayer=null` final arg
    // that defaults to self when omitted; those keep a trailing `{0}` slot so
    // the form can leave it blank for self-target.
    // =========================================================================

    // -------------------------------------------------------------------------
    // ADMIN: PLAYERS sub-tab
    // -------------------------------------------------------------------------

    // ---- General player commands (no group) ----
    public const string BCCOM_KCA_REVIVE_TARGET           = ".revivetarget";
    public const string BCCOM_KCA_RENAME_SELF_FORMAT      = ".rename {0}";                    // newName
    public const string BCCOM_KCA_RENAME_PLAYER_FORMAT    = ".rename {0} {1}";                // player, newName
    public const string BCCOM_KCA_UNBIND_PLAYER_FORMAT    = ".unbindplayer {0}";              // player
    public const string BCCOM_KCA_SWAP_PLAYERS_FORMAT     = ".swapplayers {0} {1}";           // player1, player2
    public const string BCCOM_KCA_UNLOCK_FORMAT           = ".unlock {0}";                    // player (optional)
    public const string BCCOM_KCA_REVEALMAP_FORMAT        = ".revealmap {0}";                 // player (optional)
    public const string BCCOM_KCA_TELEPORT_FORMAT         = ".teleport {0} {1} {2} {3}";      // x y z player
    public const string BCCOM_KCA_FLY_FORMAT              = ".fly {0}";                       // player (optional)
    public const string BCCOM_KCA_FLYUP_FORMAT            = ".flyup {0}";                     // player
    public const string BCCOM_KCA_FLYDOWN_FORMAT          = ".flydown {0}";                   // player
    public const string BCCOM_KCA_FLYLEVEL_FORMAT         = ".flylevel {0} {1}";              // floor, player
    public const string BCCOM_KCA_FLYHEIGHT_FORMAT        = ".flyheight {0}";                 // height (default 30)
    public const string BCCOM_KCA_FLY_OBSTACLE_HEIGHT_FORMAT = ".flyobstacleheight {0}";      // height (default 7)
    public const string BCCOM_KCA_KILL_PLAYER_FORMAT      = ".killplayer {0}";                // player
    public const string BCCOM_KCA_STAY_DOWN_FORMAT        = ".staydown {0}";                  // player
    public const string BCCOM_KCA_HEART_COUNT_FORMAT      = ".playerheartcount {0} {1}";      // amount, player
    public const string BCCOM_KCA_REVIVE_FORMAT           = ".revive {0}";                    // player (optional)
    public const string BCCOM_KCA_BUFF_FORMAT             = ".buff {0} {1} {2} {3}";          // buff, player, duration, immortal
    public const string BCCOM_KCA_DEBUFF_FORMAT           = ".debuff {0} {1}";                // buff, player
    public const string BCCOM_KCA_LISTBUFFS_FORMAT        = ".listbuffs {0}";                 // player (optional)
    public const string BCCOM_KCA_GIVE_FORMAT             = ".give {0} {1}";                  // item, quantity
    public const string BCCOM_KCA_BLOODPOTION_FORMAT      = ".bloodpotion {0} {1} {2}";       // type, quality, quantity
    public const string BCCOM_KCA_BLOODPOTION_MIX_FORMAT  = ".bloodpotionmix {0} {1} {2} {3} {4} {5}"; // pType,pQual,sType,sQual,sTrait,qty
    public const string BCCOM_KCA_GOD_FORMAT              = ".god {0}";                       // player
    public const string BCCOM_KCA_MORTAL_FORMAT           = ".mortal {0}";                    // player
    public const string BCCOM_KCA_SPECTATE_FORMAT         = ".spectate {0} {1}";              // player, returnToStart
    public const string BCCOM_KCA_RESET_COOLDOWN_FORMAT   = ".resetcooldown {0}";             // player (optional)

    // ---- Boost (bst) group ----
    public const string BCCOM_KCA_BST_PLAYERS                = ".bst players";
    public const string BCCOM_KCA_BST_STATE_FORMAT           = ".bst state {0}";
    public const string BCCOM_KCA_BST_ATTACK_SPEED_FORMAT    = ".bst attackspeed {0} {1}";    // speed, player
    public const string BCCOM_KCA_BST_REMOVE_ATTACK_SPEED_FORMAT = ".bst removeattackspeed {0}";
    public const string BCCOM_KCA_BST_DAMAGE_FORMAT          = ".bst damage {0} {1}";
    public const string BCCOM_KCA_BST_REMOVE_DAMAGE_FORMAT   = ".bst removedamage {0}";
    public const string BCCOM_KCA_BST_HEALTH_FORMAT          = ".bst health {0} {1}";
    public const string BCCOM_KCA_BST_REMOVE_HEALTH_FORMAT   = ".bst removehealth {0}";
    public const string BCCOM_KCA_BST_SPEED_FORMAT           = ".bst speed {0} {1}";
    public const string BCCOM_KCA_BST_REMOVE_SPEED_FORMAT    = ".bst removespeed {0}";
    public const string BCCOM_KCA_BST_YIELD_FORMAT           = ".bst yield {0} {1}";
    public const string BCCOM_KCA_BST_REMOVE_YIELD_FORMAT    = ".bst removeyield {0}";
    public const string BCCOM_KCA_BST_BAT_VISION_FORMAT      = ".bst batvision {0}";
    public const string BCCOM_KCA_BST_FLY_FORMAT             = ".bst fly {0}";
    public const string BCCOM_KCA_BST_NO_AGGRO_FORMAT        = ".bst noaggro {0}";
    public const string BCCOM_KCA_BST_NO_BLOOD_DRAIN_FORMAT  = ".bst noblooddrain {0}";
    public const string BCCOM_KCA_BST_NO_COOLDOWN_FORMAT     = ".bst nocooldown {0}";
    public const string BCCOM_KCA_BST_NO_DURABILITY_FORMAT   = ".bst nodurability {0}";
    public const string BCCOM_KCA_BST_IMMATERIAL_FORMAT      = ".bst immaterial {0}";
    public const string BCCOM_KCA_BST_INVINCIBLE_FORMAT      = ".bst invincible {0}";
    public const string BCCOM_KCA_BST_SHROUDED_FORMAT        = ".bst shrouded {0}";
    public const string BCCOM_KCA_BST_SUN_INVULNERABLE_FORMAT = ".bst suninvulnerable {0}";

    // ---- Gear group (player-targeting subset) ----
    public const string BCCOM_KCA_GEAR_REPAIR_FORMAT          = ".gear repair {0}";           // player (optional)
    public const string BCCOM_KCA_GEAR_BREAK_FORMAT           = ".gear break {0}";            // player (optional)
    public const string BCCOM_KCA_GEAR_SS_DURABILITY_FORMAT   = ".gear soulsharddurability {0} {1}"; // durability, player

    // ---- Clan group (player-targeting subset) ----
    public const string BCCOM_KCA_CLAN_ADD_FORMAT             = ".clan add {0} {1}";          // player, clanName
    public const string BCCOM_KCA_CLAN_KICK_FORMAT            = ".clan kick {0}";             // player
    public const string BCCOM_KCA_CLAN_CHANGE_ROLE_FORMAT     = ".clan changerole {0} {1}";   // player, role

    // -------------------------------------------------------------------------
    // ADMIN: SERVER sub-tab
    // -------------------------------------------------------------------------

    // ---- General server commands (no group) ----
    public const string BCCOM_KCA_REVEALMAP_ALL              = ".revealmapforallplayers";
    public const string BCCOM_KCA_CLEAN_CONTAINERLESS_SHARDS = ".cleancontainerlessshards";
    public const string BCCOM_KCA_EVERYONE_DAYWALKER         = ".everyonedaywalker";
    public const string BCCOM_KCA_GLOBAL_BAT_VISION          = ".globalbatvision";
    public const string BCCOM_KCA_SETTIME_FORMAT             = ".settime {0} {1}";            // day, hour
    public const string BCCOM_KCA_FORCE_RESPAWN_FORMAT       = ".forcerespawn {0}";           // range (default 10)

    // ---- Announce group ----
    public const string BCCOM_KCA_ANNOUNCE_LIST              = ".announce list";
    public const string BCCOM_KCA_ANNOUNCE_ADD_FORMAT        = ".announce add {0} {1} {2} {3}"; // name, message, time, oneTime
    public const string BCCOM_KCA_ANNOUNCE_CHANGE_FORMAT     = ".announce change {0} {1} {2} {3}";
    public const string BCCOM_KCA_ANNOUNCE_REMOVE_FORMAT     = ".announce remove {0}";        // name

    // ---- Drop items group ----
    public const string BCCOM_KCA_DROP_REMOVE_LIFETIME       = ".dropitems removelifetime";
    public const string BCCOM_KCA_DROP_CLEAR_ALL             = ".dropitems clearall";
    public const string BCCOM_KCA_DROP_CLEAR_ALL_SHARDS      = ".dropitems clearallshards";
    public const string BCCOM_KCA_DROP_LIFETIME_FORMAT       = ".dropitems lifetime {0}";      // seconds
    public const string BCCOM_KCA_DROP_LIFETIME_DISABLED_FORMAT = ".dropitems lifetimewhendisabled {0}";
    public const string BCCOM_KCA_DROP_SHARD_LIFETIME_FORMAT = ".dropitems shardlifetime {0}";
    public const string BCCOM_KCA_DROP_CLEAR_FORMAT          = ".dropitems clear {0}";        // radius
    public const string BCCOM_KCA_DROP_CLEAR_SHARDS_FORMAT   = ".dropitems clearshards {0}";  // radius

    // ---- Region group ----
    public const string BCCOM_KCA_REGION_LIST_PLAYERS        = ".region listplayers";
    public const string BCCOM_KCA_REGION_LOCK_FORMAT         = ".region lock {0}";            // region
    public const string BCCOM_KCA_REGION_UNLOCK_FORMAT       = ".region unlock {0}";
    public const string BCCOM_KCA_REGION_GATE_FORMAT         = ".region gate {0} {1}";        // region, level
    public const string BCCOM_KCA_REGION_UNGATE_FORMAT       = ".region ungate {0}";
    public const string BCCOM_KCA_REGION_ALLOW_FORMAT        = ".region allow {0}";           // player
    public const string BCCOM_KCA_REGION_BAN_FORMAT          = ".region ban {0} {1}";         // player, region
    public const string BCCOM_KCA_REGION_UNBAN_FORMAT        = ".region unban {0} {1}";
    public const string BCCOM_KCA_REGION_LIST_BANS_FORMAT    = ".region listbans {0}";        // region
    public const string BCCOM_KCA_REGION_REMOVE_FORMAT       = ".region remove {0}";          // player

    // ---- Boss group (server lock subset) ----
    public const string BCCOM_KCA_BOSS_LOCK_FORMAT           = ".boss lock {0}";              // boss
    public const string BCCOM_KCA_BOSS_UNLOCK_FORMAT         = ".boss unlock {0}";
    public const string BCCOM_KCA_BOSS_LOCK_PRIMAL_FORMAT    = ".boss lockprimal {0}";
    public const string BCCOM_KCA_BOSS_UNLOCK_PRIMAL_FORMAT  = ".boss unlockprimal {0}";

    // ---- Gear group (server subset) ----
    public const string BCCOM_KCA_GEAR_HEADGEAR              = ".gear headgear";
    public const string BCCOM_KCA_GEAR_SS_FLIGHT             = ".gear soulshardflight";
    public const string BCCOM_KCA_GEAR_SS_DROP_MGMT          = ".gear togglesoulsharddropmanagement";
    public const string BCCOM_KCA_GEAR_DESTROY_ALL_SHARDS    = ".gear destroyallshards";
    public const string BCCOM_KCA_GEAR_SS_LIMIT_FORMAT       = ".gear soulshardlimit {0} {1}"; // limit, shardType
    public const string BCCOM_KCA_GEAR_SS_DURATION_FORMAT    = ".gear soulsharddurabilitytime {0}"; // seconds

    // ---- Clan group (server rename) ----
    public const string BCCOM_KCA_CLAN_RENAME_FORMAT         = ".clan rename {0} {1} {2}";    // old, new, leader

    // ---- Prisoner group ----
    public const string BCCOM_KCA_PRISONER_GRUEL_FORMAT      = ".prisoner gruel {0} {1} {2}"; // chance, min, max
    public const string BCCOM_KCA_PRISONER_GRUEL_XFORM_FORMAT = ".prisoner grueltransform {0}";// prefab
    public const string BCCOM_KCA_PRISONER_FEED_FORMAT       = ".prisoner feed {0} {1} {2} {3} {4} {5} {6}"; // feed,hMin,hMax,mMin,mMax,qMin,qMax
    public const string BCCOM_KCA_PRISONER_FEED_DEFAULT_FORMAT = ".prisoner feeddefault {0}"; // feed

    // ---- Staff group ----
    public const string BCCOM_KCA_STAFF_RELOAD_STAFF         = ".staff reloadstaff";
    public const string BCCOM_KCA_STAFF_RELOAD_ADMIN         = ".staff reloadadmin";
    public const string BCCOM_KCA_STAFF_AUTO_ADMIN_AUTH      = ".staff autoadminauth";
    public const string BCCOM_KCA_STAFF_SET_STAFF_FORMAT     = ".staff setstaff {0} {1}";     // player, rank
    public const string BCCOM_KCA_STAFF_REMOVE_STAFF_FORMAT  = ".staff removestaff {0}";      // player
    public const string BCCOM_KCA_STAFF_TOGGLE_ADMIN_FORMAT  = ".staff toggleadmin {0}";      // player

    // -------------------------------------------------------------------------
    // ADMIN: WORLD sub-tab
    // -------------------------------------------------------------------------

    // ---- General world commands (no group) ----
    public const string BCCOM_KCA_WHERE_AM_I                 = ".whereami";
    public const string BCCOM_KCA_SPAWN_NPC_FORMAT           = ".spawnnpc {0} {1} {2}";       // unit, count, level
    public const string BCCOM_KCA_CUSTOM_SPAWN_FORMAT        = ".customspawn {0} {1} {2} {3} {4} {5}"; // unit,type,qual,consumable,duration,level
    public const string BCCOM_KCA_CUSTOM_SPAWN_AT_FORMAT     = ".customspawnat {0} {1} {2} {3} {4} {5} {6} {7} {8}"; // unit,x,y,z,type,qual,consumable,duration,level
    public const string BCCOM_KCA_DESPAWN_NPC_FORMAT         = ".despawnnpc {0} {1}";         // unit, radius
    public const string BCCOM_KCA_SPAWN_HORSE_FORMAT         = ".spawnhorse {0} {1} {2} {3}"; // speed, accel, rotation, num
    public const string BCCOM_KCA_SPAWN_BAN_FORMAT           = ".spawnban {0} {1}";           // unit, reason
    public const string BCCOM_KCA_TELEPORT_HORSE_FORMAT      = ".teleporthorse {0}";          // radius

    // ---- Search group ----
    public const string BCCOM_KCA_SEARCH_ITEM_FORMAT         = ".search item {0} {1}";        // query, page
    public const string BCCOM_KCA_SEARCH_NPC_FORMAT          = ".search npc {0} {1}";

    // ---- Boss group (world: modify+teleport) ----
    public const string BCCOM_KCA_BOSS_MODIFY_FORMAT         = ".boss modify {0} {1}";
    public const string BCCOM_KCA_BOSS_MODIFY_PRIMAL_FORMAT  = ".boss modifyprimal {0} {1}";
    public const string BCCOM_KCA_BOSS_TELEPORT_TO_FORMAT    = ".boss teleportto {0} {1}";    // boss, whichOne

    // ---- Castle group ----
    public const string BCCOM_KCA_CASTLE_RELOCATE_RESET      = ".relocatereset";
    public const string BCCOM_KCA_CASTLE_INCOMING_DECAY      = ".castle incomingdecay";
    public const string BCCOM_KCA_CASTLE_FREEZE_HEART        = ".castle freezeheart";
    public const string BCCOM_KCA_CASTLE_THAW_HEART          = ".castle thawheart";
    public const string BCCOM_KCA_CASTLE_CLAIM_FORMAT        = ".claim {0}";                  // player (optional)
    public const string BCCOM_KCA_CASTLE_PLOTS_OWNED_FORMAT  = ".castle plotsowned {0}";      // page
    public const string BCCOM_KCA_CASTLE_FROZEN_HEARTS_FORMAT = ".castle frozenhearts {0}";   // page
    public const string BCCOM_KCA_CASTLE_CLAN_PLOTS_OWNED_FORMAT = ".castle clanplotsowned {0}"; // page
    public const string BCCOM_KCA_CASTLE_TELEPORT_PLOT_FORMAT = ".castle teleporttoplot {0}";  // territoryIndex
    public const string BCCOM_KCA_CASTLE_PLOT_INFO_FORMAT    = ".castle plotinfo {0}";

    // ---- Servant group ----
    public const string BCCOM_KCA_SERVANT_CONVERT            = ".servant convert";
    public const string BCCOM_KCA_SERVANT_PERFECT            = ".servant perfect";
    public const string BCCOM_KCA_SERVANT_HEAL               = ".servant heal";
    public const string BCCOM_KCA_SERVANT_REVIVE             = ".servant revive";
    public const string BCCOM_KCA_SERVANT_COMPLETE_MISSION   = ".servant completemission";
    public const string BCCOM_KCA_SERVANT_CHANGE_FORMAT      = ".servant change {0}";         // character
    public const string BCCOM_KCA_SERVANT_ADD_FORMAT         = ".servant add {0}";

    // ---- Gear group (world: range-based) ----
    public const string BCCOM_KCA_GEAR_REPAIR_ALL_FORMAT     = ".gear repairall {0}";         // range
    public const string BCCOM_KCA_GEAR_BREAK_ALL_FORMAT      = ".gear breakall {0}";

    // ---------- 0.5.0 audit additions ----------
    // Player-info / lookup (all admin-only despite reading what feels like user info)
    public const string BCCOM_KCA_PLAYERINFO_FORMAT          = ".playerinfo {0}";             // player name
    public const string BCCOM_KCA_IDCHECK_FORMAT             = ".idcheck {0}";                // steamID
    public const string BCCOM_KCA_ASSIGN_STEAMID_FORMAT      = ".assignsteamID {0} {1}";      // player, steamID
    public const string BCCOM_KCA_LONGEST_OFFLINE_CASTLES    = ".longestofflinecastles";
    public const string BCCOM_KCA_SHOW_HAIR_FORMAT           = ".showhair {0}";               // player (optional)
    public const string BCCOM_KCA_UNBIND_ALL                 = ".unbindall";                  // DESTRUCTIVE: rename + unbind every player
    // Wipe orchestration (3-step: queue, commence, cancel)
    public const string BCCOM_KCA_WIPE_FORMAT                = ".wipe {0}";                   // comma-separated territory IDs to exclude
    public const string BCCOM_KCA_COMMENCE_WIPE              = ".commencewipe";               // DESTRUCTIVE: actually wipes
    public const string BCCOM_KCA_CANCEL_WIPE                = ".cancelwipe";
    // Clan
    public const string BCCOM_KCA_CLAN_CASTLES_FORMAT        = ".clan castles {0}";           // clan name
    public const string BCCOM_KCA_CLAN_FIX                   = ".clan fix";
    // Prisoner config readouts (paired with the existing .prisoner gruel/feed setters)
    public const string BCCOM_KCA_GRUEL_SETTINGS             = ".gruelsettings";
    public const string BCCOM_KCA_FEED_SETTINGS              = ".feedsettings";
    // Bloodbound item-attribute management
    public const string BCCOM_KCA_BLOODBOUND_ADD_FORMAT      = ".bloodbound add {0}";         // item descriptor (prefab/name)
    public const string BCCOM_KCA_BLOODBOUND_REMOVE_FORMAT   = ".bloodbound remove {0}";

    // =========================================================================
    // Inbound regex pipeline (Phase 3b)
    // =========================================================================

    /// <summary>
    /// What kind of inbound chat response we're currently expecting from a
    /// previously-sent command. Idle = no pending request.
    /// </summary>
    private enum InterceptFlag
    {
        Idle,
        AwaitingBoxList,
        ReceivingBoxList,
        AwaitingBoxContent,
        ReceivingBoxContent,
        AwaitingPrestigeInfo,
        ReceivingPrestigeInfo,
        AwaitingBloodInfo,
        ReceivingBloodInfo,
        // 0.8.3: generic capture for read-data commands that don't have a
        // dedicated structured parse. See PlayerStateService.LastResponse.
        AwaitingGenericResponse,
        ReceivingGenericResponse,
    }

    private static InterceptFlag _intercept = InterceptFlag.Idle;
    private static readonly List<string> _boxListBuffer = new();
    private static readonly List<PlayerStateService.FamiliarBoxEntry> _boxContentBuffer = new();
    // Per-query buffer for the prestige info display; reset on each
    // .prestige get send so a stale query never leaks into a fresh one.
    private static PlayerStateService.PrestigeInfo _prestigeInfoBuffer;
    private static PlayerStateService.BloodInfo    _bloodInfoBuffer;
    // 0.8.3: generic capture buffer + the originating command so subscribers
    // can show "last response from `.wep get`: ..." if useful.
    private static readonly List<string> _genericResponseBuffer = new();
    private static string _genericResponseCommand = "";
    // load-bearing: tracks last time a "useful" line for the current intercept
    // arrived. Per-frame TickInterceptTimeouts() flushes the buffered list when
    // this gets too stale - covers the case where Bloodcraft sends multiple
    // batches of box/familiar lines and then no further chat noise to act as
    // a terminator. Older code waited for "first non-color line"; that triggered
    // both too late (UI hangs forever if no other system message ever arrives)
    // and too early (any unrelated system announcement landing mid-list flushed
    // empty/partial state).
    private static double _interceptLastLineTime;
    private const double INTERCEPT_FLUSH_AFTER_SECONDS = 0.6;

    private const string BOX_LIST_HEADER          = "Familiar Boxes";
    private const string BOX_SELECTED_HEADER      = "Box Selected";
    private const string BOX_NAME_REGEX           = @"<color=[^>]+>(?<box>[^<]+)</color>";

    // Prestige info reply parsing. Bloodcraft sends 4-5 lines starting with a
    // "<TYPE> Prestige Info:" header. Keep regexes loose: capture what we can,
    // fall through to "raw text minus color tags" for everything else so the
    // user can read it in the UI even when the format drifts between versions.
    private static readonly Regex _prestigeHeaderRegex = new(
        @"<color=#90EE90>(?<type>[^<]+)</color>\s+Prestige Info:",
        RegexOptions.Compiled);
    private static readonly Regex _prestigeLevelRegex = new(
        @"Current Prestige Level:\s*<color=yellow>(?<level>\d+)</color>/(?<max>\d+)",
        RegexOptions.Compiled);
    // Strip any TMPro color/size/bold markup so the effect lines render cleanly
    // in the UI without inline tags.
    private static readonly Regex _stripTmpTagsRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    // Blood-info reply parsing. Bloodcraft sends 1 main info line + N stat lines:
    //   "You're level [<color=white>{lvl}</color>][<color=#90EE90>{prestige}</color>]
    //    with <color=yellow>{essence}</color> <color=#FFC0CB>essence</color>
    //    (<color=white>{pct}%</color>) in <color=red>{type}</color>!"
    //   "<color=red>{type}</color> Stats: <color=#00FFFF>{stat}</color>: <color=white>{val}</color>, ..."
    private static readonly Regex _bloodHeaderRegex = new(
        @"You're level \[<color=white>(?<level>\d+)</color>\]\[<color=#90EE90>(?<prestige>\d+)</color>\] with <color=yellow>(?<essence>[^<]+)</color> <color=#FFC0CB>essence</color> \(<color=white>(?<pct>[^<%]+)%?</color>\) in <color=red>(?<type>[^<]+)</color>",
        RegexOptions.Compiled);
    private static readonly Regex _bloodStatLineRegex = new(
        @"^<color=red>[^<]+</color>\s+Stats:",
        RegexOptions.Compiled);
    // Bloodcraft v1.13.x .fam l per-familiar line. Full format from FamiliarCommands.cs:
    //   <color=yellow>{idx}</color>| <color=green>{name}</color>[<color=#XYZ>*</color>] [<color=white>{level}</color>][<color=#90EE90>{prestige}</color>]
    // The shiny marker (<color=#XYZ>*</color>) and the prestige bracket are optional.
    // Capturing all of them so the UI can show level / prestige / shiny next to the name.
    private const string BOX_CONTENT_ENTRY_REGEX  =
        @"<color=yellow>(?<idx>\d+)</color>\|\s*" +
        @"<color=(?<color>[^>]+)>(?<name>[^<]+)</color>" +
        @"(?:<color=(?<shiny>[^>]+)>\*</color>)?" +
        @"\s*\[<color=[^>]+>(?<level>\d+)</color>\]" +
        @"(?:\[<color=[^>]+>(?<prestige>\d+)</color>\])?";

    private static readonly Regex _boxNameRegex         = new(BOX_NAME_REGEX,          RegexOptions.Compiled);
    private static readonly Regex _boxContentEntryRegex = new(BOX_CONTENT_ENTRY_REGEX, RegexOptions.Compiled);

    /// <summary>
    /// Called from EnqueueMessage / SendRaw when our UI dispatches a command we
    /// know triggers a parseable reply. Sets the intercept flag so the next
    /// matching inbound chat lines get routed to PlayerStateService.
    /// </summary>
    internal static void NoteOutboundForIntercept(string command)
    {
        if (string.IsNullOrEmpty(command)) return;

        if (command.Equals(BCCOM_FAM_BOXES, System.StringComparison.Ordinal))
        {
            _intercept = InterceptFlag.AwaitingBoxList;
            _boxListBuffer.Clear();
            _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            LogUtils.LogInfo("Intercept armed: AwaitingBoxList");
        }
        else if (command.Equals(BCCOM_FAM_LIST_CURRENT_BOX, System.StringComparison.Ordinal))
        {
            _intercept = InterceptFlag.AwaitingBoxContent;
            _boxContentBuffer.Clear();
            _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            LogUtils.LogInfo("Intercept armed: AwaitingBoxContent");
        }
        else if (command.StartsWith(".prestige get ", System.StringComparison.Ordinal))
        {
            _intercept = InterceptFlag.AwaitingPrestigeInfo;
            _prestigeInfoBuffer = new PlayerStateService.PrestigeInfo
            {
                EffectLines = new System.Collections.Generic.List<string>(),
            };
            _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            LogUtils.LogInfo("Intercept armed: AwaitingPrestigeInfo");
        }
        // Intercept .bl get with an explicit type (e.g. ".bl get Warrior").
        // Skip the no-arg ".bl get" form because the live Eclipse stream
        // already keeps PlayerStateService.Legacy current for the equipped
        // blood — intercepting that would clobber the structured display
        // with a partial parse on every refresh button press.
        else if (command.Length > ".bl get ".Length
              && command.StartsWith(".bl get ", System.StringComparison.Ordinal))
        {
            _intercept = InterceptFlag.AwaitingBloodInfo;
            _bloodInfoBuffer = new PlayerStateService.BloodInfo
            {
                StatLines = new System.Collections.Generic.List<string>(),
            };
            _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            LogUtils.LogInfo("Intercept armed: AwaitingBloodInfo");
        }
        // Fallback: arm the generic capture for known read-data commands so
        // their replies land in the UI's "Last server response" sections
        // instead of only the chat box. Added in 0.8.3 in response to friend-
        // testing feedback ("it would load into the chat window rather than
        // loading into the UI informational box"). Specific intercepts above
        // (.fam boxes / .fam l / .prestige get / .bl get) still take priority.
        else if (ShouldArmGenericCapture(command))
        {
            _intercept = InterceptFlag.AwaitingGenericResponse;
            _genericResponseBuffer.Clear();
            _genericResponseCommand = command;
            _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
            LogUtils.LogInfo($"Intercept armed: AwaitingGenericResponse ('{command}')");
        }
    }

    /// <summary>True for chat commands whose reply users expect to see in the
    /// UI panel rather than only in chat. Drives the generic capture fallback
    /// added in 0.8.3. Specific structured intercepts (.fam boxes / .fam l /
    /// .prestige get / .bl get) are handled separately above and shouldn't
    /// reach this list.</summary>
    private static bool ShouldArmGenericCapture(string command)
    {
        if (string.IsNullOrEmpty(command)) return false;

        // Read-data commands — explicit prefix list keeps this conservative.
        // Side-effect commands (.fam b N, .lvl set X Y, .giveset, etc.) should
        // NOT arm the capture because their server reply is a transient
        // confirmation, not info the user wants to study in a panel.
        return command.StartsWith(".fam pr",         System.StringComparison.Ordinal)
            || command.StartsWith(".fam actions",    System.StringComparison.Ordinal)
            || command.StartsWith(".fam bgs",        System.StringComparison.Ordinal)
            || command.StartsWith(".fam bg ",        System.StringComparison.Ordinal)
            || command.StartsWith(".prestige l",     System.StringComparison.Ordinal)
            || command.StartsWith(".prestige lb ",   System.StringComparison.Ordinal)
            || command.StartsWith(".bl l",           System.StringComparison.Ordinal) // .bl l + .bl lst
            || command.StartsWith(".wep get",        System.StringComparison.Ordinal)
            || command.StartsWith(".wep l",          System.StringComparison.Ordinal) // .wep l + .wep lst
            || command.StartsWith(".lvl get",        System.StringComparison.Ordinal)
            || command.StartsWith(".class l",        System.StringComparison.Ordinal) // .class l + .class lsp + .class lst
            || command.StartsWith(".prof l",         System.StringComparison.Ordinal)
            || command.StartsWith(".prof get",       System.StringComparison.Ordinal)
            || command.StartsWith(".misc userstats", System.StringComparison.Ordinal)
            || command.StartsWith(".misc health",    System.StringComparison.Ordinal)
            || command.StartsWith(".misc remindme",  System.StringComparison.Ordinal)
            || command.StartsWith(".quest p ",       System.StringComparison.Ordinal)
            || command.StartsWith(".quest t ",       System.StringComparison.Ordinal)
            || command.StartsWith(".checklevel",     System.StringComparison.Ordinal)
            || command.StartsWith(".clan list",      System.StringComparison.Ordinal)
            || command.StartsWith(".clan members",   System.StringComparison.Ordinal)
            || command.StartsWith(".boss list",      System.StringComparison.Ordinal)
            || command.StartsWith(".region list",    System.StringComparison.Ordinal)
            || command.StartsWith(".openplots",      System.StringComparison.Ordinal)
            || command.StartsWith(".staff",          System.StringComparison.Ordinal)
            || command.StartsWith(".time",           System.StringComparison.Ordinal)
            || command.StartsWith(".gear soulshardstatus", System.StringComparison.Ordinal)
            || command.StartsWith(".fc ",            System.StringComparison.Ordinal)
            || command.StartsWith(".search item ",   System.StringComparison.Ordinal)
            || command.StartsWith(".search npc ",    System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Process an inbound chat-server message text. Returns true if the message
    /// was consumed by the pipeline and the caller should destroy the entity
    /// (so it doesn't appear in the player's chat window).
    /// </summary>
    public static bool HandleInboundChat(string text)
    {
        if (_intercept == InterceptFlag.Idle || string.IsNullOrEmpty(text)) return false;

        try
        {
            switch (_intercept)
            {
                case InterceptFlag.AwaitingBoxList:
                    if (text.StartsWith(BOX_LIST_HEADER, System.StringComparison.Ordinal))
                    {
                        _intercept = InterceptFlag.ReceivingBoxList;
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        return Config.Settings.ClearServerMessages; // consume header line if hiding chat
                    }
                    return false;

                case InterceptFlag.ReceivingBoxList:
                    if (text.StartsWith("<color", System.StringComparison.Ordinal))
                    {
                        foreach (Match m in _boxNameRegex.Matches(text))
                        {
                            var name = m.Groups["box"].Value;
                            if (!string.IsNullOrEmpty(name) && !_boxListBuffer.Contains(name))
                                _boxListBuffer.Add(name);
                        }
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        return Config.Settings.ClearServerMessages;
                    }
                    // Non-color line in the middle: ignore (don't flush yet).
                    // Some other system announcement arriving between batches
                    // would otherwise truncate our list. The timeout in
                    // TickInterceptTimeouts() handles end-of-list.
                    return false;

                case InterceptFlag.AwaitingPrestigeInfo:
                {
                    var headerMatch = _prestigeHeaderRegex.Match(text);
                    if (headerMatch.Success)
                    {
                        _prestigeInfoBuffer.TypeName = headerMatch.Groups["type"].Value;
                        _intercept = InterceptFlag.ReceivingPrestigeInfo;
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        return Config.Settings.ClearServerMessages;
                    }
                    return false;
                }

                case InterceptFlag.ReceivingPrestigeInfo:
                {
                    var levelMatch = _prestigeLevelRegex.Match(text);
                    if (levelMatch.Success)
                    {
                        _prestigeInfoBuffer.Level    = PlayerStateService.ParseInt(levelMatch.Groups["level"].Value);
                        _prestigeInfoBuffer.MaxLevel = PlayerStateService.ParseInt(levelMatch.Groups["max"].Value);
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        return Config.Settings.ClearServerMessages;
                    }
                    // Any subsequent line until timeout is treated as an "effect"
                    // line (growth-rate change, stat bonus improvement, etc.).
                    // Strip color/markup so the UI label renders cleanly.
                    var clean = _stripTmpTagsRegex.Replace(text, "").Trim();
                    if (!string.IsNullOrEmpty(clean))
                    {
                        _prestigeInfoBuffer.EffectLines.Add(clean);
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        return Config.Settings.ClearServerMessages;
                    }
                    return false;
                }

                case InterceptFlag.AwaitingBloodInfo:
                {
                    var hm = _bloodHeaderRegex.Match(text);
                    if (hm.Success)
                    {
                        _bloodInfoBuffer.BloodType   = hm.Groups["type"].Value;
                        _bloodInfoBuffer.Level       = PlayerStateService.ParseInt(hm.Groups["level"].Value);
                        _bloodInfoBuffer.Prestige    = PlayerStateService.ParseInt(hm.Groups["prestige"].Value);
                        _bloodInfoBuffer.Essence     = hm.Groups["essence"].Value;
                        _bloodInfoBuffer.ProgressPct = hm.Groups["pct"].Value;
                        _intercept = InterceptFlag.ReceivingBloodInfo;
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        return Config.Settings.ClearServerMessages;
                    }
                    return false;
                }

                case InterceptFlag.ReceivingBloodInfo:
                {
                    if (_bloodStatLineRegex.IsMatch(text))
                    {
                        var clean = _stripTmpTagsRegex.Replace(text, "").Trim();
                        if (!string.IsNullOrEmpty(clean))
                            _bloodInfoBuffer.StatLines.Add(clean);
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        return Config.Settings.ClearServerMessages;
                    }
                    return false;
                }

                case InterceptFlag.AwaitingGenericResponse:
                case InterceptFlag.ReceivingGenericResponse:
                {
                    // Capture any color-tagged server line. Bloodcraft / Kindred
                    // helpers always wrap their reply text in <color=...> tags;
                    // plain unstyled lines tend to be unrelated system chatter
                    // (player joins, broadcast etc.) and would just be noise.
                    if (text.StartsWith("<color", System.StringComparison.Ordinal))
                    {
                        _intercept = InterceptFlag.ReceivingGenericResponse;
                        _genericResponseBuffer.Add(text);
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        // 0.8.3: never consume the chat copy here. The user's
                        // ClearServerMessages setting is meant for the
                        // structured intercepts above where the UI display
                        // fully replaces the chat copy. The generic capture is
                        // additive — we mirror to UI but keep the chat line
                        // so terminology like "<color>+5</color> XP gained!"
                        // still scrolls in the chat history.
                        return false;
                    }
                    return false;
                }

                case InterceptFlag.AwaitingBoxContent:
                case InterceptFlag.ReceivingBoxContent:
                    var match = _boxContentEntryRegex.Match(text);
                    if (match.Success)
                    {
                        _intercept = InterceptFlag.ReceivingBoxContent;
                        var shinyGroup = match.Groups["shiny"];
                        var prestigeGroup = match.Groups["prestige"];
                        var levelGroup = match.Groups["level"];
                        var entry = new PlayerStateService.FamiliarBoxEntry
                        {
                            Index         = PlayerStateService.ParseInt(match.Groups["idx"].Value),
                            ColorHex      = match.Groups["color"].Value,
                            Name          = match.Groups["name"].Value,
                            Level         = levelGroup.Success    ? PlayerStateService.ParseInt(levelGroup.Value)    : 0,
                            Prestige      = prestigeGroup.Success ? PlayerStateService.ParseInt(prestigeGroup.Value) : 0,
                            IsShiny       = shinyGroup.Success,
                            ShinyColorHex = shinyGroup.Success    ? shinyGroup.Value : null,
                        };
                        if (!_boxContentBuffer.Exists(e => e.Index == entry.Index))
                            _boxContentBuffer.Add(entry);
                        _interceptLastLineTime = UnityEngine.Time.realtimeSinceStartupAsDouble;
                        return Config.Settings.ClearServerMessages;
                    }
                    // Non-entry line in the middle: ignore (don't flush yet).
                    // The timeout in TickInterceptTimeouts() handles end-of-list.
                    return false;

                default:
                    return false;
            }
        }
        catch (System.Exception ex)
        {
            LogUtils.LogError($"MessageService.HandleInboundChat parse failed: {ex}");
            _intercept = InterceptFlag.Idle;
            return false;
        }
    }

    /// <summary>
    /// Per-frame: flush any buffered intercept list whose last new line arrived
    /// more than INTERCEPT_FLUSH_AFTER_SECONDS ago. Registered with
    /// CoreUpdateBehavior in Plugin.Load alongside ProcessAllMessages.
    /// </summary>
    public static void TickInterceptTimeouts()
    {
        if (_intercept == InterceptFlag.Idle) return;
        var now = UnityEngine.Time.realtimeSinceStartupAsDouble;
        if (now - _interceptLastLineTime < INTERCEPT_FLUSH_AFTER_SECONDS) return;

        switch (_intercept)
        {
            case InterceptFlag.ReceivingBoxList:
                FlushBoxList();
                break;
            case InterceptFlag.ReceivingBoxContent:
                FlushBoxContent();
                break;
            case InterceptFlag.ReceivingPrestigeInfo:
                FlushPrestigeInfo();
                break;
            case InterceptFlag.ReceivingBloodInfo:
                FlushBloodInfo();
                break;
            case InterceptFlag.ReceivingGenericResponse:
                FlushGenericResponse();
                break;
            case InterceptFlag.AwaitingBoxList:
            case InterceptFlag.AwaitingBoxContent:
            case InterceptFlag.AwaitingPrestigeInfo:
            case InterceptFlag.AwaitingBloodInfo:
            case InterceptFlag.AwaitingGenericResponse:
                // Server never replied (command rejected, comms hiccup, etc.).
                // Reset so the next user click re-arms cleanly. Don't dispatch
                // an empty list - that'd clobber any previously-loaded data.
                LogUtils.LogWarning($"Intercept '{_intercept}' timed out with no server reply; resetting.");
                _intercept = InterceptFlag.Idle;
                if (_intercept == InterceptFlag.AwaitingGenericResponse) _genericResponseBuffer.Clear();
                break;
        }
    }

    private static void FlushGenericResponse()
    {
        // Wrap into the public state slot so subscribed tabs can render. Strip
        // nothing — let the UI label keep TMP color tags so the response
        // visually matches what the user sees scrolling by in chat.
        var snapshot = new PlayerStateService.LastServerResponse
        {
            Command    = _genericResponseCommand,
            Lines      = new List<string>(_genericResponseBuffer),
            CapturedAt = System.DateTime.UtcNow,
        };
        _genericResponseBuffer.Clear();
        _genericResponseCommand = "";
        _intercept = InterceptFlag.Idle;
        PlayerStateService.UpdateLastResponse(snapshot);
        LogUtils.LogInfo($"Captured {snapshot.Lines.Count} server response line(s) for '{snapshot.Command}'.");
    }

    private static void FlushPrestigeInfo()
    {
        var snapshot = _prestigeInfoBuffer;
        _prestigeInfoBuffer = new PlayerStateService.PrestigeInfo
        {
            EffectLines = new System.Collections.Generic.List<string>(),
        };
        _intercept = InterceptFlag.Idle;
        PlayerStateService.UpdatePrestigeInfo(snapshot);
        LogUtils.LogInfo($"Parsed prestige info for '{snapshot.TypeName}' (level {snapshot.Level}/{snapshot.MaxLevel}, {snapshot.EffectLines?.Count ?? 0} effect lines).");
    }

    private static void FlushBloodInfo()
    {
        var snapshot = _bloodInfoBuffer;
        _bloodInfoBuffer = new PlayerStateService.BloodInfo
        {
            StatLines = new System.Collections.Generic.List<string>(),
        };
        _intercept = InterceptFlag.Idle;
        PlayerStateService.UpdateBloodInfo(snapshot);
        LogUtils.LogInfo($"Parsed blood info for '{snapshot.BloodType}' (level {snapshot.Level} prestige {snapshot.Prestige}, {snapshot.StatLines?.Count ?? 0} stat lines).");
    }

    private static void FlushBoxList()
    {
        var snapshot = new List<string>(_boxListBuffer);
        _boxListBuffer.Clear();
        _intercept = InterceptFlag.Idle;
        PlayerStateService.UpdateBoxList(snapshot);
        LogUtils.LogInfo($"Parsed {snapshot.Count} familiar box name(s).");
    }

    private static void FlushBoxContent()
    {
        var snapshot = new List<PlayerStateService.FamiliarBoxEntry>(_boxContentBuffer);
        _boxContentBuffer.Clear();
        _intercept = InterceptFlag.Idle;
        var box = PlayerStateService.ActiveBox;
        if (!string.IsNullOrEmpty(box))
            PlayerStateService.UpdateBoxContents(box, snapshot);
        LogUtils.LogInfo($"Parsed {snapshot.Count} familiar entries for box '{box}'.");
    }
}
