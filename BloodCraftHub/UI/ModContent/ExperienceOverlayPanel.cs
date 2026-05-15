using BloodCraftHub.Behaviors;
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
    // 0.9.0: per-overlay background transparency. Settings.XPOverlayTransparency
    // uses the user-facing convention (0=opaque, 1=invisible); TransparencyToAlpha
    // converts and applies the 95% floor so the drag handle stays visible at the
    // user's "100% transparent" choice.
    public override float Opacity => Settings.TransparencyToAlpha(Settings.XPOverlayTransparency);

    private LabelRef _levelLabel;
    private LabelRef _progressLabel;
    private LabelRef _classLabel;
    private LabelRef _exoLabel;
    private bool _subscribed;
    private bool _prestigeSubscribed;
    private bool _exoFetchScheduled;
    private int _exoLevel;
    private int _exoMaxLevel;

    public ExperienceOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();

        _levelLabel    = AddRow("LevelLabel",    "Level —", FontStyles.Bold,  fontSize: Theme.ScaledOverlay(16));
        _progressLabel = AddRow("ProgressLabel", "XP — %",  FontStyles.Normal, fontSize: Theme.ScaledOverlay(14));
        _classLabel    = AddRow("ClassLabel",    "Class —", FontStyles.Italic, fontSize: Theme.ScaledOverlay(13));
        // 0.8.3: EXO prestige row. Friend-testing surfaced that EXO data was
        // entirely missing from the overlay. Populated from PrestigeInfo
        // (TypeName "Exo") which the existing AwaitingPrestigeInfo intercept
        // already parses. Auto-fired once on first show via the deferred-
        // fetch ticker below so the data appears without the user having to
        // run .prestige get Exo manually.
        _exoLabel      = AddRow("ExoLabel",      "EXO Prestige —", FontStyles.Italic, fontSize: Theme.ScaledOverlay(13));

        Render(PlayerStateService.Experience);
        // Render any prestige info we already have cached so re-opening the
        // overlay doesn't blank out the EXO line until the next manual query.
        TryRenderExoFromState();

        if (!_subscribed)
        {
            PlayerStateService.ExperienceChanged += OnExperienceChanged;
            _subscribed = true;
        }
        if (!_prestigeSubscribed)
        {
            PlayerStateService.PrestigeInfoChanged += OnPrestigeInfoChanged;
            _prestigeSubscribed = true;
        }
        ScheduleExoFetch();
    }

    /// <summary>Defer the one-shot .prestige get Exo fetch until MessageService
    /// has bound to the local character/user — overlay panels can construct
    /// during CharacterHUDEntry.Awake, which fires BEFORE MessageService
    /// receives its SetCharacter/SetUser calls. Mirrors the pattern used by
    /// the Familiar Browser's auto-pull (CHANGELOG entry 0.8.1).</summary>
    private void ScheduleExoFetch()
    {
        if (_exoFetchScheduled) return;
        _exoFetchScheduled = true;
        // Capture our state so the ticker can self-unregister cleanly.
        System.Action ticker = null;
        ticker = () =>
        {
            if (!MessageService.IsInitialized) return;
            // De-register first so a tick exception doesn't leave us looping.
            Behaviors.CoreUpdateBehavior.Actions.Remove(ticker);
            try
            {
                MessageService.EnqueueMessage(".prestige get Exo");
            }
            catch (System.Exception ex)
            {
                Utils.LogUtils.LogWarning($"ExperienceOverlay: auto .prestige get Exo failed — {ex.Message}");
            }
        };
        Behaviors.CoreUpdateBehavior.Actions.Add(ticker);
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

    private void OnPrestigeInfoChanged() => TryRenderExoFromState();

    private void TryRenderExoFromState()
    {
        var p = PlayerStateService.PrestigeInfoLatest;
        if (p.TypeName == null) return;
        // PrestigeInfoLatest is shared across all .prestige get queries, so
        // ignore updates for non-EXO types — they're for the prestige tab.
        if (!string.Equals(p.TypeName, "Exo", System.StringComparison.OrdinalIgnoreCase)) return;
        _exoLevel    = p.Level;
        _exoMaxLevel = p.MaxLevel;
        if (_exoLabel != null)
            _exoLabel.TextMesh.text = $"EXO Prestige: {_exoLevel}" + (_exoMaxLevel > 0 ? $" / {_exoMaxLevel}" : "");
    }

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
        if (_prestigeSubscribed)
        {
            PlayerStateService.PrestigeInfoChanged -= OnPrestigeInfoChanged;
            _prestigeSubscribed = false;
        }
    }
}
