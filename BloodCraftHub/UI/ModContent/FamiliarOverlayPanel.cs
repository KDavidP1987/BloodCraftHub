using BloodCraftHub.Config;
using BloodCraftHub.Services;
using BloodCraftHub.UI.Framework.CustomLib.Controls;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Models;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using TMPro;
using UnityEngine;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;
using BloodCraftHub.UI.Framework.CustomLib.Util;

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
    public override float Opacity => Settings.TransparencyToAlpha(Settings.FamiliarOverlayTransparency);

    private LabelRef _nameLabel;
    private LabelRef _progressLabel;
    private GameObject _xpBar;
    private RectTransform _xpBarFill;
    private LabelRef _statsLabel;
    private bool _subscribed;

    public FamiliarOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();

        _nameLabel     = AddRow("FamOvName",     "—",           FontStyles.Bold,   fontSize: Theme.ScaledOverlay(15));
        _progressLabel = AddRow("FamOvProgress", "Lv —",        FontStyles.Normal, fontSize: Theme.ScaledOverlay(13));
        // 0.9.3: optional familiar XP progress bar (toggle on the Settings tab).
        _xpBar = MiniBar.Create(ContentRoot, "FamXpBar", out _xpBarFill,
            fillColor: new Color(1f, 0.6f, 0.2f, 0.95f)); // warm orange — distinct from XP overlay cyan
        _xpBar.SetActive(false);
        _statsLabel    = AddRow("FamOvStats",    "HP —",        FontStyles.Normal, fontSize: Theme.ScaledOverlay(12));

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

        // 0.9.3: re-read the progress-bar setting each render so toggling it
        // takes effect without rebuild. Hide the bar if no familiar is
        // bound (otherwise we'd render a zero-fill bar that looks broken).
        bool showBar = active && Settings.ShowProgressBars;
        if (_xpBar != null && _xpBar.activeSelf != showBar) _xpBar.SetActive(showBar);
        if (showBar) MiniBar.SetProgress(_xpBarFill, s.Progress);
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
