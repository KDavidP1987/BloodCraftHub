using BloodCraftHub.Behaviors;
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
    private GameObject _xpBar;          // 0.9.2: horizontal progress bar (visible iff Settings.ShowProgressBars)
    private UnityEngine.RectTransform _xpBarFill;
    private LabelRef _classLabel;
    // 0.9.4: equipped-weapon expertise row + optional progress bar.
    // Data lives at PlayerStateService.Expertise (filled from Bloodcraft's
    // Eclipse ProgressToClient stream at indices 9..13).
    private LabelRef _weaponLabel;
    private GameObject _weaponBar;
    private UnityEngine.RectTransform _weaponBarFill;
    private LabelRef _exoLabel;
    private bool _subscribed;
    private bool _expertiseSubscribed;
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
        // 0.9.2: progress-bar variant of the XP row. Anchored-stretched
        // child Image gives clean proportional scaling regardless of panel
        // width — no need to know the parent's rect on render. SetActive is
        // toggled live each render based on Settings.ShowProgressBars.
        _xpBar = MiniBar.Create(ContentRoot, "XpBar", out _xpBarFill,
            fillColor: new UnityEngine.Color(0.4f, 0.85f, 1f, 0.95f));
        _xpBar.SetActive(false);
        _classLabel    = AddRow("ClassLabel",    "Class —", FontStyles.Italic, fontSize: Theme.ScaledOverlay(13));
        // 0.9.4: equipped-weapon expertise row. Friend-testing: "I didn't see
        // in any of the experience overlays where it would show the current
        // weapon and its experience and prestige level". Bloodcraft only
        // streams the currently-equipped weapon's data over Eclipse — switch
        // weapons in-game and this row will update on the next stream tick.
        _weaponLabel   = AddRow("WeaponLabel",   "Weapon —", FontStyles.Normal, fontSize: Theme.ScaledOverlay(13));
        _weaponBar     = Framework.CustomLib.Controls.MiniBar.Create(ContentRoot, "WeaponXpBar", out _weaponBarFill,
            fillColor: new UnityEngine.Color(1f, 0.45f, 0.3f, 0.95f)); // distinct copper/red so it doesn't compete with the XP cyan bar above
        _weaponBar.SetActive(false);
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
        if (!_expertiseSubscribed)
        {
            PlayerStateService.ExpertiseChanged += OnExpertiseChanged;
            _expertiseSubscribed = true;
        }
        if (!_prestigeSubscribed)
        {
            PlayerStateService.PrestigeInfoChanged += OnPrestigeInfoChanged;
            _prestigeSubscribed = true;
        }
        // Initial render of the weapon row from whatever's already cached
        // (it'll be the equipped weapon's data once Eclipse has handshaken).
        RenderWeapon(PlayerStateService.Expertise);
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

    private void OnExpertiseChanged() => RenderWeapon(PlayerStateService.Expertise);

    private void RenderWeapon(PlayerStateService.ExpertiseState e)
    {
        if (_weaponLabel == null) return;
        // Bloodcraft writes Type=0 (Unarmed) + Level=0 when nothing is
        // equipped at startup. Show "—" placeholder rather than "Unarmed
        // Lv 0" which reads as broken data.
        bool armed = e.Level > 0 || (int)e.Type != 0;
        if (!armed)
        {
            _weaponLabel.TextMesh.text = "Weapon —";
            if (_weaponBar != null && _weaponBar.activeSelf) _weaponBar.SetActive(false);
            return;
        }

        string label = e.Prestige > 0
            ? $"Weapon: {e.Type}  Lv {e.Level} ({e.Progress * 100f:0.#}%)   Pr {e.Prestige}"
            : $"Weapon: {e.Type}  Lv {e.Level} ({e.Progress * 100f:0.#}%)";
        _weaponLabel.TextMesh.text = label;

        bool showBar = Settings.ShowProgressBars;
        if (_weaponBar != null && _weaponBar.activeSelf != showBar) _weaponBar.SetActive(showBar);
        if (showBar) Framework.CustomLib.Controls.MiniBar.SetProgress(_weaponBarFill, e.Progress);
    }

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

        // 0.9.2: progress-bar visibility + fill. Re-read each render so the
        // setting takes effect immediately without rebuild. ShowProgressBars
        // default false → bar stays inactive.
        bool showBar = Settings.ShowProgressBars;
        if (_xpBar != null && _xpBar.activeSelf != showBar) _xpBar.SetActive(showBar);
        if (showBar) MiniBar.SetProgress(_xpBarFill, s.Progress);
    }

    internal override void Reset()
    {
        if (_subscribed)
        {
            PlayerStateService.ExperienceChanged -= OnExperienceChanged;
            _subscribed = false;
        }
        if (_expertiseSubscribed)
        {
            PlayerStateService.ExpertiseChanged -= OnExpertiseChanged;
            _expertiseSubscribed = false;
        }
        if (_prestigeSubscribed)
        {
            PlayerStateService.PrestigeInfoChanged -= OnPrestigeInfoChanged;
            _prestigeSubscribed = false;
        }
    }
}
