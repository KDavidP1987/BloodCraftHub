using BloodCraftHub.Config;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using TMPro;
using UnityEngine;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;

namespace BloodCraftHub.UI.ModContent;

// Secondary overlay: experience tracker.
//
// Phase 2 shell: just a draggable/resizable panel with a placeholder label.
// Phase 4 fills in real XP / prestige / class data fed by EclipseProtocolService.
public class ExperienceOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "ExperienceOverlay";
    public override PanelType PanelType => PanelType.ExperienceOverlay;

    public override int MinWidth  => 220;
    public override int MinHeight => 60;

    public override Vector2 DefaultAnchorMin => new(0f, 1f);
    public override Vector2 DefaultAnchorMax => new(0f, 1f);
    public override Vector2 DefaultPivot     => new(0f, 1f);
    public override Vector2 DefaultPosition  => new(20f, -20f); // top-left inset

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.UITransparency;

    public ExperienceOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();
        var label = UIFactory.CreateLabel(ContentRoot, "XPOverlayLabel",
            "Experience tracker\n(no data yet — wiring in Phase 3)",
            TextAlignmentOptions.Center, color: null, fontSize: 14);
        UIFactory.SetLayoutElement(label.GameObject,
            minWidth: 200, preferredWidth: 220, flexibleWidth: 1,
            minHeight: 40, preferredHeight: 50, flexibleHeight: 1);
        label.TextMesh.enableWordWrapping = true;
        label.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    internal override void Reset() { }
}
