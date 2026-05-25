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
    // 0.17.0: tracks whether our chat input was focused last frame, so the Enter
    // key that SENDS a message (which defocuses our input the same frame) can't be
    // mistaken for an "open chat" press and re-focus it — the loop that trapped the
    // user in chat with no way out.
    private static bool _wasChatActiveLastFrame;

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

        // 0.17: while the tabbed-chat takeover is active, keep the native chat
        // hidden each tick (an incoming message can fade it back in) and
        // force-unfocus it if it grabbed focus — see ApplyNativeChatVisibility
        // for the freeze-safety rationale.
        Plugin.UIManager?.ApplyNativeChatVisibility();

        // 0.17.0: AUTHORITATIVE suppression flag. Drive ChatInputActive from our
        // chat input's real focus state every frame, instead of trusting the
        // onSelect/onDeselect events (DeactivateInputField doesn't reliably raise
        // onDeselect, so the event-set flag stuck true and left gameplay input
        // suppressed after chatting — the post-chat freeze). If the field isn't
        // focused, suppression can't stick on.
        InputSuppression.ChatInputActive = Plugin.UIManager?.IsChatInputFocused() ?? false;

        // 0.17.0 takeover input handling. (Replaces an earlier _FocusChat divert
        // that re-focused our input EVERY frame the game set _FocusChat — which the
        // coffin/rest state keeps set — pinning ChatInputActive on and trapping the
        // player with no escape. Now decoupled from _FocusChat entirely.)
        try
        {
            if (Plugin.UIManager?.IsNativeChatHideActive() ?? false)
            {
                bool chatActive = InputSuppression.ChatInputActive;

                // 0.17.0 THE FREEZE FIX. Pressing Enter makes V Rising OPEN its native
                // chat (IsChatOpen=true), which gates gameplay input — and we only
                // block the native FOCUS, not the open, while hiding the window. So the
                // native chat is "open" forever and the gameplay gate never clears =>
                // frozen after chatting (diagnostics showed chatOpen stuck True with all
                // our own suppression off). Force the native chat closed whenever it's
                // open and we're NOT actively typing in our input. While you type, our
                // ChatInputActive provides type-without-moving; the instant you're done
                // (send/Escape), this clears V Rising's chat-open gate.
                try
                {
                    if (!chatActive && __instance.IsChatOpen)
                        __instance.ForceClose();
                }
                catch (Exception ex) { LogUtils.LogDebug($"ForceClose: {ex.Message}"); }

                // ESCAPE HATCH: Escape always releases our chat input. Escape is
                // never suppressed (ShouldBlockMenus ignores ChatInputActive), so
                // this always reaches us and breaks any "stuck focused" trap.
                if (chatActive && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape))
                {
                    Plugin.UIManager.ReleaseChatInput();
                }
                // ENTER → focus OUR input, detected directly off the key. The
                // last-frame guard is essential: pressing Enter to SEND defocuses
                // our input the same frame (chatActive -> false) while GetKeyDown
                // stays true all frame — without it we'd instantly re-focus and the
                // user could never leave chat. Require chat to have been inactive
                // BOTH this frame and last frame before treating Enter as "open".
                else if (!chatActive && !_wasChatActiveLastFrame
                    && (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Return)
                        || UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.KeypadEnter)))
                {
                    Plugin.UIManager.FocusChatInput();
                }

                _wasChatActiveLastFrame = InputSuppression.ChatInputActive;
            }
        }
        catch (Exception ex) { LogUtils.LogDebug($"OnUpdate_Prefix takeover: {ex.Message}"); }

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

    // 0.17: capture chat for the standalone tabbed window FROM the native
    // formatter. FormatFullChatMessage hands us the message type, the body text,
    // and the GAME-RESOLVED sender name (userName) — the raw ChatMessageServerEvent
    // has no name; the client resolves it here. This is how the tabbed window
    // shows EVERY player's name, not just the local player. Read-only postfix.
    [HarmonyPatch(typeof(ClientChatSystem), "FormatFullChatMessage")]
    [HarmonyPostfix]
    private static void FormatFullChatMessage_Postfix(ServerChatMessageType messageType, string filteredText, string userName)
    {
        try { ChatRelayService.CaptureFormatted(messageType, userName, filteredText); }
        catch (Exception ex) { LogUtils.LogDebug($"FormatFullChatMessage_Postfix: {ex.Message}"); }
    }

    // 0.17.0: BLOCK native chat focus during takeover. Both native focus entry
    // points (SetFocused and FocusInputField) are blocked so the hidden native
    // chat never grabs focus / sets V Rising's ChatInputFocused gate. These are
    // block-ONLY — they must NOT pull focus to our input. Focusing our window is
    // done solely by the direct Enter-key detection in OnUpdate_Prefix; if these
    // also focused our input, the Enter that SENDS a message (which defocuses our
    // input mid-frame) would be seen by native as an open-request, re-focusing our
    // input and trapping the user in chat with no way out (observed loop).
    [HarmonyPatch(typeof(HUDChatWindow), nameof(HUDChatWindow.SetFocused))]
    [HarmonyPrefix]
    private static bool SetFocused_Prefix(bool isFocused)
    {
        try
        {
            if (isFocused && (Plugin.UIManager?.IsNativeChatHideActive() ?? false))
                return false; // skip native focus (do NOT focus ours here)
        }
        catch (Exception ex) { LogUtils.LogDebug($"SetFocused_Prefix: {ex.Message}"); }
        return true;
    }

    [HarmonyPatch(typeof(HUDChatWindow), nameof(HUDChatWindow.FocusInputField))]
    [HarmonyPrefix]
    private static bool FocusInputField_Prefix()
    {
        try
        {
            if (Plugin.UIManager?.IsNativeChatHideActive() ?? false)
                return false; // skip native focus (do NOT focus ours here)
        }
        catch (Exception ex) { LogUtils.LogDebug($"FocusInputField_Prefix: {ex.Message}"); }
        return true;
    }
}
