using System;
using BloodCraftHub.Services;
using BloodCraftHub.Utils;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using ProjectM.UI;
using Unity.Collections;
using Unity.Entities;

namespace BloodCraftHub.Patches;

// Inbound chat-message pump. Hooks ClientChatSystem.OnUpdate; for each inbound
// chat entity:
//   1. If it's a signed Eclipse-protocol message → verify MAC, route to
//      EclipseProtocolService, then destroy the entity (so the [1]:...;mac...
//      noise never reaches the chat window) — UNLESS the standalone Eclipse
//      mod is also installed, in which case we leave the entity intact so
//      Eclipse's own prefix can read it too; Eclipse will destroy it.
//   2. Otherwise leave it alone (real player chat, server announcements, etc.).
//
// Also fires the registration handshake once the player is in-world and
// MessageService has captured LocalCharacter/LocalUser.
//
// PORT REFERENCE: LearningMods/Eclipse-main/Patches/ClientChatSystemPatch.cs
[HarmonyPatch]
internal static class ClientChatPatch
{
    [HarmonyPatch(typeof(ClientChatSystem), nameof(ClientChatSystem.OnUpdate))]
    [HarmonyPrefix]
    // Run before Eclipse-main's same-target prefix (which is Priority.Normal).
    // When Eclipse is also installed we leave the chat entity intact instead
    // of destroying it (see EclipseProtocolService.IsEclipseModLoaded), but
    // Eclipse's prefix needs to see and process the entity itself — if it
    // ran first and destroyed, we'd see a dead entity and our overlays would
    // stop updating. High priority guarantees BCH parses the payload first.
    [HarmonyPriority(Priority.High)]
    private static void OnUpdate_Prefix(ClientChatSystem __instance)
    {
        // Don't try anything until MessageService has bound the local character/user.
        if (!MessageService.IsInitialized) return;

        // Send the registration handshake once. Eclipse-main delays a couple of
        // seconds with a coroutine; we just fire on the first tick after the
        // entity bindings come up - the server is fine with that.
        if (!EclipseProtocolService.UserRegistered && !EclipseProtocolService.RegistrationPending)
        {
            EclipseProtocolService.SendRegistration();
        }

        // Walk this frame's inbound chat entities. ClientChatSystem exposes a
        // ReceiveChatMessages query - Eclipse-main accesses it as
        // _ReceiveChatMessagesQuery (the IL2CPP-generated backing field).
        NativeArray<Entity> entities;
        try { entities = __instance._ReceiveChatMessagesQuery.ToEntityArray(Allocator.Temp); }
        catch (Exception ex)
        {
            // First-load NRE protection if the query field hasn't been populated yet.
            LogUtils.LogDebug($"ClientChatPatch: query unavailable: {ex.Message}");
            return;
        }

        try
        {
            foreach (var entity in entities)
            {
                if (!entity.Has<ChatMessageServerEvent>()) continue;

                var ev = entity.Read<ChatMessageServerEvent>();

                // 0.17: mirror every inbound message (all channels) into the
                // standalone tabbed chat window's buffer. Read-only — never
                // consumes the entity; filters protocol noise itself.
                ChatRelayService.Capture(ev);

                // Only system-type messages carry the Eclipse protocol. Player chat is type Local/Global/etc.
                if (ev.MessageType != ServerChatMessageType.System) continue;

                string text = ev.MessageText.Value;
                if (string.IsNullOrEmpty(text)) continue;

                if (EclipseProtocolService.TryHandleServerMessage(text))
                {
                    // Normally we destroy here so the [N]:csv;mac... noise
                    // doesn't surface in the player's chat window. BUT if the
                    // standalone Eclipse mod is also installed, its own
                    // ClientChatSystem prefix needs to read this same entity
                    // to populate its overlay — destroying it now would leave
                    // Eclipse rendering zeroed bars. Eclipse's prefix destroys
                    // the entity itself after parsing, so chat-window noise
                    // is still suppressed in that case.
                    if (!EclipseProtocolService.IsEclipseModLoaded())
                        Plugin.EntityManager.DestroyEntity(entity);
                    continue;
                }

                // Passive: harvest plausible player names from colored chat
                // tokens before the regex pipeline takes the text. Doesn't
                // consume anything; just populates the autocomplete cache.
                PlayerNameCacheService.TryHarvestNames(text);

                // Fall through to the legacy regex pipeline for things Bloodcraft
                // doesn't ship via the structured protocol (.fam boxes / .fam l).
                if (MessageService.HandleInboundChat(text))
                {
                    Plugin.EntityManager.DestroyEntity(entity);
                }
            }
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"ClientChatPatch: error processing inbound chat: {ex}");
        }
        finally
        {
            entities.Dispose();
        }
    }
}
