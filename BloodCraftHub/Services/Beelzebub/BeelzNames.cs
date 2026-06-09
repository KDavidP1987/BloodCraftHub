using System;
using System.Text;

namespace BloodCraftHub.Services.Beelzebub;

// Client-side friendly-name rendering for Beelzebub's raw prefab names.
//
// Beelzebub's api list / api bestiary / api slots emit `un=`/`an=` as the raw
// PrefabGUID name (e.g. CHAR_Blackfang_Morgana_VBlood, AB_Blood_Shadowbolt_Group).
// The server DOES have nicer localized names but doesn't expose them in those
// reads (a noted contract gap), so we humanize prefab strings here for display —
// mirroring Beelzebub's own AbilityMetadataService.HumanizeUnitPrefab. Raw values
// stay the canonical keys for commands; only the displayed text changes.
internal static class BeelzNames
{
    private static readonly string[] UnitSuffixes =
        { "_VBlood", "_Standard", "_Servant", "_Minion", "_Base", "_Unique", "_Team", "_Trader" };
    private static readonly string[] AbilitySuffixes =
        { "_AbilityGroup", "_Group", "_Cast", "_Channeled", "_Channel", "_Ability" };

    /// <summary>Friendly unit name from a CHAR_… prefab.</summary>
    public static string Unit(string prefab)
    {
        if (string.IsNullOrWhiteSpace(prefab)) return "Unknown";
        string s = prefab;
        s = StripPrefix(s, "CHAR_");
        s = StripPrefix(s, "VBloodUnit_");
        foreach (var suf in UnitSuffixes)
            if (s.EndsWith(suf, StringComparison.OrdinalIgnoreCase)) { s = s.Substring(0, s.Length - suf.Length); break; }
        return Humanize(s);
    }

    /// <summary>Friendly ability name from an AB_… prefab. Prefer the server's
    /// pre-humanized label when one was fetched via api info.</summary>
    public static string Ability(string prefab)
    {
        if (string.IsNullOrWhiteSpace(prefab)) return "Unknown";
        string s = prefab;
        s = StripPrefix(s, "AB_");
        s = StripPrefix(s, "Ability_");
        foreach (var suf in AbilitySuffixes)
            if (s.EndsWith(suf, StringComparison.OrdinalIgnoreCase)) { s = s.Substring(0, s.Length - suf.Length); break; }
        return Humanize(s);
    }

    private static string StripPrefix(string s, string prefix)
        => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? s.Substring(prefix.Length) : s;

    // Replace separators, split CamelCase / digit boundaries, collapse whitespace.
    private static string Humanize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "Unknown";
        s = s.Replace('_', ' ').Replace('-', ' ');

        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (i > 0)
            {
                char p = s[i - 1];
                bool boundary =
                    (char.IsLower(p) && char.IsUpper(c)) ||                       // aB
                    (char.IsLetter(p) && char.IsDigit(c)) ||                      // a1
                    (char.IsDigit(p) && char.IsLetter(c)) ||                      // 1a
                    (char.IsUpper(p) && char.IsUpper(c) && i + 1 < s.Length && char.IsLower(s[i + 1])); // ABc -> A Bc
                if (boundary && p != ' ' && c != ' ') sb.Append(' ');
            }
            sb.Append(c);
        }

        // Collapse runs of spaces.
        var outStr = sb.ToString();
        while (outStr.Contains("  ")) outStr = outStr.Replace("  ", " ");
        return outStr.Trim();
    }
}
