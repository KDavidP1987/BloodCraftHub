using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BloodCraftHub.Resources;
using BloodCraftHub.Utils;
using Il2CppInterop.Runtime;
using ProjectM.Network;
using Unity.Entities;

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

    private static readonly Regex _regexEventPrefix = new(@"^\[(\d+)\]:", RegexOptions.Compiled);
    private static readonly Regex _regexMacSuffix   = new(@";mac([^;]+)$", RegexOptions.Compiled);

    private static readonly ComponentType[] _networkEventComponents =
    {
        ComponentType.ReadOnly(Il2CppType.Of<FromCharacter>()),
        ComponentType.ReadOnly(Il2CppType.Of<NetworkEventType>()),
        ComponentType.ReadOnly(Il2CppType.Of<SendNetworkEventTag>()),
        ComponentType.ReadOnly(Il2CppType.Of<ChatMessageEvent>()),
    };

    private static readonly NetworkEventType _networkEventType = new()
    {
        IsAdminEvent = false,
        EventId      = NetworkEvents.EventId_ChatMessageEvent,
        IsDebugEvent = false,
    };

    public static bool   UserRegistered { get; private set; }
    public static bool   RegistrationPending { get; private set; }
    public static byte[] SharedKey { get; private set; }

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
        // Field layout per Eclipse-main DataService.ParsePlayerData (v1.3.13):
        //   [0..3]   Experience: progressPercent, level, prestige, classId
        //   [4..8]   Legacy:     progressPercent, level, prestige, legacyType, bonusStats
        //   [9..13]  Expertise:  progressPercent, level, prestige, expertiseType, bonusStats
        //   [14..18] Familiar:   progressPercent, level, prestige, familiarName, familiarStats
        //   [19..34] Profession: 8 professions x (progressPercent, level) pairs
        //   [35..39] Daily quest: type, progress, goal, target, isVBlood
        //   [40..44] Weekly quest: type, progress, goal, target, isVBlood
        //   [45]     Shift spell index
        //
        // We only consume the experience fields right now; the rest is captured in
        // PlayerStateService as we wire more overlays/tabs in later phases.
        var parts = csv.Split(',');
        if (parts.Length < 4)
        {
            LogUtils.LogWarning($"Eclipse: ProgressToClient payload too short ({parts.Length} fields).");
            return;
        }

        var exp = new PlayerStateService.ExperienceState
        {
            Progress = PlayerStateService.ParseProgress(parts[0]),
            Level    = PlayerStateService.ParseInt(parts[1]),
            Prestige = PlayerStateService.ParseInt(parts[2]),
            Class    = (PlayerStateService.PlayerClass)PlayerStateService.ParseInt(parts[3]),
        };
        PlayerStateService.UpdateExperience(exp);
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
