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
using BloodCraftHub.UI.Framework.CustomLib.Util;

namespace BloodCraftHub.UI.ModContent;

// Secondary overlay: daily + weekly quest progress.
//
// Live data feed:
//   EclipseProtocolService receives ProgressToClient → PlayerStateService.UpdateDailyQuest /
//   .UpdateWeeklyQuest → QuestChanged event. This panel subscribes and renders
//   target name + progress for BOTH daily and weekly. When a quest is complete
//   the progress line shows "Complete!" and a hint to chat .quest r d (or w) to
//   reroll.
//
// If the server hasn't pushed quest data yet, the panel shows neutral
// placeholders until the first ProgressToClient arrives.
public class DailyQuestOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "DailyQuestOverlay";
    public override PanelType PanelType => PanelType.DailyQuestOverlay;

    public override int MinWidth  => 260;
    public override int MinHeight => 130;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // Default to top-right area of screen, slightly inset.
    public override Vector2 DefaultPosition  => new(
        -Owner.Scaler.m_ReferenceResolution.x * 0.5f + 280,
         Owner.Scaler.m_ReferenceResolution.y * 0.5f - 40);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.DailyQuestTransparency);
    // 0.12.0: overlay-wide color theme.
    public override bool UsesCustomBackgroundColor => true;

    private LabelRef _dailyTitleLabel;
    private LabelRef _dailyTargetLabel;
    private LabelRef _dailyProgressLabel;
    private LabelRef _weeklyTitleLabel;
    private LabelRef _weeklyTargetLabel;
    private LabelRef _weeklyProgressLabel;
    private bool _subscribed;

    public DailyQuestOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();

        _dailyTitleLabel    = AddRow("DailyTitle",    "Daily Quest", FontStyles.Bold,   fontSize: Theme.ScaledOverlay(14));
        _dailyTitleLabel.TextMesh.color = new Color(0f, 1f, 1f); // cyan to match Bloodcraft's #00FFFF
        // 0.9.1: stronger outline so the accent color stays legible on top of
        // bright in-game backdrops when the overlay is set semi-transparent.
        ApplyStrongOutline(_dailyTitleLabel.TextMesh);
        _dailyTargetLabel   = AddRow("DailyTarget",   "—",           FontStyles.Normal, fontSize: Theme.ScaledOverlay(13));
        _dailyProgressLabel = AddRow("DailyProgress", "—",           FontStyles.Italic, fontSize: Theme.ScaledOverlay(13));

        AddSpacer(6);

        _weeklyTitleLabel    = AddRow("WeeklyTitle",    "Weekly Quest", FontStyles.Bold,   fontSize: Theme.ScaledOverlay(14));
        // 0.9.2: dropped the pink family entirely. v0.9.1's lighter magenta
        // still read as pink on red backdrops. Gold/yellow contrasts cleanly
        // against any in-game background.
        _weeklyTitleLabel.TextMesh.color = new Color(1f, 0.85f, 0.3f);
        ApplyStrongOutline(_weeklyTitleLabel.TextMesh);
        _weeklyTargetLabel   = AddRow("WeeklyTarget",   "—",            FontStyles.Normal, fontSize: Theme.ScaledOverlay(13));
        _weeklyProgressLabel = AddRow("WeeklyProgress", "—",            FontStyles.Italic, fontSize: Theme.ScaledOverlay(13));

        Render();

        if (!_subscribed)
        {
            PlayerStateService.QuestChanged += OnQuestChanged;
            _subscribed = true;
        }
    }

    private LabelRef AddRow(string name, string text, FontStyles style, int fontSize)
    {
        // 0.10.2: alignment respects Settings.OverlayTextAlignment. See Theme.OverlayMidlineAlignment.
        var lbl = UIFactory.CreateLabel(ContentRoot, name, text,
            Theme.OverlayMidlineAlignment(), color: null, fontSize: fontSize);
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 240, preferredWidth: 260, flexibleWidth: 1,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        lbl.TextMesh.fontStyle = style;
        return lbl;
    }

    private void AddSpacer(int height)
    {
        var spacer = UIFactory.CreateUIObject("Spacer", ContentRoot);
        UIFactory.SetLayoutElement(spacer, minHeight: height, preferredHeight: height, flexibleHeight: 0, flexibleWidth: 1);
    }

    private void OnQuestChanged() => Render();

    private void Render()
    {
        if (_dailyTitleLabel == null) return;
        FillRow(_dailyTargetLabel,  _dailyProgressLabel,  PlayerStateService.DailyQuest,  ".quest r d");
        FillRow(_weeklyTargetLabel, _weeklyProgressLabel, PlayerStateService.WeeklyQuest, ".quest r w");
    }

    private static void FillRow(LabelRef target, LabelRef progress,
        PlayerStateService.QuestState s, string rerollHint)
    {
        bool hasQuest = !string.IsNullOrEmpty(s.TargetName) || s.Goal > 0;
        if (!hasQuest)
        {
            target.TextMesh.text   = "(none yet)";
            progress.TextMesh.text = "Check back after refresh.";
            progress.TextMesh.color = Color.white;
            return;
        }

        target.TextMesh.text = s.IsVBlood
            ? $"⚔  {s.TargetName}  (V Blood)"
            : $"⚔  {s.TargetName}";

        if (s.Goal > 0 && s.Progress >= s.Goal)
        {
            progress.TextMesh.text  = $"Complete!  ({rerollHint} to reroll)";
            progress.TextMesh.color = new Color(0.6f, 1f, 0.6f);
        }
        else
        {
            progress.TextMesh.text  = $"Progress: {s.Progress} / {s.Goal}";
            progress.TextMesh.color = Color.white;
        }
    }

    /// <summary>0.9.1: widen the per-character outline so accent-colored
    /// labels (cyan, magenta) stay legible against bright in-game backdrops
    /// when the overlay is set semi-transparent. Same pattern as
    /// MainPanel.ApplyStrongAccentOutline.</summary>
    private static void ApplyStrongOutline(TextMeshProUGUI t)
    {
        if (t == null) return;
        try
        {
            t.outlineColor = Color.black;
            t.outlineWidth = 0.25f;
        }
        catch { /* TMP can throw during application teardown */ }
    }

    internal override void Reset()
    {
        if (_subscribed)
        {
            PlayerStateService.QuestChanged -= OnQuestChanged;
            _subscribed = false;
        }
    }
}
