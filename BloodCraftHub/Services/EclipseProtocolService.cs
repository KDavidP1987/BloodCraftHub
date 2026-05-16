using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Unity.IL2CPP;
using BloodCraftHub.Resources;
using BloodCraftHub.Utils;
using ProjectM.Network;

namespace BloodCraftHub.Services;

// INBOUND + OUTBOUND structured protocol shared with Bloodcraft (server) and Eclipse (client).
//
// Wire format (both directions):
//   <intermediate-message>;mac<base64-hmac-sha256>
// where intermediate-message is:
//   client -> server : "[ECLIPSE][<subType>]:<payload>"
//   server -> client : "[<subType>]:<payload>"   (server omits the [ECLIPSE] tag in responses)
//
// Sub-types (mirrors Eclipse-main/Patches/ClientChatSystemPatch.NetworkEventSubType):
//   0 = RegisterUser       client sends "VERSION;PLATFORM_ID" once after entering world
//   1 = ProgressToClient   server pushes player progress (XP, legacy, expertise, ...) periodically
//   2 = ConfigsToClient    server pushes config snapshot (max levels, multipliers) once on register
//
// MAC = HMAC-SHA256(UTF8(intermediate-message), shared-key). Shared key is loaded from
// embedded Resources/secrets.json (see SecretManager) - same pre-shared default that
// Bloodcraft and Eclipse ship.
//
// Lifecycle: Patches/ClientChatPatch drives this; this service is otherwise stateless.
public static class EclipseProtocolService
{
    public enum NetworkEventSubType
    {
        RegisterUser     = 0,
        ProgressToClient = 1,
        ConfigsToClient  = 2,
    }

    // Mirrors Eclipse-main's protocol version negotiation. Bloodcraft only
    // signs responses for clients that announce "1.3.x" today.
    public const string PROTOCOL_VERSION = "1.3.13";

    // BepInEx plugin GUID of the standalone Eclipse client mod (Eclipse-main).
    // Used to detect coexistence so we don't destroy the chat entity before
    // Eclipse's own ClientChatSystem prefix can read it. See IsEclipseModLoaded.
    public const string ECLIPSE_PLUGIN_GUID = "io.zfolmt.Eclipse";

    private static readonly Regex _regexEventPrefix = new(@"^\[(\d+)\]:", RegexOptions.Compiled);
    private static readonly Regex _regexMacSuffix   = new(@";mac([^;]+)$", RegexOptions.Compiled);

    // NOTE: do NOT add static ComponentType[] / NetworkEventType fields here.
    // Plugin.Load triggers this class's cctor (via Initialize -> set_SharedKey),
    // which runs BEFORE V Rising creates its ECS World. At that point
    // Unity.Entities.TypeManager isn't initialized, so any static-field
    // initializer that calls ComponentType.ReadOnly(...) NREs inside
    // TypeManager.FindTypeIndex and aborts plugin load entirely.
    //
    // Outbound protocol messages reuse MessageService.SendRaw, whose ECS-
    // related fields are only touched at first invocation (per-frame loop,
    // after the world exists), so they're safe there.

    public static bool   UserRegistered { get; private set; }
    public static bool   RegistrationPending { get; private set; }
    public static byte[] SharedKey { get; private set; }

    // Cached Eclipse-mod-coexistence flag. Resolved lazily on first inbound
    // chat tick — Plugin.Load runs before BepInEx finishes loading the other
    // plugins (alphabetical: BCH < Eclipse), so the chainloader's Plugins
    // dictionary may not contain Eclipse yet at Plugin.Load time. By the time
    // the player is in-world and chat starts flowing, all plugins are loaded.
    private static bool _eclipseModChecked;
    private static bool _eclipseModLoaded;

    /// <summary>Load the shared HMAC key once at plugin startup. Returns true if a usable key was loaded.</summary>
    public static bool Initialize()
    {
        SharedKey = SecretManager.GetSharedKey();
        if (SharedKey == null || SharedKey.Length == 0)
        {
            LogUtils.LogWarning("Eclipse protocol: no shared key loaded — structured server data will not be processed.");
            return false;
        }
        LogUtils.LogInfo($"Eclipse protocol initialized with {SharedKey.Length}-byte shared key.");
        return true;
    }

    public static void Reset()
    {
        UserRegistered = false;
        RegistrationPending = false;
    }

