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

    /// <summary>Selectable subset of PlayerClass used in the Class-apply form.
    /// PlayerClass includes "None" for the untouched/empty state — that's not a
    /// valid value for `.class s` so we expose only the real classes here.</summary>
    public enum BloodcraftClassChoice
    {
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

    // Matches Bloodcraft/Eclipse's per-weapon stat bonus codes (1-based; 0 = None).
    public enum WeaponStatType
    {
        None,
        MaxHealth,
        MovementSpeed,
        PrimaryAttackSpeed,
        PhysicalLifeLeech,
        SpellLifeLeech,
        PrimaryLifeLeech,
        PhysicalPower,
        SpellPower,
        PhysicalCriticalStrikeChance,
        PhysicalCriticalStrikeDamage,
        SpellCriticalStrikeChance,
        SpellCriticalStrikeDamage,
    }

    // Selectable list for the .wep cst form. Mirrors WeaponStatType minus None
    // and uses the SAME enum-name spellings Bloodcraft prints in its `.wep lst`
    // help reply, in the SAME order. Used with EnumIndexField so the dropdown
    // emits the 1-based index Bloodcraft's command parser expects.
    public enum WeaponBonusStat
    {
        MaxHealth,
        MovementSpeed,
        PrimaryAttackSpeed,
        PhysicalLifeLeech,
        SpellLifeLeech,
        PrimaryLifeLeech,
        PhysicalPower,
        SpellPower,
        PhysicalCritChance,
        PhysicalCritDamage,
        SpellCritChance,
        SpellCritDamage,
    }

    // Selectable list for the .bl cst form. Order matches Bloodcraft's
    // BloodStatType enum so the EnumIndexField's 1-based output lines up
    // with what the command parser decrements back to 0-based internally.
    public enum BloodBonusStat
    {
        HealingReceived,
        DamageReduction,
        PhysicalResistance,
        SpellResistance,
        ResourceYield,
        ReducedBloodDrain,
        SpellCooldownRecoveryRate,
        WeaponCooldownRecoveryRate,
        UltimateCooldownRecoveryRate,
        MinionDamage,
        AbilityAttackSpeed,
        CorruptionDamageReduction,
    }

    // Pickable subset of BloodType for the .bl cst / .bl get forms — Bloodcraft
    // rejects None/VBlood/Frailed/GateBoss as legacy choices (those are unit-
    // category markers, not player-bondable bloods).
    public enum BloodTypeChoice
    {
        Worker,
        Warrior,
        Scholar,
        Rogue,
        Mutant,
        Draculin,
        Immortal,
        Creature,
        Brute,
        Corruption,
    }

    /// <summary>Spell schools accepted by .fam shiny. Bloodcraft does case-
    /// insensitive substring match against `PrefabGUID.GetPrefabName()` (see
    /// FamiliarCommands.cs#L1190), so the bare names below all hit cleanly.</summary>
    public enum FamiliarShinySchoolChoice
    {
        Blood,
        Storm,
        Unholy,
        Chaos,
        Frost,
        Illusion,
    }

    /// <summary>Profession names accepted by `.prof get/set`. Mirrors the
    /// per-profession fields in <see cref="ProfessionState"/>; Bloodcraft's
    /// ProfessionFactory exposes these via .prof l (8 professions in v1.13.x).
    /// </summary>
    public enum BloodcraftProfession
    {
        Enchanting,
        Alchemy,
        Harvesting,
        Blacksmithing,
        Tailoring,
        Woodcutting,
        Mining,
        Fishing,
    }

    /// <summary>Quest type accepted by .quest c (admin force-complete).
    /// `.quest p|t|r` accept "d"/"w" shorthands which we hardcode rather than
    /// expose as picker; .quest c requires the full enum-name string.</summary>
    public enum BloodcraftQuestType
    {
        Daily,
        Weekly,
    }

    // Mirrors LearningMods/Bloodcraft-main/Interfaces/PrestigeInterface.cs::PrestigeType.
    // Used by the Prestige tab as the dropdown source for .prestige me/get/lb commands.
    // ORDER MATTERS - Bloodcraft accepts these as STRING names so the user types the
    // enum name (e.g. "SwordExpertise"); we just emit the name from the dropdown.
    public enum PrestigeType
    {
        Experience,
        Exo,
        SwordExpertise,
        AxeExpertise,
        MaceExpertise,
        SpearExpertise,
        CrossbowExpertise,
        GreatSwordExpertise,
        SlashersExpertise,
        PistolsExpertise,
        ReaperExpertise,
        LongbowExpertise,
        WhipExpertise,
        UnarmedExpertise,
        FishingPoleExpertise,
        TwinBladesExpertise,
        DaggersExpertise,
        ClawsExpertise,
        WorkerLegacy,
        WarriorLegacy,
        ScholarLegacy,
        RogueLegacy,
        MutantLegacy,
        DraculinLegacy,
        ImmortalLegacy,
        CreatureLegacy,
        BruteLegacy,
        CorruptionLegacy,
    }

    // Bloodcraft's exoform shapeshift options for .prestige sf.
    public enum ExoformVariant
    {
        EvolvedVampire,
        CorruptedSerpent,
    }

    // The 8 Bloodcraft professions. Used as the dropdown source for .prof set
    // (and any future profession-targeted commands). Names match the strings
    // Bloodcraft accepts.
    public enum ProfessionType
    {
        Enchanting,
        Alchemy,
        Harvesting,
        Blacksmithing,
        Tailoring,
        Woodcutting,
        Mining,
        Fishing,
    }

    // Bloodcraft's quest schedule. .quest c takes this as a string.
    public enum QuestSchedule
    {
        Daily,
        Weekly,
    }

    // Matches Bloodcraft/Eclipse's per-blood stat bonus codes (1-based; 0 = None).
    public enum BloodStatType
    {
        None,
        HealingReceived,
        DamageReduction,
        PhysicalResistance,
        SpellResistance,
        ResourceYield,
        ReducedBloodDrain,
        SpellCooldownRecoveryRate,
        WeaponCooldownRecoveryRate,
        UltimateCooldownRecoveryRate,
        MinionDamage,
        AbilityAttackSpeed,
        CorruptionDamageReduction,
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

    public struct PrestigeInfo
    {
        public string TypeName;     // e.g. "Experience", "SwordExpertise", "WorkerLegacy"
        public int    Level;        // current prestige level in this system
        public int    MaxLevel;     // server-configured cap for this system
        // Free-form lines of "effect" text Bloodcraft sends, color tags stripped.
        // Different prestige types have different numbers of effect lines, so we
        // capture them as a list rather than trying to model each variant.
        public System.Collections.Generic.List<string> EffectLines;
    }

    public struct BloodInfo
    {
        public string BloodType;     // e.g. "Worker", "Warrior", "Scholar"
        public int    Level;
        public int    Prestige;
        public string Essence;       // Bloodcraft's "essence" progress numeric (raw text)
        public string ProgressPct;   // e.g. "12.3"
        // Stat lines (color tags stripped). Each one looks like
        // "Worker Stats: PhysicalResistance: 5.2, MaxHealth: 12.5".
        public System.Collections.Generic.List<string> StatLines;
    }

    public struct FamiliarBoxEntry
    {
        public int    Index;        // 1-based position within the box
        public string Name;         // familiar display name
        public string ColorHex;     // server-sent color hex (e.g. "#FFFF00") - hints at school
        public int    Level;        // current familiar level (0 if not parsed)
        public int    Prestige;     // prestige tier (0 if none)
        public bool   IsShiny;      // true if Bloodcraft put the '*' shiny marker on the entry
        public string ShinyColorHex; // color of the '*' marker if shiny - hints at shiny school

        /// <summary>Element name ("Storm", "Chaos", etc.) for the shiny school, derived
        /// from <see cref="ShinyColorHex"/>. Empty when not shiny or color unknown.</summary>
        public string ShinySchool => IsShiny ? FamiliarShinySchools.NameForColor(ShinyColorHex) : "";
    }

    /// <summary>
    /// Maps the hex color Bloodcraft uses for each shiny buff to the school name it
    /// represents. Keep in sync with Bloodcraft's <c>FamiliarUnlockSystem.ShinyBuffColorHexes</c>
    /// (currently in <c>LearningMods/Bloodcraft-main/Systems/Familiars/FamiliarUnlockSystem.cs</c>).
    /// </summary>
    public static class FamiliarShinySchools
    {
        // Bloodcraft v1.13.x mapping. Comparison is case-insensitive against the
        // hex (with or without leading #).
        private static readonly System.Collections.Generic.Dictionary<string, string> _byHex =
            new(System.StringComparer.OrdinalIgnoreCase)
            {
                { "A020F0", "Chaos"    }, // purple
                { "FFD700", "Storm"    }, // gold
                { "FF0000", "Blood"    }, // red
                { "008080", "Illusion" }, // teal
                { "00FFFF", "Frost"    }, // cyan
                { "00FF00", "Unholy"   }, // green
            };

        public static string NameForColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return "";
            var key = hex.StartsWith("#") ? hex.Substring(1) : hex;
            return _byHex.TryGetValue(key, out var name) ? name : "";
        }
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

    // Box-browser state fed by the legacy regex pipeline (MessageService_Processing.HandleInboundChat).
    public static System.Collections.Generic.List<string> BoxList { get; private set; }
        = new System.Collections.Generic.List<string>();
    public static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<FamiliarBoxEntry>> BoxContents { get; private set; }
        = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<FamiliarBoxEntry>>();
    public static string ActiveBox { get; private set; }

    public static event Action ExperienceChanged;
    public static event Action LegacyChanged;
    public static event Action ExpertiseChanged;
    public static event Action FamiliarChanged;
    public static event Action ProfessionChanged;
    public static event Action QuestChanged;
    public static event Action ShiftSpellChanged;
    public static event Action ConfigChanged;
    public static event Action BoxListChanged;
    public static event Action BoxContentsChanged;
    public static event Action ActiveBoxChanged;

    public static PrestigeInfo PrestigeInfoLatest { get; private set; }
    public static event Action PrestigeInfoChanged;
    internal static void UpdatePrestigeInfo(in PrestigeInfo info)
    {
        PrestigeInfoLatest = info;
        Fire(PrestigeInfoChanged);
    }

    public static BloodInfo BloodInfoLatest { get; private set; }
    public static event Action BloodInfoChanged;
    internal static void UpdateBloodInfo(in BloodInfo info)
    {
        BloodInfoLatest = info;
        Fire(BloodInfoChanged);
    }

    // 0.8.3: generic capture of any server response that doesn't have a
    // dedicated structured intercept. Used for read-data chat commands like
    // .wep get / .wep l / .bl l / .prestige l / .class l / .class lst /
    // .misc userstats / .clan list / etc. — friend-testing of v0.8.1 surfaced
    // that those reply texts landed in chat only, which was easy to miss when
    // the user was browsing the UI panel. Each subscriber tab renders the
    // captured lines in a "Last server response" section.
    public struct LastServerResponse
    {
        public string Command;     // the chat command that triggered the capture (e.g. ".wep get")
        public System.Collections.Generic.List<string> Lines; // raw color-tagged lines, in order
        public System.DateTime CapturedAt;
    }

    public static LastServerResponse LastResponse { get; private set; }
    public static event Action LastResponseChanged;
    internal static void UpdateLastResponse(in LastServerResponse r)
    {
        LastResponse = r;
        Fire(LastResponseChanged);
    }

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

    internal static void UpdateBoxList(System.Collections.Generic.List<string> boxes)
    {
        BoxList = boxes ?? new System.Collections.Generic.List<string>();
        Fire(BoxListChanged);
    }

    internal static void UpdateBoxContents(string boxName, System.Collections.Generic.List<FamiliarBoxEntry> entries)
    {
        if (string.IsNullOrEmpty(boxName)) return;
        BoxContents[boxName] = entries ?? new System.Collections.Generic.List<FamiliarBoxEntry>();
        Fire(BoxContentsChanged);
    }

    public static void SetActiveBox(string boxName)
    {
        if (ActiveBox == boxName) return;
        ActiveBox = boxName;
        Fire(ActiveBoxChanged);
    }

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

    /// <summary>Split a packed bonus-stats string ("010305") into WeaponStatType values.</summary>
    public static System.Collections.Generic.List<WeaponStatType> DecodeWeaponBonusStats(string raw)
    {
        var list = new System.Collections.Generic.List<WeaponStatType>();
        if (string.IsNullOrEmpty(raw)) return list;
        for (int i = 0; i + 2 <= raw.Length; i += 2)
        {
            if (int.TryParse(raw.AsSpan(i, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                list.Add((WeaponStatType)id);
        }
        return list;
    }

    /// <summary>Split a packed bonus-stats string ("010305") into BloodStatType values.</summary>
    public static System.Collections.Generic.List<BloodStatType> DecodeBloodBonusStats(string raw)
    {
        var list = new System.Collections.Generic.List<BloodStatType>();
        if (string.IsNullOrEmpty(raw)) return list;
        for (int i = 0; i + 2 <= raw.Length; i += 2)
        {
            if (int.TryParse(raw.AsSpan(i, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                list.Add((BloodStatType)id);
        }
        return list;
    }
}
