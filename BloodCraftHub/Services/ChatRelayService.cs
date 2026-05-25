using System;
using System.Collections.Generic;
using BloodCraftHub.Utils;
using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace BloodCraftHub.Services;

// 0.17 (feature/v0.17-standalone-ui): captures EVERY inbound chat message the
// client receives, across all channels, into a persistent rolling buffer so the
// standalone tabbed chat window can render and organize them. READ-ONLY — it
// never consumes or destroys chat entities (ClientChatPatch still owns that).
//
// Increment 2b adds best-effort SENDER NAME resolution: the message carries the
// sender's User + Character network ids, which we match against the User /
// PlayerCharacter entities the client currently knows about. This resolves the
// local player and any client-visible users; senders the client has no entity
// for (e.g. a distant global/whisper) simply render without a name. Fully
// guarded — name resolution can never disrupt chat capture or display.
internal static class ChatRelayService
{
    internal enum Channel { Global, Local, Clan, System, Whisper, Other }

    internal readonly struct ChatLine
    {
        public readonly Channel Channel;
        public readonly ServerChatMessageType RawType;
        public readonly string Text;
        public readonly DateTime Received;     // local clock time the client saw it
        public readonly NetworkId FromUser;    // sender's user network id (for name resolution)
        public readonly NetworkId FromCharacter;

        public ChatLine(Channel channel, ServerChatMessageType rawType, string text, DateTime received,
                        NetworkId fromUser, NetworkId fromCharacter)
        {
            Channel = channel; RawType = rawType; Text = text; Received = received;
            FromUser = fromUser; FromCharacter = fromCharacter;
        }
    }

    private const int MaxLines = 500;
    private static readonly List<ChatLine> _buffer = new(MaxLines + 16);

    internal static event Action<ChatLine> LineCaptured;
    internal static IReadOnlyList<ChatLine> Buffer => _buffer;

    internal static Channel MapChannel(ServerChatMessageType t) => t switch
    {
        ServerChatMessageType.Global      => Channel.Global,
        ServerChatMessageType.Local       => Channel.Local,
        ServerChatMessageType.Region      => Channel.Local,
        ServerChatMessageType.Team        => Channel.Clan,
        ServerChatMessageType.System      => Channel.System,
        ServerChatMessageType.Lore        => Channel.System,
        ServerChatMessageType.WhisperFrom => Channel.Whisper,
        ServerChatMessageType.WhisperTo   => Channel.Whisper,
        _                                 => Channel.Other,
    };

    internal static void Capture(ChatMessageServerEvent ev)
    {
        try
        {
            string text = ev.MessageText.Value;
            if (string.IsNullOrEmpty(text)) return;
            if (text.Contains(";mac")) return; // signed Eclipse-protocol payload, not chat

            var line = new ChatLine(MapChannel(ev.MessageType), ev.MessageType, text, DateTime.Now,
                                    ev.FromUser, ev.FromCharacter);
            _buffer.Add(line);
            if (_buffer.Count > MaxLines) _buffer.RemoveRange(0, _buffer.Count - MaxLines);
            LineCaptured?.Invoke(line);
        }
        catch (Exception ex)
        {
            LogUtils.LogDebug($"ChatRelayService.Capture: {ex.Message}");
        }
    }

    internal static void Clear()
    {
        _buffer.Clear();
        _nameCache.Clear();
    }

    // ---- sender-name resolution (best-effort, cached, throttled) ----

    private static EntityQuery _userQuery;
    private static EntityQuery _charQuery;
    private static bool _queriesReady;
    private static readonly Dictionary<long, string> _nameCache = new();
    private static double _lastScan;

    // Pack a (Normal) network id into a stable scalar key. Player users/characters
    // are Normal-type ids (Index + Generation), which is all we need to match.
    private static long Key(NetworkId n) => ((long)n.Normal_Index << 8) | (byte)n.Normal_Generation;

    // Returns the sender's display name, or null if the client can't resolve it.
    internal static string ResolveName(NetworkId fromUser, NetworkId fromCharacter)
    {
        try
        {
            long ku = Key(fromUser);
            if (_nameCache.TryGetValue(ku, out var nm) && !string.IsNullOrEmpty(nm)) return nm;
            long kc = Key(fromCharacter);
            if (_nameCache.TryGetValue(kc, out nm) && !string.IsNullOrEmpty(nm)) return nm;

            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (now - _lastScan > 3.0)
            {
                _lastScan = now;
                Rescan();
                if (_nameCache.TryGetValue(ku, out nm) && !string.IsNullOrEmpty(nm)) return nm;
                if (_nameCache.TryGetValue(kc, out nm) && !string.IsNullOrEmpty(nm)) return nm;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void Rescan()
    {
        try
        {
            var em = Plugin.EntityManager;
            if (!_queriesReady)
            {
                _userQuery = em.CreateEntityQuery(ComponentType.ReadOnly(Il2CppType.Of<User>()));
                _charQuery = em.CreateEntityQuery(ComponentType.ReadOnly(Il2CppType.Of<PlayerCharacter>()));
                _queriesReady = true;
            }

            var users = _userQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < users.Length; i++)
                {
                    var e = users[i];
                    if (!e.Has<NetworkId>()) continue;
                    var name = e.Read<User>().CharacterName.ToString();
                    if (!string.IsNullOrEmpty(name)) _nameCache[Key(e.Read<NetworkId>())] = name;
                }
            }
            finally { users.Dispose(); }

            var chars = _charQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < chars.Length; i++)
                {
                    var e = chars[i];
                    if (!e.Has<NetworkId>()) continue;
                    var name = e.Read<PlayerCharacter>().Name.ToString();
                    if (!string.IsNullOrEmpty(name)) _nameCache[Key(e.Read<NetworkId>())] = name;
                }
            }
            finally { chars.Dispose(); }
        }
        catch (Exception ex)
        {
            _queriesReady = false; // rebuild queries next time (e.g. after a world reload)
            LogUtils.LogDebug($"ChatRelayService.Rescan: {ex.Message}");
        }
    }
}
