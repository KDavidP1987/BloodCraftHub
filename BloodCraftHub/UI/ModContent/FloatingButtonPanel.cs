using BloodCraftHub.Config;
using BloodCraftHub.UI;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using BloodCraftHub.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;

namespace BloodCraftHub.UI.ModContent;

// The minimal, always-visible "hub" button. Click toggles the main panel.
//
// Layout choices:
//   - Anchor + pivot are (0.5, 0.5). The panel's screen-bounds clamp in
//     PanelBase.EnsureValidPosition assumes screen-center-relative coords;
//     non-center anchors cause the panel to stop halfway across the screen.
//   - DefaultPosition pushes the panel hard top-right; the clamp pins it to
//     the corner so the button defaults to where players expect a HUD toggle.
//   - The panel is 56x56 with a 40x40 button centered in it. The 8px ring of
//     padding around the button is the drag area — clicking the button face
//     triggers the click handler; click-and-drag from the ring drags the
//     panel. Unity's event system routes click vs drag automatically once
//     the Button component is the topmost click target.
public class FloatingButtonPanel : ResizeablePanelBase
{
    public override string PanelId => "FloatingButton";
    public override PanelType PanelType => PanelType.Base;

    // 0.9.0: widened from 56 to 104 px so a second 40x40 "OV" button fits
    // next to the BCH button (overlay master show/hide). The 8 px outer
    // ring stays as the drag area; the 8 px gap between buttons is also
    // drag-able. Per friend-testing feedback: "beside the B, C, H button,
    // there was a toggle to show and hide all active overlays so that this
    // way a person can toggle them on and off as they need".
    public override int MinWidth  => 104;
    public override int MinHeight => 56;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // (+halfW, +halfH) gets clamped to (halfW - W/2, halfH - H/2) → top-right corner.
    public override Vector2 DefaultPosition  => new(
        Owner.Scaler.m_ReferenceResolution.x * 0.5f,
        Owner.Scaler.m_ReferenceResolution.y * 0.5f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.None;
    public override float Opacity => Settings.UITransparency;
    public override bool ResizeWholePanel => true;

    public FloatingButtonPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        // base sets TitleBar inactive and Dragger.DraggableArea = Rect (whole panel)
        // because ResizeWholePanel is true.
        base.ConstructPanelContent();

        // BCH button — left side, opens / closes the main panel.
        var hubBtn = UIFactory.CreateButton(ContentRoot, "HubToggleButton", "BCH");
        UIFactory.SetLayoutElement(hubBtn.GameObject,
            minWidth: 40, preferredWidth: 40, flexibleWidth: 0,
            minHeight: 40, preferredHeight: 40, flexibleHeight: 0,
            ignoreLayout: true);
        var hubRt = hubBtn.GameObject.GetComponent<RectTransform>();
        hubRt.anchorMin = new Vector2(0.5f, 0.5f);
        hubRt.anchorMax = new Vector2(0.5f, 0.5f);
        hubRt.pivot     = new Vector2(0.5f, 0.5f);
        hubRt.sizeDelta = new Vector2(40, 40);
        hubRt.anchoredPosition = new Vector2(-24, 0); // left of center
        DisableNavigationFocus(hubBtn.Component);
        hubBtn.OnClick = () =>
        {
            try
            {
                var sel = EventSystem.current?.currentSelectedGameObject;
                LogUtils.LogDiagnostic($"FloatingButton BCH OnClick. PrevSelected={(sel != null ? sel.name : "(none)")} MainPanelOpen={Plugin.UIManager.IsMainPanelOpen}");
                Plugin.UIManager.ToggleMainPanel();
            }
            catch (System.Exception ex) { LogUtils.LogError($"FloatingButton click failed: {ex}"); }
            finally { ClearEventSystemSelection(); }
        };
        TooltipHover.Attach(hubBtn.GameObject, "Open or close the BloodCraftHub main panel.");

        // OV button — right side, master overlay show/hide. Session-only;
        // never re-shows overlays the user disabled via the per-overlay
        // footer toggle.
        var ovBtn = UIFactory.CreateButton(ContentRoot, "OverlayToggleButton", "OV");
        UIFactory.SetLayoutElement(ovBtn.GameObject,
            minWidth: 40, preferredWidth: 40, flexibleWidth: 0,
            minHeight: 40, preferredHeight: 40, flexibleHeight: 0,
            ignoreLayout: true);
        var ovRt = ovBtn.GameObject.GetComponent<RectTransform>();
        ovRt.anchorMin = new Vector2(0.5f, 0.5f);
        ovRt.anchorMax = new Vector2(0.5f, 0.5f);
        ovRt.pivot     = new Vector2(0.5f, 0.5f);
        ovRt.sizeDelta = new Vector2(40, 40);
        ovRt.anchoredPosition = new Vector2(24, 0); // right of center
        DisableNavigationFocus(ovBtn.Component);
        ovBtn.OnClick = () =>
        {
            try
            {
                var sel = EventSystem.current?.currentSelectedGameObject;
                LogUtils.LogDiagnostic($"FloatingButton OV OnClick. PrevSelected={(sel != null ? sel.name : "(none)")} Suppressed(before)={Plugin.UIManager.AreOverlaysSuppressed}");
                Plugin.UIManager.ToggleAllOverlaysSuppressed();
            }
            catch (System.Exception ex) { LogUtils.LogError($"FloatingButton OV click failed: {ex}"); }
            finally { ClearEventSystemSelection(); }
        };
        TooltipHover.Attach(ovBtn.GameObject,
            "Show/hide all currently-enabled overlays. Useful when the in-game menus conflict with overlay positioning on smaller screens. " +
            "This only toggles overlays you've already enabled via the panel footer — it never makes hidden-by-config overlays visible. " +
            "Session-only: overlays return to their configured visibility on game restart.");

        ApplyScale(); // 0.18.4: honor the user's launcher-button size
    }

