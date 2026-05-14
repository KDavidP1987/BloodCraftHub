using System;
using System.Collections.Generic;
using BloodCraftHub.Utils;
using ProjectM;
using Stunlock.Core;

namespace BloodCraftHub.Resources;

// Looks up the name of a V Rising prefab by its PrefabGUID hash.
//
// V Rising's PrefabCollectionSystem owns a SpawnableNameToPrefabGuidDictionary
// keyed by *name string* -> PrefabGUID. To go in the other direction we walk
// it once at first call, invert it into a hash->name map, and cache.
//
// PORT REFERENCE: LearningMods/Eclipse-main/Services/LocalizationService.cs
// (the InitializePrefabGuidNames + GetPrefabName pattern). We don't use the
// localized-string layer Eclipse adds - just the raw spawnable name, which is
// readable enough for shift-spell display purposes.
public static class PrefabNameResolver
{
    private static Dictionary<int, string> _hashToName;

    /// <summary>
    /// True once we've successfully built the reverse map. Until then, TryGet
    /// returns false for everything. Safe to call from any thread, but the
    /// initial build runs on first hit.
    /// </summary>
    public static bool IsReady => _hashToName != null;

    /// <summary>
    /// Returns true and writes a human-readable name if the prefab is known.
    /// On any error (system not yet up, prefab not in registry) returns false
    /// without throwing.
    /// </summary>
    public static bool TryGet(int hash, out string name)
    {
        name = null;
        try
        {
            if (_hashToName == null && !TryBuild())
                return false;
            return _hashToName.TryGetValue(hash, out name) && !string.IsNullOrEmpty(name);
        }
        catch (Exception ex)
        {
            LogUtils.LogDebug($"PrefabNameResolver.TryGet({hash}): {ex.Message}");
            return false;
        }
    }

    private static bool TryBuild()
    {
        if (Plugin.IsClientNull()) return false;
        var sys = Plugin.EntityManager.World?.GetExistingSystemManaged<PrefabCollectionSystem>();
        if (sys == null) return false;

        var dict = sys.SpawnableNameToPrefabGuidDictionary;
        if (dict == null || dict.Count == 0) return false;

        var built = new Dictionary<int, string>(dict.Count);
        foreach (var kvp in dict)
        {
            // kvp.Key is the spawnable name (string); kvp.Value is the PrefabGUID.
            // The GuidHash int is what arrives in Eclipse-protocol ShiftSpellState.
            if (!string.IsNullOrEmpty(kvp.Key))
                built[kvp.Value.GuidHash] = kvp.Key;
        }
        _hashToName = built;
        LogUtils.LogInfo($"PrefabNameResolver: built map of {built.Count} prefab name(s).");
        return true;
    }
}
