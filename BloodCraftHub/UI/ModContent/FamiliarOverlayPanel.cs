using BloodCraftHub.Config;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using UnityEngine;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;

namespace BloodCraftHub.UI.ModContent;

// Secondary overlay: quick familiar info while playing.
//
// Phase 2 shell only. Phase 4 will fill in active familiar name, current/max
// health, level, and the bind/unbind/toggle buttons.
public class FamiliarOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "FamiliarOverlay";
    public override PanelType PanelType => PanelType.FamiliarOverlay;

    public override int MinWidth  => 220;
    public override int MinHeight => 80;

    public override Vector2 DefaultAnchorMin => new(0f, 1f);
    public override Vector2 DefaultAnchorMax => new(0f, 1f);
    public override Vector2 DefaultPivot     => new(0f, 1f);
    public override Vector2 DefaultPosition  => new(20f, -100f); // below the XP overlay

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.UITransparency;

    public FamiliarOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();
        var label = UIFactory.CreateLabel(ContentRoot, "FamOverlayLabel",
            "Familiar overlay\n(no data yet — wiring in Phase 3)");
        UIFactory.SetLayoutElement(label.GameObject, flexibleWidth: 1, flexibleHeight: 1);
    }

    internal override void Reset() { }
}
