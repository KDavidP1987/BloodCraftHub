using System;
using System.Collections.Generic;
using BloodCraftHub.Utils;
using ProjectM;
using ProjectM.Network;
using Unity.Entities;

namespace BloodCraftHub.Services;

// 0.17 (feature/v0.17-standalone-ui): captures chat for the standalone tabbed
// window. SOURCE = the native client's own ClientChatSystem.FormatFullChatMessage
// (postfixed in ClientChatPatch), which hands us the message type, the body
// text, and — crucially — the GAME-RESOLVED sender name. The raw
// ChatMessageServerEvent carries no name; the client resolves it when formatting,
// so hooking the formatter is how we show EVERY player's name (not just the
// local player, which was the limit of the entity-scan approach). READ-ONLY;
// never touches game entities or chat lifetime.
internal static class ChatRelayService
{
    internal enum Channel { Global, Local, Clan, System, Whisper, Other }

    internal readonly struct ChatLine
    {
        public readonly Channel Channel;
        public readonly string Sender;   // game-resolved; empty for system messages
        public readonly string Text;
        public readonly DateTime Received;
        // 0.17.0: whisper conversation partner (the OTHER person). For a received
        // whisper this is the sender; for one WE send it's the recipient. Empty for
        // non-whisper lines. Lets the per-person whisper sub-tabs show both sides.
        public readonly string Partner;

        public ChatLine(Channel channel, string sender, string text, DateTime received, string partner = "")
        {
            Channel = channel; Sender = sender; Text = text; Received = received; Partner = partner ?? string.Empty;
        }
    }

    // B11 (0.19): the SERVER drops old chat after a short window, so the only durable history is what we
    // keep here. Raised 500 → 2000 so a system-message flood (handshake probes, broadcasts) can't push
    // real conversation out of the client window. ~2000 ChatLine structs is trivial memory.
    private const int MaxLines = 2000;
    private static readonly List<ChatLine> _buffer = new(MaxLines + 16);

    internal static event Action<ChatLine> LineCaptured;
    internal static IReadOnlyList<ChatLine> Buffer => _buffer;

    // 0.17.0 whisper/sender targeting. EVERY sender-bearing chat message
    // (Global/Local/Region/Clan/WhisperFrom) carries the sender's NetworkId
    // (ChatMessageServerEvent.FromUser) — that NetworkId IS a valid whisper target.
    // ClientChatPatch enqueues it in arrival order as it pumps the inbound query;
    // CaptureFormatted (which has the GAME-RESOLVED name) pairs the next id with
    // that name into _playerIds. So _playerIds becomes "everyone we've seen speak"
    // → name → whisper target. This is the reliable client-side source for the
    // whisper picker, since the client holds a User entity ONLY for the local
    // player (confirmed via diagnostic) — a roster EntityQuery can't enumerate others.
    private static readonly Queue<NetworkId> _pendingSenderIds = new();
    private static readonly Dictionary<string, NetworkId> _playerIds = new();

    // True for message types that carry a real player sender (so FromUser is a
    // usable whisper target). MUST match the predicate ClientChatPatch enqueues on,
    // so the id queue and the formatter dequeue stay 1:1 aligned.
    internal static bool IsSenderBearing(ServerChatMessageType t) => t switch
    {
        ServerChatMessageType.Global      => true,
        ServerChatMessageType.Local       => true,
        ServerChatMessageType.Region      => true,
        ServerChatMessageType.Team        => true,
        ServerChatMessageType.WhisperFrom => true,
        _                                 => false,
    };

    internal static void EnqueueSenderId(NetworkId fromUser)
    {
        // Cap to avoid unbounded growth if the pairing ever desyncs.
        if (_pendingSenderIds.Count > 64) _pendingSenderIds.Clear();
        _pendingSenderIds.Enqueue(fromUser);
    }

    internal static bool TryGetWhisperTarget(string partner, out NetworkId id)
        => _playerIds.TryGetValue(partner ?? string.Empty, out id);

    // Record a whisper target chosen from the player picker, so reply-send works
    // even before that player has whispered us.
    internal static void RememberWhisperTarget(string partner, NetworkId id)
    {
        if (!string.IsNullOrEmpty(partner)) _playerIds[partner] = id;
    }

