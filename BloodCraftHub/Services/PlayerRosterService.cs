using System;
using System.Collections.Generic;
using BloodCraftHub.Utils;
using Il2CppInterop.Runtime;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace BloodCraftHub.Services;

// 0.17.0: enumerate the online players for the whisper-initiate dropdown.
//
// The client holds a `User` entity for every CONNECTED player — this is exactly
// the data the in-game player list (P key) reads — so a client-side query over
// the User component yields the full online roster, not just clan/proximity.
// Each User exposes CharacterName (display) + IsConnected (online) + PlatformId
// (self-exclusion), and the entity carries a NetworkId we use as the whisper
// target (the same NetworkId an incoming whisper's FromUser provides).
internal static class PlayerRosterService
{
    internal readonly struct PlayerRef
    {
        public readonly string Name;
        public readonly NetworkId Id;
        public PlayerRef(string name, NetworkId id) { Name = name; Id = id; }
    }

    private static EntityQuery _userQuery;
    private static bool _queryReady;

    // Online players (excluding self), sorted by name. Best-effort: returns empty
    // on any failure rather than throwing into the UI.
    internal static List<PlayerRef> GetOnlinePlayers()
    {
        var result = new List<PlayerRef>();
        try
        {
            if (Plugin.IsClientNull()) return result;
            var em = Plugin.EntityManager;
            if (!_queryReady)
            {
                _userQuery = em.CreateEntityQuery(ComponentType.ReadOnly(Il2CppType.Of<User>()));
                _queryReady = true;
            }

            ulong selfPlatform = 0;
            try { selfPlatform = MessageService.LocalUser.Read<User>().PlatformId; } catch { /* leave 0 */ }

            NativeArray<Entity> users;
            try { users = _userQuery.ToEntityArray(Allocator.Temp); }
            catch { _queryReady = false; return result; } // rebuild query next time (e.g. after world reload)

            try
            {
                var seen = new HashSet<string>();
                foreach (var e in users)
                {
                    if (!e.Has<User>()) continue;
                    var u = e.Read<User>();
                    if (!u.IsConnected) continue;
                    if (selfPlatform != 0 && u.PlatformId == selfPlatform) continue; // skip ourselves
                    var name = u.CharacterName.ToString();
                    if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                    if (!e.Has<NetworkId>()) continue; // need a whisper target
                    result.Add(new PlayerRef(name, e.Read<NetworkId>()));
                }
            }
            finally { users.Dispose(); }
        }
        catch (Exception ex)
        {
            LogUtils.LogDebug($"PlayerRosterService.GetOnlinePlayers: {ex.Message}");
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }
}
