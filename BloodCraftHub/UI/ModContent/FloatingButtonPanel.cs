using BloodCraftHub.Config;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using BloodCraftHub.Utils;
using UnityEngine;
using UnityEngine.UI;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;

namespace BloodCraftHub.UI.ModContent;

// The minimal, always-visible "hub" button. Click toggles the main panel.
//
// Sized small (40x40), anchored to the top-right of the screen by default.
// Draggable so players can move it out of the way. Not resizable.
public class FloatingButtonPanel : ResizeablePanelBase
{
    public override string PanelId => "FloatingButton";
    public override PanelType PanelType => PanelType.Base;

    public override int MinWidth  => 40;
    public override int MinHeight => 40;

    public override Vector2 DefaultAnchorMin => new(1f, 1f);
    public override Vector2 DefaultAnchorMax => new(1f, 1f);
    public override Vector2 DefaultPivot     => new(1f, 1f);
    public override Vector2 DefaultPosition  => new(-20f, -20f); // 20px inset from top-right corner

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.None;
    public override float Opacity => Settings.UITransparency;
    public override bool ResizeWholePanel => false; // do not let drag area swallow the click

    public FloatingButtonPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        TitleBar.SetActive(false);

        // One button filling the panel. Click toggles the main UI.
        var btn = UIFactory.CreateButton(ContentRoot, "HubToggleButton", "BCH");
        UIFactory.SetLayoutElement(btn.GameObject, minWidth: 40, minHeight: 40, flexibleWidth: 1, flexibleHeight: 1);
        var rt = btn.GameObject.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        btn.OnClick = () =>
        {
            try { Plugin.UIManager.ToggleMainPanel(); }
            catch (System.Exception ex) { LogUtils.LogError($"FloatingButton click failed: {ex}"); }
        };

        // Drag handle: a thin invisible strip at the top so dragging is possible
        // without stealing every click from the button.
        var dragHandle = UIFactory.CreateUIObject("DragHandle", ContentRoot);
        var dragRt = dragHandle.GetComponent<RectTransform>();
        dragRt.anchorMin = new Vector2(0, 1);
        dragRt.anchorMax = new Vector2(1, 1);
        dragRt.pivot = new Vector2(0.5f, 1);
        dragRt.sizeDelta = new Vector2(0, 8);
        dragHandle.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
        Dragger.DraggableArea = dragRt;
    }

    internal override void Reset() { /* nothing extra to reset */ }
}
