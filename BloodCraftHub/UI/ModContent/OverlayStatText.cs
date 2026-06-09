using System.Collections.Generic;
using BloodCraftHub.Config;

namespace BloodCraftHub.UI.ModContent;

// B5 (0.19): optional Eclipse-style stat-name abbreviation for the overlay bonus-stat
// sub-rows. Long Bloodcraft stat names ("PhysicalCriticalStrikeChance") wrap to multiple
// lines inside a narrow overlay and the wrapped height can overlap the progress bar above
// / the row below — especially at Large / X-Large overlay text scale. Shortening the names
// (gated by Settings.ShowOverlayStatAcronyms, default OFF) keeps each stat on one line so the
// sub-row stays compact. Display-only — never touches the underlying parsed values.
internal static class OverlayStatText
{
    // Both the long WeaponStatType / BloodStatType spellings AND the short WeaponBonusStat /
    // BloodBonusStat spellings are mapped, because the `.wep get` / `.bl get` chat replies can
    // surface either form depending on the buff name Bloodcraft prints. Ordered longest-key-first
    // at apply time so a longer name is replaced before any shorter name that is a prefix of it.
    private static readonly Dictionary<string, string> Map = new()
    {
        // Weapon expertise stats
        { "PhysicalCriticalStrikeChance", "PhysCritCh" },
        { "PhysicalCriticalStrikeDamage", "PhysCritDmg" },
        { "SpellCriticalStrikeChance",    "SpellCritCh" },
        { "SpellCriticalStrikeDamage",    "SpellCritDmg" },
        { "PhysicalCritChance",           "PhysCritCh" },
        { "PhysicalCritDamage",           "PhysCritDmg" },
        { "SpellCritChance",              "SpellCritCh" },
        { "SpellCritDamage",              "SpellCritDmg" },
        { "PrimaryAttackSpeed",           "AtkSpd" },
        { "PhysicalLifeLeech",            "PhysLL" },
        { "SpellLifeLeech",               "SpellLL" },
        { "PrimaryLifeLeech",             "PrimLL" },
        { "MovementSpeed",                "MoveSpd" },
        { "PhysicalPower",                "PhysPwr" },
        { "SpellPower",                   "SpellPwr" },
        { "MaxHealth",                    "HP" },
        // Blood legacy stats
        { "UltimateCooldownRecoveryRate", "UltCDR" },
        { "WeaponCooldownRecoveryRate",   "WepCDR" },
        { "SpellCooldownRecoveryRate",    "SpellCDR" },
        { "CorruptionDamageReduction",    "CorrDmgRdc" },
        { "AbilityAttackSpeed",           "AbilAtkSpd" },
        { "PhysicalResistance",           "PhysRes" },
        { "SpellResistance",              "SpellRes" },
        { "HealingReceived",              "HealRecv" },
        { "DamageReduction",              "DmgRdc" },
        { "ReducedBloodDrain",            "BloodDrain" },
        { "ResourceYield",                "ResYield" },
        { "MinionDamage",                 "MinionDmg" },
    };

    // Pre-sorted longest-key-first so substrings can't shadow longer names.
    private static readonly List<KeyValuePair<string, string>> Ordered = BuildOrdered();
    private static List<KeyValuePair<string, string>> BuildOrdered()
    {
        var list = new List<KeyValuePair<string, string>>(Map);
        list.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
        return list;
    }

    /// <summary>Abbreviate Bloodcraft stat names in <paramref name="text"/> when the acronyms
    /// setting is on; otherwise returns the text unchanged. Safe on null/empty.</summary>
    public static string Format(string text)
    {
        if (string.IsNullOrEmpty(text) || !Settings.ShowOverlayStatAcronyms) return text;
        foreach (var kv in Ordered)
            if (text.Contains(kv.Key)) text = text.Replace(kv.Key, kv.Value);
        return text;
    }
}
