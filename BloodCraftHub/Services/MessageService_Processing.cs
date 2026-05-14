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
    public const string BCCOM_FAM_UNBIND                 = ".fam u";
    public const string BCCOM_FAM_TOGGLE                 = ".fam toggle";
    public const string BCCOM_FAM_COMBAT                 = ".fam combat";
    public const string BCCOM_FAM_PRESTIGE               = ".fam pr";
    public const string BCCOM_FAM_RESET_STATS            = ".fam rs";
    public const string BCCOM_FAM_GET_LEVEL              = ".fam gl";
    public const string BCCOM_FAM_ENABLE_EQUIP           = ".fam smartbind";

    // ---------- Leveling (.lvl) ----------
    public const string BCCOM_LVL_GET            = ".lvl get";

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

    // ---------- Blood legacy (.bl) ----------
    public const string BCCOM_BL_GET             = ".bl get";

    // ---------- Misc (.misc) ----------
    public const string BCCOM_MISC_HEALTH         = ".misc health";

    // ---------- Class (.class) ----------
    public const string BCCOM_CLASS_LIST          = ".class l";
    public const string BCCOM_CLASS_LIST_SPELLS   = ".class lsp";
    public const string BCCOM_CLASS_LIST_STATS    = ".class lst";
    public const string BCCOM_CLASS_TOGGLE_SHIFT  = ".class shift";

    // ---------- Weapon expertise (.wep) ----------
    public const string BCCOM_WEP_GET            = ".wep get";
    public const string BCCOM_WEP_LIST           = ".wep l";
    public const string BCCOM_WEP_LIST_STATS     = ".wep lst";
    public const string BCCOM_WEP_RESET_STATS    = ".wep rst";
    public const string BCCOM_WEP_LOCK_SPELLS    = ".wep locksp";

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
    public const string BCCOM_KC_CASTLE_OPEN_PLOTS = ".castle openplots";
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
    }

    private static InterceptFlag _intercept = InterceptFlag.Idle;
    private static readonly List<string> _boxListBuffer = new();
    private static readonly List<PlayerStateService.FamiliarBoxEntry> _boxContentBuffer = new();

    private const string BOX_LIST_HEADER          = "Familiar Boxes";
    private const string BOX_SELECTED_HEADER      = "Box Selected";
    private const string BOX_NAME_REGEX           = @"<color=[^>]+>(?<box>[^<]+)</color>";
    private const string BOX_CONTENT_ENTRY_REGEX  = @"<color=yellow>(?<idx>\d+)</color>\|<color=(?<color>[^>]+)>(?<name>[^<]+)</color>";

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
        }
        else if (command.Equals(BCCOM_FAM_LIST_CURRENT_BOX, System.StringComparison.Ordinal))
        {
            _intercept = InterceptFlag.AwaitingBoxContent;
            _boxContentBuffer.Clear();
        }
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
                        return Config.Settings.ClearServerMessages;
                    }
                    // First non-color line ends the list. Flush + reset.
                    FlushBoxList();
                    return false;

                case InterceptFlag.AwaitingBoxContent:
                case InterceptFlag.ReceivingBoxContent:
                    var match = _boxContentEntryRegex.Match(text);
                    if (match.Success)
                    {
                        _intercept = InterceptFlag.ReceivingBoxContent;
                        var entry = new PlayerStateService.FamiliarBoxEntry
                        {
                            Index    = PlayerStateService.ParseInt(match.Groups["idx"].Value),
                            ColorHex = match.Groups["color"].Value,
                            Name     = match.Groups["name"].Value,
                        };
                        if (!_boxContentBuffer.Exists(e => e.Index == entry.Index))
                            _boxContentBuffer.Add(entry);
                        return Config.Settings.ClearServerMessages;
                    }
                    if (_intercept == InterceptFlag.ReceivingBoxContent)
                    {
                        // First non-entry line ends the content. Flush + reset.
                        FlushBoxContent();
                    }
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
