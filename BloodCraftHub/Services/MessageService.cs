using System;
using System.Collections.Generic;
using System.Linq;
using BloodCraftHub.Config;
using BloodCraftHub.Utils;
using Il2CppInterop.Runtime;
using ProjectM.Network;
using Unity.Entities;
using DateTime = System.DateTime;

namespace BloodCraftHub.Services;

// OUTBOUND chat-injection pipeline.
//
// Public API:
//   - EnqueueMessage(string) - queue a chat command to send.
//   - ProcessAllMessages()    - called every frame from Behaviors/CoreUpdateBehavior;
//                               throttled at Settings.GlobalQueryIntervalInSeconds
//                               (default 2s) so we don't spam the server.
//   - SetCharacter / SetUser  - called from InitializationPatch once the player's
//                               LocalCharacter / LocalUser entities are captured;
//                               messages are only sent when BOTH are set.
//
// Internals:
//   SendMessage builds a one-off ECS entity carrying the four components V Rising
//   uses to deliver a chat-typed-by-player event. The server treats it identically
//   to text the player typed in their chat box.
//
// Ported from LearningMods/BloodCraftUI-master/BloodCraftUI/Services/MessageService.cs.
// Split into partials so MessageService_Processing.cs can own the BCCOM_* command
// strings and the inbound regex/Eclipse-protocol decode (Phase 3b/3c).
public static partial class MessageService
{
    static EntityManager EntityManager => Plugin.EntityManager;

    // 0.10.3 critical fix: this used to be a static-field initializer that
    // ran inside MessageService's cctor. Every ComponentType.ReadOnly call
    // routes into Unity.Entities.TypeManager.FindTypeIndex, which NREs when
    // V Rising's ECS World hasn't been created yet. Pre-0.10.0 the cctor
    // happened to fire late enough that the World existed by then, but 0.10.0
    // added VBloodScannerService.Initialize() at Plugin.Load time which
    // touches MessageService.FamSearchCompleted — the first MessageService
    // static-field access ANY code path makes triggers the cctor, and at
    // Plugin.Load the World is definitely not up. Lazy-init the field so the
    // ComponentType.ReadOnly calls happen at SendMessage-time (gameplay,
    // World is up) rather than cctor time (plugin load, World is not).
    //
    // Same lesson is documented in EclipseProtocolService.cs's NOTE block —
    // copy/pasting it here so the next contributor sees the rationale right
    // next to the lazy pattern.
    private static ComponentType[] _networkEventComponents;
    private static ComponentType[] NetworkEventComponents =>
        _networkEventComponents ??= new[]
        {
            ComponentType.ReadOnly(Il2CppType.Of<FromCharacter>()),
            ComponentType.ReadOnly(Il2CppType.Of<NetworkEventType>()),
            ComponentType.ReadOnly(Il2CppType.Of<SendNetworkEventTag>()),
            ComponentType.ReadOnly(Il2CppType.Of<ChatMessageEvent>()),
        };

    private static readonly Queue<string> OutputMessages = new();
    private static Entity _localCharacter = Entity.Null;
    private static Entity _localUser      = Entity.Null;
    private static bool   _isInitialized;
    private static DateTime _lastAction = DateTime.MinValue;
    private static int _timeoutSeconds;

    private static readonly NetworkEventType NetworkEventType = new()
    {
        IsAdminEvent = false,
        EventId      = NetworkEvents.EventId_ChatMessageEvent,
        IsDebugEvent = false,
    };

    public static bool IsInitialized => _isInitialized;

    /// <summary>Local user entity once <see cref="SetUser"/> has been called; <see cref="Entity.Null"/> otherwise.</summary>
    public static Entity LocalUser => _localUser;

    /// <summary>Local character entity once <see cref="SetCharacter"/> has been called; <see cref="Entity.Null"/> otherwise.</summary>
    public static Entity LocalCharacter => _localCharacter;

    /// <summary>
    /// Send a player-initiated chat command. Bypasses the 2-second throttle
    /// queue so a button click reaches the server on the next frame instead of
    /// up to 2s later (live feedback during Phase 4 testing surfaced the delay
    /// as a real UX problem; rate-limiting human button-mashing is not worth
    /// the perceived input lag).
    ///
    /// Arms the regex intercept flag (NoteOutboundForIntercept) so the reply
    /// gets routed to the right state slot.
    /// </summary>
    public static void EnqueueMessage(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (!_isInitialized)
        {
            LogUtils.LogWarning($"EnqueueMessage('{text}') ignored — character/user not bound yet.");
            return;
        }
        if (ShouldHoldOutbound()) { _heldOutbound.Enqueue((text, false)); return; }
        DispatchNow(text, silent: false);
    }

