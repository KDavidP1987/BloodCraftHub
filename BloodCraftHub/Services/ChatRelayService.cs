using System;
using System.Collections.Generic;
using BloodCraftHub.Utils;
using ProjectM.Network;

namespace BloodCraftHub.Services;

// 0.17 (feature/v0.17-standalone-ui): captures EVERY inbound chat message the
// client receives, across all channels, into a persistent rolling buffer so the
// standalone tabbed chat window can render and organize them. READ-ONLY — it
// never consumes or destroys chat entities (ClientChatPatch still owns that).
//
// Increment 1 = capture + display only. Sender-name resolution (NetworkId ->
// name), sending, per-person whisper threads, and the option to REPLACE the
// native chat window all land in later increments.
internal static class ChatRelayService
{
    internal enum Channel { Global, Local, Clan, System, Whisper, Other }

    internal readonly struct ChatLine
    {
        public readonly Channel Channel;
        public readonly ServerChatMessageType RawType;
        public readonly string Text;
        public readonly DateTime Received; // local clock time the client saw it

        public ChatLine(Channel channel, ServerChatMessageType rawType, string text, DateTime received)
        {
            Channel = channel; RawType = rawType; Text = text; Received = received;
        }
    }

    private const int MaxLines = 500;
    private static readonly List<ChatLine> _buffer = new(MaxLines + 16);

    // Raised whenever a new line is captured; the chat overlay subscribes to
    // refresh. Null when no UI is listening.
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

    // Called from ClientChatPatch for EVERY inbound ChatMessageServerEvent,
    // before any protocol/regex consumption. Never throws — chat capture must
    // not disrupt the inbound pump.
    internal static void Capture(ChatMessageServerEvent ev)
    {
        try
        {
            string text = ev.MessageText.Value;
            if (string.IsNullOrEmpty(text)) return;

            // Skip the signed Eclipse-protocol payloads ([N]:csv;mac<base64>) —
            // that's machine traffic, not chat; ClientChatPatch routes/destroys
            // it separately.
            if (text.Contains(";mac")) return;

            var line = new ChatLine(MapChannel(ev.MessageType), ev.MessageType, text, DateTime.Now);
            _buffer.Add(line);
            if (_buffer.Count > MaxLines) _buffer.RemoveRange(0, _buffer.Count - MaxLines);
            LineCaptured?.Invoke(line);
        }
        catch (Exception ex)
        {
            LogUtils.LogDebug($"ChatRelayService.Capture: {ex.Message}");
        }
    }

    internal static void Clear() => _buffer.Clear();
}