    /// <summary>
    /// True when the standalone Eclipse client mod (io.zfolmt.Eclipse) is also
    /// installed in this BepInEx profile. Result is cached after the first call.
    /// </summary>
    /// <remarks>
    /// Bloodcraft's server-side mod broadcasts a single MAC-signed
    /// ProgressToClient/ConfigsToClient stream per player. Both BCH and Eclipse
    /// independently Harmony-prefix ClientChatSystem.OnUpdate and consume that
    /// stream. If BCH destroys the chat entity after processing (the default
    /// when Eclipse isn't around — keeps chat noise out of the player's
    /// chat window), Eclipse's prefix sees a destroyed entity and renders
    /// zeroed-out bars. Callers should leave the entity intact when this
    /// returns true; Eclipse's own prefix will destroy it after parsing.
    /// </remarks>
    public static bool IsEclipseModLoaded()
    {
        if (_eclipseModChecked) return _eclipseModLoaded;
        try
        {
            var plugins = IL2CPPChainloader.Instance?.Plugins;
            _eclipseModLoaded = plugins != null && plugins.ContainsKey(ECLIPSE_PLUGIN_GUID);
            if (_eclipseModLoaded)
                LogUtils.LogInfo($"Eclipse mod ({ECLIPSE_PLUGIN_GUID}) detected alongside BloodCraftHub — leaving MAC-signed chat entities intact so Eclipse can also process them.");
            else
                LogUtils.LogDebug($"Eclipse mod ({ECLIPSE_PLUGIN_GUID}) not installed; BCH will destroy MAC-signed chat entities after processing.");
        }
        catch (Exception ex)
        {
            LogUtils.LogWarning($"Eclipse mod detection failed; assuming not present: {ex.Message}");
            _eclipseModLoaded = false;
        }
        _eclipseModChecked = true;
        return _eclipseModLoaded;
    }

    // ---------- Inbound (server -> client) ----------

