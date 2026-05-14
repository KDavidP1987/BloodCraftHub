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
