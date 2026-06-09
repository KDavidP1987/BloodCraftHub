using System;
using System.Collections.Generic;
using BloodCraftHub.Utils;
using Il2CppInterop.Runtime;
using Unity.Entities;
using UnityEngine;

namespace BloodCraftHub.Services;

// 0.18.4: resolve an ability's ICON sprite from its PrefabGUID hash — the same path
// ShiftCooldownService uses for the shift slot (client PrefabCollectionSystem → the prefab entity's
// managed ProjectM.AbilityTooltipData.Icon). Generalized so the Beelz action-bar overlay can show the
// real ability art on its buttons (the captured-ability guid Beelzebub sends as `a=` IS the numeric
// PrefabGUID), making those tiles read like the Shift overlay / the native action bar.
//
// CRASH-GUARDED: a managed read off a recycled/destroyed entity is a finalizer-crash vector (see
// ShiftCooldownService + docs/LESSONS_LEARNED). So: client-null + World + em.Exists guards, everything
// in try/catch, and a global fault limit that disables resolution for the session after repeated
// failures (the icon is cosmetic — never worth a crash). Successes are cached; genuine "no icon"
// prefabs are remembered so we don't re-hammer them. A prefab simply not loaded YET is NOT cached as
// missing, so a later retry can still find it.
internal static class AbilityIconResolver
{
    private static readonly Dictionary<int, Sprite> _cache = new();   // hash -> resolved icon
    private static readonly HashSet<int> _noIcon = new();             // prefab found but carries no icon
    private static ComponentType? _tooltipCT;
    private static int _faults;
    private static bool _disabled;
    private const int FAULT_LIMIT = 8;

    /// <summary>True (with a non-null sprite) if the ability prefab's icon is known. Returns false while
    /// the prefab isn't loaded yet (safe to retry later), for a prefab with no icon, or if resolution
    /// has been disabled after repeated faults.</summary>
    public static bool TryGetIcon(int prefabHash, out Sprite icon)
    {
        icon = null;
        if (prefabHash == 0 || _disabled) return false;
        if (_cache.TryGetValue(prefabHash, out icon) && icon != null) return true;
        if (_noIcon.Contains(prefabHash)) return false;

        try
        {
            if (Plugin.IsClientNull()) return false;
            var em = Plugin.EntityManager;
            var world = em.World;
            if (world == null || !world.IsCreated) return false;

            var prefabSys = world.GetExistingSystemManaged<ProjectM.PrefabCollectionSystem>();
            if (prefabSys == null) return false;

            var guid = new Stunlock.Core.PrefabGUID(prefabHash);
            // Not loaded yet → return false WITHOUT caching as missing, so a later tick can retry.
            if (!prefabSys._PrefabGuidToEntityMap.TryGetValue(guid, out var prefabEntity)) return false;
            if (prefabEntity == Entity.Null || !em.Exists(prefabEntity)) return false;

            _tooltipCT ??= ComponentType.ReadOnly(Il2CppType.Of<ProjectM.AbilityTooltipData>());
            if (!em.HasComponent(prefabEntity, _tooltipCT.Value)) { _noIcon.Add(prefabHash); return false; }

            var ttd = em.GetComponentObject<ProjectM.AbilityTooltipData>(prefabEntity, _tooltipCT.Value);
            if (ttd != null && ttd.Icon != null)
            {
                _cache[prefabHash] = ttd.Icon;
                icon = ttd.Icon;
                return true;
            }
            _noIcon.Add(prefabHash);
            return false;
        }
        catch (Exception ex)
        {
            if (++_faults >= FAULT_LIMIT)
            {
                _disabled = true;
                LogUtils.LogWarning($"AbilityIconResolver: disabling ability-icon resolution for this session after {_faults} faults (last: {ex.Message}). Buttons fall back to text labels.");
            }
            return false;
        }
    }

    /// <summary>Convenience overload: parse the numeric PrefabGUID string Beelzebub sends as `a=`.</summary>
    public static bool TryGetIcon(string prefabGuid, out Sprite icon)
    {
        icon = null;
        return !string.IsNullOrEmpty(prefabGuid) && int.TryParse(prefabGuid, out int hash) && TryGetIcon(hash, out icon);
    }
}
