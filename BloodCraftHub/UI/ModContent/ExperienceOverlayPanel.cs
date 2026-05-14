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

// Secondary overlay: experience tracker.
//
// Live data feed:
//   EclipseProtocolService receives ProgressToClient -> PlayerStateService.UpdateExperience -> ExperienceChanged event.
//   This panel subscribes and re-renders three labels: progress, level/prestige, class.
//
// If the server hasn't pushed data yet (e.g. you just joined), the panel shows
// neutral placeholders until the first ProgressToClient arrives - typically <1s
// after registration completes.
public class ExperienceOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "ExperienceOverlay";
    public override PanelType PanelType => PanelType.ExperienceOverlay;

    public override int MinWidth  => 240;
    public override int MinHeight => 90;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    public override Vector2 DefaultPosition  => new(
        -Owner.Scaler.m_ReferenceResolution.x * 0.5f,
         Owner.Scaler.m_ReferenceResolution.y * 0.5f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.UITransparency;

    private LabelRef _levelLabel;
    private LabelRef _progressLabel;
    private LabelRef _classLabel;
    private bool _subscribed;

    public ExperienceOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();

        _levelLabel    = AddRow("LevelLabel",    "Level —", FontStyles.Bold,  fontSize: 16);
        _progressLabel = AddRow("ProgressLabel", "XP — %",  FontStyles.Normal, fontSize: 14);
        _classLabel    = AddRow("ClassLabel",    "Class —", FontStyles.Italic, fontSize: 13);

        Render(PlayerStateService.Experience);

        if (!_subscribed)
        {
            PlayerStateService.ExperienceChanged += OnExperienceChanged;
            _subscribed = true;
        }
    }

    private LabelRef AddRow(string name, string text, FontStyles style, int fontSize)
    {
        var lbl = UIFactory.CreateLabel(ContentRoot, name, text,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: fontSize);
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 220, preferredWidth: 240, flexibleWidth: 1,
            minHeight: 22,  preferredHeight: 24, flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        lbl.TextMesh.fontStyle = style;
        return lbl;
    }

    private void OnExperienceChanged() => Render(PlayerStateService.Experience);

    private void Render(PlayerStateService.ExperienceState s)
    {
        // Some labels may be null if the panel hasn't been built yet (race during first SetActive).
        if (_levelLabel == null) return;

        string levelText = s.Prestige > 0
            ? $"Level {s.Level}   Prestige {s.Prestige}"
            : $"Level {s.Level}";
        _levelLabel.TextMesh.text = levelText;

        _progressLabel.TextMesh.text = $"XP {(s.Progress * 100f):0.#}%";
        _classLabel.TextMesh.text    = $"Class: {s.Class}";
    }

    internal override void Reset()
    {
        if (_subscribed)
        {
            PlayerStateService.ExperienceChanged -= OnExperienceChanged;
            _subscribed = false;
        }
    }
}
