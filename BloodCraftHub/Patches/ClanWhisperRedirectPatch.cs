using System;
using BloodCraftHub.Config;
using BloodCraftHub.Utils;
using HarmonyLib;
using ProjectM.Network;
using ProjectM.UI;

namespace BloodCraftHub.Patches;

// 0.17.3 (#38): social-menu whisper redirect.
//
// The P-key social/clan page's member context menu has a "Whisper" entry. Clicking it
// runs ClanMenuMapper.HandleMemberContextMenuEntryClicked -> ClanMenuMapper.Whisper(
// NetworkId userIndex, string usersName), which then drives the NATIVE chat window into
// whisper-compose mode. When BCH's chat window is in use — especially with "hide native
// chat" on — that native compose target is hidden, so the whisper "does nothing" and can
// trap keystrokes in an invisible field (the lock-in the user warned about).
//
// We prefix Whisper, capture the target's NetworkId + display name (the exact pair our
// own SendWhisper / RememberWhisperTarget plumbing wants), hand it to BCH's chat window,
// and return false to skip the native path entirely. The redirect is gated to the
// chat-window setting INSIDE BeginExternalWhisper's caller check: if BCH chat isn't
// enabled, we return true and the vanilla whisper runs exactly as before (no lock-in).
//
// Discovery note: the *Social*MenuMapper context menu only exposes voice mute/unmute —
// "Whisper" lives on the CLAN menu mapper (MemberContextMenuData.LKey_Whisper), which is
// why an earlier prefix on SocialMenuMapper never fired. Confirmed against the 1.1.11
// reference metadata: ClanMenuMapper.Whisper(NetworkId, String) exists.
[HarmonyPatch(typeof(ClanMenuMapper), nameof(ClanMenuMapper.Whisper))]
internal static class ClanWhisperRedirectPatch
{
    // Return false to suppress the original (native) whisper; true lets it run.
    [HarmonyPrefix]
    private static bool Prefix(NetworkId userIndex, string usersName)
    {
        try
        {
            var mgr = Plugin.UIManager;
            // Not using BCH's chat window? Leave the vanilla whisper completely alone.
            if (mgr == null || !Settings.ShowChatWindowOverlay)
                return true;

            string name = usersName ?? string.Empty;
            bool handled = mgr.BeginExternalWhisper(userIndex, name);
            LogUtils.LogInfo($"[Whisper] social-menu whisper -> BCH chat: '{name}' (handled={handled}).");
            return !handled; // handled => skip native; otherwise fall back to native
        }
        catch (Exception ex)
        {
            LogUtils.LogWarning($"[Whisper] redirect failed; falling back to native whisper: {ex.Message}");
            return true;
        }
    }
}
