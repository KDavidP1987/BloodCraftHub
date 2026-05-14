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

    private static readonly ComponentType[] NetworkEventComponents =
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

    public static void EnqueueMessage(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        OutputMessages.Enqueue(text);
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
            LogUtils.LogInfo("MessageService initialized (character + user bound).");
        }
    }

    public static void Destroy()
    {
        _localCharacter = Entity.Null;
        _localUser      = Entity.Null;
        OutputMessages.Clear();
        _isInitialized  = false;
    }

    /// <summary>
    /// Per-frame tick called by CoreUpdateBehavior. Sends at most one queued message
    /// every <see cref="Settings.GlobalQueryIntervalInSeconds"/> seconds to avoid
    /// flooding the server's chat parser.
    /// </summary>
    public static void ProcessAllMessages()
    {
        if (!_isInitialized) return;

        if (_timeoutSeconds == 0)
            _timeoutSeconds = Settings.GlobalQueryIntervalInSeconds;

        if ((DateTime.Now - _lastAction).TotalSeconds < _timeoutSeconds)
            return;

        _lastAction = DateTime.Now;

        if (OutputMessages.Any())
            SendMessage(OutputMessages.Dequeue());
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
}
