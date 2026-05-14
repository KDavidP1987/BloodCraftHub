using Il2CppInterop.Runtime;
using Unity.Entities;
using UnityEngine;

namespace BloodCraftHub.Utils;

// Small extension helpers used across the UI framework.
//
// The full BloodCraftUI Extensions.cs is much larger and depends on ProjectM /
// Stunlock types (Entity.Read<T>/Has<T>/Write<T>, prefab lookups, team helpers,
// etc.). Port those over when we wire up real ECS reads. For now we only need
// what the copied UI framework references.
public static class Extensions
{
    /// <summary>Return a copy of <paramref name="baseColor"/> with its alpha replaced.</summary>
    public static Color GetTransparent(this Color baseColor, float alpha = 0.7f)
    {
        return new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
    }

    /// <summary>
    /// True iff <paramref name="entity"/> carries component <typeparamref name="T"/>.
    /// Ported from BloodCraftUI's Utils/Extensions.cs.
    /// </summary>
    public static bool Has<T>(this Entity entity)
    {
        return Plugin.EntityManager.HasComponent(entity, new ComponentType(Il2CppType.Of<T>()));
    }
}
