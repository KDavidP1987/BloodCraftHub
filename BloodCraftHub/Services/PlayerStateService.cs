using System;
using System.Globalization;

namespace BloodCraftHub.Services;

// Single source of truth for player progress data.
//
// Fed by EclipseProtocolService.HandleProgressMessage (preferred path, structured).
// In future phases, MessageService_Processing regex parsers will fill in things
// the structured protocol doesn't cover (familiar box listings, etc.).
//
// UI panels subscribe to the *Changed events; no panel should poll fields directly.
public static class PlayerStateService
{
    // Mirrors Eclipse-main/Services/DataService.PlayerClass (NetworkEventSubType.ProgressToClient field [3]).
    public enum PlayerClass
    {
        None,
        BloodKnight,
        DemonHunter,
        VampireLord,
        ShadowBlade,
        ArcaneSorcerer,
        DeathMage,
    }

    // ---------- Experience ----------
    public struct ExperienceState
    {
        public float       Progress;   // 0.0 .. 1.0 of the way to next level
        public int         Level;
        public int         Prestige;
        public PlayerClass Class;
    }
    public static ExperienceState Experience { get; private set; }
    public static event Action ExperienceChanged;

    // ---------- Server-config snapshot (received once on registration) ----------
    public struct ServerConfig
    {
        public bool Loaded;
        public int  MaxPlayerLevel;
        public int  MaxLegacyLevel;
        public int  MaxExpertiseLevel;
        public int  MaxFamiliarLevel;
    }
    public static ServerConfig Config { get; private set; }
    public static event Action ConfigChanged;

    // ---------- Mutators (called by EclipseProtocolService) ----------

    internal static void UpdateExperience(in ExperienceState next)
    {
        Experience = next;
        try { ExperienceChanged?.Invoke(); }
        catch (Exception ex) { Utils.LogUtils.LogError($"ExperienceChanged handler threw: {ex}"); }
    }

    internal static void UpdateConfig(in ServerConfig next)
    {
        Config = next;
        try { ConfigChanged?.Invoke(); }
        catch (Exception ex) { Utils.LogUtils.LogError($"ConfigChanged handler threw: {ex}"); }
    }

    // ---------- Parsing helpers ----------

    /// <summary>Parse a float string with invariant culture; tolerate decimals or integers; clamp to [0..1] if percentScale is true.</summary>
    internal static float ParseProgress(string s, bool percentScale = true)
    {
        if (string.IsNullOrEmpty(s)) return 0f;
        if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 0f;
        return percentScale ? Math.Clamp(v / 100f, 0f, 1f) : v;
    }

    internal static int ParseInt(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