    // Everyone we've seen speak (any channel), name → whisper target, excluding
    // the local player, sorted by name. The whisper picker's source.
    internal static List<PlayerRosterService.PlayerRef> GetKnownPlayers()
    {
        var self = LocalPlayerName();
        var list = new List<PlayerRosterService.PlayerRef>();
        foreach (var kv in _playerIds)
        {
            if (!string.IsNullOrEmpty(self) && string.Equals(kv.Key, self, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new PlayerRosterService.PlayerRef(kv.Key, kv.Value));
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

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

    // Called from the FormatFullChatMessage postfix. userName is the game-resolved
    // sender (empty for system messages); text is the message body.
    internal static void CaptureFormatted(ServerChatMessageType messageType, string userName, string text)
    {
        try
        {
            if (string.IsNullOrEmpty(text)) return;
            if (text.Contains(";mac")) return; // safety: never surface signed protocol noise

            var channel = MapChannel(messageType);
            var sender = userName ?? string.Empty;
            // The game hands a bare "?" as the sender for system/lore messages — treat that as no sender
            // so the chat shows the message without a "?:" prefix.
            if (sender.Trim() == "?") sender = string.Empty;

            // Skip an immediate exact duplicate — the native formatter can re-run
            // for the same message (channel/mode re-filter). TIME-BOUNDED so a
            // genuinely repeated message (someone says "lol" twice) still shows;
            // only the formatter's near-instant re-run is dropped.
            if (_buffer.Count > 0)
            {
                var last = _buffer[_buffer.Count - 1];
                if (last.Channel == channel && last.Sender == sender && last.Text == text
                    && (DateTime.Now - last.Received).TotalSeconds < 1.0)
                    return;
            }

            // Pair the sender's NetworkId (enqueued in arrival order by
            // ClientChatPatch for every sender-bearing message) with the resolved
            // name. Dequeue for the SAME predicate the patch enqueues on so the
            // queue stays 1:1 aligned; only store when we actually got a name.
            string partner = string.Empty;
            if (IsSenderBearing(messageType))
            {
                NetworkId id = default; bool hasId = false;
                if (_pendingSenderIds.Count > 0) { id = _pendingSenderIds.Dequeue(); hasId = true; }
                if (hasId && !string.IsNullOrEmpty(sender)) _playerIds[sender] = id;
                if (channel == Channel.Whisper && !string.IsNullOrEmpty(sender)) partner = sender;
            }

            var line = new ChatLine(channel, sender, text, DateTime.Now, partner);
            _buffer.Add(line);
            if (_buffer.Count > MaxLines) _buffer.RemoveRange(0, _buffer.Count - MaxLines);
            LineCaptured?.Invoke(line);
        }
        catch (Exception ex)
        {
            LogUtils.LogDebug($"ChatRelayService.CaptureFormatted: {ex.Message}");
        }
    }

    // Local echo for messages WE send. The server broadcasts our chat to OTHER
    // clients but does NOT echo it back to us (the native client shows your own
    // message via its send path, which our direct injection bypasses). So we add
    // it to the buffer here, with the local player's name, so the sender sees
    // their own message in the tabbed window.
    // partner: for a whisper WE send, the recipient's name (so the echo lands in
    // their sub-tab). Empty for normal channel messages.
    internal static void CaptureLocalEcho(Channel channel, string text, string partner = "")
    {
        try
        {
            if (string.IsNullOrEmpty(text)) return;
            var sender = LocalPlayerName();
            // NO dedup here: this fires exactly once per message YOU send (via
            // SubmitText), so sending the same text twice on purpose ("lol", "lol")
            // must show both. (The earlier dedup is why repeated identical sends
            // appeared to "stop working".)
            var line = new ChatLine(channel, sender, text, DateTime.Now, partner);
            _buffer.Add(line);
            if (_buffer.Count > MaxLines) _buffer.RemoveRange(0, _buffer.Count - MaxLines);
            LineCaptured?.Invoke(line);
        }
        catch (Exception ex)
        {
            LogUtils.LogDebug($"ChatRelayService.CaptureLocalEcho: {ex.Message}");
        }
    }

    /// <summary>0.21: true when a captured sender is the LOCAL player (used by the chat UI's "highlight my
    /// own messages"). Compares against the resolved local character name, case-insensitively.</summary>
    internal static bool IsOwnSender(string sender)
    {
        if (string.IsNullOrEmpty(sender)) return false;
        var me = LocalPlayerName();
        return !string.IsNullOrEmpty(me) && string.Equals(sender.Trim(), me.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string _localName;
    private static string LocalPlayerName()
    {
        if (!string.IsNullOrEmpty(_localName)) return _localName;
        try
        {
            var c = Plugin.LocalCharacter;
            if (c != Entity.Null && c.Has<PlayerCharacter>())
            {
                var n = c.Read<PlayerCharacter>().Name.ToString();
                if (!string.IsNullOrEmpty(n)) _localName = n;
            }
        }
        catch { /* best-effort; echo just shows without a name if unresolved */ }
        return _localName ?? string.Empty;
    }

    internal static void Clear()
    {
        _buffer.Clear();
        _localName = null;
        _pendingSenderIds.Clear();
        _playerIds.Clear();
    }
}
