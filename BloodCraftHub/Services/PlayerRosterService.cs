using System;
using System.Collections.Generic;
using BloodCraftHub.Utils;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace BloodCraftHub.Services;

// 0.17.0 / 0.17.3: enumerate online players for the whisper picker + whisper-by-name.
//
// PRIMARY source (0.17.3, #38): the client's `UserInfoElement` buffer, held on the
// `UserInfoBufferSingleton` entity. This is the same server-pushed list the in-game
// social/clan page reads — it is NOT spatially culled, so it contains EVERY connected
// player (matching "in the base game you can whisper anybody on the server"). Each
// element carries Name + NetworkId (the whisper target) + IsConnected + PlatformId
// (self-exclusion).
//
// FALLBACK (0.17.0): a query over `User` entities. The client only replicates User
// entities near the player (spatial culling), so this yields just nearby players —
// used only if the UserInfoElement buffer is unavailable/empty.
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
    private static EntityQuery _userInfoQuery;
    private static bool _userInfoQueryReady;

    // Online players (excluding self), sorted by name. Prefers the full (non-culled)
    // UserInfoElement roster; falls back to the nearby-only User query. Best-effort:
    // returns empty on failure rather than throwing into the UI.
    internal static List<PlayerRef> GetOnlinePlayers()
    {
        var full = GetConnectedUsers();
        if (full.Count > 0) return full;
        return GetNearbyPlayersFromUserQuery();
    }

    // 0.17.3 (#38): the full connected-player roster from the UserInfoElement buffer.
    internal static List<PlayerRef> GetConnectedUsers()
    {
        var result = new List<PlayerRef>();
        try
        {
            if (Plugin.IsClientNull()) return result;
            var em = Plugin.EntityManager;
            if (!_userInfoQueryReady)
            {
                _userInfoQuery = em.CreateEntityQuery(ComponentType.ReadOnly(Il2CppType.Of<UserInfoBufferSingleton>()));
                _userInfoQueryReady = true;
            }

            ulong selfPlatform = 0;
            try { selfPlatform = MessageService.LocalUser.Read<User>().PlatformId; } catch { /* leave 0 */ }

            NativeArray<Entity> ents;
            try { ents = _userInfoQuery.ToEntityArray(Allocator.Temp); }
            catch { _userInfoQueryReady = false; return result; } // rebuild next time (world reload)

            int entCount = ents.Length, bufTotal = 0, connected = 0;
            var sample = new List<string>();
            try
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in ents)
                {
                    if (!em.HasBuffer<UserInfoElement>(e)) continue;
                    var buf = em.GetBuffer<UserInfoElement>(e);
                    bufTotal += buf.Length;
                    for (int i = 0; i < buf.Length; i++)
                    {
                        var ui = buf[i];
                        var nm = ui.Name.ToString();
                        if (sample.Count < 16)
                            sample.Add($"{(string.IsNullOrEmpty(nm) ? "<noname>" : nm)}[conn={ui.IsConnected}]");
                        if (!ui.IsConnected) continue;
                        connected++;
                        if (selfPlatform != 0 && ui.PlatformId == selfPlatform) continue; // skip self
                        if (string.IsNullOrEmpty(nm) || !seen.Add(nm)) continue;
                        result.Add(new PlayerRef(nm, ui.NetworkId));
                    }
                }
            }
            finally { ents.Dispose(); }

            // One-line diagnostic (called on whisper actions, not per-frame): confirms
            // whether the client actually carries the UserInfoElement roster. If
            // entities=0 or buffer=0 in-game, the buffer isn't where we query it.
            LogUtils.LogWarning($"[ChatRoster] UserInfoElement: singletonEntities={entCount}, bufferElems={bufTotal}, " +
                $"connected={connected} -> roster={result.Count}. sample: {string.Join(", ", sample)}");
        }
        catch (Exception ex)
        {
            LogUtils.LogDebug($"PlayerRosterService.GetConnectedUsers: {ex.Message}");
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    // 0.17.0 fallback: nearby players from the (spatially-culled) User entity query.
    private static List<PlayerRef> GetNearbyPlayersFromUserQuery()
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
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in users)
                {
                    if (!e.Has<User>()) continue;
                    var u = e.Read<User>();
                    var nm = u.CharacterName.ToString();
                    if (!u.IsConnected) continue;
                    if (selfPlatform != 0 && u.PlatformId == selfPlatform) continue; // skip ourselves
                    if (string.IsNullOrEmpty(nm) || !seen.Add(nm)) continue;
                    if (!e.Has<NetworkId>()) continue; // need a whisper target
                    result.Add(new PlayerRef(nm, e.Read<NetworkId>()));
                }
            }
            finally { users.Dispose(); }

            LogUtils.LogDebug($"[ChatRoster] User-query fallback -> {result.Count} nearby player(s).");
        }
        catch (Exception ex)
        {
            LogUtils.LogDebug($"PlayerRosterService.GetNearbyPlayersFromUserQuery: {ex.Message}");
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }
}
