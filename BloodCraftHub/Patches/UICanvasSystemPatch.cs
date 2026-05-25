using BloodCraftHub.Config;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using HarmonyLib;
using ProjectM.UI;

namespace BloodCraftHub.Patches;

// 0.16: optionally push BCH's overlays/panels BEHIND the game's in-game menus
// (inventory, character sheet, map, build, etc.) instead of floating over them.
// Gated by Settings.OverlaysBehindGameMenus (default on). The menu-open signal is
// the SAME one the game itself uses inside UICanvasSystem.UpdateHideIfDisabled:
// any active child under UICanvasBase.HUDMenuParent.
//
// PORT NOTE: the ancestor mod (LearningMods/BloodCraftUI-master) patched this same
// method to HIDE the whole UI on menu-open. BCH instead lowers the canvas sort
// order so overlays stay present but render behind the menu; "foreground" mode
// (setting off) leaves them on top.
//
// TESTING CAVEAT: BCH canvases are ScreenSpaceOverlay. If the game's menu canvas
// turns out to be ScreenSpaceCamera, ScreenSpaceOverlay always draws above it
// regardless of sortingOrder — in which case this is a graceful no-op (overlays
// stay on top, same as the setting being off) and we'd switch to a hide-on-menu
// approach. Verify in-game that overlays actually drop behind an open menu.
[HarmonyPatch(typeof(UICanvasSystem), "UpdateHideIfDisabled")]
public static class UICanvasSystemPatch
{
    // Far below any game UI; while a menu is open BCH renders behind everything.
    private const int MENU_BEHIND_BASE = -500;

    private static bool _wasBehind;

    [HarmonyPostfix]
    private static void Postfix(UICanvasBase canvas)
    {
        try
        {
            bool wantBehind = Settings.OverlaysBehindGameMenus && IsAnyMenuOpen(canvas);

            if (wantBehind)
            {
                // Re-apply every frame while behind so a panel focus-reorder
                // (UIBase.SetOnTop resets canvases to TOP_SORTORDER) can't pop us
                // back in front of the menu mid-interaction.
                ApplySortBaseline(MENU_BEHIND_BASE);
            }
            else if (_wasBehind)
            {
                // Menu closed (or the setting was turned off) — restore the normal
                // top baseline once; UIBase.SetOnTop manages ordering afterward.
                ApplySortBaseline(UIBase.TOP_SORTORDER);
            }

            _wasBehind = wantBehind;
        }
        catch { /* never let a cosmetic layering tweak disrupt the game's canvas update */ }
    }

    private static bool IsAnyMenuOpen(UICanvasBase canvas)
    {
        if (canvas == null) return false;
        var parent = canvas.HUDMenuParent;
        if (parent == null || !parent.gameObject.activeSelf) return false;
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child != null && child.gameObject.activeSelf) return true;
        }
        return false;
    }

    // Shift every BCH UIBase canvas to (baseOrder - siblingOffset), preserving the
    // relative ordering UIBase.SetOnTop produces while moving the whole stack.
    private static void ApplySortBaseline(int baseOrder)
    {
        var uiBases = UniversalUI.uiBases;
        if (uiBases == null) return;

        int childCount = UniversalUI.CanvasRoot != null
            ? UniversalUI.CanvasRoot.transform.childCount
            : uiBases.Count;

        for (int i = 0; i < uiBases.Count; i++)
        {
            var ui = uiBases[i];
            if (ui?.Canvas == null || ui.RootRect == null) continue;
            int offset = childCount - ui.RootRect.GetSiblingIndex();
            ui.Canvas.sortingOrder = baseOrder - offset;
        }
    }
}
