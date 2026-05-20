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

    /// <summary>0.13.1: true when this blood type is one Bloodcraft accepts
    /// as a player legacy choice — i.e. one that responds to `.bl get &lt;Type&gt;`
    /// without rejection. Mirrors the BloodTypeChoice enum (the form-picker
    /// subset) by name. Used by the per-tab and per-overlay auto-refresh
    /// tickers to suppress `.bl get` calls when the player is on Frailed /
    /// VBlood / GateBoss blood — those produce no server reply, which armed
    /// `AwaitingBloodInfo` would then time out, spamming the BepInEx log.
    /// Bug originally surfaced by a player whose blood drained to Frailed
    /// mid-session while the UI was open.</summary>
    public static bool IsBondableBloodType(BloodType t)
    {
        switch (t)
        {
            case BloodType.Worker:
            case BloodType.Warrior:
            case BloodType.Scholar:
            case BloodType.Rogue:
            case BloodType.Mutant:
            case BloodType.Draculin:
            case BloodType.Immortal:
            case BloodType.Creature:
            case BloodType.Brute:
            case BloodType.Corruption:
                return true;
            // Frailed, VBlood, GateBoss are unit-category markers Bloodcraft
            // rejects as legacy choices — `.bl get` against them gets no reply.
            default:
                return false;
        }
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

        // 0.10.8: HasActive distinguishes "no familiar summoned" (server sent
        // empty name + level 0) from "familiar bound but not yet streamed".
        // Pre-0.10.8, Level was floored to 1 and Name defaulted to "Familiar"
        // by EclipseProtocolService, which made the V-Blood Summon button
        // always think a familiar was active and unconditionally pre-issue
        // `.fam ub` — producing a "Couldn't find familiar to unbind!" reply
        // when none was actually bound.
        public bool   HasActive;

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

    /// <summary>0.15.0: per-Bloodcraft-system availability inferred from
    /// ProgressToClient broadcasts. Each flag is sticky-Enabled: once we
    /// observe non-zero data for a system, it stays Enabled for the
    /// session regardless of subsequent zeroed broadcasts (a player who
    /// resets their progress should still see the tab/overlay).
    ///
    /// SettlingComplete flips true after a fixed observation window (see
    /// FEATURE_DETECTION_SETTLING_SECONDS) — only after that do any "still
    /// at zero" systems get marked Disabled. Pre-settling, every system
    /// reads as Enabled so the UI doesn't briefly hide things right after
    /// world entry while ProgressToClient broadcasts haven't accumulated.
    ///
    /// "Disabled" here means "Bloodcraft's server-side EclipseService
    /// returned all-zeros for this system across the settling window"
    /// — usually because the corresponding ConfigService.XxxSystem flag
    /// is false on the server. UI gates dim/hide their elements when
    /// the flag is Disabled.</summary>
    public struct ServerFeatureFlags
    {
        public bool Leveling;
        public bool Legacy;
        public bool Expertise;
        public bool Familiar;
        public bool Class;
        public bool Profession;
        public bool Quest;
        public bool ShiftSlot;
        public bool SettlingComplete;
        public int  ObservedBroadcasts;
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
    public static ServerFeatureFlags FeatureFlags { get; private set; } = DefaultFeatureFlags();

    // 0.15.0: settling window for per-feature detection. After the FIRST
    // ProgressToClient broadcast lands, we observe data for this many
    // seconds before marking any system as Disabled. The window has to
    // exceed Bloodcraft's broadcast interval (0.1s when ConfigService.
    // Eclipsed=true, 2.5s when false) by enough that a brand-new player
    // who hasn't done anything still has time to (a) be assigned a daily
    // quest, (b) receive their first XP/legacy gain, etc. 30 s is a
    // friendly trade-off — long enough to avoid false Disabled on fresh
    // accounts, short enough that users on partial-system servers see
    // the UI degrade quickly.
    private const float FEATURE_DETECTION_SETTLING_SECONDS = 30f;
    private static float _firstProgressAt = -1f;

    // Box-browser state fed by the legacy regex pipeline (MessageService_Processing.HandleInboundChat).
    public static System.Collections.Generic.List<string> BoxList { get; private set; }
        = new System.Collections.Generic.List<string>();
    public static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<FamiliarBoxEntry>> BoxContents { get; private set; }
        = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<FamiliarBoxEntry>>();
    public static string ActiveBox { get; private set; }

    public static event Action ExperienceChanged;
    public static event Action LegacyChanged;
    public static event Action ExpertiseChanged;
    /// <summary>0.15.0: fired whenever any flag on FeatureFlags transitions
    /// (sticky-Enabled flip, settling completion, etc.). UI subscribers
    /// (tab strip, overlay manager, combined overlay) refresh their
    /// per-system visibility on this signal.</summary>
    public static event Action FeatureFlagsChanged;
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

    // 0.10.0: V-Blood collection tracking. Each entry tracks which of the 4
    // possible variants (basic / shiny / primal / primal-shiny) the player
    // has captured, plus location info for one-click summoning.
    //
    // 0.10.9: scanner reworked from a 130-query .fam s scan to a box-sweep
    // (.fam boxes → for each box, .fam cb + .fam l). Each capture is now
    // stored as a precise VBloodInstance with its exact (box, index) and
    // per-entry shiny/school/primal/level/prestige metadata, so the chip
    // view can render one row per CAPTURE (the user explicitly picks
    // which variant to summon) and the summon path can target the exact
    // index without the BestBox-guessing the 0.10.0..0.10.8 search-based
    // scan relied on.
    //
    // The four HasBasic/HasShiny/HasPrimal/HasPrimalShiny flags are now
    // derived from the Instances list. BestBox is the box of the FIRST
    // captured instance — kept around so the existing Familiar Browser
    // overlay's name-based smart-summon path still has a fallback target
    // when the caller doesn't care which variant.
    public struct VBloodInstance
    {
        public string Box;          // box this capture lives in (used by .fam cb)
        public int    Index;        // 1-based slot inside Box (used by .fam b N)
        public int    Level;        // current familiar level (0 if not parsed)
        public int    Prestige;     // prestige tier (0 if none)
        public bool   IsShiny;      // shiny buff present
        public string ShinySchool;  // "Storm" / "Frost" / etc. (empty if not shiny or color unknown)
        public bool   IsPrimal;     // localized name carried the "Primal " prefix
    }

    public struct VBloodCaptureStatus
    {
        public string Name;                                                    // canonical basename (matches VBloodRegistry.All entry)
        public System.Collections.Generic.List<VBloodInstance> Instances;       // 0..N captures of this V-Blood across all boxes
        public System.DateTime LastScanAt;                                      // when the scanner last updated this slot

        // Derived convenience flags — read by the chip view's
        // sort/filter buttons and by the legacy summon fallback.
        public bool HasBasic         => HasVariant(isShiny: false, isPrimal: false);
        public bool HasShiny         => HasVariant(isShiny: true,  isPrimal: false);
        public bool HasPrimal        => HasVariant(isShiny: false, isPrimal: true);
        public bool HasPrimalShiny   => HasVariant(isShiny: true,  isPrimal: true);
        public string ShinySchool        => FirstSchool(isShiny: true,  isPrimal: false);
        public string PrimalShinySchool  => FirstSchool(isShiny: true,  isPrimal: true);

        /// <summary>Box of the first capture in any variant — used as a
        /// fallback summon target by the Familiar Browser overlay's
        /// name-based path. Empty when no instances.</summary>
        public string BestBox
        {
            get
            {
                if (Instances == null || Instances.Count == 0) return "";
                return Instances[0].Box;
            }
        }

        /// <summary>1-based index of the first capture — paired with
        /// BestBox for legacy summon path. 0 when no instances.</summary>
        public int BestIndex
        {
            get
            {
                if (Instances == null || Instances.Count == 0) return 0;
                return Instances[0].Index;
            }
        }

        private bool HasVariant(bool isShiny, bool isPrimal)
        {
            if (Instances == null) return false;
            foreach (var i in Instances)
                if (i.IsShiny == isShiny && i.IsPrimal == isPrimal) return true;
            return false;
        }

        private string FirstSchool(bool isShiny, bool isPrimal)
        {
            if (Instances == null) return "";
            foreach (var i in Instances)
                if (i.IsShiny == isShiny && i.IsPrimal == isPrimal && !string.IsNullOrEmpty(i.ShinySchool))
                    return i.ShinySchool;
            return "";
        }

        /// <summary>Look up the captured instance for an explicit
        /// (isShiny, isPrimal) target. Returns false when no such variant
        /// is captured.</summary>
        public bool TryGetVariant(bool isShiny, bool isPrimal, out VBloodInstance result)
        {
            if (Instances != null)
            {
                foreach (var i in Instances)
                    if (i.IsShiny == isShiny && i.IsPrimal == isPrimal)
                    { result = i; return true; }
            }
            result = default;
            return false;
        }
    }

    // Keyed by canonical basename (VBloodRegistry.All entry). Read by the
    // V-Bloods tab; written by VBloodScannerService.UpdateVBloodSlot.
    public static System.Collections.Generic.Dictionary<string, VBloodCaptureStatus> VBloodCollection { get; private set; }
        = new System.Collections.Generic.Dictionary<string, VBloodCaptureStatus>(System.StringComparer.OrdinalIgnoreCase);
    public static event Action VBloodCollectionChanged;

    internal static void UpdateVBloodSlot(in VBloodCaptureStatus s)
    {
        if (string.IsNullOrEmpty(s.Name)) return;
        VBloodCollection[s.Name] = s;
        Fire(VBloodCollectionChanged);
    }

    internal static void ResetVBloodCollection()
    {
        VBloodCollection.Clear();
        Fire(VBloodCollectionChanged);
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

    /// <summary>0.15.0: re-run per-feature availability detection. Called by
    /// EclipseProtocolService AFTER each ProgressToClient broadcast has
    /// updated the individual state structs. The detection rule is
    /// sticky-Enabled: once we see non-zero data for a system, the flag
    /// flips Enabled and stays Enabled for the session. Pre-settling we
    /// keep every flag Enabled so the UI doesn't briefly hide things at
    /// world-entry. After FEATURE_DETECTION_SETTLING_SECONDS, any system
    /// that never showed non-zero data gets marked Disabled.</summary>
    internal static void RecomputeFeatureFlagsFromLatest()
    {
        var f = FeatureFlags;
        bool anyChange = false;

        // Initialize the settling timer on the FIRST ProgressToClient. The
        // EclipseProtocolService only calls this method after a successful
        // MAC-verified ProgressToClient parse, so reaching here is itself
        // proof that the structured pipeline is live.
        if (_firstProgressAt < 0f) _firstProgressAt = UnityEngine.Time.realtimeSinceStartup;
        f.ObservedBroadcasts++;

        // Sticky-Enabled per system. The signals are chosen to be
        // unambiguously non-zero whenever the system is enabled server-side
        // — see explore-agent's audit of Bloodcraft/Services/EclipseService.cs
        // GetXxxData. Each GetXxxData returns all zeros when its
        // ConfigService.XxxSystem flag is false, so any non-zero value here
        // is positive proof the system is enabled.
        if (!f.Leveling   && Experience.Level > 0)                       { f.Leveling   = true; anyChange = true; }
        if (!f.Class      && Experience.Class != PlayerClass.None)       { f.Class      = true; anyChange = true; }
        if (!f.Legacy     && (Legacy.Level > 0 || Legacy.Prestige > 0
                              || !string.IsNullOrEmpty(Legacy.BonusStatsRaw)))
                                                                          { f.Legacy     = true; anyChange = true; }
        if (!f.Expertise  && (Expertise.Level > 0 || Expertise.Prestige > 0
                              || !string.IsNullOrEmpty(Expertise.BonusStatsRaw)))
                                                                          { f.Expertise  = true; anyChange = true; }
        if (!f.Familiar   && Familiar.HasActive)                          { f.Familiar   = true; anyChange = true; }
        if (!f.Profession && (Profession.EnchantingLevel    > 0
                              || Profession.AlchemyLevel       > 0
                              || Profession.HarvestingLevel    > 0
                              || Profession.BlacksmithingLevel > 0
                              || Profession.TailoringLevel     > 0
                              || Profession.WoodcuttingLevel   > 0
                              || Profession.MiningLevel        > 0
                              || Profession.FishingLevel       > 0))
                                                                          { f.Profession = true; anyChange = true; }
        if (!f.Quest      && (DailyQuest.Goal > 0 || WeeklyQuest.Goal > 0)) { f.Quest    = true; anyChange = true; }
        if (!f.ShiftSlot  && ShiftSpell.SpellIndex > 0)                    { f.ShiftSlot = true; anyChange = true; }

        // Settling complete? After enough wall-time has passed, lock in
        // any systems that never showed non-zero data as Disabled.
        bool settled = (UnityEngine.Time.realtimeSinceStartup - _firstProgressAt) >= FEATURE_DETECTION_SETTLING_SECONDS;
        if (settled && !f.SettlingComplete)
        {
            f.SettlingComplete = true;
            anyChange = true;
        }

        if (anyChange)
        {
            FeatureFlags = f;
            BloodCraftHub.Utils.LogUtils.LogDiagnostic(
                $"FeatureFlags update (broadcast #{f.ObservedBroadcasts}, settled={f.SettlingComplete}): "
              + $"Leveling={f.Leveling} Legacy={f.Legacy} Expertise={f.Expertise} Familiar={f.Familiar} "
              + $"Class={f.Class} Profession={f.Profession} Quest={f.Quest} ShiftSlot={f.ShiftSlot}");
            Fire(FeatureFlagsChanged);
        }
    }

    /// <summary>Reset detection state on session boundary (world exit / re-login).
    /// Called from Plugin or EclipseProtocolService.Reset.</summary>
    internal static void ResetFeatureFlags()
    {
        FeatureFlags = DefaultFeatureFlags();
        _firstProgressAt = -1f;
        Fire(FeatureFlagsChanged);
    }

    /// <summary>0.15.0: pre-settling defaults. Every system reads as Enabled
    /// so the UI doesn't dim/hide things during the first ~30 s of world
    /// entry. After settling, any flag that's still false from this
    /// default would have been overwritten in RecomputeFeatureFlagsFromLatest
    /// if its system was active — so flags still false at settling time
    /// genuinely never observed data.</summary>
    private static ServerFeatureFlags DefaultFeatureFlags() => new ServerFeatureFlags
    {
        Leveling   = false,
        Legacy     = false,
        Expertise  = false,
        Familiar   = false,
        Class      = false,
        Profession = false,
        Quest      = false,
        ShiftSlot  = false,
        SettlingComplete = false,
        ObservedBroadcasts = 0,
    };

    /// <summary>0.15.0: resolve the EFFECTIVE per-system availability. While
    /// settling, all systems read Enabled so the UI doesn't transiently
    /// hide things. After settling, systems where we never observed
    /// non-zero data read Disabled. Helper to keep call sites readable
    /// — direct FeatureFlags.X queries forget the "pre-settling = enabled"
    /// rule and produce false negatives.</summary>
    public static bool IsSystemEnabled(SystemKind kind)
    {
        var f = FeatureFlags;
        if (!f.SettlingComplete) return true;
        return kind switch
        {
            SystemKind.Leveling   => f.Leveling,
            SystemKind.Legacy     => f.Legacy,
            SystemKind.Expertise  => f.Expertise,
            SystemKind.Familiar   => f.Familiar,
            SystemKind.Class      => f.Class,
            SystemKind.Profession => f.Profession,
            SystemKind.Quest      => f.Quest,
            SystemKind.ShiftSlot  => f.ShiftSlot,
            _ => true,
        };
    }

    public enum SystemKind
    {
        Leveling,
        Legacy,
        Expertise,
        Familiar,
        Class,
        Profession,
        Quest,
        ShiftSlot,
    }

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
