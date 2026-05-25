using BloodCraftHub.Config;
using BloodCraftHub.Services;
using BloodCraftHub.UI.Framework.CustomLib.Controls;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.CustomLib.Util;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Models;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using TMPro;
using UnityEngine;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;

namespace BloodCraftHub.UI.ModContent;

// 0.14.0: combined info overlay.
//
// One draggable / resizable panel that shows up to 6 sections in a single
// container — XP, Familiar, Weapon Expertise, Blood Legacy, Professions,
// Daily/Weekly Quest. Mutually exclusive with the 5 standalone info
// overlays (ExperienceOverlay / FamiliarOverlay / DailyQuestOverlay /
// ProfessionOverlay — Weapon + Blood data lives inside the XP overlay
// today). When ShowCombinedOverlay is true, BCHubUIManager hides those
// standalone overlays regardless of their own ShowXxxOverlay flags.
//
// Each section:
//   - Has a colored bold heading (Eclipse-style color vocabulary —
//     XP green / Legacy red / Expertise grey / Familiar yellow / etc.).
//   - Renders 1–2 compact text lines fed from PlayerStateService.
//   - Auto-hides via its individual Settings.CombinedOverlayShowXxx flag.
//
// MVP rendering is text-only (no progress bars, no bonus-stat lines).
// Iterations will add bars + stats once the layout proves stable in-game.
//
// Per-profession visibility inside the Professions section still honors
// the v0.13.0 Settings.ShowProfessionXxx checkboxes — those filter rows
// here exactly the way they filter the standalone Professions overlay.
public class CombinedOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "CombinedOverlay";
    public override PanelType PanelType => PanelType.CombinedOverlay;

    public override int MinWidth  => 360;
    // 0.14.0 friend-test: pre-fix the panel loaded crowded because MinHeight
    // was a static 160 — far below what 6 stacked sections need at Standard
    // text scale. Compute from what's actually rendered: per-section header +
    // body line count (which itself depends on per-section flags and progress
    // bar visibility). This also defines the panel's INITIAL size on first
    // construct (PanelBase.EnsureValidSize bumps the panel up to MinHeight),
    // so the user no longer has to drag the bottom border to see the content.
    public override int MinHeight
    {
        get
        {
            int h = ResolveRowHeight(Theme.ScaledOverlay(11)); // chrome budget
            int sectionHeight(int bodyLines, bool barVisible)
                => ResolveRowHeight(Theme.ScaledOverlay(12))           // header row
                 + bodyLines * ResolveRowHeight(Theme.ScaledOverlay(13))
                 + (barVisible ? Settings.ProgressBarHeight + 2 : 0);
            // Bars gated per-section by the v0.14.0 friend-test v2 unified flags.
            // 0.14.0 friend-test v4: Familiar/Weapon/Blood reserve 3 body lines
            // instead of 2 so the wrapping stats line (added in v4 to prevent
            // overflow at Large / X-Large text) has room without the panel
            // clipping the bottom row.
            if (Settings.ShowExperienceOverlay)         h += sectionHeight(1, Settings.ShowProgressBarXP);
            if (Settings.ShowFamiliarOverlay)           h += sectionHeight(3, Settings.ShowProgressBarFamiliar);
            // 0.14.0 friend-test v6: Weapon / Blood sections grow when the
            // user enables ShowOverlayBonusStats (extra wrapped values row,
            // typically 2-3 lines) or ShowOverlayXpCounter (one line). Count
            // those toward the section's reserved height so the panel's
            // snap-to-MinHeight in RefreshSections reserves enough space
            // and doesn't visually clip the bonus sub-rows.
            int wepBodyLines = 3;
            if (Settings.ShowOverlayBonusStats) wepBodyLines += 2;
            if (Settings.ShowOverlayXpCounter)  wepBodyLines += 1;
            int blBodyLines = 3;
            if (Settings.ShowOverlayBonusStats) blBodyLines += 2;
            if (Settings.ShowOverlayXpCounter)  blBodyLines += 1;
            if (Settings.CombinedOverlayShowExpertise)  h += sectionHeight(wepBodyLines, Settings.ShowProgressBarExpertise);
            if (Settings.CombinedOverlayShowLegacy)     h += sectionHeight(blBodyLines,  Settings.ShowProgressBarLegacy);
            if (Settings.ShowProfessionOverlay)
            {
                if (Settings.ShowProgressBarProfessions)
                {
                    // Row mode — count visible per-profession flags and add
                    // (label-row + bar) per visible one.
                    int prof = 0;
                    if (Settings.ShowProfessionEnchanting)    prof++;
                    if (Settings.ShowProfessionAlchemy)       prof++;
                    if (Settings.ShowProfessionHarvesting)    prof++;
                    if (Settings.ShowProfessionBlacksmithing) prof++;
                    if (Settings.ShowProfessionTailoring)     prof++;
                    if (Settings.ShowProfessionWoodcutting)   prof++;
                    if (Settings.ShowProfessionMining)        prof++;
                    if (Settings.ShowProfessionFishing)       prof++;
                    h += ResolveRowHeight(Theme.ScaledOverlay(12)) // header
                       + prof * (ResolveRowHeight(Theme.ScaledOverlay(12)) + Settings.ProgressBarHeight + 2);
                }
                else
                {
                    h += sectionHeight(3, false); // wrap mode — up to ~3 lines
                }
            }
            if (Settings.ShowDailyQuestOverlay)         h += sectionHeight(2, false);
            return h;
        }
    }

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    public override Vector2 DefaultPosition  => new(
        -Owner.Scaler.m_ReferenceResolution.x * 0.5f,
         Owner.Scaler.m_ReferenceResolution.y * 0.5f - 30f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.CombinedOverlayTransparency);
    public override bool UsesCustomBackgroundColor => true;

    // Section heading colors — borrow Eclipse's vocabulary so players
    // already familiar with Eclipse-style HUDs recognize the systems
    // at a glance.
    private static readonly Color COL_XP        = new(0.55f, 0.95f, 0.55f, 1f); // green
    private static readonly Color COL_FAMILIAR  = new(1.00f, 0.78f, 0.30f, 1f); // warm amber
    private static readonly Color COL_EXPERTISE = new(0.85f, 0.85f, 0.85f, 1f); // light grey
    private static readonly Color COL_LEGACY    = new(1.00f, 0.45f, 0.45f, 1f); // Bloodcraft red
    private static readonly Color COL_PROFS     = new(0.95f, 0.75f, 0.35f, 1f); // gold
    private static readonly Color COL_QUEST_D   = new(0.00f, 1.00f, 1.00f, 1f); // cyan (Bloodcraft daily)
    private static readonly Color COL_QUEST_W   = new(1.00f, 0.85f, 0.30f, 1f); // gold (Bloodcraft weekly)

    // Section root GameObjects — SetActive based on per-section flags.
    private GameObject _xpSection;
    private GameObject _familiarSection;
    private GameObject _expertiseSection;
    private GameObject _legacySection;
    private GameObject _profSection;
    private GameObject _questSection;

    // Live labels updated by render handlers.
    private TextMeshProUGUI _xpLine;
    // 0.16.x: EXO prestige line in the combined XP section (from .prestige get
    // Exo — not in the structured protocol). Shown only when the player has exo.
    private TextMeshProUGUI _xpExoLine;
    private int  _xpExoLevel;
    private int  _xpExoMaxLevel;
    private bool _xpExoReceived;
    private bool _exoFetchScheduled;
    private TextMeshProUGUI _famNameLine;
    private TextMeshProUGUI _famStatsLine;
    private TextMeshProUGUI _wepLine;
    private TextMeshProUGUI _wepStatsLine;
    // 0.14.0 friend-test v6: bonus-stat values + XP-counter sub-rows on the
    // Weapon section, mirroring the standalone XP overlay's
    // ShowOverlayBonusStats + ShowOverlayXpCounter behavior. Data is sourced
    // from ExperienceOverlayPanel's public accessors (auto-fetched via the
    // ticker in that panel — which we now ensure is always constructed
    // even when combined is on; see BCHubUIManager.ApplyCombinedOverlayMutualExclusion).
    private TextMeshProUGUI _wepBonusValuesLine;
    private TextMeshProUGUI _wepCounterLine;
    private TextMeshProUGUI _blLine;
    private TextMeshProUGUI _blStatsLine;
    private TextMeshProUGUI _blBonusValuesLine;
    private TextMeshProUGUI _blCounterLine;
    // 0.14.0 friend-test: collapsed the two profession lines into a single
    // word-wrapped label so the row count auto-flows with overlay width
    // instead of overflowing the panel border.
    private TextMeshProUGUI _profLine;
    // 0.14.0 friend-test v3: per-profession row mode for the Professions
    // section, shown when Settings.ShowProgressBarProfessions is true.
    // Mirrors the standalone Profession overlay's label + bar pairs.
    // _profWrapMode hosts the existing wrapped label; _profRowMode hosts
    // the 8 per-profession (label + bar) pairs. Exactly one is active.
    private GameObject _profWrapMode;
    private GameObject _profRowMode;
    private TextMeshProUGUI _profRowEnchanting, _profRowAlchemy, _profRowHarvesting, _profRowBlacksmithing;
    private TextMeshProUGUI _profRowTailoring,  _profRowWoodcutting, _profRowMining, _profRowFishing;
    private GameObject _profBarEnchanting, _profBarAlchemy, _profBarHarvesting, _profBarBlacksmithing;
    private GameObject _profBarTailoring,  _profBarWoodcutting, _profBarMining, _profBarFishing;
    private RectTransform _profBarFillEnchanting, _profBarFillAlchemy, _profBarFillHarvesting, _profBarFillBlacksmithing;
    private RectTransform _profBarFillTailoring,  _profBarFillWoodcutting, _profBarFillMining, _profBarFillFishing;
    private TextMeshProUGUI _dailyLine;
    private TextMeshProUGUI _weeklyLine;

    // 0.14.0: optional progress bars per section. Visible iff
    // Settings.ShowProgressBars (the same flag the standalone overlays use).
    // Skipped for Professions (8 systems, would need 8 bars — clutter) and
    // Quests (categorical 'kill type X N times', no meaningful progress fill
    // for the combined-compact layout). Each bar is created once, the fill
    // RectTransform is reused for live updates.
    private GameObject _xpBar;          private RectTransform _xpBarFill;
    private GameObject _famBar;         private RectTransform _famBarFill;
    private GameObject _wepBar;         private RectTransform _wepBarFill;
    private GameObject _blBar;          private RectTransform _blBarFill;

    private bool _subscribed;

    public CombinedOverlayPanel(UIBase owner) : base(owner) { }

    /// <summary>0.14.0 friend-test v8: override LateConstructUI so the
    /// combined panel always opens at its current MinHeight, ignoring any
    /// previously-saved sizeDelta.
    ///
    /// Why: ResizeablePanelBase.LateConstructUI → ApplySaveData →
    /// RectExtensions.SetAnchorsFromString restores the saved sizeDelta
    /// (which encodes width+height despite the "Anchors" naming). When
    /// the user changes overlay text scale from Large back to Standard,
    /// the saved sizeDelta still reflects the Large-scale height, and
    /// restoring it leaves the rebuilt panel oversized for the new (smaller)
    /// content. v6 + v7 fixes tried to override the restoration from
    /// BCHubUIManager.RebuildAllOverlaysNow but both raced with the
    /// coroutine-deferred ApplySaveData. Doing the snap HERE — same
    /// frame, right after base.LateConstructUI completes — eliminates
    /// the race.
    ///
    /// Trade-off: combined panel's manual height resize doesn't persist
    /// across construct/rebuild. The width still persists (via the saved
    /// data's sizeDelta.x), and anchors/position persist too. Consistent
    /// with the established "track content size" pattern from
    /// RefreshSections (which also snaps height to MinHeight on toggle).</summary>
    protected override void LateConstructUI()
    {
        base.LateConstructUI();
        if (Rect != null)
        {
            // SetSizeWithCurrentAnchors handles both centered and stretched
            // anchor modes correctly (the same v0.11.2 stretched-anchor
            // safety fix we use in EnsureValidSize).
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, MinHeight);
        }
    }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();

        BuildXPSection();
        BuildFamiliarSection();
        BuildExpertiseSection();
        BuildLegacySection();
        BuildProfessionSection();
        BuildQuestSection();

        ApplySectionVisibility();
        RenderAll();

        if (!_subscribed)
        {
            PlayerStateService.ExperienceChanged += OnAnyChanged;
            PlayerStateService.FamiliarChanged   += OnAnyChanged;
            PlayerStateService.ExpertiseChanged  += OnAnyChanged;
            PlayerStateService.LegacyChanged     += OnAnyChanged;
            PlayerStateService.ProfessionChanged += OnAnyChanged;
            PlayerStateService.QuestChanged      += OnAnyChanged;
            // 0.14.0 friend-test v6: subscribe to .wep get + .bl get response
            // events so the bonus-stat values + counter sub-rows update live
            // as the auto-fetch ticker pulls fresh data.
            PlayerStateService.LastResponseChanged += OnAnyChanged;
            PlayerStateService.BloodInfoChanged    += OnAnyChanged;
            PlayerStateService.PrestigeInfoChanged += OnAnyChanged;
            _subscribed = true;
        }

        // 0.16.x: pull exo prestige once so the EXO line fills even when only the
        // combined overlay is used (no standalone XP overlay / Prestige tab open).
        ScheduleExoFetch();
    }

    /// <summary>0.14.0: public hook for the Settings tab to push live
    /// visibility + value updates after the user toggles a per-section
    /// CombinedOverlayShow* checkbox.
    /// 0.14.0 friend-test v5: snap panel height to the (now-dynamic)
    /// MinHeight so a disabled section doesn't leave dead empty space at
    /// the bottom AND an enabled section gets room immediately. Width is
    /// preserved — user controls that via drag / +-. Manual height resize
    /// is reset on each toggle by design: the common use is "configure
    /// once, glance often", and most users prefer the panel to track
    /// content size rather than retain stale empty padding.</summary>
    public void RefreshSections()
    {
        ApplySectionVisibility();
        RenderAll();
        if (Rect != null)
        {
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, MinHeight);
            EnsureValidSize();      // clamp to screen if MinHeight ever exceeds it
            EnsureValidPosition();  // keep in-bounds after height change
            if (Dragger != null) Dragger.OnEndResize();
        }
    }

    /// <summary>0.14.0: row-height resolver mirrored from ExperienceOverlayPanel.
    /// Used by the dynamic MinHeight getter to convert a font-pt to actual
    /// pixel row height (TMP's line height is ~1.45× the font size at our
    /// default; floor at 20px so headers don't collapse below readable).</summary>
    private static int ResolveRowHeight(int fontSize) =>
        UnityEngine.Mathf.Max(20, UnityEngine.Mathf.RoundToInt(fontSize * 1.45f));

    private void ApplySectionVisibility()
    {
        // 0.14.0 friend-test v2: unified visibility flags. XP/Familiar/Professions/
        // Quests share the same Settings.Show*Overlay flag as the standalone
        // overlays — so toggling them in either the footer OR the Settings
        // combined section affects both views consistently. Expertise/Legacy
        // stay combined-only (no standalone equivalent).
        // 0.15.0 (reverted): per-system feature-flag gating was added then
        // pulled — the Familiar/Shift detection signals fired false positives
        // on real-world testing. See ApplyServerFeatureFlagsToOverlays for
        // the full notes. Restoring v0.14 visibility logic.
        if (_xpSection        != null) _xpSection.SetActive(Settings.ShowExperienceOverlay);
        if (_familiarSection  != null) _familiarSection.SetActive(Settings.ShowFamiliarOverlay);
        if (_expertiseSection != null) _expertiseSection.SetActive(Settings.CombinedOverlayShowExpertise);
        if (_legacySection    != null) _legacySection.SetActive(Settings.CombinedOverlayShowLegacy);
        if (_profSection      != null) _profSection.SetActive(Settings.ShowProfessionOverlay);
        if (_questSection     != null) _questSection.SetActive(Settings.ShowDailyQuestOverlay);
    }

    private void OnAnyChanged() => RenderAll();

    private void RenderAll()
    {
        RenderXP();
        RenderFamiliar();
        RenderExpertise();
        RenderLegacy();
        RenderProfessions();
        RenderQuests();
    }

    // ─── Section builders ─────────────────────────────────────────────

    private GameObject BuildSectionRoot(string name)
    {
        // 0.14.0 friend-test v3: pass an explicit transparent bgColor so the
        // section sub-container doesn't add its own opaque Image on top of
        // the panel's transparency setting. Pre-fix, dragging Combined
        // overlay transparency to 100% only made the OUTER panel chrome
        // disappear — each section's Image stayed at Theme.PanelBackground
        // alpha (~0.8), creating an apparent "inner container that doesn't
        // respect transparency". Transparent here means panel chrome owns
        // the only visible background.
        var section = UIFactory.CreateVerticalGroup(ContentRoot, name,
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 1, padding: new Vector4(2, 2, 2, 2),
            bgColor: new Color(0f, 0f, 0f, 0f));
        UIFactory.SetLayoutElement(section,
            minWidth: 260, preferredWidth: 280, flexibleWidth: 1,
            minHeight: Theme.ScaledOverlayHeight(24), flexibleHeight: 0);
        return section;
    }

    private TextMeshProUGUI AddSectionHeader(GameObject section, string title, Color color)
    {
        var lbl = UIFactory.CreateLabel(section, $"{section.name}_Hdr", title,
            Theme.OverlayMidlineAlignment(), color: null, fontSize: Theme.ScaledOverlay(12));
        // 0.14.0 friend-test v5: scale LayoutElement heights with the overlay
        // text multiplier (via Theme.ScaledOverlayHeight) so at Large / X-Large
        // scale, the label's rendered text doesn't overflow the rect and
        // visually overlap the bar below it.
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 240, preferredWidth: 260, flexibleWidth: 1,
            minHeight: Theme.ScaledOverlayHeight(18), preferredHeight: Theme.ScaledOverlayHeight(20), flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Bold;
        lbl.TextMesh.color = color;
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        return lbl.TextMesh;
    }

    private TextMeshProUGUI AddSectionBody(GameObject section, string name, string initialText, int fontSize = 13, bool wrap = false)
    {
        var lbl = UIFactory.CreateLabel(section, name, initialText,
            Theme.OverlayMidlineAlignment(), color: null, fontSize: Theme.ScaledOverlay(fontSize));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 240, preferredWidth: 260, flexibleWidth: 1,
            minHeight: Theme.ScaledOverlayHeight(18), preferredHeight: Theme.ScaledOverlayHeight(20), flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = wrap;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        if (wrap)
        {
            // 0.14.0 friend-test v4: ContentSizeFitter grows the row height
            // so long stats-string content wraps inside the panel border
            // instead of overflowing it at Large / X-Large overlay text.
            var fitter = lbl.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        }
        return lbl.TextMesh;
    }

    private GameObject AddMiniBar(GameObject section, string name, Color fillColor, out RectTransform fill)
    {
        // Per-section progress bar. Hidden initially; Render() flips visibility
        // based on Settings.ShowProgressBars (shared with the standalone overlays).
        var bar = MiniBar.Create(section, name, out fill, fillColor: fillColor,
            height: Settings.ProgressBarHeight);
        bar.SetActive(false);
        return bar;
    }

    private void BuildXPSection()
    {
        _xpSection = BuildSectionRoot("XPSection");
        AddSectionHeader(_xpSection, "EXPERIENCE", COL_XP);
        _xpLine = AddSectionBody(_xpSection, "XPLine", "Lv —");
        _xpBar  = AddMiniBar(_xpSection, "XPBar", new Color(0.55f, 0.95f, 0.55f, 0.95f), out _xpBarFill);
        // 0.16.x: EXO prestige line. Hidden until exo data arrives AND the player
        // actually has exo prestige (keeps the glance view uncluttered for the
        // majority who haven't exo-prestiged). Mirrors the standalone XP overlay.
        _xpExoLine = AddSectionBody(_xpSection, "XPExoLine", "", fontSize: 12);
        _xpExoLine.gameObject.SetActive(false);
    }

    private void BuildFamiliarSection()
    {
        _familiarSection = BuildSectionRoot("FamiliarSection");
        AddSectionHeader(_familiarSection, "FAMILIAR", COL_FAMILIAR);
        _famNameLine  = AddSectionBody(_familiarSection, "FamName",  "(no familiar bound)");
        _famBar       = AddMiniBar(_familiarSection, "FamBar", new Color(1f, 0.6f, 0.2f, 0.95f), out _famBarFill);
        // 0.14.0 friend-test v4: wrap so long familiar names + stats don't
        // bleed past the panel border at Large / X-Large overlay text scale.
        _famStatsLine = AddSectionBody(_familiarSection, "FamStats", "—", fontSize: 12, wrap: true);
    }

    private void BuildExpertiseSection()
    {
        _expertiseSection = BuildSectionRoot("ExpertiseSection");
        AddSectionHeader(_expertiseSection, "WEAPON EXPERTISE", COL_EXPERTISE);
        _wepLine      = AddSectionBody(_expertiseSection, "WepLine",  "—");
        _wepBar       = AddMiniBar(_expertiseSection, "WepBar", new Color(0.75f, 0.75f, 0.85f, 0.95f), out _wepBarFill);
        // 0.14.0 friend-test v4: 3-stat picks like "PhysicalPower, SpellCritDamage,
        // MovementSpeed" overflow at Large text scale. Wrap + auto-fit the row.
        _wepStatsLine = AddSectionBody(_expertiseSection, "WepStats", "—", fontSize: 12, wrap: true);
        // 0.14.0 friend-test v6: optional bonus-stat values + XP counter sub-rows.
        // Hidden by default; toggled live by Settings.ShowOverlayBonusStats and
        // Settings.ShowOverlayXpCounter respectively.
        _wepBonusValuesLine = AddSectionBody(_expertiseSection, "WepBonusValues", "", fontSize: 11, wrap: true);
        _wepBonusValuesLine.fontStyle = FontStyles.Italic;
        _wepBonusValuesLine.gameObject.SetActive(false);
        _wepCounterLine = AddSectionBody(_expertiseSection, "WepCounter", "", fontSize: 11);
        _wepCounterLine.fontStyle = FontStyles.Italic;
        _wepCounterLine.gameObject.SetActive(false);
    }

    private void BuildLegacySection()
    {
        _legacySection = BuildSectionRoot("LegacySection");
        AddSectionHeader(_legacySection, "BLOOD LEGACY", COL_LEGACY);
        _blLine      = AddSectionBody(_legacySection, "BlLine",  "—");
        _blBar       = AddMiniBar(_legacySection, "BlBar", new Color(0.85f, 0.30f, 0.30f, 0.95f), out _blBarFill);
        _blStatsLine = AddSectionBody(_legacySection, "BlStats", "—", fontSize: 12, wrap: true);
        _blBonusValuesLine = AddSectionBody(_legacySection, "BlBonusValues", "", fontSize: 11, wrap: true);
        _blBonusValuesLine.fontStyle = FontStyles.Italic;
        _blBonusValuesLine.gameObject.SetActive(false);
        _blCounterLine = AddSectionBody(_legacySection, "BlCounter", "", fontSize: 11);
        _blCounterLine.fontStyle = FontStyles.Italic;
        _blCounterLine.gameObject.SetActive(false);
    }

    private void BuildProfessionSection()
    {
        _profSection = BuildSectionRoot("ProfSection");
        AddSectionHeader(_profSection, "PROFESSIONS", COL_PROFS);

        // Wrap-mode (default): single word-wrapped label "Enchanting 60  Alchemy 45 ..."
        _profWrapMode = UIFactory.CreateVerticalGroup(_profSection, "ProfWrapMode",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 0, padding: new Vector4(0, 0, 0, 0),
            bgColor: new Color(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_profWrapMode,
            minWidth: 240, preferredWidth: 320, flexibleWidth: 1,
            minHeight: Theme.ScaledOverlayHeight(18), flexibleHeight: 0);
        var lbl = UIFactory.CreateLabel(_profWrapMode, "ProfLine", "—",
            Theme.OverlayMidlineAlignment(), color: null, fontSize: Theme.ScaledOverlay(12));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 240, preferredWidth: 320, flexibleWidth: 1,
            minHeight: Theme.ScaledOverlayHeight(18), flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = true;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        var fitter = lbl.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        _profLine = lbl.TextMesh;

        // 0.14.0 friend-test v3: row-mode — 8 (label + bar) pairs, mirrors
        // the standalone Profession overlay. Used when ShowProgressBarProfessions
        // is on. Each row is gated by the existing per-profession ShowProfession*
        // flags (v0.13.0) — disabled professions hide both their label and
        // their bar.
        _profRowMode = UIFactory.CreateVerticalGroup(_profSection, "ProfRowMode",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 1, padding: new Vector4(0, 0, 0, 0),
            bgColor: new Color(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_profRowMode,
            minWidth: 240, preferredWidth: 320, flexibleWidth: 1,
            minHeight: Theme.ScaledOverlayHeight(18), flexibleHeight: 0);

        Color profBarColor = new(0.95f, 0.75f, 0.35f, 0.95f); // warm amber, matches the standalone Prof overlay
        _profRowEnchanting    = AddProfRow(_profRowMode, "ProfRow_Enchanting",    "Enchanting —");
        _profBarEnchanting    = AddMiniBar(_profRowMode, "ProfBar_Enchanting",    profBarColor, out _profBarFillEnchanting);
        _profRowAlchemy       = AddProfRow(_profRowMode, "ProfRow_Alchemy",       "Alchemy —");
        _profBarAlchemy       = AddMiniBar(_profRowMode, "ProfBar_Alchemy",       profBarColor, out _profBarFillAlchemy);
        _profRowHarvesting    = AddProfRow(_profRowMode, "ProfRow_Harvesting",    "Harvesting —");
        _profBarHarvesting    = AddMiniBar(_profRowMode, "ProfBar_Harvesting",    profBarColor, out _profBarFillHarvesting);
        _profRowBlacksmithing = AddProfRow(_profRowMode, "ProfRow_Blacksmithing", "Blacksmithing —");
        _profBarBlacksmithing = AddMiniBar(_profRowMode, "ProfBar_Blacksmithing", profBarColor, out _profBarFillBlacksmithing);
        _profRowTailoring     = AddProfRow(_profRowMode, "ProfRow_Tailoring",     "Tailoring —");
        _profBarTailoring     = AddMiniBar(_profRowMode, "ProfBar_Tailoring",     profBarColor, out _profBarFillTailoring);
        _profRowWoodcutting   = AddProfRow(_profRowMode, "ProfRow_Woodcutting",   "Woodcutting —");
        _profBarWoodcutting   = AddMiniBar(_profRowMode, "ProfBar_Woodcutting",   profBarColor, out _profBarFillWoodcutting);
        _profRowMining        = AddProfRow(_profRowMode, "ProfRow_Mining",        "Mining —");
        _profBarMining        = AddMiniBar(_profRowMode, "ProfBar_Mining",        profBarColor, out _profBarFillMining);
        _profRowFishing       = AddProfRow(_profRowMode, "ProfRow_Fishing",       "Fishing —");
        _profBarFishing       = AddMiniBar(_profRowMode, "ProfBar_Fishing",       profBarColor, out _profBarFillFishing);
    }

    private TextMeshProUGUI AddProfRow(GameObject parent, string name, string initial)
    {
        var lbl = UIFactory.CreateLabel(parent, name, initial,
            Theme.OverlayMidlineAlignment(), color: null, fontSize: Theme.ScaledOverlay(12));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 240, preferredWidth: 260, flexibleWidth: 1,
            minHeight: Theme.ScaledOverlayHeight(18), preferredHeight: Theme.ScaledOverlayHeight(20), flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        return lbl.TextMesh;
    }

    private void BuildQuestSection()
    {
        _questSection = BuildSectionRoot("QuestSection");
        AddSectionHeader(_questSection, "DAILY / WEEKLY QUEST", COL_QUEST_D);
        _dailyLine  = AddSectionBody(_questSection, "DailyLine",  "Daily: —");
        _weeklyLine = AddSectionBody(_questSection, "WeeklyLine", "Weekly: —");
        _weeklyLine.color = COL_QUEST_W;
    }

    // ─── Renderers ────────────────────────────────────────────────────

    private static void SyncBar(GameObject bar, RectTransform fill, float progress, bool show)
    {
        if (bar == null) return;
        if (bar.activeSelf != show) bar.SetActive(show);
        if (show && fill != null) MiniBar.SetProgress(fill, progress);
    }

    private void RenderXP()
    {
        if (_xpLine == null) return;
        // 0.15.2: same disabled-detection pattern Quest got in v0.15.1.
        // When the cross-system corroboration confirms Leveling is off
        // server-side, show a clear hint instead of misleading "Lv 0 (0%)"
        // (which looks identical to "brand-new level-1 character").
        if (PlayerStateService.IsSystemReliablyDisabled(PlayerStateService.SystemKind.Leveling))
        {
            _xpLine.text = "(Leveling disabled on this server)";
            SyncBar(_xpBar, _xpBarFill, 0f, false);
            if (_xpExoLine != null && _xpExoLine.gameObject.activeSelf) _xpExoLine.gameObject.SetActive(false);
            return;
        }
        var s = PlayerStateService.Experience;
        string cls = s.Class == PlayerStateService.PlayerClass.None ? "(no class)" : s.Class.ToString();
        string prestige = s.Prestige > 0 ? $"  P{s.Prestige}" : "";
        _xpLine.text = $"Lv {s.Level} ({s.Progress * 100f:0.#}%){prestige}   {cls}";
        SyncBar(_xpBar, _xpBarFill, s.Progress, Settings.ShowProgressBarXP);
        RenderExoLine();
    }

    // 0.16.x: EXO prestige isn't in the structured protocol — fill from the parsed
    // ".prestige get Exo" reply (PrestigeInfoLatest). Only adopt it when it's the
    // Exo type; shown only when the player actually has exo prestige (>0).
    private void RenderExoLine()
    {
        if (_xpExoLine == null) return;
        var p = PlayerStateService.PrestigeInfoLatest;
        if (p.TypeName != null && string.Equals(p.TypeName, "Exo", System.StringComparison.OrdinalIgnoreCase))
        {
            _xpExoLevel = p.Level; _xpExoMaxLevel = p.MaxLevel; _xpExoReceived = true;
        }
        bool show = _xpExoReceived && _xpExoLevel > 0;
        if (_xpExoLine.gameObject.activeSelf != show) _xpExoLine.gameObject.SetActive(show);
        if (show)
            _xpExoLine.text = _xpExoMaxLevel > 0
                ? $"EXO Prestige: {_xpExoLevel} / {_xpExoMaxLevel}"
                : $"EXO Prestige: {_xpExoLevel}";
    }

    // 0.16.x: one-shot ".prestige get Exo" fetch, deferred until MessageService
    // is bound. Mirrors ExperienceOverlayPanel.ScheduleExoFetch so the EXO line
    // fills even when ONLY the combined overlay is in use.
    private void ScheduleExoFetch()
    {
        if (_exoFetchScheduled) return;
        _exoFetchScheduled = true;
        System.Action ticker = null;
        ticker = () =>
        {
            if (!BloodCraftHub.Services.MessageService.IsInitialized) return;
            BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Remove(ticker);
            try { BloodCraftHub.Services.MessageService.EnqueueMessage(".prestige get Exo"); }
            catch (System.Exception ex) { BloodCraftHub.Utils.LogUtils.LogWarning($"CombinedOverlay: auto .prestige get Exo failed — {ex.Message}"); }
        };
        BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Add(ticker);
    }

    private void RenderFamiliar()
    {
        if (_famNameLine == null) return;
        // 0.15.2: distinguish "Familiar system disabled" from "user just
        // hasn't bound a familiar yet". Both look like HasActive=false in
        // the broadcast data, but the disabled case is reliably detected
        // when other systems are reporting data and Familiar never has.
        if (PlayerStateService.IsSystemReliablyDisabled(PlayerStateService.SystemKind.Familiar))
        {
            _famNameLine.text  = "(Familiars disabled on this server)";
            _famStatsLine.text = "—";
            SyncBar(_famBar, _famBarFill, 0f, false);
            return;
        }
        var s = PlayerStateService.Familiar;
        if (!s.HasActive)
        {
            _famNameLine.text  = "(no familiar bound)";
            _famStatsLine.text = "—";
            SyncBar(_famBar, _famBarFill, 0f, false);
            return;
        }
        string prestige = s.Prestige > 0 ? $"  P{s.Prestige}" : "";
        _famNameLine.text  = $"{s.Name}  Lv {s.Level} ({s.Progress * 100f:0.#}%){prestige}";
        _famStatsLine.text = $"HP {s.MaxHealth}   PP {s.PhysicalPower}   SP {s.SpellPower}";
        SyncBar(_famBar, _famBarFill, s.Progress, Settings.ShowProgressBarFamiliar);
    }

    private void RenderExpertise()
    {
        if (_wepLine == null) return;
        // 0.15.2: when Weapon Expertise is disabled server-side, the
        // protocol reports all-zero (Type=Sword default, Level=0, etc.).
        // Pre-0.15.2 this rendered as "Sword Lv 0" which is actively
        // misleading — friend-test: "I'm actually unarmed but it doesn't
        // register because unarmed is part of weapon expertise which is
        // off." Surface the disabled state instead.
        if (PlayerStateService.IsSystemReliablyDisabled(PlayerStateService.SystemKind.Expertise))
        {
            _wepLine.text      = "(Weapon Expertise disabled on this server)";
            _wepStatsLine.text = "—";
            SyncBar(_wepBar, _wepBarFill, 0f, false);
            if (_wepBonusValuesLine != null) _wepBonusValuesLine.gameObject.SetActive(false);
            if (_wepCounterLine     != null) _wepCounterLine.gameObject.SetActive(false);
            return;
        }
        var s = PlayerStateService.Expertise;
        string prestige = s.Prestige > 0 ? $"  P{s.Prestige}" : "";
        _wepLine.text = $"{s.Type}   Lv {s.Level} ({s.Progress * 100f:0.#}%){prestige}";
        // 0.14.0 friend-test: BonusStatsRaw is the packed 2-chars-per-stat
        // wire format Bloodcraft sends over the Eclipse protocol — it's not
        // human-readable as plain text. Decode to enum names using the same
        // helper the standalone Wep tab uses (MainPanel:3286).
        var stats = PlayerStateService.DecodeWeaponBonusStats(s.BonusStatsRaw);
        _wepStatsLine.text = (stats == null || stats.Count == 0)
            ? "Stats: (none chosen)"
            : $"Stats: {string.Join(", ", stats)}";
        SyncBar(_wepBar, _wepBarFill, s.Progress, Settings.ShowProgressBarExpertise);

        // 0.14.0 friend-test v6: Bonus stat values sub-row (Settings.ShowOverlayBonusStats).
        // Same content as the standalone XP overlay's RenderWeaponStats produces.
        RenderWepBonusValuesSubRow();
        // 0.14.0 friend-test v6: XP counter sub-row (Settings.ShowOverlayXpCounter).
        RenderWepCounterSubRow();
    }

    private void RenderLegacy()
    {
        if (_blLine == null) return;
        // 0.15.2: same disabled-detection as Expertise above. When Blood
        // Legacy is off server-side, Type stays at default (Worker) with
        // Level=0 which looks like a brand-new character's actual data.
        if (PlayerStateService.IsSystemReliablyDisabled(PlayerStateService.SystemKind.Legacy))
        {
            _blLine.text      = "(Blood Legacy disabled on this server)";
            _blStatsLine.text = "—";
            SyncBar(_blBar, _blBarFill, 0f, false);
            if (_blBonusValuesLine != null) _blBonusValuesLine.gameObject.SetActive(false);
            if (_blCounterLine     != null) _blCounterLine.gameObject.SetActive(false);
            return;
        }
        var s = PlayerStateService.Legacy;
        string prestige = s.Prestige > 0 ? $"  P{s.Prestige}" : "";
        _blLine.text = $"{s.Type}   Lv {s.Level} ({s.Progress * 100f:0.#}%){prestige}";
        // 0.14.0 friend-test: decode same as Expertise above.
        var stats = PlayerStateService.DecodeBloodBonusStats(s.BonusStatsRaw);
        _blStatsLine.text = (stats == null || stats.Count == 0)
            ? "Stats: (none chosen)"
            : $"Stats: {string.Join(", ", stats)}";
        SyncBar(_blBar, _blBarFill, s.Progress, Settings.ShowProgressBarLegacy);

        RenderBlBonusValuesSubRow();
        RenderBlCounterSubRow();
    }

    // 0.14.0 friend-test v6: Bonus stat values + XP counter sub-rows. Mirrors
    // ExperienceOverlayPanel.RenderWeaponStats / RenderWeaponCounter so the
    // combined overlay surfaces the same data the standalone XP overlay does
    // when Settings.ShowOverlayBonusStats / ShowOverlayXpCounter are on. Data
    // flows: ExperienceOverlay's BonusStatsTick fires .wep get + .bl get on
    // a 10s cadence; replies cache into ExperienceOverlay (for wep) and
    // PlayerStateService.BloodInfoLatest (for bl). We just read.
    private void RenderWepBonusValuesSubRow()
    {
        if (_wepBonusValuesLine == null) return;
        bool show = Settings.ShowOverlayBonusStats;
        var xp = Plugin.UIManager?.ExperienceOverlay;
        var lines = show ? xp?.BuildCleanedWepGetStatsLines() : null;
        bool hasData = lines != null && lines.Count > 0;
        if (_wepBonusValuesLine.gameObject.activeSelf != (show && hasData))
            _wepBonusValuesLine.gameObject.SetActive(show && hasData);
        if (show && hasData) _wepBonusValuesLine.text = string.Join("\n", lines);
    }

    private void RenderWepCounterSubRow()
    {
        if (_wepCounterLine == null) return;
        bool show = Settings.ShowOverlayXpCounter;
        var xp = Plugin.UIManager?.ExperienceOverlay;
        bool hasData = show && (xp?.WepGetHasData ?? false);
        if (_wepCounterLine.gameObject.activeSelf != hasData)
            _wepCounterLine.gameObject.SetActive(hasData);
        if (hasData)
        {
            int raw = xp.WepGetRawExpertise;
            float pct = xp.WepGetProgressPct;
            int total = (pct > 0.01f) ? UnityEngine.Mathf.RoundToInt(raw * 100f / pct) : 0;
            _wepCounterLine.text = total > 0
                ? $"Exp: {raw} / {total} ({pct:0.0}%)"
                : $"Exp: {raw} ({pct:0.0}%)";
        }
    }

    private void RenderBlBonusValuesSubRow()
    {
        if (_blBonusValuesLine == null) return;
        bool show = Settings.ShowOverlayBonusStats;
        var info = PlayerStateService.BloodInfoLatest;
        bool hasData = info.StatLines != null && info.StatLines.Count > 0;
        if (_blBonusValuesLine.gameObject.activeSelf != (show && hasData))
            _blBonusValuesLine.gameObject.SetActive(show && hasData);
        if (show && hasData) _blBonusValuesLine.text = string.Join("\n", info.StatLines);
    }

    private void RenderBlCounterSubRow()
    {
        if (_blCounterLine == null) return;
        bool show = Settings.ShowOverlayXpCounter;
        var info = PlayerStateService.BloodInfoLatest;
        bool hasData = show
                    && !string.IsNullOrEmpty(info.Essence)
                    && !string.IsNullOrEmpty(info.ProgressPct);
        if (_blCounterLine.gameObject.activeSelf != hasData)
            _blCounterLine.gameObject.SetActive(hasData);
        if (hasData)
        {
            if (float.TryParse(info.ProgressPct, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct)
                && int.TryParse(info.Essence, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var raw))
            {
                int total = (pct > 0.01f) ? UnityEngine.Mathf.RoundToInt(raw * 100f / pct) : 0;
                _blCounterLine.text = total > 0
                    ? $"Ess: {raw} / {total} ({pct:0.0}%)"
                    : $"Ess: {raw} ({pct:0.0}%)";
            }
            else
            {
                _blCounterLine.text = $"Ess: {info.Essence} ({info.ProgressPct}%)";
            }
        }
    }

    private void RenderProfessions()
    {
        if (_profLine == null) return;
        // 0.15.2: when Professions are disabled server-side, swap the
        // wrap-text / per-row layouts for a single "disabled" line. Both
        // sub-modes are hidden + the single _profLine carries the message.
        if (PlayerStateService.IsSystemReliablyDisabled(PlayerStateService.SystemKind.Profession))
        {
            if (_profWrapMode != null && _profWrapMode.activeSelf) _profWrapMode.SetActive(false);
            if (_profRowMode  != null && _profRowMode.activeSelf)  _profRowMode.SetActive(false);
            // Show the disabled message via the wrap-mode label (it's the
            // single-line surface that always exists). Re-activate just
            // _profLine inside the wrap container.
            if (_profWrapMode != null) _profWrapMode.SetActive(true);
            _profLine.text = "(Professions disabled on this server)";
            return;
        }
        var s = PlayerStateService.Profession;

        // 0.14.0 friend-test v3: render mode driven by the bar flag.
        // Bars-on   → per-profession row mode (label + bar pairs).
        // Bars-off  → compact wrapped-text mode.
        bool bars = Settings.ShowProgressBarProfessions;
        if (_profWrapMode != null && _profWrapMode.activeSelf != !bars) _profWrapMode.SetActive(!bars);
        if (_profRowMode  != null && _profRowMode.activeSelf  != bars)  _profRowMode.SetActive(bars);

        if (!bars)
        {
            // Wrap-text mode — keep the compact view the user liked.
            var visible = new System.Collections.Generic.List<string>(8);
            if (Settings.ShowProfessionEnchanting)    visible.Add($"Enchanting {s.EnchantingLevel}");
            if (Settings.ShowProfessionAlchemy)       visible.Add($"Alchemy {s.AlchemyLevel}");
            if (Settings.ShowProfessionHarvesting)    visible.Add($"Harvesting {s.HarvestingLevel}");
            if (Settings.ShowProfessionBlacksmithing) visible.Add($"Blacksmithing {s.BlacksmithingLevel}");
            if (Settings.ShowProfessionTailoring)     visible.Add($"Tailoring {s.TailoringLevel}");
            if (Settings.ShowProfessionWoodcutting)   visible.Add($"Woodcutting {s.WoodcuttingLevel}");
            if (Settings.ShowProfessionMining)        visible.Add($"Mining {s.MiningLevel}");
            if (Settings.ShowProfessionFishing)       visible.Add($"Fishing {s.FishingLevel}");
            _profLine.text = visible.Count == 0
                ? "(all professions hidden in Settings)"
                : string.Join("    ", visible);
            return;
        }

        // Row mode — per-profession label + bar pairs. Each pair gated by
        // the per-profession ShowProfession* flag from v0.13.0.
        RenderProfRow(_profRowEnchanting,    _profBarEnchanting,    _profBarFillEnchanting,    Settings.ShowProfessionEnchanting,    "Enchanting",    s.EnchantingLevel,    s.EnchantingProgress);
        RenderProfRow(_profRowAlchemy,       _profBarAlchemy,       _profBarFillAlchemy,       Settings.ShowProfessionAlchemy,       "Alchemy",       s.AlchemyLevel,       s.AlchemyProgress);
        RenderProfRow(_profRowHarvesting,    _profBarHarvesting,    _profBarFillHarvesting,    Settings.ShowProfessionHarvesting,    "Harvesting",    s.HarvestingLevel,    s.HarvestingProgress);
        RenderProfRow(_profRowBlacksmithing, _profBarBlacksmithing, _profBarFillBlacksmithing, Settings.ShowProfessionBlacksmithing, "Blacksmithing", s.BlacksmithingLevel, s.BlacksmithingProgress);
        RenderProfRow(_profRowTailoring,     _profBarTailoring,     _profBarFillTailoring,     Settings.ShowProfessionTailoring,     "Tailoring",     s.TailoringLevel,     s.TailoringProgress);
        RenderProfRow(_profRowWoodcutting,   _profBarWoodcutting,   _profBarFillWoodcutting,   Settings.ShowProfessionWoodcutting,   "Woodcutting",   s.WoodcuttingLevel,   s.WoodcuttingProgress);
        RenderProfRow(_profRowMining,        _profBarMining,        _profBarFillMining,        Settings.ShowProfessionMining,        "Mining",        s.MiningLevel,        s.MiningProgress);
        RenderProfRow(_profRowFishing,       _profBarFishing,       _profBarFillFishing,       Settings.ShowProfessionFishing,       "Fishing",       s.FishingLevel,       s.FishingProgress);
    }

    private static void RenderProfRow(TextMeshProUGUI label, GameObject bar, RectTransform fill,
        bool show, string name, int level, float progress)
    {
        if (label != null)
        {
            if (label.gameObject.activeSelf != show) label.gameObject.SetActive(show);
            if (show) label.text = $"{name}: Lv {level} ({progress * 100f:0.#}%)";
        }
        SyncBar(bar, fill, progress, show);
    }

    private void RenderQuests()
    {
        if (_dailyLine == null) return;
        var daily  = PlayerStateService.DailyQuest;
        var weekly = PlayerStateService.WeeklyQuest;
        // 0.15.1: when Quest is reliably detected as disabled server-side,
        // surface that on the combined overlay's QUEST section too rather
        // than rendering an indefinite "—" placeholder.
        bool questDisabled = PlayerStateService.IsSystemReliablyDisabled(PlayerStateService.SystemKind.Quest);
        _dailyLine.text  = questDisabled ? "Daily: (disabled on this server)"  : FormatQuest("Daily",  daily);
        _weeklyLine.text = questDisabled ? "Weekly: (disabled on this server)" : FormatQuest("Weekly", weekly);
    }

    private static string FormatQuest(string label, PlayerStateService.QuestState q)
    {
        if (string.IsNullOrEmpty(q.TargetName))
            return $"{label}: —";
        if (q.Goal > 0 && q.Progress >= q.Goal)
            return $"{label}: {q.TargetName} — Complete!";
        return $"{label}: {q.TargetName} ({q.Progress}/{q.Goal})";
    }

    internal override void Reset()
    {
        if (_subscribed)
        {
            PlayerStateService.ExperienceChanged -= OnAnyChanged;
            PlayerStateService.FamiliarChanged   -= OnAnyChanged;
            PlayerStateService.ExpertiseChanged  -= OnAnyChanged;
            PlayerStateService.LegacyChanged     -= OnAnyChanged;
            PlayerStateService.ProfessionChanged -= OnAnyChanged;
            PlayerStateService.QuestChanged      -= OnAnyChanged;
            PlayerStateService.LastResponseChanged -= OnAnyChanged;
            PlayerStateService.BloodInfoChanged    -= OnAnyChanged;
            PlayerStateService.PrestigeInfoChanged -= OnAnyChanged;
            _subscribed = false;
        }
    }
}