    // 0.17.1 (Eclipse coexistence): the actual arm-intercept + inject. Split out so
    // both the immediate path and the deferred drain (ProcessAllMessages) share it.
    private static void DispatchNow(string text, bool silent)
    {
        if (silent)
        {
            _nextCommandIsBchAuto = true;
            NoteOutboundForIntercept(text);
            _nextCommandIsBchAuto = false;
        }
        else
        {
            NoteOutboundForIntercept(text);
        }
        SendMessage(text);
    }

    /// <summary>
    /// 0.10.2: same as EnqueueMessage but tells the chat-capture pipeline to
    /// destroy the server's reply lines so they don't clutter chat. Used by
    /// the overlay's bonus-stats ticker, the V-Bloods scanner, and the
    /// per-tab auto-refresh paths — their replies are already rendered in
    /// the BCH UI, so the chat copy is pure noise. Manual user-triggered
    /// commands (Refresh button etc.) MUST use the regular EnqueueMessage
    /// so the user still sees their explicit query reply in chat.
    ///
    /// Mechanism: sets the static "next-capture-silent" flag on
    /// MessageService_Processing immediately before arming the intercept.
    /// NoteOutboundForIntercept consumes it during arming and stores its
    /// decision in a per-intercept slot, so subsequent regular EnqueueMessage
    /// calls aren't affected.
    /// </summary>
    public static void EnqueueMessageSilent(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (!_isInitialized)
        {
            LogUtils.LogWarning($"EnqueueMessageSilent('{text}') ignored — character/user not bound yet.");
            return;
        }
        // 0.10.6: _nextCommandIsBchAuto replaces the prior
        // _suppressNextCaptureChat flag. The intercept arming reads this
        // and classifies the command as BchAuto category (rather than
        // running the prefix classifier), so the Chat Logging BchAuto
        // toggle controls visibility. Same partial-class field — no prefix.
        if (ShouldHoldOutbound()) { _heldOutbound.Enqueue((text, true)); return; }
        DispatchNow(text, silent: true);
    }

    public static void SetCharacter(Entity entity)
    {
        _localCharacter = entity;
        UpdateInitializedFlag();
    }

    public static void SetUser(Entity entity)
    {
        _localUser = entity;
        UpdateInitializedFlag();
    }

    private static void UpdateInitializedFlag()
    {
        bool ready = _localCharacter.HasValue() && _localUser.HasValue();
        if (ready && !_isInitialized)
        {
            _isInitialized = true;
            _initRealtime = UnityEngine.Time.realtimeSinceStartup; // 0.17.1: start the Eclipse-coexistence hold window
            LogUtils.LogInfo("MessageService initialized (character + user bound).");
        }
    }

    public static void Destroy()
    {
        _localCharacter = Entity.Null;
        _localUser      = Entity.Null;
        OutputMessages.Clear();
        _heldOutbound.Clear();
        _isInitialized  = false;
    }

    // 0.17.1 (Eclipse coexistence workaround): when the Eclipse MOD is also
    // installed, BCH holds its outbound commands for a few seconds after login
    // and then releases them ONE PER FRAME, instead of firing the usual startup
    // burst immediately. Why: Eclipse's HUD coroutine reads a cached
    // BufferLookup<ModifyUnitStatBuff_DOTS> that ANY structural ECS change
    // invalidates; every BCH command is a CreateEntity (structural change), and
    // BCH's login burst racing Eclipse's stale-lookup read crashes the client on
    // load (AccessViolation in Eclipse.Services.CanvasService — see
    // [[project_v016_crash_investigation]]). Spreading our commands out of the
    // login window, and never bursting, keeps Eclipse's lookup stable. Costs only
    // a short startup delay, and ONLY when Eclipse is present (no Eclipse = the
    // immediate-send behavior is unchanged). Registration (SendRaw) is NOT held —
    // it's a single inject and is how BCH starts receiving data.
    private const float ECLIPSE_OUTBOUND_HOLD_SECONDS = 12f;
    private static float _initRealtime;
    private static readonly System.Collections.Generic.Queue<(string text, bool silent)> _heldOutbound = new();

