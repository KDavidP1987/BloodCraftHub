using System;
using System.Globalization;
using BloodCraftHub.Utils;

namespace BloodCraftHub.Services;

// Single source of truth for player state pushed by Bloodcraft's structured
// protocol (NetworkEventSubType.ProgressToClient).
//
// Field layout mirrors Eclipse-main/Services/DataService.ParsePlayerData
// (Bloodcraft v1.3.x). Each substate has its own change event so panels
// subscribe only to what they render.
//
// Phase 4a: parse all fields; expose state via structs + events.
// Phase 4b+: tabs and overlays subscribe and render.
public static class PlayerStateService
{
    // =========================================================================
    // ENUMS - mirror Eclipse-main/Services/DataService.cs verbatim. Order matters
    // - Bloodcraft's payload encodes these as integer indices.
    // =========================================================================

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

    public enum WeaponType
    {
        Sword,
        Axe,
        Mace,
        Spear,
        Crossbow,
        GreatSword,
        Slashers,
        Pistols,
        Reaper,
        Longbow,
        Whip,
        Unarmed,
        FishingPole,
        TwinBlades,
        Daggers,
        Claws,
    }

    public enum BloodType
    {
        Worker,
        Warrior,
        Scholar,
        Rogue,
        Mutant,
        VBlood,
        Frailed,
        GateBoss,
        Draculin,
        Immortal,
        Creature,
        Brute,
        Corruption,
    }

    public enum TargetType
    {
        Kill,
        Craft,
        Gather,
        Fish,
    }

    // =========================================================================
    // STATE STRUCTS - one per major subsystem
    // =========================================================================

    public struct ExperienceState
    {
        public float       Progress;   // 0.0 .. 1.0
        public int         Level;
        public int         Prestige;
        public PlayerClass Class;
    }

    public struct LegacyState
    {
        public float     Progress;
        public int       Level;
        public int       Prestige;
        public BloodType Type;
        public string    BonusStatsRaw;  // packed 2-chars-per-stat string; decode later
    }

    public struct ExpertiseState
    {
        public float      Progress;
        public int        Level;
        public int        Prestige;
        public WeaponType Type;
        public string     BonusStatsRaw;
    }

    public struct FamiliarState
    {
        public float  Progress;
        public int    Level;
        public int    Prestige;
        public string Name;
        public string RawStats;       // raw packed string; access via FamiliarStat* helpers below

        public int MaxHealth     => ExtractStat(RawStats, 0, 4);
        public int PhysicalPower => ExtractStat(RawStats, 4, 3);
        public int SpellPower    => ExtractStat(RawStats, 7, int.MaxValue);
    }

    public struct ProfessionState
    {
        public float EnchantingProgress;     public int EnchantingLevel;
        public float AlchemyProgress;        public int AlchemyLevel;
        public float HarvestingProgress;     public int HarvestingLevel;
        public float BlacksmithingProgress;  public int BlacksmithingLevel;
        public float TailoringProgress;      public int TailoringLevel;
        public float WoodcuttingProgress;    public int WoodcuttingLevel;
        public float MiningProgress;         public int MiningLevel;
        public float FishingProgress;        public int FishingLevel;
    }

    public struct QuestState
    {
        public TargetType Target;
        public int        Progress;
        public int        Goal;
        public string     TargetName;
        public bool       IsVBlood;
    }

    public struct ShiftSpellState
    {
        public int SpellIndex; // PrefabGUID hash of the equipped shift spell
    }

    public struct ServerConfig
    {
        public bool Loaded;
        public int  MaxPlayerLevel;
        public int  MaxLegacyLevel;
        public int  MaxExpertiseLevel;
        public int  MaxFamiliarLevel;
    }

    // =========================================================================
    // STATE + EVENTS
    // =========================================================================

    public static ExperienceState  Experience { get; private set; }
    public static LegacyState      Legacy     { get; private set; }
    public static ExpertiseState   Expertise  { get; private set; }
    public static FamiliarState    Familiar   { get; private set; }
    public static ProfessionState  Profession { get; private set; }
    public static QuestState       DailyQuest  { get; private set; }
    public static QuestState       WeeklyQuest { get; private set; }
    public static ShiftSpellState  ShiftSpell  { get; private set; }
    public static ServerConfig     Config      { get; private set; }

    public static event Action ExperienceChanged;
    public static event Action LegacyChanged;
    public static event Action ExpertiseChanged;
    public static event Action FamiliarChanged;
    public static event Action ProfessionChanged;
    public static event Action QuestChanged;
    public static event Action ShiftSpellChanged;
    public static event Action ConfigChanged;

    // =========================================================================
    // MUTATORS - called from EclipseProtocolService
    // =========================================================================

    internal static void UpdateExperience(in ExperienceState s) { Experience = s; Fire(ExperienceChanged); }
    internal static void UpdateLegacy(in LegacyState s)         { Legacy     = s; Fire(LegacyChanged); }
    internal static void UpdateExpertise(in ExpertiseState s)   { Expertise  = s; Fire(ExpertiseChanged); }
    internal static void UpdateFamiliar(in FamiliarState s)     { Familiar   = s; Fire(FamiliarChanged); }
    internal static void UpdateProfession(in ProfessionState s) { Profession = s; Fire(ProfessionChanged); }
    internal static void UpdateDailyQuest(in QuestState s)      { DailyQuest = s; Fire(QuestChanged); }
    internal static void UpdateWeeklyQuest(in QuestState s)     { WeeklyQuest = s; Fire(QuestChanged); }
    internal static void UpdateShiftSpell(in ShiftSpellState s) { ShiftSpell = s; Fire(ShiftSpellChanged); }
    internal static void UpdateConfig(in ServerConfig s)        { Config = s;     Fire(ConfigChanged); }

    private static void Fire(Action evt)
    {
        if (evt == null) return;
        try { evt.Invoke(); }
        catch (Exception ex) { LogUtils.LogError($"PlayerStateService event handler threw: {ex}"); }
    }

    // =========================================================================
    // PARSING HELPERS
    // =========================================================================

    /// <summary>Parse a value Bloodcraft sends as "0..100" percent → 0..1 float.</summary>
    internal static float ParseProgress(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0f;
        if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 0f;
        return Math.Clamp(v / 100f, 0f, 1f);
    }

    /// <summary>Parse an int CSV field tolerant of empty / malformed strings.</summary>
    internal static int ParseInt(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>Parse a bool CSV field tolerant of "True"/"False"/"true"/"false".</summary>
    internal static bool ParseBool(string s)
    {
        return !string.IsNullOrEmpty(s) && bool.TryParse(s, out var v) && v;
    }

    private static int ExtractStat(string raw, int start, int length)
    {
        if (string.IsNullOrEmpty(raw) || start >= raw.Length) return 0;
        int realLen = Math.Min(length, raw.Length - start);
        if (realLen <= 0) return 0;
        return int.TryParse(raw.AsSpan(start, realLen), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
