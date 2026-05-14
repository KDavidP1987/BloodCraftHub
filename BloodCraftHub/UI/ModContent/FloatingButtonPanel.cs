using BloodCraftHub.Config;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using BloodCraftHub.Utils;
using UnityEngine;
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

    public override int MinWidth  => 56;
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

        var btn = UIFactory.CreateButton(ContentRoot, "HubToggleButton", "BCH");
        // Skip the parent VerticalLayoutGroup so we can position the button manually.
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 40, preferredWidth: 40, flexibleWidth: 0,
            minHeight: 40, preferredHeight: 40, flexibleHeight: 0,
            ignoreLayout: true);
        var rt = btn.GameObject.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(40, 40);
        rt.anchoredPosition = Vector2.zero;

        btn.OnClick = () =>
        {
            try { Plugin.UIManager.ToggleMainPanel(); }
            catch (System.Exception ex) { LogUtils.LogError($"FloatingButton click failed: {ex}"); }
        };
    }

    internal override void Reset() { /* nothing extra to reset */ }
}
