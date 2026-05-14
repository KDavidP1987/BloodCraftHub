using System;
using System.Collections.Generic;
using System.Linq;
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
