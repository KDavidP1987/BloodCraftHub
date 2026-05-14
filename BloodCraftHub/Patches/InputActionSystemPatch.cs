using BloodCraftHub.Utils;
using HarmonyLib;
using ProjectM;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BloodCraftHub.Patches;

// Suspend V Rising's game input only when the user is typing into one of OUR
// UI fields (descendant of Plugin.UIManager.UIRoot). Without the descendant
// check the patch also fired for V Rising's native chat input — and on close
// (Enter or Escape) the queued keystroke bled through into gameplay because
// V Rising's own input system was effectively double-suppressed by us. With
// the scope restricted to BCH-owned fields, V Rising's chat behaves exactly
// as vanilla.
//
// The previous "SuspendGameInputWhileUIOpen" feature (return false whenever
// the main panel was open) is GONE. Returning false from InputActionSystem's
// OnUpdate also wedged the UI-input pipeline, so any user with that toggle on
// got their game frozen the moment they opened the panel — including across
// sessions because the setting persisted. Setting + footer toggle are removed
// in Settings.cs and MainPanel.AddUIOpenInputBlockToggle. Stale entries in the
// .cfg file are inert (we never read the value).
[HarmonyPatch]
internal static class InputActionSystemPatch
{
    // Diagnostic: log focus changes once each so the user can paste the
    // sequence into a bug report if input ever locks up. Tracking the previous
    // selection name (not the GameObject ref) keeps the log readable across
    // panel rebuilds.
    private static string _lastFocusName = "";

    [HarmonyPatch(typeof(InputActionSystem), nameof(InputActionSystem.OnUpdate))]
    [HarmonyPrefix]
    private static bool OnUpdate_Prefix()
    {
        if (Plugin.Settings == null) return true;
        if (!Config.Settings.SuspendGameInputWhileTyping) return true;

        var es = EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;

        string currentName = (sel != null && sel.activeInHierarchy) ? sel.name : "<null>";
        if (currentName != _lastFocusName)
        {
            try { LogUtils.LogDebug($"InputActionSystemPatch: focus '{_lastFocusName}' -> '{currentName}'"); }
            catch { /* harmless */ }
            _lastFocusName = currentName;
        }

        if (sel == null || !sel.activeInHierarchy) return true;

        var input = sel.GetComponent<TMP_InputField>();
        if (input == null || !input.interactable) return true;

        // Scope check: only suppress when the focused field belongs to OUR UI.
        // Without this, V Rising's native chat (also a TMP_InputField) would be
        // suppressed too, and its closing Enter keystroke would bleed into
        // gameplay the same frame. Walk up the parent chain looking for our
        // UIRoot GameObject.
        var ourRoot = Plugin.UIManager?.UIRoot;
        if (ourRoot == null) return true;

        bool isOurs = false;
        var t = sel.transform;
        while (t != null)
        {
            if (t.gameObject == ourRoot) { isOurs = true; break; }
            t = t.parent;
        }
        if (!isOurs) return true;

        return false;
    }
}
