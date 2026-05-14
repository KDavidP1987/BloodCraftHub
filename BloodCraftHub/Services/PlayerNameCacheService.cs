using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BloodCraftHub.Utils;

namespace BloodCraftHub.Services;

// In-memory cache of player names that have appeared in chat replies.
//
// Bloodcraft doesn't push a player roster to the client. To get autocomplete
// for admin forms we accumulate names from observed sources:
//   - Form submissions that include a PlayerNameField (caller hint that they're
//     a real name).
//   - Inbound chat parsing for ".clan list" / ".clan members" / etc. responses
//     (wired by MessageService_Processing in a later phase).
//   - Manual additions via Add (e.g., the user types a name once - it's
//     remembered for next time).
//
// The cache is best-effort and lives only for the current session. No
// persistence to disk yet (Phase 5e+ if useful).
public static class PlayerNameCacheService
{
    private static readonly SortedSet<string> _names =
        new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

    public static event Action NamesChanged;

    /// <summary>Snapshot of currently-known names (alphabetical).</summary>
    public static IReadOnlyList<string> KnownNames => _names.ToList();

    /// <summary>Add a name to the cache. No-op if empty or already present.</summary>
    public static bool Add(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = name.Trim();
        if (!_names.Add(trimmed)) return false;
        try { NamesChanged?.Invoke(); }
        catch (Exception ex) { LogUtils.LogError($"PlayerNameCache NamesChanged threw: {ex}"); }
        return true;
    }

    /// <summary>Drop all known names. Useful when switching servers.</summary>
    public static void Clear()
    {
        if (_names.Count == 0) return;
        _names.Clear();
        try { NamesChanged?.Invoke(); }
        catch (Exception ex) { LogUtils.LogError($"PlayerNameCache NamesChanged threw: {ex}"); }
    }

    // ----- Passive harvesting from inbound chat (Phase 5j) ----------------
    //
    // Bloodcraft + KindredCommands print player names wrapped in colored TMP
    // tags - e.g. `<color=#ffd700>SomeName</color>` or
    // `<color=white>SomeName</color> joined`. The regex below targets those
    // exact patterns; it's deliberately conservative (length 3-20, leading
    // letter, no spaces inside) to avoid eating numbers, item names, or
    // section headers.
    //
    // The denylist filters out tokens that look like player names but match
    // recurring server-output words; this list is small on purpose - we'd
    // rather over-cache (harmless, autocomplete dropdown can be filtered)
    // than miss real names.
    private static readonly Regex _colorTokenRegex = new(
        @"<color=[^>]+>([A-Za-z][A-Za-z0-9_]{2,19})</color>",
        RegexOptions.Compiled);
    private static readonly HashSet<string> _denylist = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bloodcraft", "Eclipse", "Server", "Console", "System", "Admin", "Familiar",
        "Familiars", "Boxes", "Box", "Selected", "Available", "None", "All",
        "Online", "Offline", "Joined", "Left", "Kicked", "Banned", "Connected",
        "True", "False", "Daily", "Weekly", "Active", "Inactive", "Combat",
        "Class", "Classes", "Spell", "Spells", "Stats", "Level", "Prestige",
    };

    /// <summary>
    /// Harvest plausible player-name tokens from an inbound chat message and
    /// add them to the cache. Safe to call on every inbound message - no-op if
    /// no candidates match. Heuristic: caller should pass already-trimmed text.
    /// </summary>
    internal static void TryHarvestNames(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            foreach (Match m in _colorTokenRegex.Matches(text))
            {
                var candidate = m.Groups[1].Value;
                if (_denylist.Contains(candidate)) continue;
                Add(candidate);
            }
        }
        catch (Exception ex)
        {
            LogUtils.LogDebug($"PlayerNameCache.TryHarvestNames: {ex.Message}");
        }
    }

    /// <summary>
    /// Ask the server for clan/player info. The inbound regex pipeline (TBD)
    /// pulls names out of the responses and calls Add().
    /// </summary>
    public static void Refresh()
    {
        if (!MessageService.IsInitialized)
        {
            LogUtils.LogWarning("PlayerNameCache.Refresh skipped — MessageService not yet bound.");
            return;
        }
        // .clan list returns clan-name -> leader pairs; .clan members <clan> drills in.
        // For now we just kick the first; subsequent phases parse the response.
        MessageService.EnqueueMessage(".clan list");
        LogUtils.LogInfo("PlayerNameCache: requested .clan list (parsing wired in a later phase).");
    }
}
