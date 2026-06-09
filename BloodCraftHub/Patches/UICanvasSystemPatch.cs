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

    // 0.17.2 CRASH FIX. UpdateHideIfDisabled is called per-canvas EVERY frame, and in
    // dense bursts while the HUD rebuilds — on login, on waypoint teleport (area
    // reload), and on starting V-Blood / boss TRACKING (which activates a
    // TargetInfoPanel under this very UICanvasBase). The old postfix walked
    // HUDMenuParent's children (a fresh IL2CPP Transform wrapper per child) AND every
    // UniversalUI.uiBases entry on EVERY call. During those bursts the flood of
    // short-lived interop object-wrappers tipped a latent Il2CppInterop GC-finalizer
    // bug (GarbageCollector_RunFinalizer_Patch) — the 0.16.x "crash on load / on
    // tracking / on teleport", which also corrupts the BepInEx interop cache so the
    // client then crashes on every load until the cache is regenerated.
    //
    // We now re-evaluate at most ~10x/sec and only touch the canvases when the
    // menu-open state actually changes (plus a throttled re-apply while behind to
    // counter focus reorders). That drops the per-rebuild wrapper churn by 1-2 orders
    // of magnitude. The layering looks identical: UIBase.SetOnTop reorders happen on
    // focus/click, not per frame, so a 10 Hz re-apply keeps overlays behind the menu
    // with no visible pop. Eclipse avoids the same trap by caching the canvas and
    // reading it from a throttled coroutine rather than working per engine call.
    private const double EVAL_INTERVAL_SECONDS = 0.1;   // ~10 Hz
    private static double _lastEvalAt;

    [HarmonyPostfix]
    private static void Postfix(UICanvasBase canvas)
    {
        try
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (now - _lastEvalAt < EVAL_INTERVAL_SECONDS) return;   // throttle: skip most calls
            _lastEvalAt = now;

            // Short-circuits BEFORE IsAnyMenuOpen (the child-walk) when the feature is
            // off, so a disabled feature costs nothing beyond this throttle check.
            bool wantBehind = Settings.OverlaysBehindGameMenus && IsAnyMenuOpen(canvas);

            // B12 (0.19): keep the baseline SetOnTop reorders around in sync EVERY eval (cheap int
            // write). This is what stops the chat overlay "phasing" over the menu: PanelManager.
            // UpdateFocus calls SetOnTop on every frame the cursor is over a panel; without this it
            // would reset the canvas to TOP_SORTORDER (in front of the menu) until the next eval.
            UIBase.CurrentSortBaseline = wantBehind ? MENU_BEHIND_BASE : UIBase.TOP_SORTORDER;

            if (wantBehind)
            {
                // Re-apply (throttled) while behind so a panel focus-reorder
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

    // 0.17: menu children that should NOT push BCH overlays behind — the coffin
    // (its child is named "SpawnMenu") and the full-screen darkening
    // ("FullscreenMenu"). In those states you can still chat / use overlays
    // (like the native chat), so overlays must stay on top + clickable. Inventory
    // / character / map / build menus have their own names and still push behind.
    private static readonly string[] _keepOverlaysOnTopFor = { "SpawnMenu", "FullscreenMenu" };

    private static double _lastMenuDiagAt;
    private static bool IsAnyMenuOpen(UICanvasBase canvas)
    {
        if (canvas == null) return false;
        var parent = canvas.HUDMenuParent;
        if (parent == null || !parent.gameObject.activeSelf) return false;
        // Diagnostic (gated): once/sec, log the active HUD-menu child names + whether each pushes overlays
        // behind or is kept-on-top. This is how we find out exactly what the MAP and spell/shapeshift-select
        // register as (and whether they're even under HUDMenuParent), so the fix is targeted, not guessed.
        // Reads only child.name (already read by IsKeepOnTop), so it adds no new IL2CPP wrapping churn.
        bool diagOn = Config.Settings.DiagnosticMode;
        System.Text.StringBuilder diag = null;
        bool any = false;
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child == null || !child.gameObject.activeSelf) continue;
            string nm = child.gameObject.name;
            bool keepTop = IsKeepOnTop(nm);
            if (diagOn) (diag ??= new System.Text.StringBuilder()).Append(nm).Append(keepTop ? "(on-top) " : "(behind) ");
            if (keepTop) continue; // coffin / fullscreen — keep overlays usable
            any = true;
            if (!diagOn) return true;   // fast path when not diagnosing
        }
        if (diagOn && diag != null)
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (now - _lastMenuDiagAt >= 1.0) { _lastMenuDiagAt = now; BloodCraftHub.Utils.LogUtils.LogDiagnostic($"[MenuLayer] active HUD menus: {diag}"); }
        }
        return any;
    }

    private static bool IsKeepOnTop(string childName)
    {
        if (string.IsNullOrEmpty(childName)) return false;
        for (int i = 0; i < _keepOverlaysOnTopFor.Length; i++)
            if (childName.StartsWith(_keepOverlaysOnTopFor[i], System.StringComparison.OrdinalIgnoreCase))
                return true;
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
