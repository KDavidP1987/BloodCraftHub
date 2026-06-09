using System;
using System.Collections.Generic;
using BloodCraftHub.Resources;
using BloodCraftHub.Utils;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace BloodCraftHub.Services.Uriel;

// 0.26: CLIENT-SIDE detection of Uriel-shared containers / prison cells — no server query needed
// (handoff §2). A placed castle container always carries CastleHeartConnection; Uriel SEVERS it when
// the container is shared (CastleHeartEntity._Entity becomes Entity.Null), while a normal world chest
// has no CastleHeartConnection at all and a private placed container's heart is non-null. So
// "InventoryOwner + CastleHeartConnection present + heart entity == Null" uniquely identifies a
// Uriel-shared container — readable straight off replicated client state.
//
// CRASH DISCIPLINE (docs/LESSONS_LEARNED + the 0.18.4 server-switch crash):
//   • Plugin.IsClientNull() + world.IsCreated gate before any ECS access.
//   • Lazy ComponentType (never a static initializer — TypeManager NRE at Plugin.Load).
//   • em.Exists() before touching any entity; everything in try/catch.
//   • The EntityQuery is rebuilt after a world teardown (OnWorldTeardown drops _queryReady) — reusing
//     a query from a disposed world is a native crash. Registered in InitializationPatch's teardown.
//   • A fault circuit-breaker disables detection for the session after repeated failures (the badge UI
//     is cosmetic — never worth a crash). NativeArray disposed in finally.
//
// ⚠ Entity ids are NOT cached across calls: Uriel REBUILDS a storage entity on share/unshare (new id),
// so every scan re-resolves from the live query.
internal static class SharedContainerDetector
{
    public enum ContainerKind { Chest, PrisonCell }

    public readonly struct SharedContainer
    {
        public readonly Entity Entity;
        public readonly ContainerKind Kind;
        public readonly string Name;
        public readonly float Distance;   // metres from the local player
        public SharedContainer(Entity entity, ContainerKind kind, string name, float distance)
        { Entity = entity; Kind = kind; Name = name; Distance = distance; }
    }

    private static EntityQuery _query;
    private static bool _queryReady;
    private static int _faults;
    private static bool _disabled;
    private const int FAULT_LIMIT = 5;

    /// <summary>True until the detector self-disables after repeated faults.</summary>
    public static bool Available => !_disabled;

    /// <summary>Drop the cached query on leave-game / server-switch (called from the
    /// ClientBootstrapSystem.OnDestroy teardown hook). Pure field reset; the query rebuilds against the
    /// new world on the next scan. Mirrors PlayerRosterService.OnWorldTeardown.</summary>
    internal static void OnWorldTeardown() => _queryReady = false;

    /// <summary>Scan for Uriel-shared containers within <paramref name="radius"/> metres of the local
    /// player, nearest first. Best-effort: returns empty on any failure rather than throwing into the UI.</summary>
    public static List<SharedContainer> ScanNearby(float radius)
    {
        var result = new List<SharedContainer>();
        if (_disabled) return result;
        try
        {
            if (Plugin.IsClientNull()) return result;
            var em = Plugin.EntityManager;
            var world = em.World;
            if (world == null || !world.IsCreated) return result;

            var character = Plugin.LocalCharacter;
            if (character == Entity.Null || !em.Exists(character) || !character.Has<Translation>()) return result;
            float3 playerPos = character.Read<Translation>().Value;

            if (!_queryReady)
            {
                _query = em.CreateEntityQuery(
                    ComponentType.ReadOnly(Il2CppType.Of<InventoryOwner>()),
                    ComponentType.ReadOnly(Il2CppType.Of<CastleHeartConnection>()));
                _queryReady = true;
            }

            NativeArray<Entity> arr;
            try { arr = _query.ToEntityArray(Allocator.Temp); }
            catch { _queryReady = false; return result; }   // rebuild next time (e.g. after world reload)

            float r2 = radius * radius;
            try
            {
                foreach (var e in arr)
                {
                    if (!em.Exists(e) || !e.Has<CastleHeartConnection>()) continue;

                    // The unique Uriel-shared signal: the castle-heart link is severed (nulled) while shared.
                    var heart = e.Read<CastleHeartConnection>().CastleHeartEntity._Entity;
                    if (heart != Entity.Null) continue;   // private (still castle-connected) — skip

                    float3 pos = e.Has<Translation>() ? e.Read<Translation>().Value : playerPos;
                    float d2 = math.distancesq(pos, playerPos);
                    if (d2 > r2) continue;

                    var kind = e.Has<Prisonstation>() ? ContainerKind.PrisonCell : ContainerKind.Chest;
                    result.Add(new SharedContainer(e, kind, ResolveName(e, kind), (float)math.sqrt(d2)));
                }
            }
            finally { arr.Dispose(); }

            result.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        }
        catch (Exception ex)
        {
            if (++_faults >= FAULT_LIMIT)
            {
                _disabled = true;
                LogUtils.LogWarning($"SharedContainerDetector: disabling shared-container detection for this session after {_faults} faults (last: {ex.Message}).");
            }
        }
        return result;
    }

    private static string ResolveName(Entity e, ContainerKind kind)
    {
        try
        {
            if (e.Has<PrefabGUID>())
            {
                int hash = e.Read<PrefabGUID>().GuidHash;
                if (PrefabNameResolver.TryGet(hash, out var raw) && !string.IsNullOrEmpty(raw))
                    return Prettify(raw);
            }
        }
        catch { /* fall through to the generic label */ }
        return kind == ContainerKind.PrisonCell ? "Prison cell" : "Container";
    }

    // The spawnable prefab names read like "TM_Castle_Container_Standard_Wood" — trim the common
    // prefixes and underscores into something readable for the badge list. Best-effort cosmetic.
    private static string Prettify(string raw)
    {
        string s = raw;
        foreach (var prefix in new[] { "TM_", "BP_", "Castle_", "Container_" })
            if (s.StartsWith(prefix, StringComparison.Ordinal)) s = s.Substring(prefix.Length);
        return s.Replace('_', ' ').Trim();
    }
}
