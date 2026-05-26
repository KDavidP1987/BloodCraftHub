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

    // 0.10.7: MinHeight is computed from what we actually render. Pre-0.10.7
    // the static 180 floor (with bonus-stats off) didn't account for the
    // three 12px progress bars users could enable, so when bars were on the
    // default-sized overlay was 200+ px of content squeezed into a 180px
    // container — the VerticalLayoutGroup compressed each row below its
    // glyph-render height and they overlapped visually. Now we sum the
    // actual contributors: title + N label rows × 20px + (optional) 3 bars
    // + (optional) 2 bonus-stat rows. The MinHeight ALSO defines initial
    // overlay size on first show, so growing it solves both "user can't
    // drag smaller than overlap-floor" AND "default size fits content".
    public override int MinWidth  => 240;
    public override int MinHeight
    {
        get
        {
            // 0.10.8: scale the per-row contribution with the overlay text
            // multiplier so the floor tracks what AddRow actually allocates.
            // Pre-0.10.8 the floor was hard-coded at 20 per row and ignored
            // the OverlayFontMultiplier; at Large scale (1.2×) the panel
            // computed MinHeight that fit only ~80% of the actual content
            // height, leaving the overlay's default size smaller than its
            // own content the moment the user enabled bars + bonus stats.
            int titleRow   = ResolveRowHeight(Theme.ScaledOverlay(16));
            int normalRow  = ResolveRowHeight(Theme.ScaledOverlay(13));
            int counterRow = ResolveRowHeight(Theme.ScaledOverlay(11));
            int bonusRow   = ResolveRowHeight(Theme.ScaledOverlay(11)) * 2; // wrapped
            int baseH = titleRow + 5 * normalRow + 12;
            // 0.14.0 friend-test v2: per-system bar flags. Count visible bars
            // among the three rows the XP overlay renders (XP / Weapon / Blood).
            int barCount = 0;
            if (Settings.ShowProgressBarXP)        barCount++;
            if (Settings.ShowProgressBarExpertise) barCount++;
            if (Settings.ShowProgressBarLegacy)    barCount++;
            if (barCount > 0)                   baseH += barCount * (Settings.ProgressBarHeight + 2);
            if (Settings.ShowOverlayBonusStats) baseH += 2 * bonusRow;
            if (Settings.ShowOverlayXpCounter)  baseH += 2 * counterRow;
            return baseH;
        }
    }

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
    // 0.12.0: overlay-wide color theme — opted in after friend-test redirect
    // ("all overlays should honor the panel color preset").
    public override bool UsesCustomBackgroundColor => true;

    private LabelRef _levelLabel;
    private LabelRef _progressLabel;
    private GameObject _xpBar;          // 0.9.2: horizontal progress bar (visible iff Settings.ShowProgressBars)
    private UnityEngine.RectTransform _xpBarFill;
    private UnityEngine.RectTransform _xpBarSubFill; // 0.10.7: prestige sub-line fill
    private LabelRef _classLabel;
    // 0.9.4: equipped-weapon expertise row + optional progress bar.
    // Data lives at PlayerStateService.Expertise (filled from Bloodcraft's
    // Eclipse ProgressToClient stream at indices 9..13).
    private LabelRef _weaponLabel;
    private LabelRef _weaponCounterLabel; // 0.10.7: optional "Exp: X / Y (P%)" sub-row
    private GameObject _weaponBar;
    private UnityEngine.RectTransform _weaponBarFill;
    private UnityEngine.RectTransform _weaponBarSubFill;
    // 0.9.6: bonus-stat detail line under the Weapon row. Visible only when
    // Settings.ShowOverlayBonusStats is on. Body is "+12.5% PhysicalPower …"
    // assembled from the cached .wep get reply (LastResponse with Command=".wep get").
    private LabelRef _weaponStatsLabel;
    // 0.9.6: equivalent Blood Legacy row. Data lives at PlayerStateService.Legacy
    // (Eclipse stream indices 4..8) and the bonus-stat values land via .bl get
    // <CurrentBlood> -> AwaitingBloodInfo intercept -> PlayerStateService.BloodInfoLatest.
    private LabelRef _legacyLabel;
    private LabelRef _legacyCounterLabel; // 0.10.7: optional "Ess: X / Y (P%)" sub-row
    private GameObject _legacyBar;
    private UnityEngine.RectTransform _legacyBarFill;
    private UnityEngine.RectTransform _legacyBarSubFill;
    private LabelRef _legacyStatsLabel;
    private LabelRef _exoLabel;
    // 0.10.7: cached values parsed from the .wep get preamble line (raw
    // expertise count + progress percentage). Used to render the optional
    // numerical XP-counter row. Pre-0.10.7 the preamble was discarded
    // entirely; the parse adds zero work to the existing capture pipeline
    // because the line is already buffered in _cachedWepGetLines.
    private int    _wepGetRawExpertise;
    private float  _wepGetProgressPct;
    private bool   _wepGetHasData;
    // 0.14.0 friend-test v6: public read-only accessors so the combined
    // overlay can render the same Bonus Stats / XP Counter sub-rows the
    // standalone XP overlay does. The data + auto-fetch lives here (this
    // panel) for historical reasons; combined just reads it.
    public bool WepGetHasData       => _wepGetHasData;
    public int  WepGetRawExpertise  => _wepGetRawExpertise;
    public float WepGetProgressPct  => _wepGetProgressPct;
    public System.Collections.Generic.IReadOnlyList<string> CachedWepGetLines => _cachedWepGetLines;

    /// <summary>0.14.0 friend-test v6: clean .wep get lines for display. Reuses
    /// the strip-tags + filter-preamble logic from RenderWeaponStats so the
    /// combined overlay's Weapon section shows the exact same content shape.
    /// Returns null when no displayable data is cached.</summary>
    public System.Collections.Generic.List<string> BuildCleanedWepGetStatsLines()
    {
        if (_cachedWepGetLines == null || _cachedWepGetLines.Count == 0) return null;
        var clean = new System.Collections.Generic.List<string>(_cachedWepGetLines.Count);
        foreach (var line in _cachedWepGetLines)
        {
            var stripped = _tmpTagStripRegex.Replace(line, string.Empty).Trim();
            if (string.IsNullOrEmpty(stripped)) continue;
            if (IsWepGetPreambleOrPlaceholder(stripped)) continue;
            clean.Add(stripped);
        }
        return clean.Count > 0 ? clean : null;
    }
    private bool _subscribed;
    private bool _expertiseSubscribed;
    private bool _legacySubscribed;
    private bool _prestigeSubscribed;
    private bool _lastResponseSubscribed;
    private bool _bloodInfoSubscribed;
    private bool _exoFetchScheduled;
    private int _exoLevel;
    private int _exoMaxLevel;
    // 0.9.6: cached snapshot of the most recent .wep get reply lines. Survives
    // subsequent unrelated AwaitingGenericResponse captures (e.g. .lvl get)
    // that would otherwise overwrite PlayerStateService.LastResponse. Refreshed
    // by OnLastResponseChanged when Command == ".wep get".
    private System.Collections.Generic.List<string> _cachedWepGetLines;
    // 0.9.6: ticker for the auto-refresh loop (.wep get + .bl get every
    // OVERLAY_BONUS_REFRESH_SECONDS while the overlay is visible AND
    // Settings.ShowOverlayBonusStats is on). Registered/unregistered as state
    // changes; survives bonus-stats toggle on/off without leaking.
    private System.Action _bonusStatsTicker;
    private double _lastBonusStatsFetchAt;
    private const double OVERLAY_BONUS_REFRESH_SECONDS = 10.0;
    // 0.9.9: alternate which command fires per tick because they share the
    // single _intercept field in MessageService_Processing. Pre-0.9.9 we
    // fired both .wep get and .bl get back-to-back; the second arming
    // overwrote the first, so the wep reply landed with the flag already
    // pointing at AwaitingBloodInfo and got dropped. Toggling means each
    // command refreshes every 2 * OVERLAY_BONUS_REFRESH_SECONDS (20s by
    // default) — acceptable since bonus values only change on level-up or
    // when the player picks a new stat via .wep cst / .bl cst.
    private bool _bonusStatsFetchToggle;

    public ExperienceOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();

        _levelLabel    = AddRow("LevelLabel",    "Level —", FontStyles.Bold,  fontSize: Theme.ScaledOverlay(16));
        _progressLabel = AddRow("ProgressLabel", "XP — %",  FontStyles.Normal, fontSize: Theme.ScaledOverlay(14));
        // 0.9.2 / 0.10.7: bar variant — CreateWithSubLine gives us both the
        // main fill RT and a secondary sub-line fill RT for the optional
        // prestige-progress overlay (Settings.ShowPrestigeSubLine).
        _xpBar = MiniBar.CreateWithSubLine(ContentRoot, "XpBar", out _xpBarFill, out _xpBarSubFill,
            fillColor: new UnityEngine.Color(0.4f, 0.85f, 1f, 0.95f),
            height: ResolveBarHeight());
        _xpBar.SetActive(false);
        _classLabel    = AddRow("ClassLabel",    "Class —", FontStyles.Italic, fontSize: Theme.ScaledOverlay(13));
        // 0.9.4: equipped-weapon expertise row. Friend-testing: "I didn't see
        // in any of the experience overlays where it would show the current
        // weapon and its experience and prestige level". Bloodcraft only
        // streams the currently-equipped weapon's data over Eclipse — switch
        // weapons in-game and this row will update on the next stream tick.
        _weaponLabel   = AddRow("WeaponLabel",   "Weapon —", FontStyles.Normal, fontSize: Theme.ScaledOverlay(13));
        // 0.10.7: optional XP-counter row under the Weapon label. Renders
        // "Exp: X / Y (P%)" when Settings.ShowOverlayXpCounter is on AND
        // the .wep get preamble has been parsed. Sub-label sized smaller
        // to read as detail rather than a peer row.
        _weaponCounterLabel = AddRow("WeaponCounterLabel", "", FontStyles.Italic, fontSize: Theme.ScaledOverlay(11));
        _weaponCounterLabel.GameObject.SetActive(false);
        _weaponBar     = Framework.CustomLib.Controls.MiniBar.CreateWithSubLine(
            ContentRoot, "WeaponXpBar", out _weaponBarFill, out _weaponBarSubFill,
            fillColor: new UnityEngine.Color(1f, 0.45f, 0.3f, 0.95f),
            height: ResolveBarHeight()); // distinct copper/red so it doesn't compete with the XP cyan bar above
        _weaponBar.SetActive(false);
        // 0.9.6: bonus-stat detail row for the equipped weapon. Visible only
        // when Settings.ShowOverlayBonusStats is on AND a .wep get response
        // is cached (auto-fetched by the bonus-stats ticker). Slightly smaller
        // font so the line reads as a sub-detail rather than a peer row.
        _weaponStatsLabel = AddRow("WeaponStatsLabel", "", FontStyles.Italic, fontSize: Theme.ScaledOverlay(11));
        ConfigureBonusStatsLabel(_weaponStatsLabel);
        _weaponStatsLabel.GameObject.SetActive(false);
        // 0.9.6: Blood Legacy row. Friend-testing: "the overlays are missing
        // the blood legacy status and experience". Data comes from
        // PlayerStateService.Legacy (Eclipse stream indices 4..8). Matches
        // the Weapon-row shape so the overlay reads as four parallel rows
        // (Level/Class/Weapon/Legacy/EXO).
        _legacyLabel   = AddRow("LegacyLabel",   "Legacy —", FontStyles.Normal, fontSize: Theme.ScaledOverlay(13));
        _legacyCounterLabel = AddRow("LegacyCounterLabel", "", FontStyles.Italic, fontSize: Theme.ScaledOverlay(11));
        _legacyCounterLabel.GameObject.SetActive(false);
        _legacyBar     = Framework.CustomLib.Controls.MiniBar.CreateWithSubLine(
            ContentRoot, "LegacyXpBar", out _legacyBarFill, out _legacyBarSubFill,
            fillColor: new UnityEngine.Color(0.75f, 0.25f, 0.45f, 0.95f),
            height: ResolveBarHeight()); // crimson/wine to evoke Bloodcraft's red blood-legacy theme without matching the warning red used for prestige
        _legacyBar.SetActive(false);
        _legacyStatsLabel = AddRow("LegacyStatsLabel", "", FontStyles.Italic, fontSize: Theme.ScaledOverlay(11));
        ConfigureBonusStatsLabel(_legacyStatsLabel);
        _legacyStatsLabel.GameObject.SetActive(false);
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
        if (!_legacySubscribed)
        {
            PlayerStateService.LegacyChanged += OnLegacyChanged;
            _legacySubscribed = true;
        }
        if (!_prestigeSubscribed)
        {
            PlayerStateService.PrestigeInfoChanged += OnPrestigeInfoChanged;
            _prestigeSubscribed = true;
        }
        if (!_lastResponseSubscribed)
        {
            PlayerStateService.LastResponseChanged += OnLastResponseChanged;
            _lastResponseSubscribed = true;
        }
        if (!_bloodInfoSubscribed)
        {
            PlayerStateService.BloodInfoChanged += OnBloodInfoChanged;
            _bloodInfoSubscribed = true;
        }
        // Initial render of the weapon + legacy rows from whatever's already
        // cached (Eclipse's first ProgressToClient typically lands sub-1s after
        // handshake, so by the time the overlay opens these usually have data).
        RenderWeapon(PlayerStateService.Expertise);
        RenderLegacy(PlayerStateService.Legacy);
        // Seed the wep-stats cache from any LastResponse already on file —
        // useful when the overlay is re-opened later in the session.
        var seed = PlayerStateService.LastResponse;
        if (seed.Command == ".wep get" && seed.Lines != null && seed.Lines.Count > 0)
            _cachedWepGetLines = new System.Collections.Generic.List<string>(seed.Lines);
        ScheduleExoFetch();
        // 0.9.6: Register the bonus-stats refresh ticker unconditionally. It
        // self-gates on Settings.ShowOverlayBonusStats inside the body — so
        // toggling the setting at runtime takes effect within one frame
        // without needing a settings-change callback. When the setting is
        // off, each tick is one bool check + return.
        if (_bonusStatsTicker == null)
        {
            _bonusStatsTicker = BonusStatsTick;
            Behaviors.CoreUpdateBehavior.Actions.Add(_bonusStatsTicker);
        }
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

    // 0.9.9: bonus-stat detail labels need to wrap to additional lines instead
    // of overflowing past the overlay edge. AddRow defaults to
    // enableWordWrapping=false (good for single-line value displays); override
    // here so a long stat line "Sword Stats: PhysicalPower: 12.5, SpellPower:
    // 8.3" wraps across as many lines as needed within the overlay's width.
    // A ContentSizeFitter on the label's vertical axis lets it auto-grow so
    // sibling rows below it don't get clipped.
    private void ConfigureBonusStatsLabel(LabelRef lbl)
    {
        if (lbl == null) return;
        lbl.TextMesh.enableWordWrapping = true;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        // 0.10.8: preferredHeight=-1 means "let the next-priority provider
        // decide" — which here is TMP's own ILayoutElement.preferredHeight
        // (the actual wrapped pixel height for the current font + text +
        // width). Pre-0.10.8 the LayoutElement hard-coded 32 and so won
        // the priority race against TMP; at Large overlay scale TMP wanted
        // ~40 and the rect stayed pinned at 32, overflowing into the
        // progress bar above (and into the Legacy row below). With -1
        // here, the ContentSizeFitter resolves to TMP's value at any
        // scale, so the row's actual height matches its rendered glyphs.
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 220, preferredWidth: 240, flexibleWidth: 1,
            minHeight: 16, preferredHeight: -1, flexibleHeight: 0);
        var fitter = lbl.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
    }

    private LabelRef AddRow(string name, string text, FontStyles style, int fontSize)
    {
        // 0.10.2: alignment reads the OverlayTextAlignment setting (Left / Right).
        // The setting toggle in Display Settings rebuilds the overlay so a flip
        // takes effect immediately rather than waiting for the next render.
        // 0.10.7: minHeight = preferredHeight = 20 floor.
        // 0.10.8: row height now scales with fontSize. Pre-0.10.8 the fixed
        // 20px row was correct for the default 13–14pt font but at the Large
        // overlay text scale (1.2×) those fonts became ~16–17pt with a
        // ~22–24px line box — the glyphs overflowed the row's rect and
        // visually overlapped the progress bar above and the sub-stat row
        // below. ResolveRowHeight scales the floor with fontSize, so a 16pt
        // line gets a 24px row at any scale and there's no glyph spill.
        int rowH = ResolveRowHeight(fontSize);
        var lbl = UIFactory.CreateLabel(ContentRoot, name, text,
            Theme.OverlayMidlineAlignment(), color: null, fontSize: fontSize);
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 220, preferredWidth: 240, flexibleWidth: 1,
            minHeight: rowH, preferredHeight: rowH, flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        lbl.TextMesh.fontStyle = style;
        return lbl;
    }

    // 0.10.8: derive a row height that comfortably fits a single line of the
    // given font size with the small inter-row gap the overlay relies on.
    // 1.45× line height (rounded) keeps glyphs inside the row at every
    // OverlayFontMultiplier value: 11→16, 12→18, 13→19, 14→21, 16→24, 17→25.
    // Floored at 20 so the regular label rows never get smaller than what
    // the rest of the panel expects.
    private static int ResolveRowHeight(int fontSize) =>
        UnityEngine.Mathf.Max(20, UnityEngine.Mathf.RoundToInt(fontSize * 1.45f));

    private void OnExperienceChanged() => Render(PlayerStateService.Experience);

    // 0.10.2: track the last-seen Type for each so an actual weapon/blood
    // switch (vs. just XP gain) can force an immediate bonus-stats refresh
    // instead of waiting up to ~20s for the next BonusStatsTick cadence.
    private PlayerStateService.WeaponType _lastSeenWeaponType;
    private PlayerStateService.BloodType  _lastSeenBloodType;
    private bool _seenWeaponTypeBaseline;
    private bool _seenBloodTypeBaseline;

    private void OnExpertiseChanged()
    {
        var e = PlayerStateService.Expertise;
        if (_seenWeaponTypeBaseline && e.Type != _lastSeenWeaponType)
        {
            // Weapon swap detected — force the bonus-stats ticker to fire on
            // its next pump regardless of the 10s cadence. Also flip the
            // fetch toggle to .wep get so the FIRST tick after this point
            // queries the new weapon's bonuses (not the blood legacy).
            _lastBonusStatsFetchAt = 0;
            _bonusStatsFetchToggle = true;
            // Clear the cached lines so the wep-stats sub-label hides until
            // the new weapon's reply lands (no flash of stale "Sword Stats…").
            _cachedWepGetLines = null;
        }
        _lastSeenWeaponType = e.Type;
        _seenWeaponTypeBaseline = true;
        RenderWeapon(e);
        RenderWeaponStats();
    }

    private void OnLegacyChanged()
    {
        var l = PlayerStateService.Legacy;
        if (_seenBloodTypeBaseline && l.Type != _lastSeenBloodType)
        {
            // Blood swap detected — same logic as weapon. Set the toggle so
            // the very next BonusStatsTick fires .bl get against the new
            // blood type rather than continuing the old alternation.
            _lastBonusStatsFetchAt = 0;
            _bonusStatsFetchToggle = false;
        }
        _lastSeenBloodType = l.Type;
        _seenBloodTypeBaseline = true;
        RenderLegacy(l);
        RenderLegacyStats();
    }

    private void OnLastResponseChanged()
    {
        // The generic capture buffer (LastResponse) is shared across many read-
        // data commands. Snapshot lines only when we recognize the .wep get
        // payload — otherwise we'd clobber our weapon-stats cache every time
        // the user runs an unrelated command like .lvl get / .class l.
        var r = PlayerStateService.LastResponse;
        if (r.Command != ".wep get" || r.Lines == null) return;
        _cachedWepGetLines = new System.Collections.Generic.List<string>(r.Lines);
        // 0.10.7: also extract raw expertise + pct from the preamble line so
        // the optional XP-counter row has data without a second server query.
        ParseWepGetPreamble(_cachedWepGetLines);
        RenderWeaponStats();
        RenderWeaponCounter();
    }

    // 0.10.7: parse the leading "Your weapon expertise is..." line for raw
    // expertise (yellow group) and progress percentage (white group inside
    // parens). The other captured values (level, prestige, type) are already
    // mirrored from the Eclipse stream so we don't need them here.
    private static readonly System.Text.RegularExpressions.Regex _wepGetPreambleRegex
        = new(@"Your weapon expertise is \[<color=white>\d+</color>\]\[<color=#90EE90>\d+</color>\] and you have <color=yellow>(?<xp>[^<]+)</color> <color=#FFC0CB>expertise</color> \(<color=white>(?<pct>[^<%]+)%?</color>\) with",
              System.Text.RegularExpressions.RegexOptions.Compiled);

    private void ParseWepGetPreamble(System.Collections.Generic.List<string> lines)
    {
        if (lines == null || lines.Count == 0) { _wepGetHasData = false; return; }
        foreach (var line in lines)
        {
            var m = _wepGetPreambleRegex.Match(line);
            if (!m.Success) continue;
            if (!int.TryParse(m.Groups["xp"].Value.Trim(), System.Globalization.NumberStyles.Integer,
                              System.Globalization.CultureInfo.InvariantCulture, out int xp))
                continue;
            if (!float.TryParse(m.Groups["pct"].Value.Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float pct))
                continue;
            _wepGetRawExpertise = xp;
            _wepGetProgressPct = pct;
            _wepGetHasData = true;
            return;
        }
        // No preamble line found in this batch — keep last-known values (could
        // be a partial capture that arrived stat-lines-only).
    }

    private void OnBloodInfoChanged()
    {
        RenderLegacyStats();
        RenderLegacyCounter();
    }

    private void RenderWeapon(PlayerStateService.ExpertiseState e)
    {
        if (_weaponLabel == null) return;
        ApplyBarChrome();
        // 0.15.2: when Weapon Expertise is reliably disabled server-side,
        // show that explicitly. Pre-0.15.2 hit the unarmed-or-pre-handshake
        // placeholder ("Weapon —") which gave no signal about the cause.
        // Friend-test report: "the weapon here I'm actually unarmed but
        // it doesn't register because unarmed is part of weapon expertise
        // which is off" — that user's overlay said "Weapon —" indefinitely.
        if (PlayerStateService.IsSystemReliablyDisabled(PlayerStateService.SystemKind.Expertise))
        {
            _weaponLabel.TextMesh.text = "Weapon (disabled on this server)";
            if (_weaponBar != null && _weaponBar.activeSelf) _weaponBar.SetActive(false);
            if (_weaponStatsLabel != null) _weaponStatsLabel.GameObject.SetActive(false);
            if (_weaponCounterLabel != null) _weaponCounterLabel.GameObject.SetActive(false);
            return;
        }
        // Bloodcraft writes Type=0 (Unarmed) + Level=0 when nothing is
        // equipped at startup. Show "—" placeholder rather than "Unarmed
        // Lv 0" which reads as broken data.
        bool armed = e.Level > 0 || (int)e.Type != 0;
        if (!armed)
        {
            _weaponLabel.TextMesh.text = "Weapon —";
            if (_weaponBar != null && _weaponBar.activeSelf) _weaponBar.SetActive(false);
            RenderWeaponCounter();
            return;
        }

        string label = e.Prestige > 0
            ? $"Weapon: {e.Type}  Lv {e.Level} ({e.Progress * 100f:0.#}%)   Pr {e.Prestige}"
            : $"Weapon: {e.Type}  Lv {e.Level} ({e.Progress * 100f:0.#}%)";
        _weaponLabel.TextMesh.text = label;

        // 0.14.0 friend-test v2: per-system bar toggle controls both standalone + combined.
        bool showBar = Settings.ShowProgressBarExpertise;
        if (_weaponBar != null && _weaponBar.activeSelf != showBar) _weaponBar.SetActive(showBar);
        if (showBar)
        {
            Framework.CustomLib.Controls.MiniBar.SetProgress(_weaponBarFill, e.Progress);
            // 0.10.7: prestige sub-line shows Level / MaxExpertiseLevel
            // (progress within the current prestige cycle toward the next).
            int max = PlayerStateService.Config.MaxExpertiseLevel;
            float subProgress = (max > 0) ? UnityEngine.Mathf.Clamp01((float)e.Level / max) : 0f;
            ApplySubLine(_weaponBar, _weaponBarSubFill, Settings.ShowPrestigeSubLine && max > 0, subProgress);
        }
        RenderWeaponCounter();
    }

    private void RenderLegacy(PlayerStateService.LegacyState l)
    {
        if (_legacyLabel == null) return;
        ApplyBarChrome();
        // 0.15.2: same disabled-detection as Weapon above.
        if (PlayerStateService.IsSystemReliablyDisabled(PlayerStateService.SystemKind.Legacy))
        {
            _legacyLabel.TextMesh.text = "Legacy (disabled on this server)";
            if (_legacyBar != null && _legacyBar.activeSelf) _legacyBar.SetActive(false);
            if (_legacyStatsLabel != null) _legacyStatsLabel.GameObject.SetActive(false);
            if (_legacyCounterLabel != null) _legacyCounterLabel.GameObject.SetActive(false);
            return;
        }
        // Pre-handshake or no blood selected: Bloodcraft streams Type=0 +
        // Level=0. Same placeholder treatment as the Weapon row.
        bool armed = l.Level > 0 || (int)l.Type != 0;
        if (!armed)
        {
            _legacyLabel.TextMesh.text = "Legacy —";
            if (_legacyBar != null && _legacyBar.activeSelf) _legacyBar.SetActive(false);
            RenderLegacyCounter();
            return;
        }

        string label = l.Prestige > 0
            ? $"Legacy: {l.Type}  Lv {l.Level} ({l.Progress * 100f:0.#}%)   Pr {l.Prestige}"
            : $"Legacy: {l.Type}  Lv {l.Level} ({l.Progress * 100f:0.#}%)";
        _legacyLabel.TextMesh.text = label;

        // 0.14.0 friend-test v2: per-system bar toggle.
        bool showBar = Settings.ShowProgressBarLegacy;
        if (_legacyBar != null && _legacyBar.activeSelf != showBar) _legacyBar.SetActive(showBar);
        if (showBar)
        {
            Framework.CustomLib.Controls.MiniBar.SetProgress(_legacyBarFill, l.Progress);
            int max = PlayerStateService.Config.MaxLegacyLevel;
            float subProgress = (max > 0) ? UnityEngine.Mathf.Clamp01((float)l.Level / max) : 0f;
            ApplySubLine(_legacyBar, _legacyBarSubFill, Settings.ShowPrestigeSubLine && max > 0, subProgress);
        }
        RenderLegacyCounter();
    }

    // 0.9.9: regex that strips TMPro color tags from a captured chat line so
    // the overlay sub-label renders as plain wrapped text. The colored chat
    // version is fine for the main panel's Last-Response panel where the
    // tags help match what the user sees in chat, but inside a narrow overlay
    // we prefer compact unstyled text because each <color> tag eats ~16 chars
    // of label width with no rendered glyphs.
    private static readonly System.Text.RegularExpressions.Regex _tmpTagStripRegex
        = new(@"</?color(=[^>]*)?>", System.Text.RegularExpressions.RegexOptions.Compiled);

    // 0.9.6 / 0.9.9 / 0.10.7: render the captured .wep get lines as multi-
    // line wrapped text under the Weapon row. 0.10.7 changes: filter out
    // the preamble line ("Your weapon expertise is...") and the "no bonuses
    // yet" placeholder. Those duplicate info already shown in the Weapon
    // title row (Lv/Pr/Pct) or are not informative. Only keep lines that
    // match the "<weaponType> Stats: ..." shape so the sub-label reads as
    // pure stat values.
    private void RenderWeaponStats()
    {
        if (_weaponStatsLabel == null) return;
        bool show = Settings.ShowOverlayBonusStats;
        if (!show || _cachedWepGetLines == null || _cachedWepGetLines.Count == 0)
        {
            if (_weaponStatsLabel.GameObject.activeSelf) _weaponStatsLabel.GameObject.SetActive(false);
            return;
        }

        var clean = new System.Collections.Generic.List<string>(_cachedWepGetLines.Count);
        foreach (var line in _cachedWepGetLines)
        {
            var stripped = _tmpTagStripRegex.Replace(line, string.Empty).Trim();
            if (string.IsNullOrEmpty(stripped)) continue;
            if (IsWepGetPreambleOrPlaceholder(stripped)) continue;
            clean.Add(stripped);
        }
        if (clean.Count == 0)
        {
            if (_weaponStatsLabel.GameObject.activeSelf) _weaponStatsLabel.GameObject.SetActive(false);
            return;
        }
        _weaponStatsLabel.TextMesh.text = string.Join("\n", clean);
        if (!_weaponStatsLabel.GameObject.activeSelf) _weaponStatsLabel.GameObject.SetActive(true);
    }

    private static bool IsWepGetPreambleOrPlaceholder(string strippedLine)
    {
        // Mirrors the three plain-leading prefixes Bloodcraft can emit for
        // `.wep get` (see MessageService_Processing._genericReplyPlainHeaders).
        // The first is the redundant title; the latter two are "no data yet"
        // states that don't add to the overlay since the title row already
        // shows Lv 0 / no weapon equipped.
        return strippedLine.StartsWith("Your weapon expertise is", System.StringComparison.Ordinal)
            || strippedLine.StartsWith("No bonuses from currently equipped", System.StringComparison.Ordinal)
            || strippedLine.StartsWith("You haven't gained any expertise for", System.StringComparison.Ordinal);
    }

    // 0.10.7: numerical XP-counter sub-row "Exp: X / Y (P%)". Derives Y
    // from X / (P/100) where P is the level-progress percentage from the
    // .wep get reply. Hidden when ShowOverlayXpCounter is off or no
    // preamble has been parsed yet.
    private void RenderWeaponCounter()
    {
        if (_weaponCounterLabel == null) return;
        bool show = Settings.ShowOverlayXpCounter;
        if (!show || !_wepGetHasData)
        {
            if (_weaponCounterLabel.GameObject.activeSelf) _weaponCounterLabel.GameObject.SetActive(false);
            return;
        }
        int total = (_wepGetProgressPct > 0.01f)
            ? UnityEngine.Mathf.RoundToInt(_wepGetRawExpertise * 100f / _wepGetProgressPct)
            : 0;
        string body = total > 0
            ? $"Exp: {_wepGetRawExpertise} / {total} ({_wepGetProgressPct:0.0}%)"
            : $"Exp: {_wepGetRawExpertise} ({_wepGetProgressPct:0.0}%)";
        _weaponCounterLabel.TextMesh.text = body;
        if (!_weaponCounterLabel.GameObject.activeSelf) _weaponCounterLabel.GameObject.SetActive(true);
    }

    // 0.10.7: Legacy XP-counter sub-row. Same shape, but PlayerStateService
    // already stores the raw essence + percentage in BloodInfoLatest (the
    // existing .bl get parser writes them as strings; we parse to numbers
    // here on render so values stay fresh without a re-parse pass).
    private void RenderLegacyCounter()
    {
        if (_legacyCounterLabel == null) return;
        bool show = Settings.ShowOverlayXpCounter;
        var info = PlayerStateService.BloodInfoLatest;
        if (!show || string.IsNullOrEmpty(info.BloodType))
        {
            if (_legacyCounterLabel.GameObject.activeSelf) _legacyCounterLabel.GameObject.SetActive(false);
            return;
        }
        if (!int.TryParse(info.Essence ?? "", System.Globalization.NumberStyles.Integer,
                          System.Globalization.CultureInfo.InvariantCulture, out int ess))
        {
            // Some replies surface "N.M" (decimal essence). Fall back to float and round.
            if (float.TryParse(info.Essence ?? "", System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float essF))
                ess = UnityEngine.Mathf.RoundToInt(essF);
            else
            {
                if (_legacyCounterLabel.GameObject.activeSelf) _legacyCounterLabel.GameObject.SetActive(false);
                return;
            }
        }
        if (!float.TryParse(info.ProgressPct ?? "", System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float pct))
            pct = 0f;
        int total = (pct > 0.01f) ? UnityEngine.Mathf.RoundToInt(ess * 100f / pct) : 0;
        string body = total > 0
            ? $"Ess: {ess} / {total} ({pct:0.0}%)"
            : $"Ess: {ess} ({pct:0.0}%)";
        _legacyCounterLabel.TextMesh.text = body;
        if (!_legacyCounterLabel.GameObject.activeSelf) _legacyCounterLabel.GameObject.SetActive(true);
    }

    // 0.10.7: Resolve current bar height from settings. Absolute pixels by
    // default (Settings.ProgressBarHeight, clamped 4..24); relative mode
    // returns the same pixel value but the caller can choose to apply a
    // multiplier later (currently no-op — relative behavior pre-0.10.7
    // came from VerticalLayoutGroup's child-controlled height with
    // flexibleHeight=1, not from our code).
    private static int ResolveBarHeight()
    {
        return Settings.ProgressBarHeight;
    }

    // 0.10.7: apply bar height + prestige sub-line visibility every render
    // so toggling settings at runtime takes effect without a panel rebuild.
    private void ApplyBarChrome()
    {
        int h = ResolveBarHeight();
        Framework.CustomLib.Controls.MiniBar.SetHeight(_xpBar, h);
        Framework.CustomLib.Controls.MiniBar.SetHeight(_weaponBar, h);
        Framework.CustomLib.Controls.MiniBar.SetHeight(_legacyBar, h);
    }

    private static void ApplySubLine(GameObject bar, UnityEngine.RectTransform subFill, bool wantVisible, float progress01)
    {
        if (bar == null || subFill == null) return;
        var subGo = subFill.gameObject;
        if (!wantVisible)
        {
            if (subGo.activeSelf) subGo.SetActive(false);
            return;
        }
        if (!subGo.activeSelf) subGo.SetActive(true);
        Framework.CustomLib.Controls.MiniBar.SetProgress(subFill, progress01);
    }

    private void RenderLegacyStats()
    {
        if (_legacyStatsLabel == null) return;
        bool show = Settings.ShowOverlayBonusStats;
        var info = PlayerStateService.BloodInfoLatest;
        bool haveData = !string.IsNullOrEmpty(info.BloodType)
                     && info.StatLines != null
                     && info.StatLines.Count > 0;
        if (!show || !haveData)
        {
            if (_legacyStatsLabel.GameObject.activeSelf) _legacyStatsLabel.GameObject.SetActive(false);
            return;
        }
        // BloodInfo.StatLines already has color tags stripped (set up by the
        // intercept's _stripTmpTagsRegex pass). 0.9.9: newline-join instead of
        // bullet-separated single line so it wraps cleanly inside the overlay.
        _legacyStatsLabel.TextMesh.text = string.Join("\n", info.StatLines);
        if (!_legacyStatsLabel.GameObject.activeSelf) _legacyStatsLabel.GameObject.SetActive(true);
    }

    // 0.9.6: per-frame ticker that drives the bonus-stats refresh loop. Always
    // registered (cheap when the feature is off — one bool check + return);
    // self-gates on Settings.ShowOverlayBonusStats so the user can toggle the
    // setting at runtime and see the effect within one frame.
    private void BonusStatsTick()
    {
        // Always re-render so the stat-values rows hide promptly when the
        // setting is toggled off (without waiting for the next data event).
        // RenderWeaponStats/RenderLegacyStats are no-ops when their data is
        // already cached and visibility matches the setting.
        RenderWeaponStats();
        RenderLegacyStats();

        if (!Settings.ShowOverlayBonusStats) return;
        if (!MessageService.IsInitialized) return;
        // 0.17.3: under Eclipse stand-down (command-console mode) the XP / weapon /
        // blood layer is ECLIPSE's domain — BCH doesn't read the Bloodcraft stream, so
        // PlayerStateService.Legacy is stale and these passive `.wep get` / `.bl get`
        // auto-queries misfire; their replies aren't consumed and SPAM chat with blood/
        // weapon system messages. Stop the auto-query entirely while standing down (the
        // manual Refresh buttons still work; Eclipse provides the live data).
        if (Services.EclipseProtocolService.StandDownForEclipse()) return;
        // 0.14.0 friend-test v6: fire when EITHER the standalone XP overlay
        // OR the combined overlay is visible. Combined needs the same cached
        // .wep get / .bl get data; without this gate change, combined-mode
        // users got no bonus stats / counter rows because this overlay was
        // hidden and the ticker self-suppressed.
        bool combinedActive = Plugin.UIManager?.CombinedOverlay?.Enabled ?? false;
        if (!Enabled && !combinedActive) return;

        var now = UnityEngine.Time.realtimeSinceStartupAsDouble;
        if (_lastBonusStatsFetchAt > 0 && now - _lastBonusStatsFetchAt < OVERLAY_BONUS_REFRESH_SECONDS) return;
        _lastBonusStatsFetchAt = now;

        try
        {
            // 0.9.9: alternate per tick. Both commands compete for the single
            // _intercept field — firing them back-to-back lost the first one
            // (see _bonusStatsFetchToggle docstring above).
            if (_bonusStatsFetchToggle)
            {
                // 0.10.2: send via EnqueueMessageSilent so the .wep get reply
                // gets eaten from chat — the values are already mirrored to the
                // overlay's stat-values sub-label, so the chat copy is noise.
                MessageService.EnqueueMessageSilent(MessageService.BCCOM_WEP_GET);
            }
            else
            {
                // .bl get <CurrentBlood> is the only form that arms
                // AwaitingBloodInfo (the no-arg .bl get is suppressed because
                // the live Eclipse stream already keeps PlayerStateService.Legacy
                // fresh; intercepting it would double-update the structured display).
                var leg = PlayerStateService.Legacy;
                // 0.13.1: gate on bondable-blood-type. Skips Frailed / VBlood /
                // GateBoss which Bloodcraft rejects as legacy choices — sending
                // `.bl get Frailed` produces no reply and the AwaitingBloodInfo
                // intercept times out, spamming the BepInEx log. The earlier
                // `Type != 0` guard mistakenly skipped Worker (a valid bondable
                // blood) and missed Frailed entirely. Now sourced from
                // PlayerStateService.IsBondableBloodType so the same check
                // drives the per-tab refresh too.
                if (PlayerStateService.IsBondableBloodType(leg.Type))
                    MessageService.EnqueueMessageSilent(string.Format(MessageService.BCCOM_BL_GET_FORMAT, leg.Type));
            }
            _bonusStatsFetchToggle = !_bonusStatsFetchToggle;
        }
        catch (System.Exception ex)
        {
            Utils.LogUtils.LogWarning($"ExperienceOverlay: bonus-stats refresh failed — {ex.Message}");
        }
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
        ApplyBarChrome();

        // 0.15.2: when Leveling is reliably detected disabled server-side,
        // swap the misleading "Level 0   XP 0.0%" rendering for a single
        // clear hint line. Friend-test 0.15.1: server with only Quest
        // enabled showed all other systems as functional (zero data
        // looked like brand-new-character data). Mirrors the Quest
        // disabled treatment v0.15.1 added.
        if (PlayerStateService.IsSystemReliablyDisabled(PlayerStateService.SystemKind.Leveling))
        {
            _levelLabel.TextMesh.text    = "(Leveling disabled)";
            _progressLabel.TextMesh.text = "—";
            _classLabel.TextMesh.text    = "";
            if (_xpBar != null && _xpBar.activeSelf) _xpBar.SetActive(false);
            return;
        }

        string levelText = s.Prestige > 0
            ? $"Level {s.Level}   Prestige {s.Prestige}"
            : $"Level {s.Level}";
        _levelLabel.TextMesh.text = levelText;

        _progressLabel.TextMesh.text = $"XP {(s.Progress * 100f):0.#}%";
        _classLabel.TextMesh.text    = $"Class: {s.Class}";

        // 0.9.2 / 0.14.0: progress-bar visibility + fill. Per-system XP bar
        // flag (unified between standalone and combined overlay).
        bool showBar = Settings.ShowProgressBarXP;
        if (_xpBar != null && _xpBar.activeSelf != showBar) _xpBar.SetActive(showBar);
        if (showBar)
        {
            MiniBar.SetProgress(_xpBarFill, s.Progress);
            // 0.10.7: prestige sub-line for player level — Level / MaxPlayerLevel.
            int max = PlayerStateService.Config.MaxPlayerLevel;
            float subProgress = (max > 0) ? UnityEngine.Mathf.Clamp01((float)s.Level / max) : 0f;
            ApplySubLine(_xpBar, _xpBarSubFill, Settings.ShowPrestigeSubLine && max > 0, subProgress);
        }
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
        if (_legacySubscribed)
        {
            PlayerStateService.LegacyChanged -= OnLegacyChanged;
            _legacySubscribed = false;
        }
        if (_prestigeSubscribed)
        {
            PlayerStateService.PrestigeInfoChanged -= OnPrestigeInfoChanged;
            _prestigeSubscribed = false;
        }
        if (_lastResponseSubscribed)
        {
            PlayerStateService.LastResponseChanged -= OnLastResponseChanged;
            _lastResponseSubscribed = false;
        }
        if (_bloodInfoSubscribed)
        {
            PlayerStateService.BloodInfoChanged -= OnBloodInfoChanged;
            _bloodInfoSubscribed = false;
        }
        if (_bonusStatsTicker != null)
        {
            Behaviors.CoreUpdateBehavior.Actions.Remove(_bonusStatsTicker);
            _bonusStatsTicker = null;
        }
    }
}
