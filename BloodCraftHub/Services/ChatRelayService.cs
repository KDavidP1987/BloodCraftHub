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

    private const int MaxLines = 500;
    private static readonly List<ChatLine> _buffer = new(MaxLines + 16);

    internal static event Action<ChatLine> LineCaptured;
    internal static IReadOnlyList<ChatLine> Buffer => _buffer;

    // 0.17.0 whisper targeting. Incoming whispers carry the partner's NetworkId
    // (ChatMessageServerEvent.FromUser); ClientChatPatch enqueues it in order as it
    // pumps the inbound query, and CaptureFormatted (which has the resolved name)
    // pairs the next id with that name. Reply-send then looks the target up by name.
    private static readonly Queue<NetworkId> _pendingWhisperIds = new();
    private static readonly Dictionary<string, NetworkId> _whisperTargets = new();

    internal static void EnqueueWhisperFrom(NetworkId fromUser)
    {
        // Cap to avoid unbounded growth if the pairing ever desyncs.
        if (_pendingWhisperIds.Count > 32) _pendingWhisperIds.Clear();
        _pendingWhisperIds.Enqueue(fromUser);
    }

    internal static bool TryGetWhisperTarget(string partner, out NetworkId id)
        => _whisperTargets.TryGetValue(partner ?? string.Empty, out id);

    // Record a whisper target chosen from the player picker, so reply-send works
    // even before that player has whispered us.
    internal static void RememberWhisperTarget(string partner, NetworkId id)
    {
        if (!string.IsNullOrEmpty(partner)) _whisperTargets[partner] = id;
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

            // Whisper: the partner is the sender, and pair the matching NetworkId
            // (enqueued in order by ClientChatPatch) so we can reply to them.
            string partner = string.Empty;
            if (channel == Channel.Whisper && !string.IsNullOrEmpty(sender))
            {
                partner = sender;
                if (_pendingWhisperIds.Count > 0)
                    _whisperTargets[sender] = _pendingWhisperIds.Dequeue();
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
    }
}
