using BloodCraftHub.Config;
using BloodCraftHub.Services;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Models;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using TMPro;
using UnityEngine;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;

namespace BloodCraftHub.UI.ModContent;

// Secondary overlay: quick at-a-glance familiar info while playing.
//
// Compact version of the Familiars-tab info: name + level + HP. Subscribes to
// PlayerStateService.FamiliarChanged. Sits below the XP overlay by default.
public class FamiliarOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "FamiliarOverlay";
    public override PanelType PanelType => PanelType.FamiliarOverlay;

    public override int MinWidth  => 240;
    public override int MinHeight => 70;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    public override Vector2 DefaultPosition  => new(
        -Owner.Scaler.m_ReferenceResolution.x * 0.5f,
         Owner.Scaler.m_ReferenceResolution.y * 0.5f - 110f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.UITransparency;

    private LabelRef _nameLabel;
    private LabelRef _progressLabel;
    private LabelRef _statsLabel;
    private bool _subscribed;

    public FamiliarOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();

        _nameLabel     = AddRow("FamOvName",     "—",           FontStyles.Bold,   fontSize: 15);
        _progressLabel = AddRow("FamOvProgress", "Lv —",        FontStyles.Normal, fontSize: 13);
        _statsLabel    = AddRow("FamOvStats",    "HP —",        FontStyles.Normal, fontSize: 12);

        Render(PlayerStateService.Familiar);

        if (!_subscribed)
        {
            PlayerStateService.FamiliarChanged += OnFamiliarChanged;
            _subscribed = true;
        }
    }

    private LabelRef AddRow(string name, string text, FontStyles style, int fontSize)
    {
        var lbl = UIFactory.CreateLabel(ContentRoot, name, text,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: fontSize);
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 220, preferredWidth: 240, flexibleWidth: 1,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        lbl.TextMesh.fontStyle = style;
        return lbl;
    }

    private void OnFamiliarChanged() => Render(PlayerStateService.Familiar);

    private void Render(PlayerStateService.FamiliarState s)
    {
        if (_nameLabel == null) return;

        bool active = s.Level > 0 || !string.IsNullOrEmpty(s.Name);
        _nameLabel.TextMesh.text = string.IsNullOrEmpty(s.Name) ? "(no familiar bound)" : s.Name;
        _progressLabel.TextMesh.text = active
            ? (s.Prestige > 0
                ? $"Lv {s.Level} ({s.Progress * 100f:0.#}%)   Pr {s.Prestige}"
                : $"Lv {s.Level} ({s.Progress * 100f:0.#}%)")
            : "Lv —";
        _statsLabel.TextMesh.text = active
            ? $"HP {s.MaxHealth}  PP {s.PhysicalPower}  SP {s.SpellPower}"
            : "HP —";
    }

    internal override void Reset()
    {
        if (_subscribed)
        {
            PlayerStateService.FamiliarChanged -= OnFamiliarChanged;
            _subscribed = false;
        }
    }
}