    // 0.18.4: scale the whole launcher (both buttons + their gap) by Settings.FloatingButtonScale.
    // Applied as the panel root's localScale — the simplest way to uniformly shrink/grow the fixed-size
    // buttons (some displays render the 40px buttons large). The drag ring stays the unscaled rect,
    // which is harmless for a tiny corner launcher. Called at construct + when the setting changes.
    private void ApplyScale()
    {
        if (uiRoot == null) return;
        float s = Settings.FloatingButtonScale;
        uiRoot.transform.localScale = new Vector3(s, s, 1f);
    }

    /// <summary>0.18.4: re-apply the launcher size after the user changes the setting (live).</summary>
    public void RefreshScale() => ApplyScale();

    // 0.15.0: prevent the floating BCH / OV buttons from being navigated to
    // (and thus activated) by gamepad UI nav. Friend-test 0.14.0: pressing A
    // on a controller after using a teleport waypoint re-clicked the BCH
    // button and re-opened the main panel because the EventSystem's
    // currentSelectedGameObject was still pointing at the floating button
    // from the user's last mouse click. With Navigation.Mode.None the button
    // is excluded from the UI navigation graph entirely — A presses no
    // longer route to it. Real mouse clicks still work because they go
    // through the pointer-event path, not the navigation path.
    private static void DisableNavigationFocus(Selectable selectable)
    {
        if (selectable == null) return;
        var nav = selectable.navigation;
        nav.mode = Navigation.Mode.None;
        selectable.navigation = nav;
    }

    // 0.15.0: belt-and-braces — even with Navigation.Mode.None we explicitly
    // clear the EventSystem's selected GameObject after each click so a stale
    // selection from elsewhere in the UI can't ghost-activate via controller A
    // on the next press. Mirrors the same pattern FormBuilder uses after a
    // form submit (~line 180 of FormBuilder.cs).
    private static void ClearEventSystemSelection()
    {
        try { EventSystem.current?.SetSelectedGameObject(null); }
        catch { /* harmless — EventSystem may not exist on first frame */ }
    }

    internal override void Reset() { /* nothing extra to reset */ }
}