    private static bool ShouldHoldOutbound()
    {
        try
        {
            if (!EclipseProtocolService.IsEclipseModLoaded()) return false;
            return UnityEngine.Time.realtimeSinceStartup < _initRealtime + ECLIPSE_OUTBOUND_HOLD_SECONDS;
        }
        catch { return false; }
    }

    /// <summary>
    /// Per-frame tick (CoreUpdateBehavior). Drains the legacy queue, and — once the
    /// Eclipse-coexistence hold window has expired — releases held commands one per
    /// frame so structural changes never bunch up against Eclipse's HUD lookup.
    /// </summary>
    public static void ProcessAllMessages()
    {
        if (!_isInitialized) return;

        // Drain anything that snuck in via the legacy queue path. Cheap; no throttle.
        while (OutputMessages.Count > 0)
            SendMessage(OutputMessages.Dequeue());

        // 0.17.1: release ONE held (Eclipse-coexistence) command per frame after the
        // hold window — staggered so each frame has at most one structural change.
        if (_heldOutbound.Count > 0 && !ShouldHoldOutbound())
        {
            var item = _heldOutbound.Dequeue();
            DispatchNow(item.text, item.silent);
        }
    }

    /// <summary>
    /// Send a chat message immediately, bypassing the 2-second throttle queue.
    /// Use for one-off protocol messages that must land on the next frame
    /// (e.g. the Eclipse-protocol registration handshake); use
    /// <see cref="EnqueueMessage"/> for player-initiated commands so we don't
    /// flood the server parser.
    /// </summary>
    public static void SendRaw(string text)
    {
        if (!_isInitialized || string.IsNullOrEmpty(text)) return;
        SendMessage(text);
    }

    private static void SendMessage(string text)
    {
        try
        {
            var chatMessageEvent = new ChatMessageEvent
            {
                MessageText    = text,
                MessageType    = ChatMessageType.Local,
                ReceiverEntity = _localUser.Read<NetworkId>(),
            };

            Entity networkEntity = EntityManager.CreateEntity(NetworkEventComponents);
            networkEntity.Write(new FromCharacter { Character = _localCharacter, User = _localUser });
            networkEntity.Write(NetworkEventType);
            networkEntity.Write(chatMessageEvent);
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"MessageService.SendMessage failed: {ex}");
        }
    }

    // 0.17: send a chat message on a specific channel (Global / Local / Team).
    // Used by the tabbed chat window's input box. No intercept arming — this is
    // real player chat, not a command awaiting a parsed reply.
    // 0.17: send chat on a channel (Global / Local / Team). Mirrors the proven
    // command-send path (self ReceiverEntity — same as Eclipse/FamBook). NOTE:
    // broadcast chat sent via this raw ChatMessageEvent injection is under
    // investigation — COMMANDS round-trip fine through it, but plain broadcast
    // chat hasn't surfaced yet; diagnosing whether it needs the native send path.
    public static void SendChat(string text, ChatMessageType type)
    {
        if (!_isInitialized || string.IsNullOrEmpty(text)) return;
        try
        {
            var chatMessageEvent = new ChatMessageEvent
            {
                MessageText    = text,
                MessageType    = type,
                ReceiverEntity = _localUser.Read<NetworkId>(),
            };

            Entity networkEntity = EntityManager.CreateEntity(NetworkEventComponents);
            networkEntity.Write(new FromCharacter { Character = _localCharacter, User = _localUser });
            networkEntity.Write(NetworkEventType);
            networkEntity.Write(chatMessageEvent);
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"MessageService.SendChat failed: {ex}");
        }
    }

    // 0.17.0: send a WHISPER to a specific player. target is the recipient's
    // NetworkId (captured from an incoming whisper's FromUser). Same injection as
    // SendChat but MessageType=Whisper and ReceiverEntity=the target, not self.
    public static void SendWhisper(string text, NetworkId target)
    {
        if (!_isInitialized || string.IsNullOrEmpty(text)) return;
        try
        {
            var chatMessageEvent = new ChatMessageEvent
            {
                MessageText    = text,
                MessageType    = ChatMessageType.Whisper,
                ReceiverEntity = target,
            };

            Entity networkEntity = EntityManager.CreateEntity(NetworkEventComponents);
            networkEntity.Write(new FromCharacter { Character = _localCharacter, User = _localUser });
            networkEntity.Write(NetworkEventType);
            networkEntity.Write(chatMessageEvent);
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"MessageService.SendWhisper failed: {ex}");
        }
    }
}
