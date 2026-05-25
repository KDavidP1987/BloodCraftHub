using System;
using System.Collections.Generic;
using BloodCraftHub.Utils;
using ProjectM.Network;

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

        public ChatLine(Channel channel, string sender, string text, DateTime received)
        {
            Channel = channel; Sender = sender; Text = text; Received = received;
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
            // for the same message (e.g. on a channel/mode re-filter).
            if (_buffer.Count > 0)
            {
                var last = _buffer[_buffer.Count - 1];
                if (last.Channel == channel && last.Sender == sender && last.Text == text)
                    return;
            }

            var line = new ChatLine(channel, sender, text, DateTime.Now);
            _buffer.Add(line);
            if (_buffer.Count > MaxLines) _buffer.RemoveRange(0, _buffer.Count - MaxLines);
            LineCaptured?.Invoke(line);
        }
        catch (Exception ex)
        {
            LogUtils.LogDebug($"ChatRelayService.CaptureFormatted: {ex.Message}");
        }
    }

    internal static void Clear() => _buffer.Clear();
}