    /// <summary>
    /// Try to interpret a chat-message string as a signed Eclipse-protocol message.
    /// On match (MAC valid + recognized event id) routes the payload to PlayerStateService
    /// and returns true; the caller should then suppress the chat entity so it doesn't
    /// land in the player's chat window.
    /// </summary>
    public static bool TryHandleServerMessage(string raw)
    {
        if (SharedKey == null || string.IsNullOrEmpty(raw)) return false;

        var macMatch = _regexMacSuffix.Match(raw);
        if (!macMatch.Success) return false;

        string receivedMac = macMatch.Groups[1].Value;
        string intermediate = _regexMacSuffix.Replace(raw, string.Empty);

        if (!VerifyMac(intermediate, receivedMac, SharedKey))
        {
            // Could be a normal player message containing literal ";mac..." — quiet at info level.
            LogUtils.LogDebug($"Eclipse: MAC mismatch on candidate message — ignoring. ({raw.Length} chars)");
            return false;
        }

        var idMatch = _regexEventPrefix.Match(intermediate);
        if (!idMatch.Success ||
            !int.TryParse(idMatch.Groups[1].Value, out int subTypeId))
        {
            LogUtils.LogWarning($"Eclipse: MAC-verified message has no [<int>]: prefix — {intermediate}");
            return true; // we consumed it; just unable to route
        }

        string payload = _regexEventPrefix.Replace(intermediate, string.Empty);

        try
        {
            switch ((NetworkEventSubType)subTypeId)
            {
                case NetworkEventSubType.ProgressToClient:
                    HandleProgressMessage(payload);
                    break;
                case NetworkEventSubType.ConfigsToClient:
                    HandleConfigMessage(payload);
                    UserRegistered = true; // server has accepted us
                    RegistrationPending = false;
                    LogUtils.LogInfo("Eclipse: server acknowledged registration; structured data flowing.");
                    break;
                default:
                    LogUtils.LogWarning($"Eclipse: unknown event id {subTypeId}");
                    break;
            }
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"Eclipse: failed handling subType={subTypeId}: {ex}");
        }
        return true;
    }

    private static void HandleProgressMessage(string csv)
    {
        // Field layout per Eclipse-main DataService.ParsePlayerData (Bloodcraft v1.3.x):
        //   [0..3]   Experience: progressPercent, level, prestige, classId          (4)
        //   [4..8]   Legacy:     progressPercent, level, prestige, type, bonusStats  (5)
        //   [9..13]  Expertise:  progressPercent, level, prestige, type, bonusStats  (5)
        //   [14..18] Familiar:   progressPercent, level, prestige, name, packedStats (5)
        //   [19..34] Profession: 8 professions x (progressPercent, level)           (16)
        //   [35..39] Daily quest: type, progress, goal, target, isVBlood             (5)
        //   [40..44] Weekly quest: type, progress, goal, target, isVBlood            (5)
        //   [45]     Shift spell PrefabGUID                                          (1)
        //   Total: 46 fields.
        //
        // Defensive parsing: each subsection only fires if its fields are present.
        // Bloodcraft may add fields in future versions - we won't crash on a longer
        // payload, just on a shorter one.
        var p = csv.Split(',');
        int n = p.Length;

        if (n < 4)
        {
            LogUtils.LogWarning($"Eclipse: ProgressToClient payload too short ({n} fields).");
            return;
        }

        // [0..3] Experience
        PlayerStateService.UpdateExperience(new PlayerStateService.ExperienceState
        {
            Progress = PlayerStateService.ParseProgress(p[0]),
            Level    = PlayerStateService.ParseInt(p[1]),
            Prestige = PlayerStateService.ParseInt(p[2]),
            Class    = (PlayerStateService.PlayerClass)PlayerStateService.ParseInt(p[3]),
        });

        // [4..8] Legacy
        if (n >= 9)
        {
            PlayerStateService.UpdateLegacy(new PlayerStateService.LegacyState
            {
                Progress      = PlayerStateService.ParseProgress(p[4]),
                Level         = PlayerStateService.ParseInt(p[5]),
                Prestige      = PlayerStateService.ParseInt(p[6]),
                Type          = (PlayerStateService.BloodType)PlayerStateService.ParseInt(p[7]),
                BonusStatsRaw = p[8],
            });
        }

        // [9..13] Expertise
        if (n >= 14)
        {
            PlayerStateService.UpdateExpertise(new PlayerStateService.ExpertiseState
            {
                Progress      = PlayerStateService.ParseProgress(p[9]),
                Level         = PlayerStateService.ParseInt(p[10]),
                Prestige      = PlayerStateService.ParseInt(p[11]),
                Type          = (PlayerStateService.WeaponType)PlayerStateService.ParseInt(p[12]),
                BonusStatsRaw = p[13],
            });
        }

        // [14..18] Familiar
        if (n >= 19)
        {
            // 0.10.8: HasActive is the raw "name field is non-empty" signal —
            // before the empty-string mask gets replaced with the "Familiar"
            // placeholder for display. The Summon flow on the V-Bloods tab
            // uses this to decide whether to pre-issue `.fam ub`.
            string famNameRaw = p[17];
            bool   famActive  = !string.IsNullOrEmpty(famNameRaw);
            PlayerStateService.UpdateFamiliar(new PlayerStateService.FamiliarState
            {
                Progress  = PlayerStateService.ParseProgress(p[14]),
                Level     = Math.Max(1, PlayerStateService.ParseInt(p[15])),
                Prestige  = PlayerStateService.ParseInt(p[16]),
                Name      = famActive ? famNameRaw : "Familiar",
                RawStats  = p[18] ?? string.Empty,
                HasActive = famActive,
            });
        }

        // [19..34] Profession (8 x 2)
        if (n >= 35)
        {
            PlayerStateService.UpdateProfession(new PlayerStateService.ProfessionState
            {
                EnchantingProgress    = PlayerStateService.ParseProgress(p[19]),
                EnchantingLevel       = PlayerStateService.ParseInt(p[20]),
                AlchemyProgress       = PlayerStateService.ParseProgress(p[21]),
                AlchemyLevel          = PlayerStateService.ParseInt(p[22]),
                HarvestingProgress    = PlayerStateService.ParseProgress(p[23]),
                HarvestingLevel       = PlayerStateService.ParseInt(p[24]),
                BlacksmithingProgress = PlayerStateService.ParseProgress(p[25]),
                BlacksmithingLevel    = PlayerStateService.ParseInt(p[26]),
                TailoringProgress     = PlayerStateService.ParseProgress(p[27]),
                TailoringLevel        = PlayerStateService.ParseInt(p[28]),
                WoodcuttingProgress   = PlayerStateService.ParseProgress(p[29]),
                WoodcuttingLevel      = PlayerStateService.ParseInt(p[30]),
                MiningProgress        = PlayerStateService.ParseProgress(p[31]),
                MiningLevel           = PlayerStateService.ParseInt(p[32]),
                FishingProgress       = PlayerStateService.ParseProgress(p[33]),
                FishingLevel          = PlayerStateService.ParseInt(p[34]),
            });
        }

        // [35..39] Daily quest
        if (n >= 40)
        {
            PlayerStateService.UpdateDailyQuest(new PlayerStateService.QuestState
            {
                Target     = (PlayerStateService.TargetType)PlayerStateService.ParseInt(p[35]),
                Progress   = PlayerStateService.ParseInt(p[36]),
                Goal       = PlayerStateService.ParseInt(p[37]),
                TargetName = p[38] ?? string.Empty,
                IsVBlood   = PlayerStateService.ParseBool(p[39]),
            });
        }

        // [40..44] Weekly quest
        if (n >= 45)
        {
            PlayerStateService.UpdateWeeklyQuest(new PlayerStateService.QuestState
            {
                Target     = (PlayerStateService.TargetType)PlayerStateService.ParseInt(p[40]),
                Progress   = PlayerStateService.ParseInt(p[41]),
                Goal       = PlayerStateService.ParseInt(p[42]),
                TargetName = p[43] ?? string.Empty,
                IsVBlood   = PlayerStateService.ParseBool(p[44]),
            });
        }

        // [45] Shift spell PrefabGUID
        if (n >= 46)
        {
            PlayerStateService.UpdateShiftSpell(new PlayerStateService.ShiftSpellState
            {
                SpellIndex = PlayerStateService.ParseInt(p[45]),
            });
        }
    }

    private static void HandleConfigMessage(string csv)
    {
        var parts = csv.Split(',');
        // Eclipse-main ConfigDataV1_3 takes the first 9 fields as scalars:
        //   prestigeMult, classMult, maxPlayer, maxLegacy, maxExpertise, maxFamiliar, maxProf(unused), extraRecipes, primalCost
        // followed by 12 weaponStats + 12 bloodStats + classSynergies. We only need the max-level scalars right now.
        if (parts.Length < 7)
        {
            LogUtils.LogWarning($"Eclipse: ConfigsToClient payload too short ({parts.Length} fields).");
            return;
        }

        var cfg = new PlayerStateService.ServerConfig
        {
            Loaded            = true,
            MaxPlayerLevel    = PlayerStateService.ParseInt(parts[2]),
            MaxLegacyLevel    = PlayerStateService.ParseInt(parts[3]),
            MaxExpertiseLevel = PlayerStateService.ParseInt(parts[4]),
            MaxFamiliarLevel  = PlayerStateService.ParseInt(parts[5]),
        };
        PlayerStateService.UpdateConfig(cfg);
    }

    // ---------- Outbound (client -> server) ----------

    /// <summary>Send the RegisterUser handshake. Called from the chat patch once the player is in-world.</summary>
    public static void SendRegistration()
    {
        if (SharedKey == null) return;
        if (UserRegistered || RegistrationPending) return;

        try
        {
            if (!MessageService.IsInitialized)
            {
                LogUtils.LogDebug("Eclipse: registration deferred — MessageService not yet bound to character/user.");
                return;
            }

            // Build the payload Eclipse uses: "<PROTOCOL_VERSION>;<PlatformId>".
            ulong platformId = MessageService.LocalUser.Read<User>().PlatformId;
            string innerPayload = $"{PROTOCOL_VERSION};{platformId}";
            string intermediate = $"[ECLIPSE][{(int)NetworkEventSubType.RegisterUser}]:{innerPayload}";
            string mac = GenerateMac(intermediate, SharedKey);
            string signed = $"{intermediate};mac{mac}";

            RegistrationPending = true;
            MessageService.SendRaw(signed);
            LogUtils.LogInfo($"Eclipse: registration sent (platformId={platformId}).");
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"Eclipse: SendRegistration failed: {ex}");
        }
    }

    // ---------- HMAC plumbing ----------

    private static bool VerifyMac(string message, string receivedMacBase64, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        byte[] computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        string expected = Convert.ToBase64String(computed);

        // Constant-time compare to avoid timing leaks (consistent with upstream).
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(receivedMacBase64));
    }

    private static string GenerateMac(string message, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        byte[] computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToBase64String(computed);
    }
}
