using System;
using System.Collections.Generic;
using BloodCraftHub.Config;
using BloodCraftHub.Resources;
using BloodCraftHub.Services;
using BloodCraftHub.UI.Forms;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Models;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using BloodCraftHub.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;
using BloodCraftHub.UI.Framework.CustomLib.Util;

namespace BloodCraftHub.UI.ModContent;

// The primary tabbed UI. See the matching ASCII diagram in docs/MOD_DESIGN.md
// for the layout. Each tab's body is built by a dedicated BuildXxxTab method
// dispatched in BuildContentArea; tabs that need live data subscribe to
// PlayerStateService events and unsubscribe in Reset.
public partial class MainPanel : ResizeablePanelBase
{
    public override string PanelId => "MainPanel";
    public override PanelType PanelType => PanelType.Base;

    public override int MinWidth  => 600;
    public override int MinHeight => 380;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    public override Vector2 DefaultPosition  => Vector2.zero;

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.UITransparency;
    public override bool ResizeWholePanel => false;
    // 0.12.0: opt the main panel into both color pickers. Construct-time
    // application happens in PanelBase.ConstructUI; live picks flow in via
    // BCHubUIManager.RefreshAllPanelBackgrounds (outer) and
    // RefreshScopedInnerBackgrounds (inner — main + familiar browser only).
    public override bool UsesCustomBackgroundColor      => true;
    public override bool UsesCustomInnerBackgroundColor => true;
    // 0.10.14: the "Lock overlays" toggle pins the five overlays; the
    // main panel is NOT an overlay and must stay drag/resize-enabled.
    protected override bool RespectsLockOverlays => false;

    public PanelType ActiveTab { get; private set; } = PanelType.FamiliarsTab;

    private readonly Dictionary<PanelType, GameObject> _tabContent = new();
    // Inner-content GameObject *inside* the ScrollView wrapper for each tab.
    // AutoResize walks this to compute the actual children-sum height; _tabContent
    // points at the scroll wrapper whose own height tracks the viewport, not
    // its overflow.
    private readonly Dictionary<PanelType, GameObject> _tabInnerContent = new();
    private readonly Dictionary<PanelType, ButtonRef> _tabButtons = new();
    private Toggle _xpOverlayToggle;
    private Toggle _famOverlayToggle;
    private Toggle _famBrowserToggle;
    private Toggle _dqOverlayToggle;
    private Toggle _profOverlayToggle;
    private Toggle _shiftOverlayToggle;

    // Familiars-tab live labels
    private TextMeshProUGUI _famNameLabel;
    private TextMeshProUGUI _famProgressLabel;
    private TextMeshProUGUI _famStatsLabel;
    private bool _famSubscribed;
    // 0.10.11: in-panel search-result display. The .fam s reply was
    // previously visible only in chat — invisible when the user had BCH
    // chat-suppression on. These fields hold the result-panel labels +
    // dynamic list container that subscribes to FamSearchCompleted.
    private TextMeshProUGUI _famSearchResultHeader;
    private GameObject      _famSearchResultList;
    private bool            _famSearchSubscribed;

    // Class-tab live labels
    private TextMeshProUGUI _classNameLabel;
    private TextMeshProUGUI _classLevelLabel;
    private bool _classSubscribed;

    // Expertise-tab live labels
    private TextMeshProUGUI _wepTypeLabel;
    private TextMeshProUGUI _wepProgressLabel;
    private TextMeshProUGUI _wepBonusLabel;
    // 0.9.6: stat-values line populated from the most recent .wep get reply
    // (cached via LastResponseChanged). Renders below the existing bonus-name
    // line so the header shows both "which stats are chosen" and "what their
    // current values are at this level".
    private TextMeshProUGUI _wepStatsValuesLabel;
    private bool _wepSubscribed;
    private bool _wepLastResponseSubscribed;
    private System.Collections.Generic.List<string> _cachedWepGetLines;

    // 0.10.0: V-Bloods tab — collection tracker fed by VBloodScannerService.
    // Each registry entry gets one row built once; refresh re-binds labels in
    // place rather than tearing down + rebuilding, so the list stays scrolled
    // and responsive while the scanner streams in results across ~4 minutes.
    // 0.10.9: chip view rebuilt around the box-sweep scanner's per-variant
    // VBloodInstance records. One row per captured variant (basic / shiny /
    // primal / primal-shiny) — the user picks which variant to summon
    // explicitly. "Missing" filter renders un-captured V-Blood names as
    // their own placeholder rows. The 0.10.7 dual-view toggle (Chips /
    // Instances) is retired because the new single view does both jobs.
    private TextMeshProUGUI _vbProgressLabel;          // "23 / 65 captured + 4 primals"
    private ButtonRef       _vbScanButton;             // "Scan all" / "Cancel" toggle
    private TextMeshProUGUI _vbScanStatusLabel;        // "Scanning… box 3 / 14: BoxName"
    private ButtonRef       _vbSortButton;             // 0.10.1: cycle button, shows current sort mode
    private GameObject      _vbRowContainer;           // VerticalLayoutGroup holding all variant + missing rows
    private bool            _vbSubscribed;
    private enum VBloodFilter { All, Captured, Missing, ShinyOnly }
    private VBloodFilter    _vbFilter = VBloodFilter.All;

    // Blood-Legacy-tab live labels
    private TextMeshProUGUI _blTypeLabel;
    private TextMeshProUGUI _blProgressLabel;
    private TextMeshProUGUI _blBonusLabel;
    // 0.9.6: stat-values line populated from BloodInfoLatest. Equivalent
    // to _wepStatsValuesLabel above.
    private TextMeshProUGUI _blStatsValuesLabel;
    private bool _blSubscribed;

    // In-UI Blood Info display (parsed from `.bl get [Type]` reply).
    private TextMeshProUGUI _blInfoTitleLabel;
    private TextMeshProUGUI _blInfoLevelLabel;
    private TextMeshProUGUI _blInfoStatsLabel;
    private bool _blInfoSubscribed;

    // Unarmed+Shift-tab live labels
    private TextMeshProUGUI _shiftSpellLabel;
    private TextMeshProUGUI _unarmedStatusLabel;
    private TextMeshProUGUI _unarmedBonusLabel;
    private bool _shiftSubscribed;

    // Prestige-tab live labels (current-prestige across systems)
    private TextMeshProUGUI _prestigeXpLabel;
    private TextMeshProUGUI _prestigeLegacyLabel;
    private TextMeshProUGUI _prestigeExpertiseLabel;
    private TextMeshProUGUI _prestigeFamLabel;
    private bool _prestigeSubscribed;

    // In-UI Prestige info display (parsed from `.prestige get` reply).
    private GameObject       _prestigeInfoSection;
    private TextMeshProUGUI  _prestigeInfoTitleLabel;
    private TextMeshProUGUI  _prestigeInfoLevelLabel;
    private TextMeshProUGUI  _prestigeInfoEffectsLabel;
    private GameObject       _prestigeBar;        // 0.9.2: optional progress bar
    private RectTransform    _prestigeBarFill;
    private bool _prestigeInfoSubscribed;

    // Levels-tab live labels (full overview)
    private TextMeshProUGUI _lvlXpLabel;
    private TextMeshProUGUI _lvlLegacyLabel;
    private TextMeshProUGUI _lvlExpertiseLabel;
    private TextMeshProUGUI _lvlExpertiseBonusLabel;
    private TextMeshProUGUI _lvlFamLabel;
    private TextMeshProUGUI _lvlFamStatsLabel;
    private TextMeshProUGUI _lvlProfessions1Label;
    private TextMeshProUGUI _lvlProfessions2Label;
    private TextMeshProUGUI _lvlProfessions3Label;
    private TextMeshProUGUI _lvlProfessions4Label;
    private bool _lvlSubscribed;

    // Kindred Commands tab - stateful pager for .clan list. Page is 1-based;
    // server defaults to page 1 when no arg is given. The label TMP is updated
    // each time the user clicks Prev/Next so the row reflects the page number
    // currently being requested.
    private int _clanListPage = 1;
    private TextMeshProUGUI _clanListPageLabel;

    // Boxes-tab live state
    private TextMeshProUGUI _boxesActiveBoxLabel;
    private TextMeshProUGUI _boxesContentHeading;
    private TextMeshProUGUI _boxesStatusLabel;
    private LabelRef        _boxesSwapWarning;     // hidden by default; warns of pending destructive swap
    private GameObject _boxesPickerSection;       // parent wrapping picker heading + list
    private GameObject _boxesContentSection;      // parent wrapping content heading + list
    private GameObject _boxesListContainer;       // box-name buttons go here
    private GameObject _boxesContentContainer;    // familiar-name buttons go here
    private bool _boxesShowingContents;
    private bool _boxesSubscribed;

    // Two-click destruction-confirm state for switching active familiar.
    // Bloodcraft's `.fam b` errors with "You already have an active familiar!
    // Unbind that one first." when called with one bound, and `.fam t` (toggle/
    // dismiss) does NOT free the bind slot - HasActiveFamiliar() only returns
    // false once the entity is destroyed. So switching requires `.fam ub`
    // (DESTROY current) → `.fam b N`. We never fire that silently: first click
    // sets the pending-swap state and shows a warning banner; a second click
    // on the SAME familiar within SWAP_CONFIRM_WINDOW seconds executes it.
    private int   _pendingSwapIndex = -1;
    private float _pendingSwapDeadline = -1f;
    private const float SWAP_CONFIRM_WINDOW_SECONDS = 5f;

    // Per-row edit mode for the box contents view. When ON, each familiar row
    // gets a destructive Delete button next to the bind button. Two-click
    // confirm on the Delete; first click changes its label to "Confirm?".
    private bool _boxesEditMode = false;
    private int  _pendingDeleteIndex = -1;
    private float _pendingDeleteDeadline = -1f;
    private const float DELETE_CONFIRM_WINDOW_SECONDS = 3f;
    private Toggle _boxesEditModeToggle;

    // -------- Tab grouping (Phase 5c) ------------------------------------
    // The left rail is split into 3 collapsible groups. Each group has a
    // header button and a list of sub-tabs. KINDRED/HELP start empty (their
    // tabs land in Phases 5d-5i).
    private sealed class TabGroupDef
    {
        public string Title;
        public bool   StartExpanded;
        public (PanelType Tab, string Label)[] Tabs;
    }

    private static readonly TabGroupDef[] TabGroups = new[]
    {
        new TabGroupDef
        {
            Title = "Bloodcraft",
            StartExpanded = true,
            Tabs = new[]
            {
                (PanelType.FamiliarsTab,    "Familiars"),
                (PanelType.BoxesTab,        "Boxes"),
                (PanelType.VBloodsTab,      "V-Bloods"),
                (PanelType.AllFamiliarsTab, "All Familiars"),
                (PanelType.ClassTab,        "Class"),
                (PanelType.ExpertiseTab,    "Weapon Expertise"),
                (PanelType.BloodLegacyTab,  "Blood Legacy"),
                (PanelType.UnarmedShiftTab, "Unarmed + Shift"),
                (PanelType.PrestigeTab,     "Prestige"),
                (PanelType.LevelsTab,       "Levels"),
                (PanelType.DailyQuestTab,   "Daily Quests"),
                (PanelType.AdminTab,        "Admin"),
            },
        },
        new TabGroupDef
        {
            Title = "Kindred",
            StartExpanded = false,
            Tabs = new[]
            {
                (PanelType.KindredLogisticsTab,      "Logistics"),
                (PanelType.KindredLogisticsAdminTab, "Logistics: Admin"),
                (PanelType.KindredCommandsPlayerTab, "Commands"),
                (PanelType.KindredAdminPlayersTab,   "Admin: Players"),
                (PanelType.KindredAdminServerTab,    "Admin: Server"),
                (PanelType.KindredAdminWorldTab,     "Admin: World"),
            },
        },
        new TabGroupDef
        {
            // 0.9.8: was "Help"; renamed because friend-testing surfaced that
            // users didn't notice there was a Settings page under what looked
            // like a documentation-only group. The Settings tab is the more
            // actionable child here — putting it in the group name makes the
            // group worth expanding.
            Title = "Settings and Help",
            StartExpanded = false,
            Tabs = new[]
            {
                (PanelType.QuickStartTab,    "Quick Start"),
                // 0.12.1: V Rising game guide + community-resource links.
                // Placed right after Quick Start so a new player reads
                // "what this mod does" → "what this game is" in natural
                // order before settings/admin tabs.
                (PanelType.GameGuideTab,     "Game Guide"),
                (PanelType.SettingsTab,      "Settings"),
                (PanelType.VanillaAdminTab,  "Vanilla Admin"),
                (PanelType.AboutTab,         "About"),
            },
        },
    };

    // Flattened view: every (Tab, Label) across all groups. Useful when other
    // code (BuildContentArea, etc.) needs to iterate all tabs without caring
    // which group they belong to.
    private static System.Collections.Generic.IEnumerable<(PanelType Tab, string Label)> AllTabs()
    {
        foreach (var g in TabGroups)
            foreach (var t in g.Tabs)
                yield return t;
    }

    private readonly System.Collections.Generic.Dictionary<string, bool>             _groupExpanded   = new();
    private readonly System.Collections.Generic.Dictionary<string, GameObject>       _groupContent    = new();
    private readonly System.Collections.Generic.Dictionary<string, TextMeshProUGUI>  _groupHeaderText = new();
    // 0.12.1: keep header ButtonRef around so the Bloodcraft handshake retry
    // (in EclipseProtocolService) can flip the group from tentative-available
    // → confirmed-available (or → unavailable on give-up) in place when the
    // AvailabilityChanged event fires.
    private readonly System.Collections.Generic.Dictionary<string, BloodCraftHub.UI.Framework.UniverseLib.UI.Models.ButtonRef> _groupHeaderButton = new();
    private bool _availabilitySubscribed;
    private GameObject _tabStripGo;

    public MainPanel(UIBase owner) : base(owner) { }

    // The "—" button in the title-bar's top-right used to be inert because we
    // skip ResizeablePanelBase.ConstructPanelContent (which would hide the
    // title bar entirely) — and PanelBase wires it to OnClosePanelClicked,
    // which the base class no-ops. Hide the panel here so the button does
    // what users expect: close the UI. The floating button stays visible so
    // the user can re-open.
    protected override void OnClosePanelClicked() => SetActive(false);

    // 0.9.7: fullscreen toggle state. Snapshot of pre-fullscreen Rect data so
    // we can restore exactly what the user had after toggling off. NOT
    // persisted across sessions — fullscreen is treated as transient.
    private bool _isFullscreen;
    private UnityEngine.Vector2 _preFullscreenSizeDelta;
    private UnityEngine.Vector2 _preFullscreenAnchoredPos;
    private UnityEngine.Vector2 _preFullscreenAnchorMin;
    private UnityEngine.Vector2 _preFullscreenAnchorMax;
    private UnityEngine.Vector2 _preFullscreenPivot;
    // 0.11.2: store pre-fullscreen pin state so exit restores it. We force
    // IsPinned=true during fullscreen to block PanelDragger drag/resize —
    // stretched anchors break sizeDelta arithmetic, so the safest UX is
    // "only the maximize button works while fullscreen."
    private bool _preFullscreenPinned;
    private BloodCraftHub.UI.Framework.UniverseLib.UI.Models.ButtonRef _maximizeBtn;

    public bool IsFullscreen => _isFullscreen;

    /// <summary>0.9.7: toggle the main panel between its current size+pos and
    /// a stretched fullscreen layout (a small inset preserves border-grab
    /// for the resize handle in case the user wants to exit by dragging).
    /// Snapshots the prior layout so a second toggle restores it pixel-for-
    /// pixel. Only applies to the Primary UI — overlays are size-only.</summary>
    public void ToggleFullscreen() => SetFullscreen(!_isFullscreen);

    public void SetFullscreen(bool fullscreen)
    {
        if (Rect == null) return;
        if (fullscreen == _isFullscreen) return;

        if (fullscreen)
        {
            // Snapshot every Rect field that's about to change. anchorMin/Max
            // pivot tend to be (0.5, 0.5) by default for this panel, but we
            // don't assume — restore exactly what was there.
            _preFullscreenSizeDelta   = Rect.sizeDelta;
            _preFullscreenAnchoredPos = Rect.anchoredPosition;
            _preFullscreenAnchorMin   = Rect.anchorMin;
            _preFullscreenAnchorMax   = Rect.anchorMax;
            _preFullscreenPivot       = Rect.pivot;
            _preFullscreenPinned      = IsPinned;

            // Stretch to fill the canvas. With anchorMin=(0,0)/anchorMax=(1,1)
            // sizeDelta becomes the margin (offset from each edge), so setting
            // it to zero makes the panel exactly canvas-sized; the small inset
            // applied via offsetMin/offsetMax leaves room for resize-by-edge
            // gestures so the user can manually shrink back if needed.
            Rect.anchorMin = UnityEngine.Vector2.zero;
            Rect.anchorMax = UnityEngine.Vector2.one;
            Rect.pivot     = new UnityEngine.Vector2(0.5f, 0.5f);
            Rect.offsetMin = new UnityEngine.Vector2(20f, 20f);
            Rect.offsetMax = new UnityEngine.Vector2(-20f, -20f);

            // 0.11.2 critical fix: force IsPinned=true while fullscreen.
            // Friend-test surfaced two bugs that both came from the same
            // root cause — stretched anchors invert sizeDelta semantics:
            //   (a) AutoResizeIfEnabled assigns sizeDelta.y = desired
            //       height; with stretched anchors that means "make me
            //       desired px LARGER than the parent" = panel becomes
            //       screen.height + desired tall.
            //   (b) PanelDragger's resize-drag does the same assignment,
            //       so grabbing the edge to resize ALSO blows the panel
            //       up the moment the mouse moves.
            // Both vectors are eliminated by blocking PanelDragger
            // entirely while fullscreen — IsPinned makes Update() early-
            // return. The only safe interaction in fullscreen mode is
            // the maximize button itself, which calls back into this
            // method to exit fullscreen and restore IsPinned.
            IsPinned = true;
            _isFullscreen = true;
        }
        else
        {
            Rect.anchorMin       = _preFullscreenAnchorMin;
            Rect.anchorMax       = _preFullscreenAnchorMax;
            Rect.pivot           = _preFullscreenPivot;
            Rect.sizeDelta       = _preFullscreenSizeDelta;
            Rect.anchoredPosition= _preFullscreenAnchoredPos;
            IsPinned             = _preFullscreenPinned;
            _isFullscreen = false;
        }

        Dragger?.OnEndResize();
        UpdateMaximizeBtnVisuals();
        // 0.11.2 IMPORTANT: do NOT call OnFinishResize() here. The
        // pre-0.11.2 code did, which persisted the fullscreen-mode
        // sizeDelta to config — directly contradicting the comment at
        // line 295 saying "fullscreen is treated as transient." If the
        // user closed the game in fullscreen, next session restored the
        // stretched anchors and oversized sizeDelta, immediately re-
        // entering the broken state. Normal drag/resize still saves
        // through PanelBase.OnFinishDrag/OnFinishResize when the user
        // is NOT in fullscreen; the pre-fullscreen save is preserved.
    }

    private void UpdateMaximizeBtnVisuals()
    {
        if (_maximizeBtn == null) return;
        // Plain text glyphs known to render in V Rising's TMPro fallback set
        // (see docs/LESSONS_LEARNED.md). "[ ]" reads as "make window full" and
        // "[X]" as "restore" without needing icon glyph support.
        _maximizeBtn.ButtonText.text = _isFullscreen ? "[X]" : "[ ]";
    }

    /// <summary>0.9.7: insert a maximize/restore button to the LEFT of the close
    /// button in the PanelBase-built title bar. Called once at end of
    /// ConstructPanelContent — by then the base class has built TitleBar and
    /// CloseButton (see PanelBase.ConstructUI which builds the title bar
    /// before invoking the subclass ConstructPanelContent).</summary>
    private void BuildMaximizeButton()
    {
        if (CloseButton == null) return; // title bar was hidden by subclass override

        // The CloseButton GameObject is actually the right-aligned HOLDER
        // containing the actual close button. Add a sibling button inside
        // the same holder so both share the right-aligned cluster — and put
        // our button at sibling index 0 so it renders LEFT of the close.
        _maximizeBtn = BloodCraftHub.UI.Framework.UniverseLib.UI.UIFactory.CreateButton(
            CloseButton, "MaximizeButton", _isFullscreen ? "[X]" : "[ ]");
        UnityEngine.Object.Destroy(_maximizeBtn.Component.gameObject.GetComponent<UnityEngine.UI.Outline>());
        BloodCraftHub.UI.Framework.UniverseLib.UI.UIFactory.SetLayoutElement(
            _maximizeBtn.Component.gameObject,
            minHeight: 25, minWidth: 36, flexibleWidth: 0);
        _maximizeBtn.Component.colors = new UnityEngine.UI.ColorBlock()
        {
            normalColor    = BloodCraftHub.UI.Framework.CustomLib.Util.Theme.SliderHandle,
            colorMultiplier = 1,
        };
        _maximizeBtn.OnClick += ToggleFullscreen;
        // Move to the leftmost position inside CloseHolder so it appears
        // before the existing "—" close button. CreateButton appended at end;
        // SetSiblingIndex(0) pulls it to the front.
        _maximizeBtn.Component.gameObject.transform.SetSiblingIndex(0);
    }

    protected override void ConstructPanelContent()
    {
        // 0.10.13 fix (vertical analog of the 0.9.8 horizontal fix below):
        // ContentRoot's VLG was created with childForceExpandHeight=true in
        // PanelBase.CreatePanel. Unity's vertical layout distributes any
        // extra space EQUALLY among all children when forceExpand is true,
        // regardless of per-child flexibleHeight — so the LastResponse
        // panel + OverlayFooter + TooltipFooter all grew alongside the
        // tab content area when the user dragged the panel taller, even
        // though only `body` (flex=1) was supposed to absorb extra space.
        // Friend-test: "the tooltip area grows unnecessarily when the
        // settings/text pane is what should grow." Force-expand=false
        // makes only flex>0 children absorb extra space — `body` gets it
        // all, and the footers stay at their preferred sizes.
        var rootVlg = ContentRoot.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (rootVlg != null) rootVlg.childForceExpandHeight = false;

        // 0.9.8 fix: forceExpandWidth was true through 0.9.7. Unity's
        // HorizontalLayoutGroup ignores per-child flexibleWidth=0 when the
        // parent has childForceExpandWidth=true — every child gets an equal
        // share of extra space regardless. That's why the 0.9.7 attempt to
        // cap the tab strip via flexibleWidth=0 + preferredWidth=180 didn't
        // hold: as the main panel widened, the strip kept getting half the
        // extra width even though it was supposed to stay at 180. The right
        // content area still expands cleanly without forceExpandWidth because
        // BuildContentArea sets flexibleWidth=1 on the content's LayoutElement.
        var body = UIFactory.CreateHorizontalGroup(ContentRoot, "Body",
            forceExpandWidth: false, forceExpandHeight: true,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(6, 6, 6, 6));
        UIFactory.SetLayoutElement(body, flexibleHeight: 1, flexibleWidth: 1);

        BuildTabStrip(body);
        BuildContentArea(body);
        BuildLastResponsePanel(ContentRoot);
        BuildOverlayFooter(ContentRoot);
        BuildTooltipFooter(ContentRoot);
        // 0.9.7: maximize/restore button in the title bar, left of "—".
        BuildMaximizeButton();

        ShowTab(ActiveTab);

        // 0.9.6: per-frame ticker that auto-refreshes the wep / blood-legacy
        // stat-values on a 10s cadence while their tab is the active page.
        // Cheap when no relevant tab is active (one ActiveTab compare + one
        // time check). Registered once; Reset() unregisters.
        if (_tabAutoRefreshTicker == null)
        {
            _tabAutoRefreshTicker = TickTabAutoRefresh;
            BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Add(_tabAutoRefreshTicker);
        }
    }

    private void BuildTooltipFooter(GameObject parent)
    {
        // 0.9.3: footer height bumped 22 → 56 and label set to word-wrap.
        // Pre-0.9.3 long tooltips (notably the new OV master-overlay button's
        // multi-sentence description) overflowed the panel's right edge as
        // a single un-wrapped line. The new height holds ~3 lines of 12pt
        // italic text; longer tooltips get ellipsized instead of overflowing.
        var footer = UIFactory.CreateHorizontalGroup(parent, "TooltipFooter",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(8, 8, 4, 4));
        UIFactory.SetLayoutElement(footer, minHeight: 56, preferredHeight: 56, flexibleHeight: 0, flexibleWidth: 1);

        // 0.10.13: dropped italic and bumped font size 12 → 13 for legibility.
        // Friend-test: italic at standard text scale is hard to read.
        var lbl = UIFactory.CreateLabel(footer, "TooltipText",
            TooltipHover.IdlePlaceholder,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 400, preferredWidth: 600, flexibleWidth: 1,
            minHeight: 48, preferredHeight: 52, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Normal;
        lbl.TextMesh.enableWordWrapping = true;
        lbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
        // Apply muted color so the tooltip text reads as secondary/contextual.
        lbl.TextMesh.color = Theme.MutedBody;

        // Wire the static Sink so the per-frame TooltipHover.TickAll updates
        // this label. The TickAll action itself is registered in Plugin.Load,
        // so it's already running by the time we get here.
        TooltipHover.Sink = lbl.TextMesh;
        LogUtils.LogInfo($"TooltipHover sink wired ({TooltipHover.BindingCount} bindings tracked).");

        // Subscribe AutoResizeIfEnabled to CollapsibleSection toggles so the
        // panel grows/shrinks when the user expands an admin/prestige form.
        // Static event, single subscription per panel construction; Reset
        // unsubscribes.
        if (!_collapsibleSubscribed)
        {
            CollapsibleSection.Toggled += AutoResizeIfEnabled;
            _collapsibleSubscribed = true;
        }
    }

    private bool _collapsibleSubscribed;

    // -----------------------------------------------------------------------
    // Last server response panel (0.8.3)
    //
    // Always docked above the overlay footer. When the user clicks a read-data
    // command (.wep get / .class l / .misc userstats / etc.), the response is
    // captured by MessageService_Processing.AwaitingGenericResponse and routed
    // here so it lands in the UI instead of only in chat. Friend-testing of
    // v0.8.1 surfaced "it would load into the chat window rather than loading
    // into the UI informational box" — this is the structural fix.
    //
    // Hidden until the first response arrives. Click the header to collapse
    // the body so the panel doesn't crowd the active tab on narrow screens.
    // -----------------------------------------------------------------------

    private GameObject _lastResponseRoot;
    private GameObject _lastResponseBodyWrap;
    private TextMeshProUGUI _lastResponseHeader;
    private TextMeshProUGUI _lastResponseBody;
    private bool _lastResponseCollapsed;
    private bool _lastResponseSubscribed;

    private void BuildLastResponsePanel(GameObject parent)
    {
        _lastResponseRoot = UIFactory.CreateVerticalGroup(parent, "LastResponsePanel",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(8, 8, 4, 4));
        UIFactory.SetLayoutElement(_lastResponseRoot,
            minHeight: 0, flexibleHeight: 0, flexibleWidth: 1);
        _lastResponseRoot.SetActive(false); // shown when first response arrives

        // Header button — click to collapse/expand the body.
        var headerBtn = UIFactory.CreateButton(_lastResponseRoot, "LastResponseHeaderBtn", "");
        UIFactory.SetLayoutElement(headerBtn.GameObject,
            minWidth: 360, preferredWidth: 600, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        var headerText = headerBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (headerText != null)
        {
            headerText.alignment = TextAlignmentOptions.MidlineLeft;
            headerText.fontSize = Theme.ScaledUI(12);
            headerText.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _lastResponseHeader = headerText;
        }
        headerBtn.OnClick = () =>
        {
            _lastResponseCollapsed = !_lastResponseCollapsed;
            if (_lastResponseBodyWrap != null)
                _lastResponseBodyWrap.SetActive(!_lastResponseCollapsed);
            UpdateLastResponseHeaderText();
            AutoResizeIfEnabled();
        };
        TooltipHover.Attach(headerBtn.GameObject,
            "Click to collapse/expand. Updates whenever you click a read-data command in any tab (.wep get, .class l, .misc userstats, .clan list, .boss list, etc.). Replies still also appear in chat unless you've enabled Clear server messages.");

        _lastResponseBodyWrap = UIFactory.CreateVerticalGroup(_lastResponseRoot, "LastResponseBody",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 0, padding: new Vector4(6, 6, 4, 4));
        UIFactory.SetLayoutElement(_lastResponseBodyWrap,
            minHeight: 0, flexibleHeight: 0, flexibleWidth: 1);

        // Multi-line label with ContentSizeFitter so the panel sizes to fit
        // whatever the server returned without truncation.
        var bodyLbl = UIFactory.CreateLabel(_lastResponseBodyWrap, "LastResponseText",
            "", TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(bodyLbl.GameObject,
            minWidth: 360, preferredWidth: 600, flexibleWidth: 1,
            minHeight: 0, flexibleHeight: 0);
        bodyLbl.TextMesh.enableWordWrapping = true;
        bodyLbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        bodyLbl.TextMesh.richText = true; // keep server-sent <color=...> tags
        // 0.9.1: server color tags (e.g. <color=red> blood headings) get a
        // wider outline so they stay legible when the panel sits over a red
        // in-game background.
        ApplyStrongAccentOutline(bodyLbl.TextMesh);
        var fitter = bodyLbl.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        _lastResponseBody = bodyLbl.TextMesh;

        if (!_lastResponseSubscribed)
        {
            PlayerStateService.LastResponseChanged += OnLastResponseChanged;
            _lastResponseSubscribed = true;
        }
    }

    private void OnLastResponseChanged()
    {
        if (_lastResponseRoot == null) return;
        var r = PlayerStateService.LastResponse;

        _lastResponseRoot.SetActive(true);
        UpdateLastResponseHeaderText();

        if (_lastResponseBody != null)
        {
            // Concatenate lines with newlines. TMP renders the inline color
            // tags, so the response looks like the server-side chat output.
            _lastResponseBody.text = r.Lines != null
                ? string.Join("\n", r.Lines)
                : "";
        }

        // Whenever a new response arrives, auto-expand so the user notices.
        _lastResponseCollapsed = false;
        if (_lastResponseBodyWrap != null) _lastResponseBodyWrap.SetActive(true);

        AutoResizeIfEnabled();
    }

    private void UpdateLastResponseHeaderText()
    {
        if (_lastResponseHeader == null) return;
        var r = PlayerStateService.LastResponse;
        var arrow = _lastResponseCollapsed ? "▶" : "▼";
        var cmd   = string.IsNullOrEmpty(r.Command) ? "(no response yet)" : r.Command;
        var count = r.Lines?.Count ?? 0;
        _lastResponseHeader.text = $"{arrow}  Last server response — <color=#9ECCFF>{cmd}</color>  ({count} line{(count == 1 ? "" : "s")})";
    }

    // -----------------------------------------------------------------------
    // Tab strip (left rail)
    // -----------------------------------------------------------------------

    // 0.9.7: max width applied to the tab strip so it doesn't grow
    // proportionally when the main panel widens (or when UI text scales up).
    // 0.10.2 bump: 180 → 220. The v0.9.8 "Help" → "SETTINGS AND HELP" rename
    // pushed the longest group header to ~17 chars ("SETTINGS AND HELP") plus
    // the ▶/▼ prefix, which overlapped the rail edge at Standard scale and
    // truncated at Large scale. 220 covers Large scale with margin.
    private const float TAB_STRIP_MAX_WIDTH = 220f;

    private void BuildTabStrip(GameObject parent)
    {
        // 0.12.1: subscribe to Bloodcraft availability transitions so a late
        // handshake ACK flips the group from tentative-available to confirmed
        // (or to unavailable on give-up) without rebuilding the panel. Idempotent
        // — Reset() unsubscribes, and the flag prevents double-subscribe if
        // BuildTabStrip is called again on rebuild.
        if (!_availabilitySubscribed)
        {
            Services.EclipseProtocolService.AvailabilityChanged += OnBloodcraftAvailabilityChanged;
            _availabilitySubscribed = true;
        }

        // childControlHeight: true is required - the strip stacks group headers
        // and group-content blocks of varying heights, and without it the layout
        // group leaves children at default sizeDelta (~0px) so KINDRED/HELP
        // headers overlap the BLOODCRAFT sub-tab list.
        var strip = UIFactory.CreateVerticalGroup(parent, "TabStrip",
            forceWidth: false, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(2, 2, 2, 2));
        // 0.9.7: pin preferredWidth = minWidth = TAB_STRIP_MAX_WIDTH so the
        // strip doesn't expand past the cap regardless of panel width. The
        // body's HorizontalLayoutGroup with childControlWidth=true respects
        // preferredWidth, so the right content area absorbs all the extra
        // horizontal space.
        UIFactory.SetLayoutElement(strip,
            minWidth: (int)TAB_STRIP_MAX_WIDTH,
            preferredWidth: (int)TAB_STRIP_MAX_WIDTH,
            flexibleWidth: 0,
            flexibleHeight: 1);
        _tabStripGo = strip;

        foreach (var group in TabGroups)
            BuildTabGroup(strip, group);
    }

    private void BuildTabGroup(GameObject parent, TabGroupDef group)
    {
        _groupExpanded[group.Title] = group.StartExpanded;

        // Resolve per-group availability. If the server doesn't have the
        // backing mod, the group is rendered as collapsed-and-disabled with
        // a "(unavailable)" suffix and grayed text — still visible so the
        // user can see what BCH supports, but unable to expand or click in.
        bool available = IsTabGroupAvailable(group.Title);
        bool startExpanded = group.StartExpanded && available;

        // Header button - clicking toggles the group's content visibility (when available).
        var header = UIFactory.CreateButton(parent, $"GroupHeader_{group.Title}",
            FormatGroupHeader(group.Title, startExpanded, available));
        UIFactory.SetLayoutElement(header.GameObject,
            minWidth: 140, preferredWidth: 144, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var headerText = header.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (headerText != null)
        {
            headerText.alignment = TextAlignmentOptions.MidlineLeft;
            headerText.enableWordWrapping = false;
            headerText.overflowMode = TextOverflowModes.Overflow;
            headerText.fontStyle = FontStyles.Bold;
            headerText.fontSize = Theme.ScaledUI(12);
            if (!available) headerText.color = new Color(0.55f, 0.55f, 0.55f); // grayed
            _groupHeaderText[group.Title] = headerText;
        }
        if (!available) header.Component.interactable = false;
        _groupHeaderButton[group.Title] = header;
        TooltipHover.Attach(header.GameObject,
            available
                ? $"Show / hide the {group.Title} tab list."
                : $"{group.Title} is marked unavailable on this server (no backing mod detected). Adjust via .cfg: BloodcraftAvailability / KindredAvailability = On to force-enable.");

        // Sub-tabs container (slight left indent so the group structure reads).
        var content = UIFactory.CreateVerticalGroup(parent, $"GroupContent_{group.Title}",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(6, 2, 2, 2));
        UIFactory.SetLayoutElement(content,
            minWidth: 140, preferredWidth: 144, flexibleWidth: 1,
            minHeight: 0, preferredHeight: Mathf.Max(28, group.Tabs.Length * 30 + 4), flexibleHeight: 0);
        _groupContent[group.Title] = content;

        if (group.Tabs.Length == 0)
        {
            var placeholder = UIFactory.CreateLabel(content, "Empty",
                "(coming soon)",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
            UIFactory.SetLayoutElement(placeholder.GameObject,
                minWidth: 130, preferredWidth: 140, flexibleWidth: 1,
                minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
            placeholder.TextMesh.fontStyle = FontStyles.Italic;
            placeholder.TextMesh.enableWordWrapping = false;
        }
        else
        {
            foreach (var (tab, label) in group.Tabs)
            {
                var b = UIFactory.CreateButton(content, $"TabBtn_{tab}", label);
                UIFactory.SetLayoutElement(b.GameObject,
                    minWidth: 130, preferredWidth: 138, flexibleWidth: 1,
                    minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
                var t = b.Component.GetComponentInChildren<TextMeshProUGUI>();
                if (t != null)
                {
                    t.enableWordWrapping = false;
                    t.overflowMode = TextOverflowModes.Overflow;
                    t.alignment = TextAlignmentOptions.Center;
                    t.fontSize = Theme.ScaledUI(13);
                }
                var captured = tab;
                b.OnClick = () => ShowTab(captured);
                _tabButtons[tab] = b;
            }
        }

        content.SetActive(startExpanded);
        if (available) header.OnClick = () => ToggleGroup(group.Title);
    }

    /// <summary>
    /// Resolves availability for a tab group ("Bloodcraft" / "Kindred" / "Help").
    /// Auto = Bloodcraft uses the Eclipse handshake ACK, Kindred is currently
    /// always-on (no probe wired). Help is always available.
    /// </summary>
    private static bool IsTabGroupAvailable(string title)
    {
        switch (title)
        {
            case "Bloodcraft":
                return Settings.BloodcraftAvailability switch
                {
                    Settings.ModAvailability.On  => true,
                    Settings.ModAvailability.Off => false,
                    // 0.12.1: during the handshake retry window
                    // (~15 s after world entry — see REGISTRATION_MAX_ATTEMPTS),
                    // treat as tentatively available instead of greying out.
                    // Pre-0.12.1 returned plain UserRegistered which produced
                    // a "Bloodcraft Unavailable" race for users on actual
                    // Bloodcraft servers. Now we only mark Unavailable once
                    // the registration has REALLY given up.
                    _ => Services.EclipseProtocolService.UserRegistered
                      || !Services.EclipseProtocolService.RegistrationGaveUp,
                };
            case "Kindred":
                return Settings.KindredAvailability switch
                {
                    Settings.ModAvailability.On   => true,
                    Settings.ModAvailability.Off  => false,
                    _ => true, // no probe wired - assume present
                };
            default:
                return true; // Help, future groups
        }
    }

    private void ToggleGroup(string title)
    {
        if (!_groupExpanded.TryGetValue(title, out var current)) return;
        var next = !current;
        _groupExpanded[title] = next;
        if (_groupContent.TryGetValue(title, out var go))
            go.SetActive(next);
        if (_groupHeaderText.TryGetValue(title, out var txt))
            txt.text = FormatGroupHeader(title, next);
        AutoResizeIfEnabled();
    }

    private static string FormatGroupHeader(string title, bool expanded, bool available = true)
    {
        var t = title.ToUpper();
        if (!available) return $"–  {t}  (unavailable)";
        return expanded ? $"▼  {t}" : $"▶  {t}";
    }

    // 0.12.1: AvailabilityChanged subscriber. Defers to the next CoreUpdateBehavior
    // tick because the event fires from inside ClientChatPatch.OnUpdate_Prefix
    // (mid-iteration of the chat entity array) — running UI mutations there is
    // legal but the deferred-frame timing keeps it consistent with the rest of
    // our event handlers (RequestRebuildMainPanel, etc.).
    private System.Action _deferredAvailabilityRefresh;

    private void OnBloodcraftAvailabilityChanged()
    {
        if (_deferredAvailabilityRefresh != null) return; // already queued for this frame
        _deferredAvailabilityRefresh = () =>
        {
            BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Remove(_deferredAvailabilityRefresh);
            _deferredAvailabilityRefresh = null;
            RefreshAllTabGroupAvailability();
        };
        BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Add(_deferredAvailabilityRefresh);
    }

    private void RefreshAllTabGroupAvailability()
    {
        foreach (var title in new System.Collections.Generic.List<string>(_groupHeaderText.Keys))
            RefreshTabGroupAvailability(title);
        AutoResizeIfEnabled();
    }

    private void RefreshTabGroupAvailability(string title)
    {
        bool available = IsTabGroupAvailable(title);
        bool expanded = _groupExpanded.TryGetValue(title, out var e) && e;

        if (_groupHeaderText.TryGetValue(title, out var headerText))
        {
            headerText.text = FormatGroupHeader(title, expanded && available, available);
            headerText.color = available ? Theme.DefaultText : new Color(0.55f, 0.55f, 0.55f);
        }
        if (_groupHeaderButton.TryGetValue(title, out var btn))
        {
            btn.Component.interactable = available;
            // Re-wire the OnClick: a previously-unavailable header had its OnClick
            // skipped during BuildTabGroup. Assign now if it's become available.
            btn.OnClick = available ? () => ToggleGroup(title) : null;
        }
        if (_groupContent.TryGetValue(title, out var go))
        {
            // Don't auto-expand on becoming available — preserve user agency.
            // Just ensure that if currently expanded but no longer available,
            // we collapse it (defensive — Off via .cfg edit while panel open).
            if (!available && go.activeSelf) go.SetActive(false);
        }
    }

    // -----------------------------------------------------------------------
    // Tab content area (right side) - dispatches per-tab builders
    // -----------------------------------------------------------------------

    private void BuildContentArea(GameObject parent)
    {
        var content = UIFactory.CreateVerticalGroup(parent, "TabContent",
            forceWidth: true, forceHeight: true,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(6, 6, 6, 6));
        UIFactory.SetLayoutElement(content,
            minWidth: 380, preferredWidth: 420, flexibleWidth: 1,
            minHeight: 280, preferredHeight: 320, flexibleHeight: 1);

        foreach (var (tab, label) in AllTabs())
        {
            var pageWrapper = CreateTabPage(content, out var page);
            AddTabHeading(page, label);

            switch (tab)
            {
                case PanelType.FamiliarsTab:
                    BuildFamiliarsTab(page);
                    break;
                case PanelType.BoxesTab:
                    BuildBoxesTab(page);
                    break;
                case PanelType.VBloodsTab:
                    BuildVBloodsTab(page);
                    break;
                case PanelType.AllFamiliarsTab:
                    BuildAllFamiliarsTab(page);
                    break;
                case PanelType.ClassTab:
                    BuildClassTab(page);
                    break;
                case PanelType.ExpertiseTab:
                    BuildExpertiseTab(page);
                    break;
                case PanelType.BloodLegacyTab:
                    BuildBloodLegacyTab(page);
                    break;
                case PanelType.UnarmedShiftTab:
                    BuildUnarmedShiftTab(page);
                    break;
                case PanelType.PrestigeTab:
                    BuildPrestigeTab(page);
                    break;
                case PanelType.LevelsTab:
                    BuildLevelsTab(page);
                    break;
                case PanelType.AdminTab:
                    BuildAdminTab(page);
                    break;
                case PanelType.KindredLogisticsTab:
                    BuildKindredLogisticsTab(page);
                    break;
                case PanelType.KindredLogisticsAdminTab:
                    BuildKindredLogisticsAdminTab(page);
                    break;
                case PanelType.DailyQuestTab:
                    BuildDailyQuestTab(page);
                    break;
                case PanelType.KindredCommandsPlayerTab:
                    BuildKindredCommandsPlayerTab(page);
                    break;
                case PanelType.KindredAdminPlayersTab:
                    BuildKindredAdminPlayersTab(page);
                    break;
                case PanelType.KindredAdminServerTab:
                    BuildKindredAdminServerTab(page);
                    break;
                case PanelType.KindredAdminWorldTab:
                    BuildKindredAdminWorldTab(page);
                    break;
                case PanelType.QuickStartTab:
                    BuildQuickStartTab(page);
                    break;
                case PanelType.GameGuideTab:
                    BuildGameGuideTab(page);
                    break;
                case PanelType.SettingsTab:
                    BuildSettingsTab(page);
                    break;
                case PanelType.AboutTab:
                    BuildAboutTab(page);
                    break;
                case PanelType.VanillaAdminTab:
                    BuildVanillaAdminTab(page);
                    break;
                default:
                    AddComingSoonBody(page, label);
                    break;
            }

            pageWrapper.SetActive(false);
            _tabContent[tab] = pageWrapper;
            _tabInnerContent[tab] = page;
        }
    }

    // Returns the ScrollView wrapper (used for SetActive + as the visible tab
    // GameObject); out-param yields the inner content GameObject where the
    // BuildXxxTab methods append children. AutoResize walks the inner so it
    // sees the true children-sum height, not the viewport.
    private GameObject CreateTabPage(GameObject parent, out GameObject content)
    {
        var wrapper = UIFactory.CreateScrollView(parent, "TabPage",
            out content, out _, color: new Color(0f, 0f, 0f, 0f));
        UIFactory.SetLayoutElement(wrapper,
            minWidth: 380, preferredWidth: 420, flexibleWidth: 1,
            minHeight: 280, flexibleHeight: 1);

        // Re-style the auto-created content VerticalLayoutGroup to match the
        // old CreateTabPage layout (spacing 6, padding 8/8/8/8).
        var vlg = content.GetComponent<VerticalLayoutGroup>();
        if (vlg != null)
        {
            vlg.spacing = 6;
            vlg.padding.left  = 8;
            vlg.padding.right = 8;
            vlg.padding.top   = 8;
            vlg.padding.bottom = 8;
            vlg.childControlWidth  = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperLeft;
        }
        return wrapper;
    }

    private static void AddTabHeading(GameObject page, string text)
    {
        var heading = UIFactory.CreateLabel(page, "TabHeading", text,
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(20));
        UIFactory.SetLayoutElement(heading.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        heading.TextMesh.fontStyle = FontStyles.Bold;
        heading.TextMesh.enableWordWrapping = false;
        heading.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    private static void AddComingSoonBody(GameObject page, string label)
    {
        var placeholder = UIFactory.CreateLabel(page, "Placeholder",
            "Coming soon — this tab will surface the matching Bloodcraft commands.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(14));
        UIFactory.SetLayoutElement(placeholder.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 40, preferredHeight: 80, flexibleHeight: 0);
        placeholder.TextMesh.enableWordWrapping = true;
        placeholder.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    // -----------------------------------------------------------------------
    // Familiars tab
    // -----------------------------------------------------------------------

    private void BuildFamiliarsTab(GameObject page)
    {
        // 0.10.9: card-wrapped sections. Pre-0.10.9 this was the densest
        // tab — 8 action buttons, an emote-binding wall, and 6 collapsible
        // forms all stacked on the same panel background. Cards give the
        // four conceptual zones (current state / quick actions / emotes
        // reference / more actions / battle groups) explicit grouping.

        // ── Active Familiar ─────────────────────────────────────────────
        var activeCard = AddCard(page, "FamActiveCard", Theme.SystemTintFamiliar);
        AddSectionHeading(activeCard, "★  Active Familiar");
        _famNameLabel     = AddInfoLabel(activeCard, "FamName",     "—", FontStyles.Bold,   fontSize: Theme.ScaledUI(18));
        _famProgressLabel = AddInfoLabel(activeCard, "FamProgress", "Level — ", FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        _famStatsLabel    = AddInfoLabel(activeCard, "FamStats",    "HP —  PP —  SP —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        AddSpacer(page, 6);

        // ── Quick actions ───────────────────────────────────────────────
        var actionsCard = AddCard(page, "FamActionsCard");
        AddSectionHeading(actionsCard, "Actions");
        var row1 = UIFactory.CreateHorizontalGroup(actionsCard, "FamActionsRow1",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row1,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(row1, "Recall / Dismiss", MessageService.BCCOM_FAM_TOGGLE,
            "Calls or dismisses your active familiar (.fam t). Recallable — does NOT destroy the familiar.");
        AddCommandButton(row1, "Toggle Combat", MessageService.BCCOM_FAM_COMBAT,
            "Toggle combat mode for the active familiar (.fam c). Off = passive, won't engage enemies.");
        AddCommandButton(row1, "Toggle Emotes", MessageService.BCCOM_FAM_TOGGLE_EMOTES,
            "Enable/disable emote-action bindings (.fam e). When OFF, emoting (e.g. clap) won't open the familiar's inventory or trigger other actions.");
        AddCommandButton(row1, "List Emotes", MessageService.BCCOM_FAM_LIST_EMOTES,
            "List the current emote→action bindings (.fam actions). Tells you which emote does what (e.g. clap = open inventory).");

        var row2 = UIFactory.CreateHorizontalGroup(actionsCard, "FamActionsRow2",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row2,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(row2, "★  Prestige", MessageService.BCCOM_FAM_PRESTIGE,
            "Prestige the active familiar (.fam pr). Requires max level; resets level and grants permanent bonuses.");
        AddCommandButton(row2, "Unbind", MessageService.BCCOM_FAM_UNBIND,
            "Unbind the active familiar (.fam ub). The in-world entity is released but the familiar STAYS in your box — you can re-bind it from the Boxes tab any time. Use this to free the bind slot so you can summon a different familiar. To permanently delete a familiar from your collection, use the Boxes tab → Permanently Delete form.");

        AddDivider(actionsCard);
        AddBodyText(actionsCard, "Switch to the Boxes tab to browse your familiar boxes and click-to-bind.");

        AddSpacer(page, 6);

        // ── Emote bindings reference ────────────────────────────────────
        var emoteCard = AddCard(page, "FamEmoteCard");
        AddSectionHeading(emoteCard, "Emote Bindings (perform these in-world)");
        AddBodyText(emoteCard,
            "Bloodcraft binds these emotes to familiar actions. Trigger by performing the emote in-world (e.g. /clap), NOT via this UI — there's no chat command to invoke an emote programmatically. Toggle Emotes (above) enables/disables the whole system.");
        var emoteRef = UIFactory.CreateLabel(emoteCard, "FamEmoteRef",
            "  • Wave   →  Recall / Dismiss\n" +
            "  • Salute →  Toggle Combat Mode\n" +
            "  • Clap   →  Bind / Unbind active familiar\n" +
            "  • Beckon →  Interact (opens familiar's inventory, equipment, name & settings)",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(emoteRef.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 80, preferredHeight: 96, flexibleHeight: 0);
        emoteRef.TextMesh.enableWordWrapping = true;
        emoteRef.TextMesh.overflowMode = TextOverflowModes.Overflow;

        AddSpacer(page, 6);

        // ── More familiar actions (collapsibles) ────────────────────────
        var moreActionsCard = AddCard(page, "FamMoreActionsCard");
        AddSectionHeading(moreActionsCard, "More Familiar Actions");

        CollapsibleSection.Build(moreActionsCard,
            title: "Search boxes by name (.fam s)",
            startExpanded: false,
            tooltip: "Search across ALL your boxes for familiars whose name matches the text. Reply appears in chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Search familiars",
                commandTemplate: ".fam s \"{name}\"",
                new TextField("name", "Name (substring)", placeholder: "Wolf",
                    tooltip: "Substring of the familiar's display name. Bloodcraft does case-insensitive matching across boxes.")));

        CollapsibleSection.Build(moreActionsCard,
            title: "Smart bind by name (.fam sb)",
            startExpanded: false,
            tooltip: "Search + bind in one step. If multiple matches are found Bloodcraft returns the list for clarification (no destructive action). Will fail if you already have a familiar bound.",
            buildContent: c => FormBuilder.Build(c,
                title: "Smart bind",
                commandTemplate: ".fam sb \"{name}\"",
                new TextField("name", "Name (substring)", placeholder: "Wolf",
                    tooltip: "Substring of the familiar's display name to bind.")));

        CollapsibleSection.Build(moreActionsCard,
            title: "Make active familiar shiny (.fam shiny)",
            startExpanded: false,
            tooltip: "Spends vampiric dust to permanently mark your CURRENT active familiar with a shiny buff of the chosen school. Requires an active familiar bound first.",
            buildContent: c => FormBuilder.Build(c,
                title: "Apply shiny",
                commandTemplate: ".fam shiny {school}",
                new EnumField<PlayerStateService.FamiliarShinySchoolChoice>("school", "Spell school",
                    defaultValue: PlayerStateService.FamiliarShinySchoolChoice.Storm,
                    tooltip: "The shiny element to apply. Each school has a flavour (Storm = stun, Blood = leech, etc.).")));

        CollapsibleSection.Build(moreActionsCard,
            title: "Toggle a familiar setting (.fam option)",
            startExpanded: false,
            tooltip: "Flips one of Bloodcraft's per-player familiar settings. Common settings: 'shiny' (apply shiny visuals), 'vbloodemotes' (familiar plays VBlood emotes). Bloodcraft's reply tells you what's now on/off.",
            buildContent: c => FormBuilder.Build(c,
                title: "Toggle option",
                commandTemplate: ".fam option {setting}",
                new TextField("setting", "Setting name", placeholder: "shiny",
                    tooltip: "Name of the setting to toggle. Server replies with the new state in chat.")));

        CollapsibleSection.Build(moreActionsCard,
            title: "Buy V-Blood echoes (.fam echoes)",
            startExpanded: false,
            tooltip: "Spend V-Blood essence to purchase the exo reward tied to the named V-Blood unit. Cost scales with unit tier.",
            buildContent: c => FormBuilder.Build(c,
                title: "Buy echoes",
                commandTemplate: ".fam echoes \"{vblood}\"",
                new TextField("vblood", "V-Blood name", placeholder: "Quincey the Bandit King",
                    tooltip: "Exact display name of the V-Blood whose echo reward you want.")));

        CollapsibleSection.Build(moreActionsCard,
            title: "Reset all familiar entities (.fam reset) — DESTRUCTIVE",
            startExpanded: false,
            tooltip: "Destroys every entity in your follower buffer and clears your familiar-actives state. Use to recover from a bugged or stuck familiar bind. Required confirm checkbox.",
            buildContent: c => FormBuilder.Build(c,
                title: "Reset familiars",
                commandTemplate: ".fam reset",
                new BoolField("confirm", "Yes, destroy active follower entities",
                    tooltip: "Required. Box records and unlock data are NOT touched — this only clears in-world entities + active state. Re-bind from a box to summon again.",
                    requireTrue: true)));

        // 0.10.11: in-panel results display for the .fam s search above.
        // Pre-0.10.11 the only place results showed up was chat — when
        // the user had BCH chat-suppression on (Settings → Chat Logging
        // → Bloodcraft toggle off, or ClearServerMessages), the reply
        // landed nowhere visible. The parser already fires
        // MessageService.FamSearchCompleted for every .fam s reply, so
        // we just need a subscriber that surfaces the result in the UI.
        BuildFamSearchResultPanel(moreActionsCard);

        AddSpacer(page, 6);

        // ── Battle groups ───────────────────────────────────────────────
        var bgCard = AddCard(page, "FamBgCard");
        AddSectionHeading(bgCard, "Battle Groups");
        AddBodyText(bgCard,
            "Battle groups are pre-built lineups of familiars for PvP challenges. List shows the groups you've made; create one, slot familiars into it, then challenge another player.");

        var bgRow1 = UIFactory.CreateHorizontalGroup(bgCard, "FamBgRow1",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(bgRow1,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(bgRow1, "List Groups", MessageService.BCCOM_FAM_BG_LIST,
            "List your battle groups (.fam bgs).");

        CollapsibleSection.Build(bgCard,
            title: "Show battle group details (.fam bg)",
            startExpanded: false,
            tooltip: "Show the contents of a battle group. Leave blank to inspect your active group.",
            buildContent: c => FormBuilder.Build(c,
                title: "Show battle group",
                commandTemplate: ".fam bg {group}",
                new TextField("group", "Group name (blank = active)")));

        CollapsibleSection.Build(bgCard,
            title: "Choose active battle group (.fam cbg)",
            startExpanded: false,
            tooltip: "Sets which battle group is your active one (used by .fam challenge).",
            buildContent: c => FormBuilder.Build(c,
                title: "Choose battle group",
                commandTemplate: ".fam cbg {group}",
                new TextField("group", "Group name", placeholder: "MyTeam")));

        CollapsibleSection.Build(bgCard,
            title: "Create battle group (.fam abg)",
            startExpanded: false,
            tooltip: "Create a new (empty) battle group. Use Slot Familiar below to fill it.",
            buildContent: c => FormBuilder.Build(c,
                title: "Create battle group",
                commandTemplate: ".fam abg {group}",
                new TextField("group", "New group name", placeholder: "MyTeam")));

        CollapsibleSection.Build(bgCard,
            title: "Slot active familiar into group (.fam sbg)",
            startExpanded: false,
            tooltip: "Assigns your CURRENTLY-bound familiar to a slot in the named group. Bind the familiar you want to slot first.",
            buildContent: c => FormBuilder.Build(c,
                title: "Slot familiar",
                commandTemplate: ".fam sbg {group} {slot}",
                new TextField("group", "Group name", placeholder: "MyTeam"),
                new IntField("slot", "Slot (1-3)", min: 1, max: 3,
                    tooltip: "Which slot in the group to put the familiar.")));

        CollapsibleSection.Build(bgCard,
            title: "Delete battle group (.fam dbg) — DESTRUCTIVE",
            startExpanded: false,
            tooltip: "Permanently removes the battle group. The slotted familiars themselves are NOT destroyed (only the grouping). Required confirm checkbox.",
            buildContent: c => FormBuilder.Build(c,
                title: "Delete battle group",
                commandTemplate: ".fam dbg {group}",
                new TextField("group", "Group name"),
                new BoolField("confirm", "Yes, delete this group",
                    tooltip: "Required. The slotted familiars stay in your boxes; only the group definition is removed.",
                    requireTrue: true)));

        CollapsibleSection.Build(bgCard,
            title: "Challenge a player (.fam challenge)",
            startExpanded: false,
            tooltip: "Initiate (or accept/queue) a battle-group fight against another player. Leave blank to view the current queue.",
            buildContent: c => FormBuilder.Build(c,
                title: "Challenge",
                commandTemplate: ".fam challenge {player}",
                new PlayerNameField("player", "Player (blank = view queue)")));

        RenderFamiliar(PlayerStateService.Familiar);
        if (!_famSubscribed)
        {
            PlayerStateService.FamiliarChanged += OnFamiliarChanged;
            _famSubscribed = true;
        }
        // 0.10.11: subscribe to the parser's FamSearchCompleted event so
        // the in-panel result list updates whenever a .fam s reply lands.
        if (!_famSearchSubscribed)
        {
            MessageService.FamSearchCompleted += OnFamSearchCompletedForFamTab;
            _famSearchSubscribed = true;
        }
    }

    private void OnFamiliarChanged() => RenderFamiliar(PlayerStateService.Familiar);

    /// <summary>0.10.11: build the in-panel result display for the .fam s
    /// search form above. Mounted at the bottom of the More Actions card
    /// so it's visually adjacent to the form that produces its data.</summary>
    private void BuildFamSearchResultPanel(GameObject parent)
    {
        AddDivider(parent);
        AddSectionHeading(parent, "Last search result");

        _famSearchResultHeader = AddInfoLabel(parent, "FamSearchResultHeader",
            "(submit a search above to populate)", FontStyles.Italic, fontSize: Theme.ScaledUI(12));
        _famSearchResultHeader.color = new UnityEngine.Color(0.7f, 0.7f, 0.7f);

        _famSearchResultList = UIFactory.CreateVerticalGroup(parent, "FamSearchResultList",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 6, 4));
        UIFactory.SetLayoutElement(_famSearchResultList,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 0, flexibleHeight: 0);
    }

    /// <summary>0.10.11: render a FamSearchCompleted payload into the
    /// Last-search-result panel. Each matching box renders as its own
    /// row with a shiny indicator when the server attached the pink-star
    /// marker. No-match results show a single italic "no matches" line.
    /// Cheap rebuild: typical reply has 0-5 boxes.</summary>
    private void OnFamSearchCompletedForFamTab(MessageService.FamSearchResult r)
    {
        if (_famSearchResultHeader == null || _famSearchResultList == null) return;

        // Header: "Search: 'name' → N match(es)" or "no matches".
        if (string.IsNullOrEmpty(r.Query))
        {
            _famSearchResultHeader.text = "(submit a search above to populate)";
        }
        else if (!r.HadAnyMatch || r.Boxes == null || r.Boxes.Count == 0)
        {
            _famSearchResultHeader.text = $"Search: \"{r.Query}\"  →  no matches.";
        }
        else
        {
            _famSearchResultHeader.text = $"Search: \"{r.Query}\"  →  {r.Boxes.Count} box{(r.Boxes.Count == 1 ? "" : "es")}.";
        }
        _famSearchResultHeader.color = new UnityEngine.Color(0.9f, 0.9f, 0.9f);

        // Wipe and rebuild the box list.
        for (int i = _famSearchResultList.transform.childCount - 1; i >= 0; --i)
            UnityEngine.Object.Destroy(_famSearchResultList.transform.GetChild(i).gameObject);

        if (r.HadAnyMatch && r.Boxes != null)
        {
            foreach (var b in r.Boxes)
            {
                string suffix = b.HasShiny
                    ? $"   <color=#FFA0F0>★ shiny</color>"
                    : "";
                var line = UIFactory.CreateLabel(_famSearchResultList, "ResultRow",
                    $"<color=#9AC8D9>•</color>  {b.Box}{suffix}",
                    TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
                UIFactory.SetLayoutElement(line.GameObject,
                    minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
                    minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
                line.TextMesh.enableWordWrapping = false;
                line.TextMesh.overflowMode = TextOverflowModes.Overflow;
            }
        }
        AutoResizeIfEnabled();
    }

    // -----------------------------------------------------------------------
    // Boxes tab
    // -----------------------------------------------------------------------

    private void BuildBoxesTab(GameObject page)
    {
        // 0.10.11: wrap the active-box header + tip in a card so the
        // bold "Active Box: …" label isn't flush with the panel border,
        // and the italic Tip body has muted styling that recedes
        // visually next to the bold header.
        var headerCard = AddCard(page, "BoxesHeaderCard", Theme.SystemTintFamiliar);
        _boxesActiveBoxLabel = AddInfoLabel(headerCard, "ActiveBox",
            "Active Box: (none selected)",
            FontStyles.Bold, fontSize: Theme.ScaledUI(14));
        AddBodyText(headerCard,
            "Tip: click Refresh to pull your box list, click a box to see its familiars, click a familiar to bind it. Use ← Back to return.");

        AddSpacer(page, 6);

        // ---------------- Picker section (visible when no box selected) ----------------
        // childControlHeight: true is crucial here - without it the layout group
        // does NOT enforce children's heights, so the action row, section heading,
        // and list container all draw at their default (0) sizeDelta and overlap.
        // flexibleHeight: 0 (was 1) so the section takes only its natural height.
        // The tab-page ScrollView (added in Phase 5j) handles overflow when the
        // box list is long, and AutoResize grows the panel before that point.
        _boxesPickerSection = UIFactory.CreateVerticalGroup(page, "BoxPickerSection",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_boxesPickerSection,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 80, flexibleHeight: 0);

        var pickerActions = UIFactory.CreateHorizontalGroup(_boxesPickerSection, "PickerActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(pickerActions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        var refreshBtn = UIFactory.CreateButton(pickerActions, "Cmd_RefreshBoxes", "Refresh");
        UIFactory.SetLayoutElement(refreshBtn.GameObject,
            minWidth: 70, preferredWidth: 110, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        var refreshText = refreshBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (refreshText != null)
        {
            refreshText.enableWordWrapping = false;
            refreshText.overflowMode = TextOverflowModes.Overflow;
            refreshText.alignment = TextAlignmentOptions.Center;
            refreshText.fontSize = Theme.ScaledUI(13);
        }
        TooltipHover.Attach(refreshBtn.GameObject,
            "Re-fetch your familiar boxes from the server (.fam boxes). The server reply can take a few seconds.");
        refreshBtn.OnClick = () =>
        {
            if (_boxesStatusLabel != null) _boxesStatusLabel.text = "Loading boxes from the server…";
            EnqueueOrWarn(MessageService.BCCOM_FAM_BOXES);
        };

        _boxesStatusLabel = AddInfoLabel(_boxesPickerSection, "BoxesStatus", "",
            FontStyles.Italic, fontSize: Theme.ScaledUI(11));

        AddSectionHeading(_boxesPickerSection, "Available Boxes");
        // 0.9.2: bumped top padding 2→6 so the first row of box buttons has
        // breathing room from the "Available Boxes" heading above it. The
        // heading itself now reserves a larger height (AddSectionHeading
        // 0.9.2 fix) but the row gap was tight too.
        _boxesListContainer = UIFactory.CreateVerticalGroup(_boxesPickerSection, "BoxListContainer",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(2, 2, 6, 2));
        // No fixed preferredHeight - the VerticalLayoutGroup computes from
        // its dynamic children (box buttons), so auto-resize picks up the
        // actual list height after .fam boxes returns.
        UIFactory.SetLayoutElement(_boxesListContainer,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 60, flexibleHeight: 0);

        // ---- Box management (collapsed by default) ----
        AddSpacer(_boxesPickerSection, 4);
        AddSectionHeading(_boxesPickerSection, "Manage Boxes");

        // Each of the box-management forms re-pulls the box list after submit
        // so the picker reflects the new state without a manual Refresh click.
        Action refreshBoxes = () => EnqueueOrWarn(MessageService.BCCOM_FAM_BOXES);

        CollapsibleSection.Build(_boxesPickerSection,
            title: "Create new box (.fam ab)",
            startExpanded: false,
            tooltip: "Adds an empty box you can move familiars into.",
            buildContent: content => FormBuilder.Build(content,
                title: "Create box",
                commandTemplate: ".fam ab {boxName}",
                onSubmitted: refreshBoxes,
                new TextField("boxName", "Box name", placeholder: "MyBox",
                    tooltip: "Name for the new box. Avoid spaces if possible — Bloodcraft can be picky.")));

        CollapsibleSection.Build(_boxesPickerSection,
            title: "Delete empty box (.fam db)",
            startExpanded: false,
            tooltip: "Removes a box. Bloodcraft only allows deleting boxes that are already empty — move familiars out first.",
            buildContent: content => FormBuilder.Build(content,
                title: "Delete empty box",
                commandTemplate: ".fam db {boxName}",
                onSubmitted: refreshBoxes,
                new BoxNameDropdownField("boxName", "Box name",
                    tooltip: "Pick the box to delete. Server will reject if it isn't empty — move familiars out first.")));

        CollapsibleSection.Build(_boxesPickerSection,
            title: "Rename box (.fam rb)",
            startExpanded: false,
            tooltip: "Renames an existing box.",
            buildContent: content => FormBuilder.Build(content,
                title: "Rename box",
                commandTemplate: ".fam rb {current} {newName}",
                onSubmitted: refreshBoxes,
                new BoxNameDropdownField("current", "Current name",
                    tooltip: "Pick the box to rename."),
                new TextField("newName", "New name", placeholder: "NewName",
                    tooltip: "What to rename it to.")));

        // ---------------- Content section (visible when a box is selected) ----------------
        // flexibleHeight: 0 - same reasoning as the picker section above.
        _boxesContentSection = UIFactory.CreateVerticalGroup(page, "BoxContentSection",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_boxesContentSection,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 80, flexibleHeight: 0);

        var contentActions = UIFactory.CreateHorizontalGroup(_boxesContentSection, "ContentActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(contentActions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);

        // "Back" doesn't send a command — wire manually instead of via AddCommandButton.
        var backBtn = UIFactory.CreateButton(contentActions, "BackToBoxes", "← Back");
        UIFactory.SetLayoutElement(backBtn.GameObject,
            minWidth: 70, preferredWidth: 110, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        var backText = backBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (backText != null)
        {
            backText.enableWordWrapping = false;
            backText.overflowMode = TextOverflowModes.Overflow;
            backText.alignment = TextAlignmentOptions.Center;
            backText.fontSize = Theme.ScaledUI(13);
        }
        backBtn.OnClick = OnBackToBoxesClicked;
        TooltipHover.Attach(backBtn.GameObject, "Return to the box list without changing your active box.");

        AddCommandButton(contentActions, "Reload", MessageService.BCCOM_FAM_LIST_CURRENT_BOX,
            "Re-fetch the familiars in the currently-active box (sends .fam l).");

        // Edit mode toggle. When ON, each familiar row sprouts a destructive
        // Delete button (two-click confirm). Off by default so accidental
        // clicks can't trigger destruction.
        var editToggle = UIFactory.CreateToggle(contentActions, "BoxEditMode");
        UIFactory.SetLayoutElement(editToggle.GameObject,
            minWidth: 110, preferredWidth: 120, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        editToggle.Text.text = "Edit mode";
        editToggle.Text.fontSize = Theme.ScaledUI(12);
        editToggle.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(editToggle.Text.gameObject,
            minWidth: 80, preferredWidth: 90, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        editToggle.Toggle.isOn = _boxesEditMode;
        TooltipHover.Attach(editToggle.GameObject,
            "When ON, each familiar row gets a Delete button (red, two-click confirm) for permanent removal. Move stays as the form below — Bloodcraft's .fam mb only acts on the bound familiar, so the Move workflow is multi-step.");
        editToggle.OnValueChanged += value =>
        {
            _boxesEditMode = value;
            ClearPendingDelete();
            RenderBoxContents();
            AutoResizeIfEnabled();
        };
        _boxesEditModeToggle = editToggle.Toggle;

        _boxesContentHeading = AddInfoLabel(_boxesContentSection, "ContentHeading",
            "Familiars in (none)", FontStyles.Italic, fontSize: Theme.ScaledUI(13));

        // Stays in the layout always (text empty when idle) so showing/hiding
        // the swap-confirm warning doesn't shift the familiar list and yank
        // your click target out from under your cursor. ~38px reserves room
        // for ~2 lines of wrapped text at fontSize 12.
        _boxesSwapWarning = UIFactory.CreateLabel(_boxesContentSection, "SwapWarning",
            "", TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(_boxesSwapWarning.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 38, preferredHeight: 38, flexibleHeight: 0);
        _boxesSwapWarning.TextMesh.color = new Color(1f, 0.65f, 0.45f); // warm orange
        _boxesSwapWarning.TextMesh.enableWordWrapping = true;
        _boxesSwapWarning.TextMesh.overflowMode = TextOverflowModes.Overflow;

        _boxesContentContainer = UIFactory.CreateVerticalGroup(_boxesContentSection, "BoxContentContainer",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(2, 2, 2, 2));
        // No fixed preferredHeight - auto-derives from the dynamic familiar
        // button rows so auto-resize grows the panel for tall content.
        UIFactory.SetLayoutElement(_boxesContentContainer,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 60, flexibleHeight: 0);

        // ---- Edit-familiar forms (visible only when viewing a box's contents) ----
        AddSpacer(_boxesContentSection, 6);
        AddSectionHeading(_boxesContentSection, "Edit Familiar (advanced)");

        CollapsibleSection.Build(_boxesContentSection,
            title: "Move active familiar to box (.fam mb)",
            startExpanded: false,
            tooltip: "Moves your CURRENTLY-BOUND familiar to the named box. You must bind a familiar first (click it in the list above). The active familiar stays bound after the move; use Unbind on the Familiars tab to release it.",
            buildContent: c => FormBuilder.Build(c,
                title: "Move active familiar",
                commandTemplate: ".fam mb {boxName}",
                onSubmitted: () =>
                {
                    // Re-pull the source box's familiar list so the moved
                    // familiar disappears from the visible list immediately.
                    EnqueueOrWarn(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);
                },
                new BoxNameDropdownField("boxName", "Destination box",
                    tooltip: "Pick the destination box. Must already exist; create one with the box management form on the picker view.")));

        CollapsibleSection.Build(_boxesContentSection,
            title: "Permanently delete familiar from box (.fam r)",
            startExpanded: false,
            tooltip: "DESTRUCTIVE — permanently removes a familiar from your collection. The level/prestige/shiny are gone forever; only re-unlockable via gameplay drop. Two-click the Submit button (and check the box) so you don't fire it by accident.",
            buildContent: c => FormBuilder.Build(c,
                title: "Permanently delete familiar",
                commandTemplate: ".fam r {index}",
                onSubmitted: () =>
                {
                    // Re-pull the box's familiar list after a delete so the UI
                    // reflects the new contents without the user having to
                    // hit Reload manually. The intercept timeout (~600ms)
                    // covers any server-side delay between commands.
                    EnqueueOrWarn(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);
                },
                new IntField("index", "Familiar index", min: 1, max: 999,
                    tooltip: "The 1-based index from the list above (the leading '01', '02', etc.)."),
                new BoolField("confirm", "Yes, permanently delete",
                    tooltip: "Required. Check this box to confirm the deletion is intentional.",
                    requireTrue: true)));

        UpdateBoxesSectionVisibility();
        RenderBoxList();
        RenderBoxContents();

        if (!_boxesSubscribed)
        {
            PlayerStateService.BoxListChanged     += OnBoxListChanged;
            PlayerStateService.BoxContentsChanged += OnBoxContentsChanged;
            PlayerStateService.ActiveBoxChanged   += OnActiveBoxChanged;
            _boxesSubscribed = true;
        }
    }

    private void UpdateBoxesSectionVisibility()
    {
        if (_boxesPickerSection != null)  _boxesPickerSection.SetActive(!_boxesShowingContents);
        if (_boxesContentSection != null) _boxesContentSection.SetActive(_boxesShowingContents);
    }

    private void OnBackToBoxesClicked()
    {
        ClearPendingSwap();
        ClearPendingDelete();
        _boxesShowingContents = false;
        PlayerStateService.SetActiveBox(null);
        UpdateBoxesSectionVisibility();
    }

    private void OnBoxListChanged()
    {
        if (_boxesStatusLabel != null) _boxesStatusLabel.text = "";
        RenderBoxList();
        AutoResizeIfEnabled();
    }
    private void OnBoxContentsChanged() { RenderBoxContents(); AutoResizeIfEnabled(); }
    private void OnActiveBoxChanged()
    {
        var name = PlayerStateService.ActiveBox;
        if (_boxesActiveBoxLabel != null)
            _boxesActiveBoxLabel.text = string.IsNullOrEmpty(name)
                ? "Active Box: (none selected)" : $"Active Box: {name}";
        if (_boxesContentHeading != null)
            _boxesContentHeading.text = $"Familiars in {(string.IsNullOrEmpty(name) ? "(none)" : name)}";
        RenderBoxContents();
    }

    private void RenderBoxList()
    {
        if (_boxesListContainer == null) return;
        ClearChildren(_boxesListContainer);

        var boxes = PlayerStateService.BoxList;
        if (boxes.Count == 0)
        {
            var empty = UIFactory.CreateLabel(_boxesListContainer, "BoxesEmpty",
                "(no boxes loaded yet — click Refresh Boxes)",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
            UIFactory.SetLayoutElement(empty.GameObject,
                minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
                minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
            empty.TextMesh.fontStyle = FontStyles.Italic;
            return;
        }

        foreach (var name in boxes)
        {
            var captured = name;
            var b = UIFactory.CreateButton(_boxesListContainer, $"BoxBtn_{name}", name);
            UIFactory.SetLayoutElement(b.GameObject,
                minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
                minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
            b.OnClick = () => OnBoxClicked(captured);
        }
    }

    private void RenderBoxContents()
    {
        if (_boxesContentContainer == null) return;
        ClearChildren(_boxesContentContainer);

        var active = PlayerStateService.ActiveBox;
        if (string.IsNullOrEmpty(active))
        {
            var empty = UIFactory.CreateLabel(_boxesContentContainer, "ContentEmpty",
                "(click a box above to load its familiars)",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
            UIFactory.SetLayoutElement(empty.GameObject,
                minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
                minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
            empty.TextMesh.fontStyle = FontStyles.Italic;
            return;
        }

        if (!PlayerStateService.BoxContents.TryGetValue(active, out var entries) || entries.Count == 0)
        {
            var pending = UIFactory.CreateLabel(_boxesContentContainer, "ContentPending",
                $"Loading familiars for {active}…",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
            UIFactory.SetLayoutElement(pending.GameObject,
                minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
                minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
            pending.TextMesh.fontStyle = FontStyles.Italic;
            return;
        }

        foreach (var entry in entries)
        {
            var idx = entry.Index;

            // Compose the label with level / prestige / shiny inline so the
            // user can see at a glance which familiar to bind. Format:
            //   "01  —  RoyalRavager  Lv 12  P3  ★ Storm"
            string label = $"{entry.Index:00}  —  {entry.Name}";
            if (entry.Level > 0) label += $"   Lv {entry.Level}";
            if (entry.Prestige > 0) label += $"  P{entry.Prestige}";
            if (entry.IsShiny)
            {
                label += "  ★";
                var school = entry.ShinySchool;
                if (!string.IsNullOrEmpty(school)) label += $" {school}";
            }

            if (_boxesEditMode)
            {
                // Edit-mode row: bind button + red two-click Delete button.
                var row = UIFactory.CreateHorizontalGroup(_boxesContentContainer, $"FamRow_{entry.Index}",
                    forceExpandWidth: true, forceExpandHeight: false,
                    childControlWidth: true, childControlHeight: true,
                    spacing: 4, padding: new Vector4(0, 0, 0, 0));
                UIFactory.SetLayoutElement(row,
                    minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
                    minHeight: 26, preferredHeight: 28, flexibleHeight: 0);

                var bindBtn = UIFactory.CreateButton(row, $"FamBtn_{entry.Index}", label);
                UIFactory.SetLayoutElement(bindBtn.GameObject,
                    minWidth: 240, preferredWidth: 280, flexibleWidth: 1,
                    minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
                bindBtn.OnClick = () => OnFamiliarClicked(idx);

                var delBtn = UIFactory.CreateButton(row, $"FamDel_{entry.Index}", "Delete",
                    new Color(0.55f, 0.18f, 0.18f));
                UIFactory.SetLayoutElement(delBtn.GameObject,
                    minWidth: 70, preferredWidth: 80, flexibleWidth: 0,
                    minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
                var delText = delBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
                if (delText != null) delText.fontSize = Theme.ScaledUI(12);
                TooltipHover.Attach(delBtn.GameObject,
                    "PERMANENTLY delete this familiar (.fam r). Two-click confirm — first click changes the label to 'Confirm?' and waits 3 seconds. Box record is gone forever.");
                int capturedIdx = idx;
                delBtn.OnClick = () => OnDeleteClicked(capturedIdx, delBtn, "Delete");
            }
            else
            {
                var b = UIFactory.CreateButton(_boxesContentContainer, $"FamBtn_{entry.Index}", label);
                UIFactory.SetLayoutElement(b.GameObject,
                    minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
                    minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
                b.OnClick = () => OnFamiliarClicked(idx);
            }
        }
    }

    private void OnDeleteClicked(int index, BloodCraftHub.UI.Framework.UniverseLib.UI.Models.ButtonRef btn, string originalLabel)
    {
        float now = Time.realtimeSinceStartup;
        bool armed = _pendingDeleteIndex == index && now <= _pendingDeleteDeadline;
        if (armed)
        {
            ClearPendingDelete();
            var label = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = originalLabel;
            EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_REMOVE_FORMAT, index));
            // Refresh after deletion so the row vanishes without a manual reload.
            EnqueueOrWarn(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);
            return;
        }

        _pendingDeleteIndex = index;
        _pendingDeleteDeadline = now + DELETE_CONFIRM_WINDOW_SECONDS;
        var lbl = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (lbl != null) lbl.text = "Confirm?";
    }

    private void ClearPendingDelete()
    {
        _pendingDeleteIndex = -1;
        _pendingDeleteDeadline = -1f;
    }

    private void OnBoxClicked(string boxName)
    {
        ClearPendingSwap();
        ClearPendingDelete();
        PlayerStateService.SetActiveBox(boxName);
        // .fam cb selects the box server-side, .fam l lists its contents.
        // Both fire immediately (no queue throttle) so the user sees the
        // contents view populate within ~1s of the click.
        EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_SWITCH_BOX_FORMAT, boxName));
        EnqueueOrWarn(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);

        _boxesShowingContents = true;
        UpdateBoxesSectionVisibility();
    }

    private void OnFamiliarClicked(int index)
    {
        // No active familiar → straight bind. (Familiar.Name is empty when
        // PlayerStateService has no active familiar yet.)
        bool hasActive = !string.IsNullOrEmpty(PlayerStateService.Familiar.Name);
        if (!hasActive)
        {
            ClearPendingSwap();
            EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_BIND_BY_INDEX_FORMAT, index));
            return;
        }

        // Active familiar: Bloodcraft requires destruction to switch. Two-click
        // confirm — first click arms, second click on the SAME index within
        // the window executes destroy → bind.
        float now = Time.realtimeSinceStartup;
        bool armed = _pendingSwapIndex == index && now <= _pendingSwapDeadline;
        if (armed)
        {
            ClearPendingSwap();
            // Send destroy first, then the new bind. Both are immediate
            // (MessageService.EnqueueMessage is throttle-free), so the server
            // sees them in order on the next frame's chat-message processing.
            EnqueueOrWarn(MessageService.BCCOM_FAM_UNBIND);
            EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_BIND_BY_INDEX_FORMAT, index));
            return;
        }

        // Arm pending swap. Find the entry to put its name in the warning.
        string targetName = $"#{index}";
        var active = PlayerStateService.ActiveBox;
        if (!string.IsNullOrEmpty(active)
            && PlayerStateService.BoxContents.TryGetValue(active, out var entries))
        {
            foreach (var e in entries) if (e.Index == index) { targetName = e.Name; break; }
        }

        _pendingSwapIndex = index;
        _pendingSwapDeadline = now + SWAP_CONFIRM_WINDOW_SECONDS;
        ShowSwapWarning($"Active: {PlayerStateService.Familiar.Name}. Click {targetName} again within {(int)SWAP_CONFIRM_WINDOW_SECONDS}s to unbind current and bind it. The current familiar returns to its box (level/prestige preserved); use Permanently Delete below to actually remove it from your collection.");
    }

    private void ClearPendingSwap()
    {
        _pendingSwapIndex = -1;
        _pendingSwapDeadline = -1f;
        // Empty text rather than SetActive(false) — the label stays in the
        // layout so showing/hiding the warning doesn't shift the box list.
        if (_boxesSwapWarning != null) _boxesSwapWarning.TextMesh.text = "";
    }

    private void ShowSwapWarning(string msg)
    {
        if (_boxesSwapWarning == null) return;
        _boxesSwapWarning.TextMesh.text = msg;
    }

    private static void ClearChildren(GameObject parent)
    {
        if (parent == null) return;
        var t = parent.transform;
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }
    }

    // -----------------------------------------------------------------------
    // Class tab
    // -----------------------------------------------------------------------

    private void BuildClassTab(GameObject page)
    {
        // 0.10.11: card-wrap the three sections so the tab reads as
        // grouped content instead of one stacked column.
        var currentCard = AddCard(page, "ClassCurrentCard", Theme.SystemTintExpertise);
        AddSectionHeading(currentCard, "Active Class");
        _classNameLabel  = AddInfoLabel(currentCard, "ClassName",  "—",       FontStyles.Bold,   fontSize: Theme.ScaledUI(18));
        _classLevelLabel = AddInfoLabel(currentCard, "ClassLevel", "Level —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        AddSpacer(page, 6);

        var actionsCard = AddCard(page, "ClassActionsCard");
        AddSectionHeading(actionsCard, "Actions");
        var actions = UIFactory.CreateHorizontalGroup(actionsCard, "ClassActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(actions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(actions, "List Classes",  MessageService.BCCOM_CLASS_LIST,
            "List the classes available on this server (.class l). Response appears in chat.");
        AddCommandButton(actions, "List Spells",   MessageService.BCCOM_CLASS_LIST_SPELLS,
            "List the spells granted by your current class (.class lsp). Response in chat.");
        AddCommandButton(actions, "List Stats",    MessageService.BCCOM_CLASS_LIST_STATS,
            "List the weapon/blood stat synergies for your current class (.class lst).");
        AddCommandButton(actions, "Toggle Shift",  MessageService.BCCOM_CLASS_TOGGLE_SHIFT,
            "Toggle whether the class spell occupies your shift-slot (.class shift).");

        AddSpacer(page, 6);

        var changeCard = AddCard(page, "ClassChangeCard");
        AddSectionHeading(changeCard, "Change Class");
        CollapsibleSection.Build(changeCard,
            title: "Select / change your class (.class s)",
            startExpanded: false,
            tooltip: "Pick a class from the dropdown and Submit. Some servers may rate-limit class changes or require a cost — Bloodcraft replies in chat with success or the rejection reason.",
            buildContent: c => FormBuilder.Build(c,
                title: "Select class",
                commandTemplate: ".class s {class}",
                new EnumField<PlayerStateService.BloodcraftClassChoice>("class", "Class",
                    defaultValue: PlayerStateService.BloodcraftClassChoice.BloodKnight,
                    tooltip: "The class you want active. Bloodcraft's six built-in classes are listed.")));

        CollapsibleSection.Build(changeCard,
            title: "Choose class shift spell (.class csp)",
            startExpanded: false,
            tooltip: "Set which of your class's spells occupies your shift slot. Use 'List Spells' above to see the available spells (numbered) for your current class, then enter the spell's 1-based index here.",
            buildContent: c => FormBuilder.Build(c,
                title: "Choose shift spell",
                commandTemplate: ".class csp {index}",
                new IntField("index", "Spell #", min: 1, max: 32,
                    tooltip: "1-based index of the class spell. Run 'List Spells' to see what each number maps to before submitting.")));

        AddDivider(changeCard);
        AddBodyText(changeCard,
            $"Tip: {Mono("List Spells")} / {Mono("List Stats")} above describe what each class grants before you commit.");

        RenderClass(PlayerStateService.Experience);
        if (!_classSubscribed)
        {
            PlayerStateService.ExperienceChanged += OnExperienceChangedForClass;
            _classSubscribed = true;
        }
    }

    private void OnExperienceChangedForClass() => RenderClass(PlayerStateService.Experience);

    private void RenderClass(PlayerStateService.ExperienceState s)
    {
        if (_classNameLabel == null) return;
        _classNameLabel.text = s.Class == PlayerStateService.PlayerClass.None
            ? "(no class selected)"
            : s.Class.ToString();
        _classLevelLabel.text = s.Class == PlayerStateService.PlayerClass.None
            ? "Use .class s <Class> in chat to choose one."
            : $"Player Level {s.Level}   Prestige {s.Prestige}";
    }

    // -----------------------------------------------------------------------
    // Weapon Expertise tab
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // 0.10.0: V-Bloods tab. Collection tracker for the 65 named V-Bloods
    // listed in VBloodRegistry. Rows are built once at tab-construct time,
    // refreshed in place when PlayerStateService.VBloodCollection changes or
    // when the scanner ticks. Filter buttons re-show/hide the existing row
    // GameObjects rather than rebuilding — keeps scroll position stable
    // across filter switches.
    //
    // Status chips per row:
    //   B  — basic variant captured
    //   S  — basic + shiny captured
    //   P  — Primal variant captured
    //   PS — Primal + shiny captured
    //
    // Each chip is one of:
    //   green "[B]" = captured     gray "[B]" = not captured (yet)
    // We pack all four chips into a single TMP label using <color> tags so
    // each row only needs one label instead of four, keeping layout cheap.
    // -----------------------------------------------------------------------
    // 0.10.9: V-Bloods tab rebuilt around the box-sweep scanner. One row
    // per CAPTURED VARIANT (basic / shiny / primal / primal-shiny) with
    // explicit per-variant Summon. Pre-0.10.9 the chip view rendered a
    // single row per V-Blood name with 4 status chips and a single
    // ambiguous Summon button — users had no way to choose which variant
    // to bind. The 0.10.7 "Instances" view sourced data from BoxContents
    // (required manual box navigation); the new scanner populates
    // BoxContents AND a precise per-variant index, so the two views are
    // collapsed back into one.
    private void BuildVBloodsTab(GameObject page)
    {
        // 0.10.10: every section wrapped in a card so progress / filter
        // controls aren't flush with the panel edge anymore (friend-test
        // feedback: "X / Y captured is flush left against the border").

        // Header card — Section heading + help text inside.
        var headerCard = AddCard(page, "VBloodsHeaderCard");
        AddSectionHeading(headerCard, "V-Blood Collection");
        AddBodyText(headerCard,
            "One row per captured V-Blood variant (basic / shiny / primal / primal shiny). " +
            $"Scan walks each of your familiar boxes once via {Mono(".fam boxes")} + {Mono(".fam l")} — your active box is restored when it finishes. " +
            "Filter shows All / Captured / Missing / Shiny only.");

        AddSpacer(page, 6);

        // Progress + Scan card.
        var progressCard = AddCard(page, "VBloodsProgressCard");
        var headerRow = UIFactory.CreateHorizontalGroup(progressCard, "VBloodsHeader",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(headerRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 30, preferredHeight: 32, flexibleHeight: 0);

        _vbProgressLabel = AddInfoLabel(headerRow, "VBProgress", "0 / 65 captured",
            FontStyles.Bold, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(_vbProgressLabel.gameObject,
            minWidth: 180, preferredWidth: 240, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 28, flexibleHeight: 0);

        _vbScanButton = UIFactory.CreateButton(headerRow, "VBScanBtn", "Scan all");
        UIFactory.SetLayoutElement(_vbScanButton.GameObject,
            minWidth: 90, preferredWidth: 110, flexibleWidth: 0,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        var scanBtnText = _vbScanButton.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (scanBtnText != null) { scanBtnText.fontSize = Theme.ScaledUI(12); scanBtnText.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(_vbScanButton.GameObject,
            "Sweep every familiar box via .fam boxes + .fam cb + .fam l — populates the V-Blood collection AND per-box contents in one pass. ~30–60s. Your active box is restored when the sweep finishes. Cancel halts the sweep mid-flight.");
        _vbScanButton.OnClick = () =>
        {
            if (VBloodScannerService.Scanning) VBloodScannerService.CancelScan();
            else                                VBloodScannerService.StartScan();
            RefreshVBScanButton();
        };

        _vbScanStatusLabel = AddInfoLabel(progressCard, "VBScanStatus", "",
            FontStyles.Italic, fontSize: Theme.ScaledUI(11));
        _vbScanStatusLabel.gameObject.SetActive(false);

        AddSpacer(page, 6);

        // Filter + Sort card.
        var filterCard = AddCard(page, "VBloodsFilterCard");
        var filterRow = UIFactory.CreateHorizontalGroup(filterCard, "VBloodsFilters",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(filterRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        AddVBFilterButton(filterRow, "All",      VBloodFilter.All);
        AddVBFilterButton(filterRow, "Captured", VBloodFilter.Captured);
        AddVBFilterButton(filterRow, "Missing",  VBloodFilter.Missing);
        AddVBFilterButton(filterRow, "Shiny",    VBloodFilter.ShinyOnly);

        _vbSortButton = UIFactory.CreateButton(filterRow, "VBSortBtn", FormatVBSortButtonText());
        UIFactory.SetLayoutElement(_vbSortButton.GameObject,
            minWidth: 100, preferredWidth: 130, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        var sortTxt = _vbSortButton.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (sortTxt != null) { sortTxt.fontSize = Theme.ScaledUI(12); sortTxt.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(_vbSortButton.GameObject,
            "Cycle sort order: Default (alpha by name) → Alphabetical → By level (descending across all captured instances) → By region.");
        _vbSortButton.OnClick = () =>
        {
            var current = Config.Settings.FamiliarSortOrderSetting;
            var next = current switch
            {
                Config.Settings.FamiliarSortOrder.Default      => Config.Settings.FamiliarSortOrder.Alphabetical,
                Config.Settings.FamiliarSortOrder.Alphabetical => Config.Settings.FamiliarSortOrder.Level,
                Config.Settings.FamiliarSortOrder.Level        => Config.Settings.FamiliarSortOrder.Location,
                Config.Settings.FamiliarSortOrder.Location     => Config.Settings.FamiliarSortOrder.Default,
                _                                               => Config.Settings.FamiliarSortOrder.Default,
            };
            Config.Settings.SetFamiliarSortOrder(next);
            RefreshVBSortButtonText();
            RebuildVBRows();
            // The overlay also reads this setting; ping it so its list re-orders too.
            try { Plugin.UIManager?.FamiliarBrowserOverlay?.NotifySortOrderChanged(); }
            catch { /* overlay may not be open */ }
        };

        AddSpacer(page, 6);

        // Rows card — section heading + column-header row + the dynamic
        // rows themselves. Pre-0.10.10 the rows lived flush against the
        // panel edge and shifted column widths because the shiny-school
        // chip was conditionally rendered.
        var rowsCard = AddCard(page, "VBloodsRowsCard", padding: 4, innerSpacing: 2);
        AddSectionHeading(rowsCard, "V-Bloods");
        BuildVBColumnHeader(rowsCard); // 0.10.10: column labels above the rows for clarity

        // Rows are dynamic — rebuilt each time the collection / filter / sort
        // changes. Cheap; the registry caps at ~65 names plus 0..N variants
        // each (typical owned ~10-30 instances, so list is small).
        _vbRowContainer = UIFactory.CreateVerticalGroup(rowsCard, "VBloodRows",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 2, 2));
        UIFactory.SetLayoutElement(_vbRowContainer,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 60, flexibleHeight: 0);

        if (!_vbSubscribed)
        {
            PlayerStateService.VBloodCollectionChanged += OnVBloodCollectionChanged;
            VBloodScannerService.ScanStateChanged      += OnVBloodScanStateChanged;
            _vbSubscribed = true;
        }

        // 0.10.10: auto-scan is now opt-in. Friend-testing 0.10.9: the scan
        // walks the box list and the `.fam cb`/`.fam l` confirmations leaked
        // into chat (suppression-flag gap fixed below, but the unannounced
        // box-switching was still surprising). Pre-0.10.10 we triggered
        // StartScan unconditionally when VBloodCollection was empty; now
        // the user clicks "Scan all" (or can opt back in via Display
        // Settings → "Auto-scan on tab open").
        if (Config.Settings.AutoScanVBloodsOnTabOpen
            && PlayerStateService.VBloodCollection.Count == 0
            && !VBloodScannerService.Scanning)
        {
            System.Action deferStart = null;
            deferStart = () =>
            {
                BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Remove(deferStart);
                if (MessageService.IsInitialized && PlayerStateService.VBloodCollection.Count == 0)
                    VBloodScannerService.StartScan();
                RefreshVBScanButton();
            };
            BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Add(deferStart);
        }

        RebuildVBRows();
        RefreshVBHeader();
        RefreshVBScanButton();
    }

    /// <summary>0.10.9: subscribed to VBloodCollectionChanged. Cheap, but
    /// rebuilds the entire row list — fine because typical scenarios have
    /// at most a few dozen instances.</summary>
    private void OnVBloodCollectionChanged()
    {
        RebuildVBRows();
        RefreshVBHeader();
    }

    private void OnVBloodScanStateChanged()
    {
        RefreshVBScanButton();
        RefreshVBHeader();
    }

    private void RefreshVBScanButton()
    {
        if (_vbScanButton == null) return;
        var t = _vbScanButton.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = VBloodScannerService.Scanning ? "Cancel" : "Scan all";
    }

    private void RefreshVBHeader()
    {
        if (_vbProgressLabel == null) return;
        int total = Resources.VBloodRegistry.All.Length;
        int capturedNames = 0, primalNames = 0, shinyInstances = 0, totalInstances = 0;
        foreach (var slot in PlayerStateService.VBloodCollection.Values)
        {
            if (slot.Instances == null || slot.Instances.Count == 0) continue;
            capturedNames++;
            if (slot.HasPrimal || slot.HasPrimalShiny) primalNames++;
            foreach (var i in slot.Instances)
            {
                totalInstances++;
                if (i.IsShiny) shinyInstances++;
            }
        }
        string text = $"{capturedNames} / {total} captured  ·  {totalInstances} instance{(totalInstances == 1 ? "" : "s")}";
        if (primalNames    > 0) text += $"  ·  {primalNames} primal";
        if (shinyInstances > 0) text += $"  ·  {shinyInstances} shiny";
        _vbProgressLabel.text = text;

        if (_vbScanStatusLabel != null)
        {
            if (VBloodScannerService.Scanning)
            {
                string box = VBloodScannerService.CurrentBoxBeingScanned;
                string suffix = string.IsNullOrEmpty(box) ? "" : $" — {box}";
                _vbScanStatusLabel.text = $"Scanning… box {VBloodScannerService.CompletedForCurrentScan + 1} / {VBloodScannerService.TotalForCurrentScan}{suffix}";
                if (!_vbScanStatusLabel.gameObject.activeSelf) _vbScanStatusLabel.gameObject.SetActive(true);
            }
            else if (_vbScanStatusLabel.text != null && _vbScanStatusLabel.text.StartsWith("Scanning"))
            {
                _vbScanStatusLabel.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>0.10.9: full teardown + rebuild. Sorts rows per the
    /// FamiliarSortOrder setting and applies the current filter. Captured
    /// variants render with full detail + Summon; un-captured V-Bloods (in
    /// All / Missing filter) render as a muted placeholder row with no
    /// Summon button.</summary>
    private void RebuildVBRows()
    {
        if (_vbRowContainer == null) return;

        // Tear down all existing rows.
        for (int i = _vbRowContainer.transform.childCount - 1; i >= 0; --i)
        {
            var child = _vbRowContainer.transform.GetChild(i);
            if (child == null) continue;
            UnityEngine.Object.Destroy(child.gameObject);
        }

        // Build the row list. Each row is either a captured variant
        // (VBRow with Name + variant tag + stats + box + Summon) or a
        // missing-name placeholder (VBRowMissing — name only, dim).
        var rows = new List<VBRowSpec>();
        bool includeMissing = _vbFilter == VBloodFilter.All || _vbFilter == VBloodFilter.Missing;
        bool includeCaptured = _vbFilter != VBloodFilter.Missing;
        bool shinyOnly = _vbFilter == VBloodFilter.ShinyOnly;

        if (includeCaptured)
        {
            foreach (var kv in PlayerStateService.VBloodCollection)
            {
                var slot = kv.Value;
                if (slot.Instances == null) continue;
                foreach (var inst in slot.Instances)
                {
                    if (shinyOnly && !inst.IsShiny) continue;
                    if (_vbFilter == VBloodFilter.Captured && shinyOnly) continue; // already shiny path
                    rows.Add(new VBRowSpec { Name = slot.Name, Instance = inst, IsMissing = false });
                }
            }
        }
        if (includeMissing)
        {
            foreach (var name in Resources.VBloodRegistry.All)
            {
                if (PlayerStateService.VBloodCollection.TryGetValue(name, out var slot)
                    && slot.Instances != null && slot.Instances.Count > 0) continue;
                rows.Add(new VBRowSpec { Name = name, IsMissing = true });
            }
        }

        SortVBRows(rows);

        if (rows.Count == 0)
        {
            var empty = UIFactory.CreateLabel(_vbRowContainer, "VBEmpty",
                _vbFilter == VBloodFilter.Missing
                    ? "Nothing missing — every registered V-Blood has at least one capture. Nice work."
                    : "No captures match the current filter. Try Scan or switch filter to All.",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
            UIFactory.SetLayoutElement(empty.GameObject,
                minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
                minHeight: 28, preferredHeight: 32, flexibleHeight: 0);
            empty.TextMesh.fontStyle = FontStyles.Italic;
            return;
        }

        foreach (var spec in rows)
        {
            if (spec.IsMissing) BuildVBMissingRow(_vbRowContainer, spec.Name);
            else                BuildVBVariantRow(_vbRowContainer, spec.Name, spec.Instance);
        }
    }

    private struct VBRowSpec
    {
        public string Name;
        public PlayerStateService.VBloodInstance Instance;
        public bool   IsMissing;
    }

    private static void SortVBRows(List<VBRowSpec> rows)
    {
        var mode = Config.Settings.FamiliarSortOrderSetting;
        // Always: captured before missing in the All filter so the user
        // sees their actual collection at the top.
        rows.Sort((a, b) =>
        {
            int missCmp = a.IsMissing.CompareTo(b.IsMissing);
            if (missCmp != 0) return missCmp; // false < true → captured first
            switch (mode)
            {
                case Config.Settings.FamiliarSortOrder.Alphabetical:
                {
                    int c = string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return CompareVariantOrder(a, b);
                }
                case Config.Settings.FamiliarSortOrder.Level:
                {
                    if (a.IsMissing || b.IsMissing)
                        return string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
                    int c = b.Instance.Level.CompareTo(a.Instance.Level);
                    if (c != 0) return c;
                    c = b.Instance.Prestige.CompareTo(a.Instance.Prestige);
                    if (c != 0) return c;
                    return string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
                }
                case Config.Settings.FamiliarSortOrder.Location:
                {
                    int ra = Resources.VBloodRegistry.RegionOrderFor(a.Name);
                    int rb = Resources.VBloodRegistry.RegionOrderFor(b.Name);
                    if (ra != rb) return ra.CompareTo(rb);
                    int c = string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return CompareVariantOrder(a, b);
                }
                case Config.Settings.FamiliarSortOrder.Default:
                default:
                {
                    int c = string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return CompareVariantOrder(a, b);
                }
            }
        });
    }

    /// <summary>Stable variant ordering within one V-Blood name: basic →
    /// shiny → primal → primal-shiny. Keeps the chip view readable when
    /// the player has 2+ variants of the same name back-to-back.</summary>
    private static int CompareVariantOrder(VBRowSpec a, VBRowSpec b)
    {
        if (a.IsMissing || b.IsMissing) return 0;
        int va = VariantOrder(a.Instance);
        int vb = VariantOrder(b.Instance);
        return va.CompareTo(vb);
    }
    private static int VariantOrder(PlayerStateService.VBloodInstance i)
        => (i.IsPrimal ? 2 : 0) + (i.IsShiny ? 1 : 0); // basic 0, shiny 1, primal 2, primal-shiny 3

    private static string FormatVBSortButtonText()
    {
        var mode = Config.Settings.FamiliarSortOrderSetting;
        return mode switch
        {
            Config.Settings.FamiliarSortOrder.Alphabetical => "Sort: Alpha",
            Config.Settings.FamiliarSortOrder.Level        => "Sort: Level",
            Config.Settings.FamiliarSortOrder.Location     => "Sort: Region",
            _                                              => "Sort: Default",
        };
    }

    private void RefreshVBSortButtonText()
    {
        if (_vbSortButton == null) return;
        var t = _vbSortButton.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = FormatVBSortButtonText();
    }

    private void AddVBFilterButton(GameObject parent, string label, VBloodFilter mode)
    {
        var btn = UIFactory.CreateButton(parent, $"VBFilter_{label}", label);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 70, preferredWidth: 90, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledUI(12); t.alignment = TextAlignmentOptions.Center; }
        btn.OnClick = () =>
        {
            _vbFilter = mode;
            RebuildVBRows();
        };
    }

    // Variant-tag color palette. Bright captured colors stand out against
    // the muted "missing" rows; primal gets a warmer gold-orange and shiny
    // / primal-shiny inherit cyan to echo the school-color convention used
    // in the Boxes tab. Picked for contrast against the panel background,
    // not lifted from Theme.Level* (those are for system-progress tinting).
    private const string VB_VARIANT_BASIC_HEX        = "#7CDA7C"; // green
    private const string VB_VARIANT_SHINY_HEX        = "#9AE0FF"; // light cyan
    private const string VB_VARIANT_PRIMAL_HEX       = "#FFC066"; // gold-orange
    private const string VB_VARIANT_PRIMAL_SHINY_HEX = "#FFA0F0"; // pink — Bloodcraft's own shiny-marker hue
    private const string VB_MISSING_HEX              = "#888888"; // mid-grey

    private static string VariantTag(PlayerStateService.VBloodInstance i)
    {
        string label = i.IsPrimal
            ? (i.IsShiny ? "PS" : "P")
            : (i.IsShiny ? "S"  : "B");
        string hex = i.IsPrimal
            ? (i.IsShiny ? VB_VARIANT_PRIMAL_SHINY_HEX : VB_VARIANT_PRIMAL_HEX)
            : (i.IsShiny ? VB_VARIANT_SHINY_HEX        : VB_VARIANT_BASIC_HEX);
        return $"<color={hex}><b>[{label}]</b></color>";
    }

    // 0.10.10: strict column widths so the rows form a real table. Pre-
    // 0.10.10 the shiny-school chip was OMITTED for non-shiny rows, which
    // collapsed that column — name and box shifted left and the layout
    // looked ragged. Every row now reserves all columns even when blank;
    // BuildVBColumnHeader uses these exact constants too.
    private const int VB_COL_TAG_W     = 36;
    private const int VB_COL_LV_W      = 70;
    private const int VB_COL_SHINY_W   = 96;
    private const int VB_COL_BOX_W     = 100;
    private const int VB_COL_SUMMON_W  = 78;
    private const int VB_ROW_SPACING   = 6;

    /// <summary>0.10.10: tabular column-header row that lives just above
    /// the rows. Uses the same widths as BuildVBVariantRow /
    /// BuildVBMissingRow so the visual columns line up.</summary>
    private static void BuildVBColumnHeader(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "VBColHeader",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: VB_ROW_SPACING, padding: new Vector4(2, 2, 1, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);

        void Col(string label, int w, TextAlignmentOptions align)
        {
            var l = UIFactory.CreateLabel(row, $"H_{label}",
                $"<color={Theme.MutedBodyHex}>{label}</color>",
                align, color: null, fontSize: Theme.ScaledUI(10));
            UIFactory.SetLayoutElement(l.GameObject,
                minWidth: w, preferredWidth: w, flexibleWidth: 0,
                minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
            l.TextMesh.enableWordWrapping = false;
            l.TextMesh.overflowMode = TextOverflowModes.Overflow;
            l.TextMesh.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;
        }

        Col("Type", VB_COL_TAG_W, TextAlignmentOptions.Midline);

        // Name column is flex so it absorbs the leftover space.
        var nameHdr = UIFactory.CreateLabel(row, "H_Name",
            $"<color={Theme.MutedBodyHex}>Name</color>",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(10));
        UIFactory.SetLayoutElement(nameHdr.GameObject,
            minWidth: 100, preferredWidth: 140, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        nameHdr.TextMesh.enableWordWrapping = false;
        nameHdr.TextMesh.overflowMode = TextOverflowModes.Overflow;
        nameHdr.TextMesh.fontStyle = FontStyles.Bold | FontStyles.SmallCaps;

        Col("Lv",     VB_COL_LV_W,     TextAlignmentOptions.Midline);
        Col("Shiny",  VB_COL_SHINY_W,  TextAlignmentOptions.Midline);
        Col("Box",    VB_COL_BOX_W,    TextAlignmentOptions.MidlineLeft);
        Col("",       VB_COL_SUMMON_W, TextAlignmentOptions.Midline); // summon column has no header label
    }

    private void BuildVBVariantRow(GameObject parent, string name, PlayerStateService.VBloodInstance instance)
    {
        var row = UIFactory.CreateHorizontalGroup(parent,
            $"VBRow_{name}_{(instance.IsPrimal ? 'P' : 'B')}{(instance.IsShiny ? 'S' : '_')}_{instance.Box}_{instance.Index}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: VB_ROW_SPACING, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        // Type column — variant tag.
        var tag = UIFactory.CreateLabel(row, "Tag", VariantTag(instance),
            TextAlignmentOptions.Midline, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(tag.GameObject,
            minWidth: VB_COL_TAG_W, preferredWidth: VB_COL_TAG_W, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        tag.TextMesh.enableWordWrapping = false;
        tag.TextMesh.overflowMode = TextOverflowModes.Overflow;

        // Name column — flex.
        var nameLbl = AddInfoLabel(row, "Name", name,
            FontStyles.Normal, fontSize: Theme.ScaledUI(12));
        nameLbl.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(nameLbl.gameObject,
            minWidth: 100, preferredWidth: 140, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        nameLbl.enableWordWrapping = false;
        nameLbl.overflowMode = TextOverflowModes.Ellipsis;

        // Level column — fixed width, center-aligned.
        string statsTxt = instance.Prestige > 0
            ? $"Lv {instance.Level}  Pr {instance.Prestige}"
            : (instance.Level > 0 ? $"Lv {instance.Level}" : "—");
        var statsLbl = AddInfoLabel(row, "Stats", statsTxt,
            FontStyles.Normal, fontSize: Theme.ScaledUI(11));
        statsLbl.alignment = TextAlignmentOptions.Midline;
        UIFactory.SetLayoutElement(statsLbl.gameObject,
            minWidth: VB_COL_LV_W, preferredWidth: VB_COL_LV_W, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        statsLbl.enableWordWrapping = false;
        statsLbl.overflowMode = TextOverflowModes.Overflow;

        // Shiny column — ALWAYS rendered (with "—" placeholder when not
        // shiny) so the box and Summon columns stay column-aligned across
        // rows. Pre-0.10.10 the column was omitted entirely when not
        // shiny, which collapsed the layout for that row only.
        string schoolTxt = instance.IsShiny
            ? (string.IsNullOrEmpty(instance.ShinySchool) ? "★" : $"★ {instance.ShinySchool}")
            : $"<color={Theme.MutedBodyHex}>—</color>";
        var schoolLbl = UIFactory.CreateLabel(row, "School", schoolTxt,
            TextAlignmentOptions.Midline, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(schoolLbl.GameObject,
            minWidth: VB_COL_SHINY_W, preferredWidth: VB_COL_SHINY_W, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        schoolLbl.TextMesh.fontStyle = FontStyles.Italic;
        schoolLbl.TextMesh.enableWordWrapping = false;
        schoolLbl.TextMesh.overflowMode = TextOverflowModes.Overflow;

        // Box column.
        var boxLbl = AddInfoLabel(row, "Box", instance.Box,
            FontStyles.Italic, fontSize: Theme.ScaledUI(11));
        boxLbl.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(boxLbl.gameObject,
            minWidth: VB_COL_BOX_W, preferredWidth: VB_COL_BOX_W, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        boxLbl.enableWordWrapping = false;
        boxLbl.overflowMode = TextOverflowModes.Ellipsis;

        // Summon button — fixed column.
        var summonBtn = UIFactory.CreateButton(row, "Summon", "Summon");
        UIFactory.SetLayoutElement(summonBtn.GameObject,
            minWidth: VB_COL_SUMMON_W, preferredWidth: VB_COL_SUMMON_W, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        var sbt = summonBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (sbt != null) { sbt.fontSize = Theme.ScaledUI(11); sbt.alignment = TextAlignmentOptions.Center; }
        string capturedName    = name;
        bool   capturedShiny   = instance.IsShiny;
        bool   capturedPrimal  = instance.IsPrimal;
        summonBtn.OnClick = () => OnVBSummonVariantClicked(capturedName, capturedShiny, capturedPrimal);
    }

    private void BuildVBMissingRow(GameObject parent, string name)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"VBMissingRow_{name}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: VB_ROW_SPACING, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);

        // Type column.
        var tag = UIFactory.CreateLabel(row, "Tag", $"<color={VB_MISSING_HEX}>—</color>",
            TextAlignmentOptions.Midline, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(tag.GameObject,
            minWidth: VB_COL_TAG_W, preferredWidth: VB_COL_TAG_W, flexibleWidth: 0,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);

        // Name column.
        var nameLbl = UIFactory.CreateLabel(row, "Name",
            $"<color={VB_MISSING_HEX}>{name}</color>",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(nameLbl.GameObject,
            minWidth: 100, preferredWidth: 140, flexibleWidth: 1,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        nameLbl.TextMesh.fontStyle = FontStyles.Italic;
        nameLbl.TextMesh.enableWordWrapping = false;
        nameLbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;

        // Empty Lv / Shiny / Box columns reserved so the row column-aligns
        // with captured rows even when there's no data to display.
        void EmptyCol(string label, int w)
        {
            var l = UIFactory.CreateLabel(row, label,
                $"<color={VB_MISSING_HEX}>—</color>",
                TextAlignmentOptions.Midline, color: null, fontSize: Theme.ScaledUI(11));
            UIFactory.SetLayoutElement(l.GameObject,
                minWidth: w, preferredWidth: w, flexibleWidth: 0,
                minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
            l.TextMesh.fontStyle = FontStyles.Italic;
        }
        EmptyCol("LvEmpty",    VB_COL_LV_W);
        EmptyCol("ShinyEmpty", VB_COL_SHINY_W);
        EmptyCol("BoxEmpty",   VB_COL_BOX_W);

        // Status label in the Summon-button column slot.
        var statusLbl = UIFactory.CreateLabel(row, "Status",
            $"<color={VB_MISSING_HEX}>not captured</color>",
            TextAlignmentOptions.Midline, color: null, fontSize: Theme.ScaledUI(10));
        UIFactory.SetLayoutElement(statusLbl.GameObject,
            minWidth: VB_COL_SUMMON_W, preferredWidth: VB_COL_SUMMON_W, flexibleWidth: 0,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        statusLbl.TextMesh.fontStyle = FontStyles.Italic;
        statusLbl.TextMesh.enableWordWrapping = false;
        statusLbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    private void OnVBSummonVariantClicked(string name, bool isShiny, bool isPrimal)
    {
        if (!_vbSummonStatusSubscribed)
        {
            Services.VBloodSummonService.StatusChanged += OnVBSummonStatusChanged;
            _vbSummonStatusSubscribed = true;
        }
        Services.VBloodSummonService.SummonVariant(name, isShiny, isPrimal);
    }

    private bool _vbSummonStatusSubscribed;

    private void OnVBSummonStatusChanged(string status)
    {
        if (_vbScanStatusLabel == null) return;
        _vbScanStatusLabel.text = status;
        if (!_vbScanStatusLabel.gameObject.activeSelf) _vbScanStatusLabel.gameObject.SetActive(true);
    }

    private void BuildExpertiseTab(GameObject page)
    {
        // 0.10.9: card-wrapped current state for visual breathing room.
        var currentCard = AddCard(page, "WepCurrentCard", Theme.SystemTintExpertise);
        AddSectionHeading(currentCard, "Current Weapon Expertise");

        _wepTypeLabel     = AddInfoLabel(currentCard, "WepType",     "—",                  FontStyles.Bold,   fontSize: Theme.ScaledUI(18));
        _wepProgressLabel = AddInfoLabel(currentCard, "WepProgress", "Level —",            FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        _wepBonusLabel    = AddInfoLabel(currentCard, "WepBonus",    "Bonus Stats: —",     FontStyles.Normal, fontSize: Theme.ScaledUI(13));
        _wepStatsValuesLabel = AddInfoLabel(currentCard, "WepStatsValues", "", FontStyles.Italic, fontSize: Theme.ScaledUI(12));
        _wepStatsValuesLabel.gameObject.SetActive(false);
        _wepStatsValuesLabel.enableWordWrapping = true;
        _wepStatsValuesLabel.overflowMode = TextOverflowModes.Overflow;

        AddSpacer(page, 6);

        var actionsCard = AddCard(page, "WepActionsCard");
        AddSectionHeading(actionsCard, "Actions");
        var actions = UIFactory.CreateHorizontalGroup(actionsCard, "WepActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(actions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(actions, "Refresh",     MessageService.BCCOM_WEP_GET,
            "Display your current weapon expertise details in chat (.wep get).");
        AddCommandButton(actions, "List Weps",   MessageService.BCCOM_WEP_LIST,
            "List all weapon expertise types tracked by Bloodcraft (.wep l).");
        AddCommandButton(actions, "List Stats",  MessageService.BCCOM_WEP_LIST_STATS,
            "List the weapon-stat bonuses you can choose between (.wep lst).");
        AddCommandButton(actions, "Reset Stats", MessageService.BCCOM_WEP_RESET_STATS,
            "Reset your chosen bonus stats for the current weapon (.wep rst).");
        AddCommandButton(actions, "Lock Spells", MessageService.BCCOM_WEP_LOCK_SPELLS,
            "Lock in the next spells you equip to use as your unarmed slot spells (.wep locksp).");

        AddSpacer(page, 6);

        var statCard = AddCard(page, "WepStatPickerCard");
        AddSectionHeading(statCard, "Choose Bonus Stat");
        CollapsibleSection.Build(statCard,
            title: "Set bonus stat for a weapon (.wep cst)",
            startExpanded: false,
            tooltip: "Pick the weapon type AND the bonus stat you want to lock in for it. Bloodcraft applies the chosen stat scaled by your expertise level for that weapon. Each weapon tracks up to 3 chosen stats; submit again to add more (or use Reset Stats to clear).",
            buildContent: c => FormBuilder.Build(c,
                title: "Set bonus stat",
                commandTemplate: ".wep cst {weapon} {stat}",
                onSubmitted: () => EnqueueOrWarn(MessageService.BCCOM_WEP_GET),
                new EnumField<PlayerStateService.WeaponType>("weapon", "Weapon",
                    defaultValue: PlayerStateService.WeaponType.Sword,
                    tooltip: "Which weapon type the chosen bonus stat applies to."),
                new EnumIndexField<PlayerStateService.WeaponBonusStat>("stat", "Bonus stat",
                    defaultValue: PlayerStateService.WeaponBonusStat.PhysicalPower,
                    tooltip: "The stat to enhance. Bloodcraft expects a 1-12 index; the dropdown sends it for you.")));

        AddDivider(statCard);
        AddBodyText(statCard,
            $"Bloodcraft only streams the EQUIPPED weapon's expertise. Switch weapons to see each one's level + chosen stats above. The {Mono(".wep l")} button lists every weapon type you can level.");

        RenderExpertise(PlayerStateService.Expertise);
        if (!_wepSubscribed)
        {
            PlayerStateService.ExpertiseChanged += OnExpertiseChanged;
            _wepSubscribed = true;
        }
        if (!_wepLastResponseSubscribed)
        {
            PlayerStateService.LastResponseChanged += OnLastResponseChangedForWep;
            _wepLastResponseSubscribed = true;
        }
        // Seed from any LastResponse already on file in case the user opens
        // this tab AFTER a .wep get fired (e.g. the overlay's bonus-stats
        // ticker has been running, or they clicked Refresh and switched away
        // before the reply landed).
        var seed = PlayerStateService.LastResponse;
        if (seed.Command == ".wep get" && seed.Lines != null && seed.Lines.Count > 0)
        {
            _cachedWepGetLines = new System.Collections.Generic.List<string>(seed.Lines);
            RenderWepStatsValues();
        }
    }

    private void OnLastResponseChangedForWep()
    {
        var r = PlayerStateService.LastResponse;
        if (r.Command != ".wep get" || r.Lines == null) return;
        _cachedWepGetLines = new System.Collections.Generic.List<string>(r.Lines);
        RenderWepStatsValues();
        AutoResizeIfEnabled();
    }

    // 0.9.6: render the cached .wep get reply (raw color-tagged lines) into
    // the stats-values label. Hidden while we have no data so the tab stays
    // visually tidy on cold opens.
    private void RenderWepStatsValues()
    {
        if (_wepStatsValuesLabel == null) return;
        if (_cachedWepGetLines == null || _cachedWepGetLines.Count == 0)
        {
            if (_wepStatsValuesLabel.gameObject.activeSelf) _wepStatsValuesLabel.gameObject.SetActive(false);
            return;
        }
        _wepStatsValuesLabel.text = "• " + string.Join("\n• ", _cachedWepGetLines);
        if (!_wepStatsValuesLabel.gameObject.activeSelf) _wepStatsValuesLabel.gameObject.SetActive(true);
    }

    private void OnExpertiseChanged()
    {
        var e = PlayerStateService.Expertise;
        // 0.10.2: weapon-swap detection — fast-refresh the bonus-stat values
        // by zeroing the auto-fetch cooldown so the next TickTabAutoRefresh
        // sends .wep get immediately, instead of waiting up to 10s.
        if (_wepTabTypeBaseline && e.Type != _wepTabLastType)
        {
            _lastWepAutoFetchAt = 0;
            _cachedWepGetLines = null; // hide stale values while reply is in-flight
            if (_wepStatsValuesLabel != null) RenderWepStatsValues();
        }
        _wepTabLastType = e.Type;
        _wepTabTypeBaseline = true;
        RenderExpertise(e);
    }

    // -----------------------------------------------------------------------
    // Blood Legacy tab
    //
    // Mirrors the Weapon Expertise tab structure: a live "current blood" panel
    // up top fed by PlayerStateService.Legacy (already streamed via Eclipse),
    // chat-action buttons in the middle, and forms at the bottom for setting
    // the bonus stat per blood type and querying any blood's current state.
    //
    // Unlike .wep get (equipped-weapon-only), .bl get [BloodType] accepts an
    // explicit type argument server-side, so we expose a "Show info for blood"
    // form that lets you query a non-current blood's level + chosen stats.
    // -----------------------------------------------------------------------

    private void BuildBloodLegacyTab(GameObject page)
    {
        // 0.10.9: tinted card for current legacy state.
        var currentCard = AddCard(page, "BlCurrentCard", Theme.SystemTintLegacy);
        AddSectionHeading(currentCard, "Current Blood Legacy");

        _blTypeLabel     = AddInfoLabel(currentCard, "BlType",     "—",                  FontStyles.Bold,   fontSize: Theme.ScaledUI(18));
        _blTypeLabel.color = new Color(1f, 0.4f, 0.4f); // Bloodcraft uses red for blood headings
        ApplyStrongAccentOutline(_blTypeLabel);
        _blProgressLabel = AddInfoLabel(currentCard, "BlProgress", "Level —",            FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        _blBonusLabel    = AddInfoLabel(currentCard, "BlBonus",    "Bonus Stats: —",     FontStyles.Normal, fontSize: Theme.ScaledUI(13));
        _blStatsValuesLabel = AddInfoLabel(currentCard, "BlStatsValues", "", FontStyles.Italic, fontSize: Theme.ScaledUI(12));
        _blStatsValuesLabel.gameObject.SetActive(false);
        ApplyStrongAccentOutline(_blStatsValuesLabel);
        _blStatsValuesLabel.enableWordWrapping = true;
        _blStatsValuesLabel.overflowMode = TextOverflowModes.Overflow;

        AddSpacer(page, 6);

        var actionsCard = AddCard(page, "BlActionsCard");
        AddSectionHeading(actionsCard, "Actions");
        var actions = UIFactory.CreateHorizontalGroup(actionsCard, "BlActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(actions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(actions, "Refresh",     MessageService.BCCOM_BL_GET,
            "Display your current blood legacy details in chat (.bl get).");
        AddCommandButton(actions, "List Bloods", MessageService.BCCOM_BL_LIST,
            "List all blood legacy types tracked by Bloodcraft (.bl l).");
        AddCommandButton(actions, "List Stats",  MessageService.BCCOM_BL_LIST_STATS,
            "List the blood-stat bonuses you can choose between (.bl lst).");
        AddCommandButton(actions, "Reset Stats", MessageService.BCCOM_BL_RESET_STATS,
            "Reset your chosen bonus stats for the current blood (.bl rst).");

        AddSpacer(page, 6);

        var statCard = AddCard(page, "BlStatPickerCard");
        AddSectionHeading(statCard, "Choose Bonus Stat");
        CollapsibleSection.Build(statCard,
            title: "Set bonus stat for a blood type (.bl cst)",
            startExpanded: false,
            tooltip: "Pick a blood type AND the bonus stat you want to lock in for it. Bloodcraft applies the chosen stat scaled by your legacy level for that blood. Each blood tracks up to 3 chosen stats; submit again to add more (or use Reset Stats to clear the current blood).",
            buildContent: c => FormBuilder.Build(c,
                title: "Set bonus stat",
                commandTemplate: ".bl cst {blood} {stat}",
                onSubmitted: () => EnqueueOrWarn(MessageService.BCCOM_BL_GET),
                new EnumField<PlayerStateService.BloodTypeChoice>("blood", "Blood",
                    defaultValue: PlayerStateService.BloodTypeChoice.Warrior,
                    tooltip: "Which blood type the chosen bonus stat applies to."),
                new EnumIndexField<PlayerStateService.BloodBonusStat>("stat", "Bonus stat",
                    defaultValue: PlayerStateService.BloodBonusStat.PhysicalResistance,
                    tooltip: "The stat to enhance. Bloodcraft expects a 1-12 index; the dropdown sends it for you.")));

        CollapsibleSection.Build(statCard,
            title: "Show info for a specific blood (.bl get [Blood])",
            startExpanded: false,
            tooltip: "Query any blood type's level + chosen stats — not just the one you currently have. Result is parsed and shown in the panel below; chat is also updated unless you've enabled 'Clear server messages'.",
            buildContent: c => FormBuilder.Build(c,
                title: "Show blood info",
                commandTemplate: ".bl get {blood}",
                new EnumField<PlayerStateService.BloodTypeChoice>("blood", "Blood",
                    defaultValue: PlayerStateService.BloodTypeChoice.Warrior,
                    tooltip: "Which blood type to inspect.")));

        AddDivider(statCard);
        AddBodyText(statCard,
            $"Unlike weapon expertise, {Mono(".bl get")} accepts a blood-type argument — so the 'Show info' form above can inspect ANY blood you've leveled, not just your current one.");

        AddSpacer(page, 6);
        BuildBloodInfoDisplay(page);

        RenderBloodLegacy(PlayerStateService.Legacy);
        if (!_blSubscribed)
        {
            PlayerStateService.LegacyChanged += OnLegacyChanged;
            _blSubscribed = true;
        }
        if (!_blInfoSubscribed)
        {
            PlayerStateService.BloodInfoChanged += OnBloodInfoChanged;
            _blInfoSubscribed = true;
        }
        RenderBloodInfo();
    }

    private void BuildBloodInfoDisplay(GameObject page)
    {
        var section = UIFactory.CreateVerticalGroup(page, "BloodInfoDisplay",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(6, 6, 6, 6));
        UIFactory.SetLayoutElement(section,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 80, flexibleHeight: 0);

        // The accent labels in this section render in Bloodcraft red over a
        // dark panel — fine on the panel background, but at lower opacity the
        // in-game world (often red-tinted in vampire areas) bleeds through
        // and the red-on-red kills contrast. ApplyStrongAccentOutline bumps
        // the per-character outline so the text stays legible. Friend-testing
        // feedback (v0.9.0): "pink or red text on the red background".
        _blInfoTitleLabel = AddInfoLabel(section, "BloodInfoTitle",
            "Blood Info", FontStyles.Bold | FontStyles.Italic, fontSize: Theme.ScaledUI(14));
        _blInfoTitleLabel.color = new Color(1f, 0.4f, 0.4f); // Bloodcraft red
        ApplyStrongAccentOutline(_blInfoTitleLabel);

        _blInfoLevelLabel = AddInfoLabel(section, "BloodInfoLevel",
            "(submit Show info above to populate)", FontStyles.Italic, fontSize: Theme.ScaledUI(12));

        // Stat lines preserve server <color=red> markup; the outline applies
        // globally to the label so the red/cyan/white tokens all get the
        // dark border.
        _blInfoStatsLabel = AddInfoLabel(section, "BloodInfoStats",
            "", FontStyles.Normal, fontSize: Theme.ScaledUI(12));
        ApplyStrongAccentOutline(_blInfoStatsLabel);
        var fitter = _blInfoStatsLabel.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        _blInfoStatsLabel.enableWordWrapping = true;
        _blInfoStatsLabel.overflowMode = TextOverflowModes.Overflow;
    }

    private void OnBloodInfoChanged()
    {
        RenderBloodInfo();
        RenderBlStatsValues();
        AutoResizeIfEnabled();
    }

    // 0.9.6: render the structured stat-values line in the Blood Legacy tab
    // HEADER (separate from the full Blood Info display further down the
    // page, which renders any blood the user queries via the form). Only
    // shows when BloodInfoLatest matches the currently-equipped blood — if
    // the user just queried "Worker" but is currently using "Warrior", the
    // header keeps showing nothing rather than misleading values.
    private void RenderBlStatsValues()
    {
        if (_blStatsValuesLabel == null) return;
        var info = PlayerStateService.BloodInfoLatest;
        var leg = PlayerStateService.Legacy;
        bool currentMatches = !string.IsNullOrEmpty(info.BloodType)
                           && string.Equals(info.BloodType, leg.Type.ToString(), System.StringComparison.OrdinalIgnoreCase);
        bool haveLines = info.StatLines != null && info.StatLines.Count > 0;
        if (!currentMatches || !haveLines)
        {
            if (_blStatsValuesLabel.gameObject.activeSelf) _blStatsValuesLabel.gameObject.SetActive(false);
            return;
        }
        _blStatsValuesLabel.text = "• " + string.Join("\n• ", info.StatLines);
        if (!_blStatsValuesLabel.gameObject.activeSelf) _blStatsValuesLabel.gameObject.SetActive(true);
    }

    private void RenderBloodInfo()
    {
        if (_blInfoTitleLabel == null) return;
        var info = PlayerStateService.BloodInfoLatest;
        if (string.IsNullOrEmpty(info.BloodType))
        {
            _blInfoTitleLabel.text  = "Blood Info";
            _blInfoLevelLabel.text  = "(submit Show info above to populate)";
            _blInfoStatsLabel.text  = "";
            return;
        }
        _blInfoTitleLabel.text = $"{info.BloodType} Blood Info";
        _blInfoLevelLabel.text = info.Prestige > 0
            ? $"Level {info.Level}  Prestige {info.Prestige}   Essence {info.Essence}  ({info.ProgressPct}%)"
            : $"Level {info.Level}   Essence {info.Essence}  ({info.ProgressPct}%)";
        if (info.StatLines != null && info.StatLines.Count > 0)
            _blInfoStatsLabel.text = "• " + string.Join("\n• ", info.StatLines);
        else
            _blInfoStatsLabel.text = "(no stat lines parsed — Bloodcraft may not have sent any)";
    }

    private void OnLegacyChanged()
    {
        var l = PlayerStateService.Legacy;
        // 0.10.2: blood-swap fast refresh — same pattern as weapon.
        if (_blTabTypeBaseline && l.Type != _blTabLastType)
        {
            _lastBlAutoFetchAt = 0;
            // BloodInfoLatest survives — that's a different blood now, so
            // RenderBlStatsValues will hide the old values via its
            // "currentMatches" gate until the new .bl get reply lands.
        }
        _blTabLastType = l.Type;
        _blTabTypeBaseline = true;
        RenderBloodLegacy(l);
        // The current blood may have changed (player switched bloods); refresh
        // the stats-values header which gates on Legacy.Type matching
        // BloodInfoLatest.BloodType.
        RenderBlStatsValues();
    }

    private void RenderBloodLegacy(PlayerStateService.LegacyState s)
    {
        if (_blTypeLabel == null) return;
        _blTypeLabel.text = s.Type.ToString();
        _blProgressLabel.text = s.Prestige > 0
            ? $"Level {s.Level}  ({s.Progress * 100f:0.#}%)   Prestige {s.Prestige}"
            : $"Level {s.Level}  ({s.Progress * 100f:0.#}%)";

        var stats = PlayerStateService.DecodeBloodBonusStats(s.BonusStatsRaw);
        var named = new System.Collections.Generic.List<string>();
        foreach (var st in stats)
            if (st != PlayerStateService.BloodStatType.None) named.Add(st.ToString());
        _blBonusLabel.text = named.Count > 0
            ? $"Bonus Stats: {string.Join(", ", named)}"
            : "Bonus Stats: (none yet — use the form below to choose)";
    }

    // -----------------------------------------------------------------------
    // Unarmed + Shift Skill tab
    // -----------------------------------------------------------------------

    private void BuildUnarmedShiftTab(GameObject page)
    {
        // 0.10.11: card-wrap shift spell + unarmed expertise + actions.
        var shiftCard = AddCard(page, "ShiftSpellCard", Theme.SystemTintExpertise);
        AddSectionHeading(shiftCard, "Shift Spell");
        _shiftSpellLabel = AddInfoLabel(shiftCard, "ShiftSpell",
            "Equipped: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        AddSpacer(page, 6);

        var unarmedCard = AddCard(page, "UnarmedCard", Theme.SystemTintExpertise);
        AddSectionHeading(unarmedCard, "Unarmed Expertise");
        _unarmedStatusLabel = AddInfoLabel(unarmedCard, "UnarmedStatus",
            "Equip your fists (no weapon) to inspect unarmed expertise.",
            FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        _unarmedBonusLabel = AddInfoLabel(unarmedCard, "UnarmedBonus",
            "Bonus Stats: —", FontStyles.Normal, fontSize: Theme.ScaledUI(13));

        AddSpacer(page, 6);

        var actionsCard = AddCard(page, "ShiftActionsCard");
        AddSectionHeading(actionsCard, "Actions");
        var actions = UIFactory.CreateHorizontalGroup(actionsCard, "UnarmedShiftActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(actions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(actions, "Toggle Shift",  MessageService.BCCOM_CLASS_TOGGLE_SHIFT,
            "Toggle whether your class spell is locked into the shift slot (.class shift).");
        AddCommandButton(actions, "Lock Spells",   MessageService.BCCOM_WEP_LOCK_SPELLS,
            "Lock in next-equipped spells for use in your unarmed slots (.wep locksp).");
        AddCommandButton(actions, "Refresh",       MessageService.BCCOM_WEP_GET,
            "Refresh weapon expertise details (.wep get). Chat receives the response.");

        AddDivider(actionsCard);
        AddBodyText(actionsCard,
            $"Choosing which class spell goes in the shift slot takes a number ({Mono(".class csp <#>")}). Use chat for now; a spell picker arrives in a later phase.");

        RenderUnarmedShift(PlayerStateService.Expertise, PlayerStateService.ShiftSpell);
        if (!_shiftSubscribed)
        {
            PlayerStateService.ExpertiseChanged  += OnExpertiseChangedForUnarmed;
            PlayerStateService.ShiftSpellChanged += OnShiftSpellChanged;
            _shiftSubscribed = true;
        }
    }

    private void OnExpertiseChangedForUnarmed()
        => RenderUnarmedShift(PlayerStateService.Expertise, PlayerStateService.ShiftSpell);
    private void OnShiftSpellChanged()
        => RenderUnarmedShift(PlayerStateService.Expertise, PlayerStateService.ShiftSpell);

    private void RenderUnarmedShift(
        PlayerStateService.ExpertiseState exp,
        PlayerStateService.ShiftSpellState shift)
    {
        if (_shiftSpellLabel == null) return;

        _shiftSpellLabel.text = shift.SpellIndex == 0
            ? "Equipped: (none)"
            : PrefabNameResolver.TryGet(shift.SpellIndex, out var spellName)
                ? $"Equipped: {spellName}"
                : $"Equipped: PrefabGUID {shift.SpellIndex}";

        bool unarmedEquipped = exp.Type == PlayerStateService.WeaponType.Unarmed;
        if (unarmedEquipped)
        {
            _unarmedStatusLabel.text = exp.Prestige > 0
                ? $"Level {exp.Level}   ({exp.Progress * 100f:0.#}%)   Prestige {exp.Prestige}"
                : $"Level {exp.Level}   ({exp.Progress * 100f:0.#}%)";

            var stats = PlayerStateService.DecodeWeaponBonusStats(exp.BonusStatsRaw);
            var named = new System.Collections.Generic.List<string>();
            foreach (var st in stats)
                if (st != PlayerStateService.WeaponStatType.None) named.Add(st.ToString());
            _unarmedBonusLabel.text = named.Count > 0
                ? $"Bonus Stats: {string.Join(", ", named)}"
                : "Bonus Stats: (none yet — pick one via .wep cst)";
        }
        else
        {
            _unarmedStatusLabel.text =
                $"Currently equipped: {exp.Type}. Equip your fists (unarm) to inspect unarmed expertise.";
            _unarmedBonusLabel.text = "Bonus Stats: —";
        }
    }

    // -----------------------------------------------------------------------
    // Prestige tab
    // -----------------------------------------------------------------------

    private void BuildPrestigeTab(GameObject page)
    {
        // 0.10.9: each system's prestige sits in its own tinted card so the
        // 4-system breakdown reads at a glance. Pre-0.10.9 the four lines
        // sat in a single padded VLG, distinguished only by their prose
        // text — visually mushy.
        AddSectionHeading(page, "Current Prestige");

        var xpCard = AddCard(page, "PrestigeXpCard", Theme.SystemTintXP, padding: 6, innerSpacing: 2);
        _prestigeXpLabel = AddInfoLabel(xpCard, "PrestigeXp",
            "Experience prestige: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        var legacyCard = AddCard(page, "PrestigeLegacyCard", Theme.SystemTintLegacy, padding: 6, innerSpacing: 2);
        _prestigeLegacyLabel = AddInfoLabel(legacyCard, "PrestigeLegacy",
            "Blood legacy prestige: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        var expertiseCard = AddCard(page, "PrestigeExpertiseCard", Theme.SystemTintExpertise, padding: 6, innerSpacing: 2);
        _prestigeExpertiseLabel = AddInfoLabel(expertiseCard, "PrestigeExpertise",
            "Weapon expertise prestige: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        var famCard = AddCard(page, "PrestigeFamCard", Theme.SystemTintFamiliar, padding: 6, innerSpacing: 2);
        _prestigeFamLabel = AddInfoLabel(famCard, "PrestigeFam",
            "Familiar prestige: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        AddSpacer(page, 6);
        AddSectionHeading(page, "Quick actions");

        var actions = UIFactory.CreateHorizontalGroup(page, "PrestigeActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(actions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(actions, "List",      MessageService.BCCOM_PRESTIGE_LIST,
            "List the prestige systems available on this server (.prestige l). Response in chat.");
        AddCommandButton(actions, "Sync Buffs",MessageService.BCCOM_PRESTIGE_SYNC_BUFFS,
            "Re-apply your prestige buffs if any have dropped (.prestige sb).");
        AddCommandButton(actions, "Exoform",   MessageService.BCCOM_PRESTIGE_TOGGLE_EXOFORM,
            "Toggle taunting to enter exoform shapeshift (.prestige exoform). Requires Exo prestige.");
        AddCommandButton(actions, "Shroud",    MessageService.BCCOM_PRESTIGE_TOGGLE_SHROUD,
            "Toggle permashroud if you qualify for it (.prestige shroud).");

        AddSpacer(page, 4);
        AddSectionHeading(page, "Prestige actions (forms)");

        CollapsibleSection.Build(page,
            title: "Prestige in a system (.prestige me)",
            startExpanded: false,
            tooltip: "Expand to prestige in a specific system — Experience, a weapon expertise, a blood legacy, or Exo.",
            buildContent: c => FormBuilder.Build(c,
                title: "Prestige in a system",
                commandTemplate: ".prestige me {type}",
                new EnumField<PlayerStateService.PrestigeType>("type", "Prestige type",
                    defaultValue: PlayerStateService.PrestigeType.Experience,
                    tooltip: "Which leveling system to prestige in. You must be at max level in that system.")));

        CollapsibleSection.Build(page,
            title: "Show prestige info (.prestige get)",
            startExpanded: false,
            tooltip: "Expand, pick a system, Submit. The reply is parsed and rendered in-panel below — chat is also updated unless you've enabled 'Clear server messages' in settings.",
            buildContent: c => FormBuilder.Build(c,
                title: "Show prestige info",
                commandTemplate: ".prestige get {type}",
                new EnumField<PlayerStateService.PrestigeType>("type", "Prestige type",
                    defaultValue: PlayerStateService.PrestigeType.Experience)));

        AddSpacer(page, 4);
        BuildPrestigeInfoDisplay(page);

        CollapsibleSection.Build(page,
            title: "Leaderboard (.prestige lb)",
            startExpanded: false,
            tooltip: "Expand to view the prestige leaderboard for a system.",
            buildContent: c => FormBuilder.Build(c,
                title: "Prestige leaderboard",
                commandTemplate: ".prestige lb {type}",
                new EnumField<PlayerStateService.PrestigeType>("type", "Prestige type",
                    defaultValue: PlayerStateService.PrestigeType.Experience)));

        CollapsibleSection.Build(page,
            title: "Select exoform variant (.prestige sf)",
            startExpanded: false,
            tooltip: "Expand to switch between the Evolved Vampire and Corrupted Serpent exoform shapeshifts.",
            buildContent: c => FormBuilder.Build(c,
                title: "Select exoform",
                commandTemplate: ".prestige sf {form}",
                new EnumField<PlayerStateService.ExoformVariant>("form", "Exoform",
                    defaultValue: PlayerStateService.ExoformVariant.EvolvedVampire,
                    tooltip: "EvolvedVampire or CorruptedSerpent.")));

        RenderPrestige();
        if (!_prestigeSubscribed)
        {
            PlayerStateService.ExperienceChanged += OnAnyForPrestige;
            PlayerStateService.LegacyChanged     += OnAnyForPrestige;
            PlayerStateService.ExpertiseChanged  += OnAnyForPrestige;
            PlayerStateService.FamiliarChanged   += OnAnyForPrestige;
            _prestigeSubscribed = true;
        }

        if (!_prestigeInfoSubscribed)
        {
            PlayerStateService.PrestigeInfoChanged += OnPrestigeInfoChanged;
            _prestigeInfoSubscribed = true;
        }
        RenderPrestigeInfo();
    }

    private void BuildPrestigeInfoDisplay(GameObject page)
    {
        // 0.8.2: bumped spacing 2→8 and padding 6→12 so the parsed prestige
        // info ("Prestige Info" title, level line, effect lines) doesn't crowd
        // together at the top of the box. Friend-testing called this out as
        // "crammed" — the issue is more visible when the effect lines wrap.
        _prestigeInfoSection = UIFactory.CreateVerticalGroup(page, "PrestigeInfoDisplay",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 8, padding: new Vector4(12, 12, 10, 10));
        UIFactory.SetLayoutElement(_prestigeInfoSection,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 120, flexibleHeight: 0);

        _prestigeInfoTitleLabel = AddInfoLabel(_prestigeInfoSection, "PrestigeInfoTitle",
            "Prestige Info", FontStyles.Bold | FontStyles.Italic, fontSize: Theme.ScaledUI(16));
        _prestigeInfoTitleLabel.color = new Color(0.6f, 0.95f, 0.6f); // Bloodcraft #90EE90

        _prestigeInfoLevelLabel = AddInfoLabel(_prestigeInfoSection, "PrestigeInfoLevel",
            "(submit Show prestige info above to populate)", FontStyles.Italic, fontSize: Theme.ScaledUI(13));
        // 0.9.2: optional progress bar showing level / maxLevel. Visibility +
        // fill are pushed during RenderPrestigeInfo, which re-reads
        // Settings.ShowProgressBars so the toggle takes effect live.
        _prestigeBar = UI.Framework.CustomLib.Controls.MiniBar.Create(
            _prestigeInfoSection, "PrestigeBar", out _prestigeBarFill,
            fillColor: new Color(0.6f, 0.95f, 0.6f, 0.95f)); // matches the Bloodcraft #90EE90 title accent
        _prestigeBar.SetActive(false);

        // Multi-line "effects" label. Using ContentSizeFitter so however many
        // lines the server sends back render flush together — the parser emits
        // one effect per inbound chat line (color tags stripped).
        _prestigeInfoEffectsLabel = AddInfoLabel(_prestigeInfoSection, "PrestigeInfoEffects",
            "", FontStyles.Normal, fontSize: Theme.ScaledUI(13));
        var fitter = _prestigeInfoEffectsLabel.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        _prestigeInfoEffectsLabel.enableWordWrapping = true;
        _prestigeInfoEffectsLabel.overflowMode = TextOverflowModes.Overflow;
    }

    private void OnPrestigeInfoChanged()
    {
        RenderPrestigeInfo();
        AutoResizeIfEnabled();
    }

    private void RenderPrestigeInfo()
    {
        if (_prestigeInfoTitleLabel == null) return;
        var info = PlayerStateService.PrestigeInfoLatest;
        if (string.IsNullOrEmpty(info.TypeName))
        {
            _prestigeInfoTitleLabel.text  = "Prestige Info";
            _prestigeInfoLevelLabel.text  = "(submit Show prestige info above to populate)";
            _prestigeInfoEffectsLabel.text = "";
            if (_prestigeBar != null && _prestigeBar.activeSelf) _prestigeBar.SetActive(false);
            return;
        }

        _prestigeInfoTitleLabel.text = $"{info.TypeName} Prestige Info";
        _prestigeInfoLevelLabel.text = info.MaxLevel > 0
            ? $"Current Prestige Level: {info.Level} / {info.MaxLevel}"
            : $"Current Prestige Level: {info.Level}";

        if (info.EffectLines != null && info.EffectLines.Count > 0)
            _prestigeInfoEffectsLabel.text = "• " + string.Join("\n• ", info.EffectLines);
        else
            _prestigeInfoEffectsLabel.text = "(no additional effect lines parsed)";

        // 0.9.2: progress bar. Only meaningful when MaxLevel > 0 (i.e. the
        // server reported a cap); otherwise hide so we don't show a bar
        // that never fills.
        bool showBar = Config.Settings.ShowProgressBars && info.MaxLevel > 0;
        if (_prestigeBar != null && _prestigeBar.activeSelf != showBar) _prestigeBar.SetActive(showBar);
        if (showBar)
            UI.Framework.CustomLib.Controls.MiniBar.SetProgress(_prestigeBarFill, info.Level / (float)info.MaxLevel);
    }

    private void OnAnyForPrestige() => RenderPrestige();

    private void RenderPrestige()
    {
        if (_prestigeXpLabel == null) return;
        _prestigeXpLabel.text        = $"Experience prestige: {PlayerStateService.Experience.Prestige}";
        _prestigeLegacyLabel.text    = $"Blood legacy prestige ({PlayerStateService.Legacy.Type}): {PlayerStateService.Legacy.Prestige}";
        _prestigeExpertiseLabel.text = $"Weapon expertise prestige ({PlayerStateService.Expertise.Type}): {PlayerStateService.Expertise.Prestige}";
        _prestigeFamLabel.text       = $"Familiar prestige ({(string.IsNullOrEmpty(PlayerStateService.Familiar.Name) ? "no familiar" : PlayerStateService.Familiar.Name)}): {PlayerStateService.Familiar.Prestige}";
    }

    // -----------------------------------------------------------------------
    // Experience Levels overview tab (read-only summary across systems)
    // -----------------------------------------------------------------------

    private void BuildLevelsTab(GameObject page)
    {
        // 0.10.9: each progression system gets a tinted card so the four
        // streams (XP / Legacy / Expertise / Familiar) read as visually
        // distinct rather than four indistinguishable label stacks. Tints
        // are 6%-alpha washes over the card; text remains full-contrast.

        // ── Player Experience ───────────────────────────────────────────
        var xpCard = AddCard(page, "LvlXpCard", Theme.SystemTintXP);
        AddSectionHeading(xpCard, "Player Experience");
        _lvlXpLabel = AddInfoLabel(xpCard, "LvlXp", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(13));

        AddSpacer(page, 6);

        // ── Blood Legacy ────────────────────────────────────────────────
        var legacyCard = AddCard(page, "LvlLegacyCard", Theme.SystemTintLegacy);
        AddSectionHeading(legacyCard, "Blood Legacy");
        _lvlLegacyLabel = AddInfoLabel(legacyCard, "LvlLegacy", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(13));

        AddSpacer(page, 6);

        // ── Weapon Expertise ────────────────────────────────────────────
        var expertiseCard = AddCard(page, "LvlExpertiseCard", Theme.SystemTintExpertise);
        AddSectionHeading(expertiseCard, "Weapon Expertise (active weapon)");
        _lvlExpertiseLabel      = AddInfoLabel(expertiseCard, "LvlExpertise",      "—", FontStyles.Normal, fontSize: Theme.ScaledUI(13));
        _lvlExpertiseBonusLabel = AddInfoLabel(expertiseCard, "LvlExpertiseBonus", "Bonus stats: —", FontStyles.Italic, fontSize: Theme.ScaledUI(12));

        // Bloodcraft's Eclipse protocol only streams the currently-equipped
        // weapon's expertise level — no per-weapon snapshot. Surface a
        // "List Weapons" button + a muted explanation so the user isn't
        // left wondering why only one weapon shows.
        AddBodyText(expertiseCard,
            $"Bloodcraft streams only the currently-equipped weapon. Swap weapons to update the row above, or use {Mono(".wep l")} for the full list (reply lands in chat).");
        var allWepRow = UIFactory.CreateHorizontalGroup(expertiseCard, "AllWepRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(allWepRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        AddCommandButton(allWepRow, "List Weapon Types", MessageService.BCCOM_WEP_LIST,
            "Sends .wep l. Bloodcraft replies in chat with the list of weapon types you can level.");

        AddSpacer(page, 6);

        // ── Familiar (active) ───────────────────────────────────────────
        var famCard = AddCard(page, "LvlFamCard", Theme.SystemTintFamiliar);
        AddSectionHeading(famCard, "★  Familiar (active)");
        _lvlFamLabel      = AddInfoLabel(famCard, "LvlFam",      "—", FontStyles.Normal, fontSize: Theme.ScaledUI(13));
        _lvlFamStatsLabel = AddInfoLabel(famCard, "LvlFamStats", "HP —   PP —   SP —", FontStyles.Italic, fontSize: Theme.ScaledUI(12));

        AddSpacer(page, 6);

        // ── Professions ─────────────────────────────────────────────────
        var profCard = AddCard(page, "LvlProfCard", Theme.SystemTintProfession);
        AddSectionHeading(profCard, "Professions");
        _lvlProfessions1Label = AddInfoLabel(profCard, "LvlProf1", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(12));
        _lvlProfessions2Label = AddInfoLabel(profCard, "LvlProf2", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(12));
        _lvlProfessions3Label = AddInfoLabel(profCard, "LvlProf3", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(12));
        _lvlProfessions4Label = AddInfoLabel(profCard, "LvlProf4", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(12));

        AddSpacer(page, 6);

        // ── Profession Tools ────────────────────────────────────────────
        var profToolsCard = AddCard(page, "LvlProfToolsCard");
        AddSectionHeading(profToolsCard, "Profession Tools");
        var profRow = UIFactory.CreateHorizontalGroup(profToolsCard, "ProfRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(profRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(profRow, "List",          MessageService.BCCOM_PROF_LIST,
            "List the professions Bloodcraft tracks (.prof l). Reply in chat.");
        AddCommandButton(profRow, "Toggle Log",    MessageService.BCCOM_PROF_LOG_TOGGLE,
            "Toggle in-chat profession-progress logging (.prof log). SERVER-side toggle.");

        CollapsibleSection.Build(profToolsCard,
            title: "Show profession progress (.prof get)",
            startExpanded: false,
            tooltip: "Displays your current level + progress for the chosen profession in chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Show profession",
                commandTemplate: ".prof get {profession}",
                new EnumField<PlayerStateService.BloodcraftProfession>("profession", "Profession",
                    defaultValue: PlayerStateService.BloodcraftProfession.Enchanting,
                    tooltip: "Which profession to inspect.")));

        AddSpacer(page, 6);

        // ── Player Tools ────────────────────────────────────────────────
        var playerToolsCard = AddCard(page, "LvlPlayerToolsCard");
        AddSectionHeading(playerToolsCard, "Player Tools");
        var toolsRow1 = UIFactory.CreateHorizontalGroup(playerToolsCard, "PlayerToolsRow1",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(toolsRow1,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(toolsRow1, "User Stats",   MessageService.BCCOM_MISC_USER_STATS,
            "Print a summary of player stats in chat (.misc userstats).");
        AddCommandButton(toolsRow1, "Toggle XP Log", MessageService.BCCOM_LVL_LOG_TOGGLE,
            "Toggle in-chat logging of leveling-progress messages (.lvl log). SERVER-side toggle — Bloodcraft replies with the new state in chat.");
        AddCommandButton(toolsRow1, "Reminders",    MessageService.BCCOM_MISC_REMINDERS,
            "Toggle general feature reminders (.misc remindme). SERVER-side toggle.");
        AddCommandButton(toolsRow1, "Silence",      MessageService.BCCOM_MISC_SILENCE,
            "Reset stuck combat music if it won't stop (.misc silence).");

        var toolsRow2 = UIFactory.CreateHorizontalGroup(playerToolsCard, "PlayerToolsRow2",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(toolsRow2,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(toolsRow2, "Starter Kit",  MessageService.BCCOM_MISC_KIT_ME,
            "Claim the server's starter kit (.misc kitme). One-time on most servers.");
        AddCommandButton(toolsRow2, "Prepare Hunt",  MessageService.BCCOM_MISC_PREPARE,
            "Auto-complete the GettingReadyForTheHunt quest if it's stuck (.misc prepare).");

        CollapsibleSection.Build(playerToolsCard,
            title: "Toggle scrolling combat text (.misc sct)",
            startExpanded: false,
            tooltip: "Enable or disable a specific scrolling-combat-text element. Bloodcraft replies with the new state in chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Toggle SCT",
                commandTemplate: ".misc sct {type}",
                new TextField("type", "SCT element type",
                    tooltip: "Element name (e.g. 'damage', 'heal'). Bloodcraft's reply tells you the new state.")));

        AddDivider(playerToolsCard);
        AddBodyText(playerToolsCard,
            $"Heads up: most of these are server-side TOGGLES — Bloodcraft flips a flag and reports the new state in chat. The client can't 'remember' the new state across sessions because the server is the source of truth (same with {Mono(".fam t")} / {Mono(".lvl log")} elsewhere).");

        RenderLevels();
        if (!_lvlSubscribed)
        {
            PlayerStateService.ExperienceChanged += OnAnyForLevels;
            PlayerStateService.LegacyChanged     += OnAnyForLevels;
            PlayerStateService.ExpertiseChanged  += OnAnyForLevels;
            PlayerStateService.FamiliarChanged   += OnAnyForLevels;
            PlayerStateService.ProfessionChanged += OnAnyForLevels;
            _lvlSubscribed = true;
        }
    }

    private void OnAnyForLevels() => RenderLevels();

    private void RenderLevels()
    {
        if (_lvlXpLabel == null) return;
        var exp = PlayerStateService.Experience;
        var leg = PlayerStateService.Legacy;
        var wep = PlayerStateService.Expertise;
        var fam = PlayerStateService.Familiar;
        var pro = PlayerStateService.Profession;

        _lvlXpLabel.text = exp.Prestige > 0
            ? $"Level {exp.Level} ({exp.Progress * 100f:0.#}%)   Prestige {exp.Prestige}   Class: {exp.Class}"
            : $"Level {exp.Level} ({exp.Progress * 100f:0.#}%)   Class: {exp.Class}";

        _lvlLegacyLabel.text = leg.Prestige > 0
            ? $"{leg.Type}   Level {leg.Level} ({leg.Progress * 100f:0.#}%)   Prestige {leg.Prestige}"
            : $"{leg.Type}   Level {leg.Level} ({leg.Progress * 100f:0.#}%)";

        _lvlExpertiseLabel.text = wep.Prestige > 0
            ? $"{wep.Type}   Level {wep.Level} ({wep.Progress * 100f:0.#}%)   Prestige {wep.Prestige}"
            : $"{wep.Type}   Level {wep.Level} ({wep.Progress * 100f:0.#}%)";

        var wepStats = PlayerStateService.DecodeWeaponBonusStats(wep.BonusStatsRaw);
        var wepNamed = new System.Collections.Generic.List<string>();
        foreach (var s in wepStats)
            if (s != PlayerStateService.WeaponStatType.None) wepNamed.Add(s.ToString());
        _lvlExpertiseBonusLabel.text = wepNamed.Count > 0
            ? $"Bonus stats: {string.Join(", ", wepNamed)}"
            : "Bonus stats: (none yet — choose via .wep cst)";

        // 0.10.8: HasActive is sourced from the raw Eclipse protocol name
        // field. Pre-0.10.8 the Level > 0 || !empty(Name) check was always
        // true because EclipseProtocolService defaults Name to "Familiar"
        // and floors Level to 1 — so the "(no familiar bound)" branch was
        // unreachable and the Levels tab always rendered "Familiar Lv 1".
        bool famActive = fam.HasActive;
        _lvlFamLabel.text = famActive
            ? (fam.Prestige > 0
                ? $"{fam.Name}   Level {fam.Level} ({fam.Progress * 100f:0.#}%)   Prestige {fam.Prestige}"
                : $"{fam.Name}   Level {fam.Level} ({fam.Progress * 100f:0.#}%)")
            : "(no familiar bound)";
        _lvlFamStatsLabel.text = famActive
            ? $"HP {fam.MaxHealth}   PP {fam.PhysicalPower}   SP {fam.SpellPower}"
            : "HP —   PP —   SP —";

        _lvlProfessions1Label.text = $"Enchanting    Lv {pro.EnchantingLevel:00} ({pro.EnchantingProgress * 100f:0.#}%)        Alchemy        Lv {pro.AlchemyLevel:00} ({pro.AlchemyProgress * 100f:0.#}%)";
        _lvlProfessions2Label.text = $"Harvesting    Lv {pro.HarvestingLevel:00} ({pro.HarvestingProgress * 100f:0.#}%)        Blacksmithing  Lv {pro.BlacksmithingLevel:00} ({pro.BlacksmithingProgress * 100f:0.#}%)";
        _lvlProfessions3Label.text = $"Tailoring     Lv {pro.TailoringLevel:00} ({pro.TailoringProgress * 100f:0.#}%)        Woodcutting    Lv {pro.WoodcuttingLevel:00} ({pro.WoodcuttingProgress * 100f:0.#}%)";
        _lvlProfessions4Label.text = $"Mining        Lv {pro.MiningLevel:00} ({pro.MiningProgress * 100f:0.#}%)        Fishing        Lv {pro.FishingLevel:00} ({pro.FishingProgress * 100f:0.#}%)";
    }

    // -----------------------------------------------------------------------
    // Daily Quest tab
    // -----------------------------------------------------------------------

    private TextMeshProUGUI _dqDailyTargetLabel;
    private TextMeshProUGUI _dqDailyProgressLabel;
    private TextMeshProUGUI _dqWeeklyTargetLabel;
    private TextMeshProUGUI _dqWeeklyProgressLabel;
    private bool _dqSubscribed;

    private void BuildDailyQuestTab(GameObject page)
    {
        // 0.10.11: card-wrap daily / weekly / settings sections.
        var dailyCard = AddCard(page, "DQDailyCard", Theme.SystemTintQuest);
        AddSectionHeading(dailyCard, "Daily Quest");
        _dqDailyTargetLabel   = AddInfoLabel(dailyCard, "DQDailyTarget",   "—", FontStyles.Bold,   fontSize: Theme.ScaledUI(15));
        _dqDailyTargetLabel.color = new Color(0f, 1f, 1f); // Bloodcraft cyan #00FFFF
        ApplyStrongAccentOutline(_dqDailyTargetLabel);
        _dqDailyProgressLabel = AddInfoLabel(dailyCard, "DQDailyProgress", "—", FontStyles.Italic, fontSize: Theme.ScaledUI(13));
        var dailyRow = UIFactory.CreateHorizontalGroup(dailyCard, "DQDailyActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(dailyRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(dailyRow, "Refresh",  MessageService.BCCOM_QUEST_PROGRESS_DAILY,
            "Print the daily quest objective into chat (.quest p d). The UI updates automatically when Bloodcraft pushes new state via the Eclipse protocol; this is for an explicit poll.");
        AddCommandButton(dailyRow, "Track",    MessageService.BCCOM_QUEST_TRACK_DAILY,
            "Print the location/direction to your daily target (.quest t d). Reply appears in chat.");
        AddCommandButton(dailyRow, "Reroll",   MessageService.BCCOM_QUEST_REROLL_DAILY,
            "Reroll the daily quest (.quest r d). Costs the server-configured reroll item; only works once the daily is complete OR if the server allows mid-quest rerolls.");

        AddSpacer(page, 6);

        var weeklyCard = AddCard(page, "DQWeeklyCard", Theme.SystemTintQuest);
        AddSectionHeading(weeklyCard, "Weekly Quest");
        _dqWeeklyTargetLabel   = AddInfoLabel(weeklyCard, "DQWeeklyTarget",   "—", FontStyles.Bold,   fontSize: Theme.ScaledUI(15));
        _dqWeeklyTargetLabel.color = new Color(1f, 0.85f, 0.3f);
        ApplyStrongAccentOutline(_dqWeeklyTargetLabel);
        _dqWeeklyProgressLabel = AddInfoLabel(weeklyCard, "DQWeeklyProgress", "—", FontStyles.Italic, fontSize: Theme.ScaledUI(13));
        var weeklyRow = UIFactory.CreateHorizontalGroup(weeklyCard, "DQWeeklyActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(weeklyRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(weeklyRow, "Refresh", MessageService.BCCOM_QUEST_PROGRESS_WEEKLY,
            "Print the weekly quest objective into chat (.quest p w).");
        AddCommandButton(weeklyRow, "Track",   MessageService.BCCOM_QUEST_TRACK_WEEKLY,
            "Print the location/direction to your weekly target (.quest t w).");
        AddCommandButton(weeklyRow, "Reroll",  MessageService.BCCOM_QUEST_REROLL_WEEKLY,
            "Reroll the weekly quest (.quest r w). Costs the server-configured reroll item.");

        AddSpacer(page, 6);

        var settingsCard = AddCard(page, "DQSettingsCard");
        AddSectionHeading(settingsCard, "Settings");
        var setRow = UIFactory.CreateHorizontalGroup(settingsCard, "DQSettings",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(setRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(setRow, "Toggle Quest Log", MessageService.BCCOM_QUEST_LOG_TOGGLE,
            "Toggle in-chat progress logging (.quest log). When on, Bloodcraft prints a message each time you progress an objective.");

        AddDivider(settingsCard);
        AddBodyText(settingsCard,
            "Toggle the Daily Quest overlay from the panel footer to track progress in a small movable HUD.");

        RenderDailyQuestTab();
        if (!_dqSubscribed)
        {
            PlayerStateService.QuestChanged += OnQuestChangedForTab;
            _dqSubscribed = true;
        }
    }

    private void OnQuestChangedForTab() => RenderDailyQuestTab();

    private void RenderDailyQuestTab()
    {
        if (_dqDailyTargetLabel == null) return;
        var d = PlayerStateService.DailyQuest;
        var w = PlayerStateService.WeeklyQuest;
        FormatQuestRow(_dqDailyTargetLabel,  _dqDailyProgressLabel,  d, "(no daily quest yet — check back after the next refresh)");
        FormatQuestRow(_dqWeeklyTargetLabel, _dqWeeklyProgressLabel, w, "(no weekly quest yet — check back after the next refresh)");
    }

    private static void FormatQuestRow(TextMeshProUGUI target, TextMeshProUGUI progress,
        PlayerStateService.QuestState s, string emptyHint)
    {
        bool hasQuest = !string.IsNullOrEmpty(s.TargetName) || s.Goal > 0;
        if (!hasQuest)
        {
            target.text   = emptyHint;
            progress.text = "";
            return;
        }
        target.text = s.IsVBlood
            ? $"{s.TargetName}  (V Blood)"
            : $"{s.TargetName}";
        if (s.Goal > 0 && s.Progress >= s.Goal)
        {
            progress.text  = "Complete!  Reroll for the next one.";
            progress.color = new Color(0.6f, 1f, 0.6f);
        }
        else
        {
            progress.text  = $"Progress: {s.Progress} / {s.Goal}";
            progress.color = Color.white;
        }
    }

    // -----------------------------------------------------------------------
    // Admin tab
    // -----------------------------------------------------------------------

    /// <summary>
    /// Renders an info note at the top of every admin tab explaining that the
    /// commands here require server-admin permission. 0.8.2: replaced the
    /// previous self-asserted "Is admin" gate (which needed a full game
    /// restart to actually surface the commands — `Settings.SetIsAdmin` flipped
    /// a bool but `ShowTab(ActiveTab)` didn't always rebuild the page). The
    /// gate added no security since the server enforces permissions anyway;
    /// non-admins clicking commands just get rejection messages.
    /// </summary>
    private void RenderAdminInfoNote(GameObject page, string contextLabel)
    {
        var msg = UIFactory.CreateLabel(page, "AdminInfoNote",
            $"<b>Admin only.</b> {contextLabel} commands require server-admin permission. " +
            "Non-admins can click these buttons, but the server will reject them with a " +
            "permission error. Nothing here can damage your client.",
            TextAlignmentOptions.TopLeft, color: new Color(1f, 0.85f, 0.5f), fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(msg.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 40, preferredHeight: 48, flexibleHeight: 0);
        msg.TextMesh.enableWordWrapping = true;
        msg.TextMesh.overflowMode = TextOverflowModes.Overflow;
        msg.TextMesh.richText = true;

        AddSpacer(page, 4);
    }

    private void BuildAdminTab(GameObject page)
    {
        // 0.10.12: wrap the admin note + diagnostics row in cards. The
        // forms below are collapsibles which are already self-contained
        // visual units; an outer card around the long form stack would
        // double-nest without adding value.
        var noteCard = AddCard(page, "AdminNoteCard");
        RenderAdminInfoNote(noteCard, "Bloodcraft admin");

        AddSpacer(page, 6);

        var diagCard = AddCard(page, "AdminDiagCard");
        AddSectionHeading(diagCard, "Server Diagnostics");
        var diagRow = UIFactory.CreateHorizontalGroup(diagCard, "AdminDiag",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(diagRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(diagRow, "Server Health", MessageService.BCCOM_MISC_HEALTH,
            "Show the Bloodcraft server's startup readiness summary in chat (.misc health). Admin only.");

        AddSpacer(page, 6);
        AddSectionHeading(page, "Admin forms");

        // Collapsible form: header click toggles the form's content visibility.
        // Many forms collapsed by default keeps the Admin tab compact even after
        // Phase 5e migrates the remaining 8 reference lines.
        CollapsibleSection.Build(page,
            title: "Set player level (.lvl set)",
            startExpanded: false,
            tooltip: "Expand to set a player's character level. Admin only.",
            buildContent: content => FormBuilder.Build(content,
                title: "Set player level",
                commandTemplate: ".lvl set {player} {level}",
                new PlayerNameField("player", "Player",
                    tooltip: "Target player's character name (must match exactly)."),
                new IntField("level", "Level", min: 1, max: 200,
                    tooltip: "Target character level. Bloodcraft default cap is 90.")));

        CollapsibleSection.Build(page,
            title: "Toggle shared-XP exclusion (.lvl ignore)",
            startExpanded: false,
            tooltip: "Adds (or removes) a player from the list of those NOT eligible to receive shared experience. Toggle — Bloodcraft replies with the new state in chat.",
            buildContent: content => FormBuilder.Build(content,
                title: "Toggle shared-XP ignore",
                commandTemplate: ".lvl ignore {player}",
                new PlayerNameField("player", "Player",
                    tooltip: "Target player. The flip is reversible — call again to remove from the ignore list.")));

        // ---- 0.7.0 admin profession setter ----
        CollapsibleSection.Build(page,
            title: "Set player profession level (.prof set)",
            startExpanded: false,
            tooltip: "Set a player's profession level for the named profession.",
            buildContent: content => FormBuilder.Build(content,
                title: "Set profession",
                commandTemplate: ".prof set {player} {profession} {level}",
                new PlayerNameField("player", "Player"),
                new EnumField<PlayerStateService.BloodcraftProfession>("profession", "Profession",
                    defaultValue: PlayerStateService.BloodcraftProfession.Enchanting),
                new IntField("level", "Level", min: 0, max: 100,
                    tooltip: "Target level. 0 resets.")));

        // Set prestige
        CollapsibleSection.Build(page,
            title: "Set player prestige (.prestige set)",
            startExpanded: false,
            tooltip: "Expand to set a player's prestige level in a specific system.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set player prestige",
                commandTemplate: ".prestige set {player} {type} {level}",
                new PlayerNameField("player", "Player",
                    tooltip: "Target player's character name."),
                new EnumField<PlayerStateService.PrestigeType>("type", "Prestige type",
                    defaultValue: PlayerStateService.PrestigeType.Experience,
                    tooltip: "Which prestige system to set."),
                new IntField("level", "Level", min: 0, max: 100,
                    tooltip: "Prestige level (0 to reset).")));

        // Reset prestige
        CollapsibleSection.Build(page,
            title: "Reset player prestige (.prestige r)",
            startExpanded: false,
            tooltip: "Expand to reset a player's prestige in a specific system.",
            buildContent: c => FormBuilder.Build(c,
                title: "Reset player prestige",
                commandTemplate: ".prestige r {player} {type}",
                new PlayerNameField("player", "Player"),
                new EnumField<PlayerStateService.PrestigeType>("type", "Prestige type",
                    defaultValue: PlayerStateService.PrestigeType.Experience,
                    tooltip: "Which prestige system to reset.")));

        // ---- 0.7.0 prestige audit additions ----
        CollapsibleSection.Build(page,
            title: "Toggle prestige-leaderboard exclusion (.prestige ignore)",
            startExpanded: false,
            tooltip: "Adds (or removes) the player from the list of those who are HIDDEN from prestige leaderboards. Intended for admin/staff accounts.",
            buildContent: c => FormBuilder.Build(c,
                title: "Toggle leaderboard exclusion",
                commandTemplate: ".prestige ignore {player}",
                new PlayerNameField("player", "Player",
                    tooltip: "Toggle — calling again removes them from the exclusion list.")));

        CollapsibleSection.Build(page,
            title: "GLOBAL prestige-buff purge — DESTRUCTIVE",
            startExpanded: false,
            tooltip: "Removes prestige buffs from EVERY player on the server, so config-changed buffs can be re-applied cleanly. Cannot be undone in one click — every player would need to re-apply via .prestige sb. Required confirm.",
            buildContent: c => FormBuilder.Build(c,
                title: "Global prestige-buff purge",
                commandTemplate: MessageService.BCCOM_PRESTIGE_GLOBAL_BUFF_PURGE,
                new BoolField("confirm", "Yes, purge prestige buffs from EVERY player",
                    tooltip: "Required. Affects every player on the server — they each need to .prestige sb to re-apply.",
                    requireTrue: true)));

        // ---- 0.7.0 quest audit addition ----
        CollapsibleSection.Build(page,
            title: "Force-complete a player's quest (.quest c)",
            startExpanded: false,
            tooltip: "Marks a Daily or Weekly quest as complete for the named player without them having to fulfil the objective.",
            buildContent: c => FormBuilder.Build(c,
                title: "Force-complete quest",
                commandTemplate: ".quest c {player} {type}",
                new PlayerNameField("player", "Player"),
                new EnumField<PlayerStateService.BloodcraftQuestType>("type", "Quest type",
                    defaultValue: PlayerStateService.BloodcraftQuestType.Daily,
                    tooltip: "Daily or Weekly.")));

        // Set blood legacy
        CollapsibleSection.Build(page,
            title: "Set blood legacy (.bl set)",
            startExpanded: false,
            tooltip: "Expand to set a player's blood legacy level.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set blood legacy",
                commandTemplate: ".bl set {player} {blood} {level}",
                new PlayerNameField("player", "Player"),
                new EnumField<PlayerStateService.BloodType>("blood", "Blood",
                    defaultValue: PlayerStateService.BloodType.Warrior,
                    tooltip: "Worker / Warrior / Scholar / Rogue / Mutant / Draculin / Immortal / Creature / Brute / Corruption."),
                new IntField("level", "Level", min: 0, max: 100)));

        // Set weapon expertise
        CollapsibleSection.Build(page,
            title: "Set weapon expertise (.wep set)",
            startExpanded: false,
            tooltip: "Expand to set a player's weapon expertise level for a specific weapon.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set weapon expertise",
                commandTemplate: ".wep set {player} {weapon} {level}",
                new PlayerNameField("player", "Player"),
                new EnumField<PlayerStateService.WeaponType>("weapon", "Weapon",
                    defaultValue: PlayerStateService.WeaponType.Sword),
                new IntField("level", "Level", min: 0, max: 100)));

        // Set profession
        CollapsibleSection.Build(page,
            title: "Set profession (.prof set)",
            startExpanded: false,
            tooltip: "Expand to set a player's level in a specific profession.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set profession",
                commandTemplate: ".prof set {player} {profession} {level}",
                new PlayerNameField("player", "Player"),
                new EnumField<PlayerStateService.ProfessionType>("profession", "Profession",
                    defaultValue: PlayerStateService.ProfessionType.Mining),
                new IntField("level", "Level", min: 0, max: 100)));

        // Set familiar level
        CollapsibleSection.Build(page,
            title: "Set familiar level (.fam sl)",
            startExpanded: false,
            tooltip: "Expand to set a player's currently-bound familiar to a specific level.",
            buildContent: c => FormBuilder.Build(c,
                title: "Set familiar level",
                commandTemplate: ".fam sl {player} {level}",
                new PlayerNameField("player", "Player",
                    tooltip: "Target player. Their currently-bound familiar is affected."),
                new IntField("level", "Level", min: 1, max: 100)));

        // Refresh quests
        CollapsibleSection.Build(page,
            title: "Refresh quests (.quest rf)",
            startExpanded: false,
            tooltip: "Expand to force-refresh a player's daily and weekly quests.",
            buildContent: c => FormBuilder.Build(c,
                title: "Refresh quests",
                commandTemplate: ".quest rf {player}",
                new PlayerNameField("player", "Player")));

        // Complete a quest
        CollapsibleSection.Build(page,
            title: "Complete quest (.quest c)",
            startExpanded: false,
            tooltip: "Expand to forcibly complete a player's daily or weekly quest.",
            buildContent: c => FormBuilder.Build(c,
                title: "Complete quest",
                commandTemplate: ".quest c {player} {schedule}",
                new PlayerNameField("player", "Player"),
                new EnumField<PlayerStateService.QuestSchedule>("schedule", "Schedule",
                    defaultValue: PlayerStateService.QuestSchedule.Daily,
                    tooltip: "Daily or Weekly.")));

        AddSpacer(page, 6);
        AddBodyText(page,
            "All admin commands now have forms. If you aren't an admin on this server, commands return a permission error.");
    }

    // -----------------------------------------------------------------------
    // KindredLogistics tab (Kindred group)
    //
    // Surfaces the KindredLogistics server mod's 28 chat commands as buttons +
    // forms. KindredLogistics returns no structured data, so every control here
    // just fires-and-forgets - the server echoes confirmation/state into chat
    // (which the player can read with `.l s` / `.lg s` settings buttons).
    // -----------------------------------------------------------------------

    private void BuildKindredLogisticsTab(GameObject page)
    {
        // 0.10.12: card-wrap the intro + personal-toggles + utility sections.
        var introCard = AddCard(page, "KLIntroCard");
        AddBodyText(introCard,
            $"Requires the KindredLogistics server mod. Personal toggles affect only your character; admin globals affect the whole server (admin only). Personal toggles use {Mono(".l ...")}; admin globals use {Mono(".lg ...")}.");

        AddSpacer(page, 6);

        var personalCard = AddCard(page, "KLPersonalCard");
        AddSectionHeading(personalCard, "Personal Toggles (.l)");

        var pr1 = AddKLRow(personalCard, "KLPersonal1");
        AddCommandButton(pr1, "Sort Stash",     MessageService.BCCOM_KL_SORT_STASH,
            "Toggle auto-stash on double-click of the sort button (.l ss).");
        AddCommandButton(pr1, "Craft Pull",     MessageService.BCCOM_KL_CRAFT_PULL,
            "Toggle right-click on a recipe pulling missing ingredients (.l cr).");
        AddCommandButton(pr1, "Don't Pull Last", MessageService.BCCOM_KL_DONT_PULL_LAST,
            "Toggle never pulling the last item from a container (.l dpl).");
        AddCommandButton(pr1, "Servant Stash",  MessageService.BCCOM_KL_AUTOSTASH_MISSION,
            "Toggle auto-stash of servant mission rewards (.l asm).");

        var pr2 = AddKLRow(personalCard, "KLPersonal2");
        AddCommandButton(pr2, "Conveyor",      MessageService.BCCOM_KL_CONVEYOR,
            "Toggle named sender/receiver chests routing items between them (.l co).");
        AddCommandButton(pr2, "Salvage",       MessageService.BCCOM_KL_SALVAGE,
            "Toggle chests named 'salvage' auto-salvaging their contents (.l sal).");
        AddCommandButton(pr2, "Unit Spawner",  MessageService.BCCOM_KL_UNIT_SPAWNER,
            "Toggle chests named 'spawner' auto-filling unit stations (.l us).");
        AddCommandButton(pr2, "Brazier",       MessageService.BCCOM_KL_BRAZIER,
            "Toggle chests named 'brazier' auto-fueling braziers (.l bz).");

        var pr3 = AddKLRow(personalCard, "KLPersonal3");
        AddCommandButton(pr3, "Silent Pull",   MessageService.BCCOM_KL_SILENT_PULL,
            "Toggle suppressing chat messages when pulling items (.l sp).");
        AddCommandButton(pr3, "Silent Stash",  MessageService.BCCOM_KL_SILENT_STASH,
            "Toggle suppressing chat messages when stashing items (.l ssh).");
        AddCommandButton(pr3, "Show Settings", MessageService.BCCOM_KL_SETTINGS,
            "Print your current personal Logistics settings into chat (.l s).");

        AddSpacer(page, 6);

        var utilCard = AddCard(page, "KLUtilityCard");
        AddSectionHeading(utilCard, "Utility");
        var util = AddKLRow(utilCard, "KLUtility");
        AddCommandButton(util, "Stash All",    MessageService.BCCOM_KL_STASH_ALL,
            "Stash all items in your inventory into nearby chests (.stash).");

        CollapsibleSection.Build(utilCard,
            title: "Pull item from containers (.pull)",
            startExpanded: false,
            tooltip: "Pulls a specific item (and quantity) from nearby chests into your inventory.",
            buildContent: c => FormBuilder.Build(c,
                title: "Pull item",
                commandTemplate: ".pull {item} {quantity}",
                new TextField("item", "Item name",
                    tooltip: "Item to pull. Exact match against the item's prefab name (e.g. 'Iron Ingot')."),
                new IntField("quantity", "Quantity", min: 1, max: 9999,
                    tooltip: "How many to pull. KindredLogistics caps at what's available across all reachable chests.")));

        CollapsibleSection.Build(utilCard,
            title: "Find item (.fi)",
            startExpanded: false,
            tooltip: "Locates the specified item in nearby chests and prints which chest holds it.",
            buildContent: c => FormBuilder.Build(c,
                title: "Find item",
                commandTemplate: ".fi {item}",
                new TextField("item", "Item name",
                    tooltip: "Item to search for. Exact match against the item's prefab name.")));

        CollapsibleSection.Build(utilCard,
            title: "Find chest by name (.fc)",
            startExpanded: false,
            tooltip: "Locates chests with the specified custom name.",
            buildContent: c => FormBuilder.Build(c,
                title: "Find chest",
                commandTemplate: ".fc {name}",
                new TextField("name", "Chest name",
                    tooltip: "The custom name written on the chest's sign (e.g. 'salvage', 'spawner', 'brazier').")));

        // Admin globals (.lg ...) live on the dedicated KindredLogisticsAdminTab.
    }

    private void BuildKindredLogisticsAdminTab(GameObject page)
    {
        // 0.10.12: card-wrap the admin info + admin-globals + spawn-form
        // sections.
        var noteCard = AddCard(page, "KLAdminNoteCard");
        RenderAdminInfoNote(noteCard, "Kindred Logistics admin");
        AddBodyText(noteCard,
            "Server-wide toggles for the KindredLogistics features. These affect every player on the server. Requires admin permission server-side.");

        AddSpacer(page, 6);

        var globalsCard = AddCard(page, "KLAdminGlobalsCard");
        AddSectionHeading(globalsCard, "Admin Globals (.lg)");

        var ar1 = AddKLRow(globalsCard, "KLAdmin1");
        AddCommandButton(ar1, "Sort Stash",      MessageService.BCCOM_KL_ADMIN_SORT_STASH,
            "Server-wide: enable auto-stash on sort double-click (.lg ss).");
        AddCommandButton(ar1, "Pull",            MessageService.BCCOM_KL_ADMIN_PULL,
            "Server-wide: enable the .pull command for all players (.lg p).");
        AddCommandButton(ar1, "Craft Pull",      MessageService.BCCOM_KL_ADMIN_CRAFT_PULL,
            "Server-wide: enable right-click-recipe ingredient pulling (.lg cr).");
        AddCommandButton(ar1, "Servant Stash",   MessageService.BCCOM_KL_ADMIN_AUTOSTASH_MISSION,
            "Server-wide: enable auto-stash for servant mission rewards (.lg asm).");

        var ar2 = AddKLRow(globalsCard, "KLAdmin2");
        AddCommandButton(ar2, "Conveyor",        MessageService.BCCOM_KL_ADMIN_CONVEYOR,
            "Server-wide: enable sender/receiver conveyor chests (.lg co).");
        AddCommandButton(ar2, "Salvage",         MessageService.BCCOM_KL_ADMIN_SALVAGE,
            "Server-wide: enable 'salvage' chests (.lg sal).");
        AddCommandButton(ar2, "Unit Spawner",    MessageService.BCCOM_KL_ADMIN_UNIT_SPAWNER,
            "Server-wide: enable 'spawner' chests filling unit stations (.lg us).");
        AddCommandButton(ar2, "Brazier",         MessageService.BCCOM_KL_ADMIN_BRAZIER,
            "Server-wide: enable 'brazier' chests auto-fueling braziers (.lg bz).");

        var ar3 = AddKLRow(globalsCard, "KLAdmin3");
        AddCommandButton(ar3, "Named Brazier",   MessageService.BCCOM_KL_ADMIN_NAMED_BRAZIER,
            "Server-wide: enable night/proximity-controlled named braziers (.lg nam).");
        AddCommandButton(ar3, "Trash",           MessageService.BCCOM_KL_ADMIN_TRASH,
            "Server-wide: allow 'trash' chests to delete their contents (.lg trash).");
        AddCommandButton(ar3, "Show Settings",   MessageService.BCCOM_KL_ADMIN_SETTINGS,
            "Print the current server-wide Logistics settings into chat (.lg s).");
        AddCommandButton(ar3, "Empty Trash",     MessageService.BCCOM_KL_ADMIN_EMPTY_TRASH,
            "Empty all trash containers in your current territory (.emptytrash).");

        AddSpacer(page, 6);

        var spawnCard = AddCard(page, "KLAdminSpawnCard");
        AddSectionHeading(spawnCard, "Admin Item Spawn");
        CollapsibleSection.Build(spawnCard,
            title: "Spawn item to territory stash (.adminstash)",
            startExpanded: false,
            tooltip: "Spawns a quantity of an item directly into the current territory's stash containers.",
            buildContent: c => FormBuilder.Build(c,
                title: "Admin stash spawn",
                commandTemplate: ".adminstash {item} {quantity}",
                new TextField("item", "Item name",
                    tooltip: "Item prefab name to spawn (e.g. 'Iron Ingot', 'Blood Essence')."),
                new IntField("quantity", "Quantity", min: 1, max: 9999,
                    tooltip: "How many to spawn.")));
    }

    // Action row matching the look used by other tabs (Familiars, Class, etc.):
    // horizontal group, child controls expand, single-row layout.
    private static GameObject AddKLRow(GameObject parent, string id)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, id,
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        return row;
    }

    // -----------------------------------------------------------------------
    // KindredCommands - Player tab (Kindred group)
    //
    // Surfaces the 13 player-facing (non-admin) KindredCommands. The much
    // larger admin surface (~120 commands) lands in Phase 5i as its own
    // sub-tabs - keeping the player view minimal here.
    // -----------------------------------------------------------------------

    private void BuildKindredCommandsPlayerTab(GameObject page)
    {
        // 0.10.12: card-wrap Intro / Self / Server info / Lookups.
        var introCard = AddCard(page, "KCPlayerIntroCard");
        AddBodyText(introCard,
            "Requires the KindredCommands server mod. Player-facing commands only — admin commands land in their own tab.");

        AddSpacer(page, 6);

        var selfCard = AddCard(page, "KCSelfCard");
        AddSectionHeading(selfCard, "Self");
        var selfRow = AddKLRow(selfCard, "KCSelf");
        AddCommandButton(selfRow, "AFK",   MessageService.BCCOM_KC_AFK,
            "Toggle AFK animation - locks WASD movement until you run .afk again (.afk).");
        AddCommandButton(selfRow, "Ping",  MessageService.BCCOM_KC_PING,
            "Show your latency in chat (.ping).");
        AddCommandButton(selfRow, "Pace",  MessageService.BCCOM_KC_PACE,
            "Pace at the closest NPC near you - a cosmetic walk loop (.pace).");

        AddSpacer(page, 6);

        var infoCard = AddCard(page, "KCInfoCard");
        AddSectionHeading(infoCard, "Server info");
        var infoRow1 = AddKLRow(infoCard, "KCInfo1");
        AddCommandButton(infoRow1, "Server Time", MessageService.BCCOM_KC_TIME,
            "Print the current server time into chat (.time).");
        AddCommandButton(infoRow1, "Online Staff", MessageService.BCCOM_KC_STAFF,
            "List staff members currently online (.staff).");
        AddCommandButton(infoRow1, "Open Plots",   MessageService.BCCOM_KC_CASTLE_OPEN_PLOTS,
            "Report territories with open or decaying castle plots (.openplots — alias .op). Reply appears in chat.");
        AddCommandButton(infoRow1, "Soulshards",   MessageService.BCCOM_KC_GEAR_SOULSHARD_STATUS,
            "Print the status of soulshards on the server (.gear soulshardstatus).");

        var infoRow2 = AddKLRow(infoCard, "KCInfo2");
        AddCommandButton(infoRow2, "Boss List",   MessageService.BCCOM_KC_BOSS_LIST,
            "List all locked bosses on the server (.boss list).");
        AddCommandButton(infoRow2, "Region List", MessageService.BCCOM_KC_REGION_LIST,
            "List all locked and gated regions on the server (.region list).");

        BuildClanListPager(infoRow2);

        AddSpacer(page, 6);

        var lookupsCard = AddCard(page, "KCLookupsCard");
        AddSectionHeading(lookupsCard, "Lookups");

        CollapsibleSection.Build(lookupsCard,
            title: "Check player level (.checklevel)",
            startExpanded: false,
            tooltip: "Print a player's current level into chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Check player level",
                commandTemplate: ".checklevel {player}",
                new PlayerNameField("player", "Player",
                    tooltip: "Player whose level you want to look up. Exact character-name match.")));

        CollapsibleSection.Build(lookupsCard,
            title: "List clan members (.clan members)",
            startExpanded: false,
            tooltip: "List the members of a specific clan.",
            buildContent: c => FormBuilder.Build(c,
                title: "List clan members",
                commandTemplate: ".clan members {clan}",
                new TextField("clan", "Clan name",
                    tooltip: "Exact clan name. Use the Clan List pager above to find it.")));
    }

    // Stateful pager for `.clan list <page>` - three buttons in a row plus a
    // current-page label between Prev and Next. Page-1 is fired on click of
    // any button; the label reflects which page the next press will request.
    private void BuildClanListPager(GameObject parent)
    {
        var prev = UIFactory.CreateButton(parent, "ClanListPrev", "<");
        UIFactory.SetLayoutElement(prev.GameObject,
            minWidth: 32, preferredWidth: 36, flexibleWidth: 0,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        TooltipHover.Attach(prev.GameObject, "Previous page of clans (.clan list <page-1>).");
        prev.OnClick = () =>
        {
            if (_clanListPage > 1) _clanListPage--;
            RefreshClanListPage(send: true);
        };

        _clanListPageLabel = UIFactory.CreateLabel(parent, "ClanListPage",
            $"Clan List p{_clanListPage}",
            TextAlignmentOptions.Center, color: null, fontSize: Theme.ScaledUI(12)).TextMesh;
        UIFactory.SetLayoutElement(_clanListPageLabel.gameObject,
            minWidth: 90, preferredWidth: 100, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        _clanListPageLabel.enableWordWrapping = false;
        _clanListPageLabel.overflowMode = TextOverflowModes.Overflow;

        var next = UIFactory.CreateButton(parent, "ClanListNext", ">");
        UIFactory.SetLayoutElement(next.GameObject,
            minWidth: 32, preferredWidth: 36, flexibleWidth: 0,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        TooltipHover.Attach(next.GameObject, "Next page of clans (.clan list <page+1>).");
        next.OnClick = () =>
        {
            _clanListPage++;
            RefreshClanListPage(send: true);
        };
    }

    private void RefreshClanListPage(bool send)
    {
        if (_clanListPageLabel != null)
            _clanListPageLabel.text = $"Clan List p{_clanListPage}";
        if (send)
            MessageService.EnqueueMessage($"{MessageService.BCCOM_KC_CLAN_LIST} {_clanListPage}");
    }

    // -----------------------------------------------------------------------
    // Quick Start Guide tab (Help group)
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // Vanilla Admin Reference tab (Help group) — documents the in-game console
    // commands BCH cannot trigger directly, plus the chat-command equivalents
    // we DO surface elsewhere.
    // -----------------------------------------------------------------------

    private void BuildVanillaAdminTab(GameObject page)
    {
        AddGuideSection(page,
            "Why this is reference, not action",
            "V Rising's vanilla admin commands (the ones the game ships with — " +
            "no mods needed) are CONSOLE commands, typed into the in-game " +
            "console (default key F1) — NOT chat commands. They use a different " +
            "input pipeline than mod commands like .fam or .lvl. BloodCraftHub " +
            "is a CLIENT mod that sends CHAT messages, so it can't trigger " +
            "console commands directly.\n\n" +
            "The good news: KindredCommands re-implements most of the " +
            "common admin actions (kick / ban / give / spawn / teleport / etc.) " +
            "as CHAT commands — and BCH wires every one of those into the " +
            "KINDRED → Admin: Players / Server / World tabs. So the actions " +
            "you want to take are already covered there. This tab is a " +
            "reference for the underlying vanilla console commands in case " +
            "you need to type them yourself (open the console with F1).");

        // 0.10.7: switched from monolithic AddGuideSection bodies (manually
        // tab-aligned, proportional font misaligned the columns) to the new
        // AddCommandTable helper for crisp 2-column layout.
        AddCommandTable(page, "Authentication",
            ("adminauth",          "Grant yourself admin powers (needed before any other vanilla admin command)."),
            ("adminderegister",    "Drop admin powers for the current session."));

        AddCommandTable(page, "Player management",
            ("Kick <CharacterName>",       "Kick a player by name."),
            ("BanUser <SteamID>",          "Ban a player by SteamID."),
            ("Banhammer <SteamID>",        "Ban + delete the player's characters."),
            ("Unban <UserIndex>",          "Unban (use BanList to find the index)."),
            ("BanList",                    "List current bans."),
            ("Mute <SteamID> <minutes>",   "Silence a player."),
            ("PlayerInfo <CharacterName>", "Print info about a specific player."),
            ("UserList",                   "List all users registered on the server."),
            ("WhoIsOnline",                "List currently-connected players."),
            ("Connectinfo",                "Print connection info for diagnostics."),
            ("ForceConnectInfo",           "Force-refresh connection info display."));
        AddGuideSection(page, "",
            "Chat-command equivalents already in BCH (KINDRED → Admin: Players):  .kick, .ban (via Kindred or vanilla), .unban, etc.");

        AddCommandTable(page, "Character actions",
            ("Suicide",                    "Kill your own character (no penalty)."),
            ("KillPlayer <CharacterName>", "Kill a player."),
            ("RevivePlayer <Name>",        "Revive a downed player."),
            ("HealPlayer <Name>",          "Fully restore a player's HP."),
            ("DamagePlayer <Name> <amt>",  "Apply damage to a player."),
            ("ResetCharacter <Name>",      "Fully reset a player's character (DESTRUCTIVE)."),
            ("KillUnit",                   "Kill the unit your reticle is targeting."),
            ("HealUnit",                   "Heal the targeted unit to full."),
            ("DamageUnit <amount>",        "Apply damage to the targeted unit."),
            ("Despawn",                    "Despawn the targeted unit."));

        AddCommandTable(page, "Item / character spawning",
            ("give <PrefabName>",          "Give yourself an item by prefab name."),
            ("giveset",                    "Open the giveset menu (sets of armor/weapons)."),
            ("SpawnUnit <PrefabName>",     "Spawn an NPC at your position."),
            ("SpawnCastle <PrefabName>",   "Spawn a castle structure."),
            ("FillStorage",                "Fill the targeted storage container."),
            ("ClearAllInventories",        "Wipe every inventory on the server (DESTRUCTIVE)."),
            ("DespawnAll",                 "Despawn all units in the world (DESTRUCTIVE)."));
        AddGuideSection(page, "",
            "Chat-command equivalents already in BCH (KINDRED → Admin: World):  .give {item} {qty},  .spawnnpc / .customspawn / .customspawnat.  Use the Lookups section on the same tab to find prefab names.");

        AddCommandTable(page, "Teleportation",
            ("teleporttowaypoint <name>",  "Teleport to a named waypoint."),
            ("TeleportToPlayer <Name>",    "Teleport to a specific player."),
            ("TeleportToBoss <Boss>",      "Teleport to a boss's spawn location."),
            ("TeleportToHorse",            "Teleport to your horse (if any)."),
            ("TeleportToOwner",            "Teleport to the targeted creature's owner."),
            ("TeleportToWorld <x> <z>",    "Teleport to absolute world coordinates."),
            ("UnlockAllPlayerWaypoints",   "Unlock every waypoint for a player."),
            ("MapMarker <args>",           "Add / manage map markers."));
        AddGuideSection(page, "",
            "Chat-command equivalents:  .teleport {x} {y} {z} {player},  .tpb {boss} (Kindred teleport-to-boss).");

        AddCommandTable(page, "Time, world & difficulty",
            ("Time",                       "Print current server time."),
            ("ChangeMapTime <hh:mm>",      "Set the in-world time of day."),
            ("SetTimeOfDay <hh:mm>",       "Alias of ChangeMapTime on some versions."),
            ("weather <type>",             "Change weather (clear / rain / mist / storm)."),
            ("GameDifficulty <level>",     "Adjust the server's difficulty."),
            ("Lockdown",                   "Toggle PvP / siege lockdown."),
            ("alllockdown",                "Server-wide lockdown."));

        AddCommandTable(page, "Server administration",
            ("Save",                       "Force a server save."),
            ("AutoSave",                   "Enable auto-save."),
            ("StopAutoSave",               "Disable auto-save."),
            ("ReloadServerSettings",       "Re-apply ServerHostSettings.json without restart."),
            ("Restart",                    "Restart the server."),
            ("Disconnect",                 "Disconnect yourself from the server."),
            ("Quit",                       "Close the V Rising client."),
            ("List",                       "List every available console command (live reference)."),
            ("Help <command>",             "Detailed help for a specific command."),
            ("ShowVersion",                "Display the V Rising client/server version."),
            ("ShowAdminCommands",          "List admin-only commands (live filtered List)."));

        AddCommandTable(page, "Debugging / display",
            ("DebugHud",                   "Toggle the debug heads-up display."),
            ("ShowDebugUI",                "Toggle extended debug UI."),
            ("ShowFPS",                    "Show frame-rate counter."),
            ("ShowInputBindings",          "List current input bindings."),
            ("BlockUserInput",             "Block all input (anti-stuck recovery)."),
            ("Console.SetCheats",          "Enable cheat-level commands (requires extra setup)."));

        AddGuideSection(page,
            "Authoritative list",
            "V Rising occasionally adds/removes commands between patches. The " +
            "in-game console's `List` command always shows the live set the " +
            "current build accepts — use it as the authoritative reference if " +
            "any command listed here is rejected. `Help <command>` prints " +
            "usage details for a specific entry.");

        AddGuideSection(page,
            "How to use the in-game console",
            "  1. Press F1 while in-game (or whatever your bound console key is)\n" +
            "  2. Type the command exactly as written (case can matter for some)\n" +
            "  3. Press Enter\n\n" +
            "If F1 doesn't open the console, check your keybindings in the V " +
            "Rising settings or run with `-console` on the command line.\n\n" +
            "If a command says 'access denied' you haven't run adminauth yet, " +
            "OR your SteamID isn't in the server's adminlist (server-side config).");
    }

    // -----------------------------------------------------------------------
    // About tab (Help group) — credits + community links
    // -----------------------------------------------------------------------

    private void BuildAboutTab(GameObject page)
    {
        // 0.10.8: reworked from a stack of dense AddGuideSection calls into a
        // four-region layout with explicit spacers between regions, so the
        // page reads as discrete cards instead of one wall of text:
        //   1. Header band — version + one-line description
        //   2. Acknowledgements — Bloodcraft + KindredCommands credits
        //   3. About me / community — author info + support links
        //   4. Project — GitHub / Thunderstore / license footer
        // Each region opens with a section heading; AddSpacer separates them.

        // ── Region 1 ─────────────────────────────────────────────────────
        AddGuideSection(page,
            $"BloodCraftHub  v{MyPluginInfo.PLUGIN_VERSION}",
            "A unified CLIENT UI for the Bloodcraft suite of V Rising " +
            "server mods. Surfaces every Bloodcraft, KindredCommands, and " +
            "KindredLogistics chat command as buttons and forms — no more " +
            "typing in chat to manage your familiars, run a class change, " +
            "or fire an admin command. Live progress overlays for XP, " +
            "weapon expertise, blood legacy, familiars, professions, and " +
            "daily quests stream in at ~1 Hz over the signed [ECLIPSE] " +
            "protocol Bloodcraft already speaks, so the cost on the " +
            "server is the same as if you had Eclipse installed.");
        AddSpacer(page, 12);

        // ── Region 2 ─────────────────────────────────────────────────────
        AddSectionHeading(page, "Mods this UI is built on");
        AddGuideSection(page, "",
            "BloodCraftHub is purely a client-side overlay — it doesn't " +
            "modify the server or add new gameplay systems. Every feature " +
            "you see is wrapping the chat-command surface of these " +
            "server-side mods by other developers:");

        AddSpacer(page, 4);
        AddGuideSection(page,
            "Bloodcraft  —  by zfolmt",
            "Leveling, weapon expertise, blood legacies, professions, " +
            "familiars, classes, quests, and prestige. The bulk of what " +
            "BloodCraftHub surfaces (every tab in the BLOODCRAFT group) " +
            "would not exist without zfolmt's mod.");
        AddLinkRow(page, "Bloodcraft on Thunderstore",
            "https://thunderstore.io/c/v-rising/p/zfolmt/Bloodcraft/");

        AddSpacer(page, 6);
        AddGuideSection(page,
            "KindredCommands  —  by odjit",
            "Commands to expand server administration and add quality-" +
            "of-life affordances for players. The KINDRED admin tabs " +
            "(Players, Server, World) and the entire Logistics section " +
            "call into odjit's mods.");
        AddLinkRow(page, "KindredCommands on Thunderstore",
            "https://thunderstore.io/c/v-rising/p/odjit/KindredCommands/");
        AddSpacer(page, 12);

        // ── Region 3 ─────────────────────────────────────────────────────
        AddSectionHeading(page, "About the author");
        AddGuideSection(page, "",
            "Maintained by kdpen (in-game: Chaos). I play on The Shadow " +
            "Realm — a Brutal, PvE community server — and built this mod " +
            "to give that community a click-driven alternative to typing " +
            "every Bloodcraft command. Feedback, bug reports, and pull " +
            "requests are very welcome through any of the links below.");
        AddSpacer(page, 4);

        AddLinkRow(page, "Server Discord  (The Shadow Realm)",
            "https://discord.gg/usC9QgBrXK");
        // 0.9.8: direct-message link to the author. Friend-testing: users
        // wanted a way to reach me one-on-one for mod feedback / bug reports
        // without joining the server Discord first.
        AddLinkRow(page, "DM me on Discord  (PerpetualChaos)",
            "https://discord.com/users/PerpetualChaos");
        AddLinkRow(page, "Support development  (PayPal)",
            "https://www.paypal.com/paypalme/KrisPenland");
        AddLinkRow(page, "SkillEra.IO  (other projects)",
            "https://SkillEra.IO");
        AddSpacer(page, 12);

        // ── Region 4 ─────────────────────────────────────────────────────
        AddSectionHeading(page, "Project");
        AddGuideSection(page, "",
            "BloodCraftHub is open source under the MIT license. The " +
            "repository, the release feed, and every prior version's " +
            "CHANGELOG entry are public:");
        AddSpacer(page, 4);

        AddLinkRow(page, "GitHub repository",
            "https://github.com/KDavidP1987/BloodCraftHub");
        AddLinkRow(page, "Thunderstore listing",
            "https://thunderstore.io/c/v-rising/p/kdpen/BloodCraftHub/");
        AddSpacer(page, 6);

        AddGuideSection(page, "",
            "Bloodcraft compatibility: v1.13.x   •   License: MIT   •   " +
            "Plugin GUID: kdpen.BloodCraftHub");
    }

    // -----------------------------------------------------------------------
    // Settings tab (Help group) — 0.9.0 sections, 0.9.2 promoted to its own tab.
    //
    // Three segmented controls (text scale UI, text scale overlay, plus a
    // grid of per-overlay transparency selectors) and the chat-noise toggle.
    // 0.9.2 hooks rebuild + opacity-refresh so changes take effect live.
    // -----------------------------------------------------------------------

    private void BuildSettingsTab(GameObject page)
    {
        BuildDisplaySettingsSection(page);
    }

    private void BuildDisplaySettingsSection(GameObject page)
    {
        AddGuideSection(page,
            "Display settings",
            "Adjust text size and overlay transparency. " +
            "Text-size changes apply when the panel is closed and reopened " +
            "(or when an overlay is toggled off and back on). Transparency " +
            "changes apply immediately. 0% transparency = solid background; " +
            "100% transparency = invisible background (capped internally at " +
            "95% so the drag handle stays visible).");

        AddTextScaleRow(page, "UI text size",
            currentScaleSetting: () => Config.Settings.UITextScale,
            applyScale: v => {
                Config.Settings.SetUITextScale(v);
                UI.Framework.CustomLib.Util.Theme.UIFontMultiplier = v;
                // 0.9.2: rebuild the main panel so labels pick up the new
                // multiplier. Deferred to next frame so this click handler
                // completes before the panel hosting it is destroyed.
                Plugin.UIManager.RequestRebuildMainPanel();
            });

        AddTextScaleRow(page, "Overlay text size",
            currentScaleSetting: () => Config.Settings.OverlayTextScale,
            applyScale: v => {
                Config.Settings.SetOverlayTextScale(v);
                UI.Framework.CustomLib.Util.Theme.OverlayFontMultiplier = v;
                // 0.9.2: rebuild each overlay so its labels pick up the new
                // multiplier. Only rebuilds overlays the user has enabled
                // via the per-overlay toggles — disabled overlays stay
                // un-constructed.
                Plugin.UIManager.RequestRebuildAllOverlays();
            });

        AddSpacer(page, 4);
        AddSectionHeading(page, "Overlay transparency");

        AddTransparencyRow(page, "XP overlay",
            () => Config.Settings.XPOverlayTransparency,
            v => Config.Settings.SetXPOverlayTransparency(v));
        AddTransparencyRow(page, "Familiar overlay",
            () => Config.Settings.FamiliarOverlayTransparency,
            v => Config.Settings.SetFamiliarOverlayTransparency(v));
        AddTransparencyRow(page, "Familiar Browser",
            () => Config.Settings.FamiliarBrowserTransparency,
            v => Config.Settings.SetFamiliarBrowserTransparency(v));
        AddTransparencyRow(page, "Daily quest",
            () => Config.Settings.DailyQuestTransparency,
            v => Config.Settings.SetDailyQuestTransparency(v));
        AddTransparencyRow(page, "Professions",
            () => Config.Settings.ProfessionOverlayTransparency,
            v => Config.Settings.SetProfessionOverlayTransparency(v));

        AddSpacer(page, 8);
        BuildPanelBackgroundColorSection(page);

        AddSpacer(page, 8);
        AddSectionHeading(page, "HUD extras");
        AddShowProgressBarsToggle(page);
        AddShowOverlayBonusStatsToggle(page);
        AddShowOverlayXpCounterToggle(page);
        AddProgressBarHeightControls(page);
        AddOverlayEdgePaddingControls(page);
        AddShowPrestigeSubLineToggle(page);
        AddOverlayAlignmentToggle(page);
        AddAutoScanVBloodsToggle(page);
        AddSpacer(page, 8);
        AddSectionHeading(page, "Chat noise");
        AddSuppressActionChatterToggle(page);
        AddSpacer(page, 8);
        // 0.9.7: per-component size adjustment + reset-to-default controls.
        BuildSizePositioningSection(page);
        AddSpacer(page, 8);
        // 0.10.6: Chat Logging diagnostic toggles. At the bottom of Settings
        // so users see it last when scanning the page top-to-bottom.
        BuildChatLoggingSection(page);
    }

    // -----------------------------------------------------------------------
    // 0.10.6: Chat Logging section
    //
    // Diagnostic visibility controls for chat replies BCH parses + mirrors to
    // its own UI. Three toggles (BchAuto / Bloodcraft / Kindred) plus master
    // Show All / Hide All buttons. Per the design discussion with the user:
    //   - Toggling a category off suppresses ONLY the chat copies of commands
    //     whose data BCH renders structurally. Action confirmations and any
    //     command BCH doesn't parse stay visible regardless.
    //   - Suppression doesn't touch ClearServerMessages (the global admin
    //     toggle) — those are independent.
    //   - Data extraction always works regardless of these settings (the
    //     intercept parses BEFORE the destroy decision, so flipping every
    //     toggle to "hide" doesn't break any feature).
    // -----------------------------------------------------------------------
    private void BuildChatLoggingSection(GameObject page)
    {
        AddSectionHeading(page, "Chat Logging");

        // 0.10.13: dropped italic + bumped 11 → 13 + applied muted color
        // so the help paragraph is legible at standard text size.
        var help = UIFactory.CreateLabel(page, "ChatLoggingHelp",
            $"<color={Theme.MutedBodyHex}>Diagnostic toggles. Each controls whether chat shows the SERVER REPLIES " +
            "to commands BCH already mirrors to its UI. Action confirmations and " +
            "commands without a BCH UI display stay visible regardless. Suppression " +
            "is purely cosmetic — data collection (familiars, V-Bloods, expertise, " +
            "etc.) always works.</color>",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(help.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 48, preferredHeight: 68, flexibleHeight: 0);
        help.TextMesh.fontStyle = FontStyles.Normal;
        help.TextMesh.enableWordWrapping = true;
        help.TextMesh.overflowMode = TextOverflowModes.Overflow;

        // Master Show All / Hide All buttons row.
        var masterRow = UIFactory.CreateHorizontalGroup(page, "ChatLogMasterRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(masterRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var showAll = UIFactory.CreateButton(masterRow, "ChatShowAllBtn", "Show all mod chat");
        UIFactory.SetLayoutElement(showAll.GameObject,
            minWidth: 140, preferredWidth: 180, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var showAllTxt = showAll.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (showAllTxt != null) { showAllTxt.fontSize = Theme.ScaledUI(12); showAllTxt.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(showAll.GameObject,
            "Enable visibility for all three Chat Logging categories — useful when diagnosing why a BCH feature isn't picking up server data. Does NOT touch the global ClearServerMessages admin setting.");
        showAll.OnClick = () => { Config.Settings.ShowAllChat(); RefreshChatLoggingTogglesUI(); };

        var hideAll = UIFactory.CreateButton(masterRow, "ChatHideAllBtn", "Hide all mod chat");
        UIFactory.SetLayoutElement(hideAll.GameObject,
            minWidth: 140, preferredWidth: 180, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var hideAllTxt = hideAll.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (hideAllTxt != null) { hideAllTxt.fontSize = Theme.ScaledUI(12); hideAllTxt.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(hideAll.GameObject,
            "Disable visibility for all three Chat Logging categories — maximum chat quiet. Action confirmations and any command BCH doesn't parse will still appear in chat (those have no alternative UI display).");
        hideAll.OnClick = () => { Config.Settings.HideAllChat(); RefreshChatLoggingTogglesUI(); };

        AddSpacer(page, 4);

        _chatBchAutoToggle    = AddChatLoggingToggle(page, "BCH internal auto-fires",
            "Replies to BCH's own automatic background commands — V-Blood scanner searches, the XP overlay bonus-stats ticker, the Wep/Blood-Legacy tab auto-refresh. Off by default to avoid spam from background polling. Turn ON if you want to see what BCH is sending and verify the server is replying.",
            () => Config.Settings.ShowChatBchAuto,
            v => Config.Settings.SetShowChatBchAuto(v));

        _chatBloodcraftToggle = AddChatLoggingToggle(page, "Bloodcraft command replies",
            "Replies to user-initiated Bloodcraft commands BCH structurally parses — .fam boxes / .fam l / .fam s / .bl get / .wep get / .prestige get. On by default. Off = the BCH UI is the only place this data shows (less chat noise). Action confirmations (.fam b, .fam ub, etc.) and commands BCH doesn't parse stay visible regardless.",
            () => Config.Settings.ShowChatBloodcraft,
            v => Config.Settings.SetShowChatBloodcraft(v));

        _chatKindredToggle    = AddChatLoggingToggle(page, "Kindred command replies",
            "Same as the Bloodcraft toggle, applied to KindredCommands / KindredLogistics commands. BCH doesn't structurally parse any Kindred replies in this version, so the toggle is currently a no-op — reserved for future Kindred structured parsing.",
            () => Config.Settings.ShowChatKindred,
            v => Config.Settings.SetShowChatKindred(v));
    }

    private UI.Framework.UniverseLib.UI.Models.ToggleRef _chatBchAutoToggle;
    private UI.Framework.UniverseLib.UI.Models.ToggleRef _chatBloodcraftToggle;
    private UI.Framework.UniverseLib.UI.Models.ToggleRef _chatKindredToggle;

    private UI.Framework.UniverseLib.UI.Models.ToggleRef AddChatLoggingToggle(
        GameObject parent, string label, string tooltip,
        System.Func<bool> get, System.Action<bool> set)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"ChatLogRow_{label}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, $"ChatLogTog_{label}");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = label;
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = get();
        TooltipHover.Attach(t.GameObject, tooltip);
        t.OnValueChanged += v => set(v);
        return t;
    }

    /// <summary>0.10.6: re-read the three toggle states from settings and push to
    /// the rendered UI. Called after Show All / Hide All so the visible
    /// checkbox state matches the setting state.</summary>
    private void RefreshChatLoggingTogglesUI()
    {
        if (_chatBchAutoToggle    != null) _chatBchAutoToggle.Toggle.isOn    = Config.Settings.ShowChatBchAuto;
        if (_chatBloodcraftToggle != null) _chatBloodcraftToggle.Toggle.isOn = Config.Settings.ShowChatBloodcraft;
        if (_chatKindredToggle    != null) _chatKindredToggle.Toggle.isOn    = Config.Settings.ShowChatKindred;
    }

    // -----------------------------------------------------------------------
    // 0.9.7: Size & Positioning section (Settings tab)
    //
    // Per-component subsections (Primary UI + each of the 5 overlays). Each
    // exposes [-]/[+] for width and height (20 px step; Shift+click = 100 px)
    // plus a [Default] button that calls SetDefaultSizeAndPosition(). Primary
    // UI additionally has [Auto-size] (mirrors footer toggle) and [Fullscreen]
    // (mirrors the title-bar maximize button). Manual drag-from-edge resize
    // remains unaffected — these controls just provide a click-driven
    // alternative for users who didn't realize the panels were resizable.
    // -----------------------------------------------------------------------

    private const int SIZE_STEP_NORMAL = 20;
    private const int SIZE_STEP_LARGE  = 100;

    // 0.9.8: list of size-readout refreshers. Each AddSizePosStepRow registers
    // its own refresh action here; the per-frame ticker (TickSizePosReadouts)
    // walks them while Settings is the active tab so dragging a panel by its
    // edge updates the readout immediately — pre-0.9.8 the readout only
    // refreshed inside the +/- click handlers, leaving manual drag-resize
    // out of sync. Cleared on Reset to avoid leaking references.
    private readonly System.Collections.Generic.List<System.Action> _sizePosRefreshers = new();
    private System.Action _sizePosReadoutTicker;

    private void BuildSizePositioningSection(GameObject page)
    {
        AddSectionHeading(page, "Size & Positioning");

        // 0.9.8: per-frame readout refresher. Self-gates on ActiveTab so it's
        // a no-op except when the Settings tab is open. Registered once when
        // the section builds.
        if (_sizePosReadoutTicker == null)
        {
            _sizePosReadoutTicker = TickSizePosReadouts;
            BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Add(_sizePosReadoutTicker);
        }

        // 0.10.13: dropped italic + bumped 11 → 13 + muted color for legibility.
        var help = UIFactory.CreateLabel(page, "SizePosHelp",
            $"<color={Theme.MutedBodyHex}>Click +/- to adjust the width/height of the main panel or any overlay " +
            "(hold Shift while clicking for 100 px steps). Default returns it to its " +
            "factory size and position. Drag-to-resize from any edge still works.</color>",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(help.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 36, preferredHeight: 52, flexibleHeight: 0);
        help.TextMesh.fontStyle = FontStyles.Normal;
        help.TextMesh.enableWordWrapping = true;
        help.TextMesh.overflowMode = TextOverflowModes.Overflow;

        AddSpacer(page, 4);
        BuildPrimaryUISizeControls(page);
        AddSpacer(page, 4);
        BuildOverlaySizeControls(page, "XP overlay",       () => Plugin.UIManager?.ExperienceOverlay);
        BuildOverlaySizeControls(page, "Familiar overlay", () => Plugin.UIManager?.FamiliarOverlay);
        BuildOverlaySizeControls(page, "Familiar Browser", () => Plugin.UIManager?.FamiliarBrowserOverlay);
        BuildOverlaySizeControls(page, "Daily Quest",      () => Plugin.UIManager?.DailyQuestOverlay);
        BuildOverlaySizeControls(page, "Professions",      () => Plugin.UIManager?.ProfessionOverlay);
    }

    private void BuildPrimaryUISizeControls(GameObject page)
    {
        AddSizePosSubHeading(page, "Primary UI");

        // Row: [Auto-size] [Fullscreen] [Default]
        var btnRow = UIFactory.CreateHorizontalGroup(page, "PrimaryUISizeBtns",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(btnRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 30, preferredHeight: 32, flexibleHeight: 0);

        AddSizePosButton(btnRow, "Auto-size",
            "Auto-resize the main panel vertically to fit the active tab's content. Mirrors the footer toggle.",
            () => {
                Config.Settings.SetIsPanelAutoResizeEnabled(!Config.Settings.IsPanelAutoResizeEnabled);
                AutoResizeIfEnabled();
            });
        AddSizePosButton(btnRow, "Fullscreen",
            "Toggle the main panel between its current size+position and a fullscreen stretch (with a small inset so the edges stay grabbable). Mirrors the maximize button on the title bar.",
            ToggleFullscreen);
        AddSizePosButton(btnRow, "Default",
            "Reset the main panel to its default size (does NOT move it — drag to re-center if you want).",
            () => { SetFullscreen(false); SetDefaultSize(); });

        // Width + Height step rows
        AddSizePosStepRow(page, "Width",
            () => Rect != null ? (int)Rect.sizeDelta.x : 0,
            d => { SetFullscreen(false); AdjustSize(d, 0); });
        AddSizePosStepRow(page, "Height",
            () => Rect != null ? (int)Rect.sizeDelta.y : 0,
            d => { SetFullscreen(false); AdjustSize(0, d); });
    }

    private void BuildOverlaySizeControls(GameObject page, string label,
        System.Func<BloodCraftHub.UI.Framework.CustomLib.Panel.ResizeablePanelBase> getter)
    {
        AddSizePosSubHeading(page, label);

        var btnRow = UIFactory.CreateHorizontalGroup(page, $"{label}_SizeBtns",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(btnRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 30, preferredHeight: 32, flexibleHeight: 0);

        AddSizePosButton(btnRow, "Default",
            $"Reset the {label}'s size to its default. Does NOT move it — drag the overlay if you also want to reset position. Only affects this overlay if it's currently open.",
            () => {
                var p = getter();
                if (p != null) p.SetDefaultSize();
            });

        AddSizePosStepRow(page, "Width",
            () => { var p = getter(); return p?.Rect != null ? (int)p.Rect.sizeDelta.x : 0; },
            d => { var p = getter(); if (p != null) p.AdjustSize(d, 0); });
        AddSizePosStepRow(page, "Height",
            () => { var p = getter(); return p?.Rect != null ? (int)p.Rect.sizeDelta.y : 0; },
            d => { var p = getter(); if (p != null) p.AdjustSize(0, d); });

        AddSpacer(page, 2);
    }

    private void AddSizePosSubHeading(GameObject parent, string text)
    {
        var lbl = UIFactory.CreateLabel(parent, $"SizePosSub_{text}", text,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Bold;
    }

    private void AddSizePosButton(GameObject parent, string label, string tooltip, System.Action onClick)
    {
        var btn = UIFactory.CreateButton(parent, $"SizePosBtn_{label}", label);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 90, preferredWidth: 110, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledUI(12); t.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(btn.GameObject, tooltip);
        btn.OnClick = onClick;
    }

    /// <summary>0.9.7: builds a "Width [-] 720 px [+]" row. The label text
    /// re-reads the current value from the supplied getter after each click
    /// so the user sees the immediate effect. Shift-click on +/- jumps by
    /// SIZE_STEP_LARGE instead of SIZE_STEP_NORMAL.</summary>
    private void AddSizePosStepRow(GameObject parent, string label,
        System.Func<int> getCurrent, System.Action<int> applyDelta)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"SizePosRow_{label}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);

        var lblText = UIFactory.CreateLabel(row, $"SizePosRowLabel_{label}", $"{label}:",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(lblText.GameObject,
            minWidth: 60, preferredWidth: 70, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var minusBtn = UIFactory.CreateButton(row, $"SizePosMinus_{label}", "−");
        UIFactory.SetLayoutElement(minusBtn.GameObject,
            minWidth: 32, preferredWidth: 32, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        var minusT = minusBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (minusT != null) { minusT.fontSize = Theme.ScaledUI(14); minusT.alignment = TextAlignmentOptions.Center; }

        var valLabel = UIFactory.CreateLabel(row, $"SizePosVal_{label}", $"{getCurrent()} px",
            TextAlignmentOptions.Center, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(valLabel.GameObject,
            minWidth: 60, preferredWidth: 80, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var plusBtn = UIFactory.CreateButton(row, $"SizePosPlus_{label}", "+");
        UIFactory.SetLayoutElement(plusBtn.GameObject,
            minWidth: 32, preferredWidth: 32, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        var plusT = plusBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (plusT != null) { plusT.fontSize = Theme.ScaledUI(14); plusT.alignment = TextAlignmentOptions.Center; }

        TooltipHover.Attach(minusBtn.GameObject, $"Shrink {label.ToLowerInvariant()} by {SIZE_STEP_NORMAL} px (Shift+click: {SIZE_STEP_LARGE} px).");
        TooltipHover.Attach(plusBtn.GameObject,  $"Grow {label.ToLowerInvariant()} by {SIZE_STEP_NORMAL} px (Shift+click: {SIZE_STEP_LARGE} px).");

        // Capture the val label so click handlers can refresh it after applying.
        System.Action refresh = () => {
            if (valLabel?.TextMesh == null) return;
            var text = $"{getCurrent()} px";
            if (valLabel.TextMesh.text != text)
                valLabel.TextMesh.text = text;
        };
        minusBtn.OnClick = () => { applyDelta(-CurrentStep()); refresh(); };
        plusBtn.OnClick  = () => { applyDelta( CurrentStep()); refresh(); };
        // 0.9.8: per-frame refresh path — picks up changes from manual edge-
        // drag resize so the readout stays in sync with the live panel size.
        _sizePosRefreshers.Add(refresh);
    }

    private void TickSizePosReadouts()
    {
        if (ActiveTab != PanelType.SettingsTab) return;
        if (!Enabled) return;
        // Iterate via index — refresh actions don't mutate the list at runtime,
        // but a foreach over a possibly-extended list would also be cheap.
        for (int i = 0; i < _sizePosRefreshers.Count; i++)
        {
            try { _sizePosRefreshers[i]?.Invoke(); }
            catch (System.Exception ex)
            {
                Utils.LogUtils.LogWarning($"Size-pos readout refresher #{i} threw: {ex.Message}");
            }
        }
    }

    /// <summary>0.9.7: detect Shift modifier at click time so users can hold
    /// Shift to jump by SIZE_STEP_LARGE instead of SIZE_STEP_NORMAL. Uses the
    /// legacy UnityEngine.Input API which is what V Rising's IL2CPP wrap
    /// exposes; reading is allocation-free.</summary>
    private static int CurrentStep()
    {
        bool shift = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift)
                  || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift);
        return shift ? SIZE_STEP_LARGE : SIZE_STEP_NORMAL;
    }

    /// <summary>0.9.2: toggle XP and prestige progress visualization as
    /// horizontal bars (alongside the existing % numeric value). Off by
    /// default. Applies immediately because each render re-reads the
    /// setting and toggles the bar GameObject's SetActive.</summary>
    private void AddShowProgressBarsToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "ShowProgressBarsRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, "ShowProgressBarsToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = "Show XP / Prestige progress as horizontal bars";
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = Config.Settings.ShowProgressBars;
        TooltipHover.Attach(t.GameObject,
            "When on, the XP overlay and the Prestige info box render a slim horizontal progress bar alongside the % / level value. Off by default — % numbers stay visible either way.");
        t.OnValueChanged += value =>
        {
            Config.Settings.SetShowProgressBars(value);
            // 0.9.2: force a re-render on the Prestige info panel so its bar
            // appears/disappears immediately. XP overlay re-reads the setting
            // on its next render tick (triggered when Bloodcraft pushes new
            // experience data, which it does multiple times per second).
            try { RenderPrestigeInfo(); } catch { /* prestige tab not built yet */ }
        };
    }

    /// <summary>0.9.6: toggle the XP overlay's per-row bonus-stat detail line
    /// (chosen stat names + current numeric values from .wep get / .bl get).
    /// Off by default. When toggled on, the overlay's always-on ticker starts
    /// auto-fetching .wep get and .bl get &lt;CurrentBlood&gt; every 10s while
    /// the overlay is visible. The render methods self-gate on the setting,
    /// so toggling off immediately hides the rows on the next frame.</summary>
    private void AddShowOverlayBonusStatsToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "ShowOverlayBonusStatsRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, "ShowOverlayBonusStatsToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = "Show weapon expertise & blood legacy bonus stats on the XP overlay";
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = Config.Settings.ShowOverlayBonusStats;
        TooltipHover.Attach(t.GameObject,
            "When on, the XP overlay shows the chosen bonus-stat names AND their current numeric values under the Weapon and Legacy rows (e.g. '+12.5% PhysicalPower'). Auto-fetches .wep get and .bl get every 10s while the overlay is visible. Off by default for a minimal HUD.");
        t.OnValueChanged += value =>
        {
            Config.Settings.SetShowOverlayBonusStats(value);
            // Overlay re-renders on its next frame tick (always-on
            // BonusStatsTick), so no explicit forced refresh needed here.
        };
    }

    // 0.10.7: Show numerical Exp/Ess counter under the Weapon and Legacy
    // rows on the XP overlay. Values come from parsing .wep get / .bl get
    // chat replies; off by default to match the rest of the HUD-extra toggles.
    private void AddShowOverlayXpCounterToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "ShowOverlayXpCounterRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, "ShowOverlayXpCounterToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = "Show numerical Exp / Ess counter on the XP overlay";
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = Config.Settings.ShowOverlayXpCounter;
        TooltipHover.Attach(t.GameObject,
            "Adds a sub-row under Weapon and Legacy showing 'Exp: 123 / 4500 (2.7%)' — current expertise / essence and the threshold to the next level. Derives the threshold from the percentage the server prints, so it's accurate to within ±1 of the true value.");
        t.OnValueChanged += value => Config.Settings.SetShowOverlayXpCounter(value);
    }

    // 0.10.7: Progress-bar height: relative-vs-absolute toggle + slider for
    // the absolute mode. Default is absolute (8 px) because user feedback was
    // that pre-0.10.7 bars grew aggressively when the overlay was enlarged
    // for additional info rows.
    private void AddProgressBarHeightControls(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "ProgressBarHeightRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var label = UIFactory.CreateLabel(row, "ProgressBarHeightLabel",
            "Progress bar height:", TextAlignmentOptions.MidlineLeft,
            color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(label.GameObject,
            minWidth: 160, preferredWidth: 180, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        // Decrement
        var minus = UIFactory.CreateButton(row, "ProgressBarHeightMinus", "−");
        UIFactory.SetLayoutElement(minus.Component.gameObject,
            minWidth: 30, preferredWidth: 30, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var valueLbl = UIFactory.CreateLabel(row, "ProgressBarHeightValue",
            $"{Config.Settings.ProgressBarHeight} px",
            TextAlignmentOptions.Center, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(valueLbl.GameObject,
            minWidth: 60, preferredWidth: 70, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var plus = UIFactory.CreateButton(row, "ProgressBarHeightPlus", "+");
        UIFactory.SetLayoutElement(plus.Component.gameObject,
            minWidth: 30, preferredWidth: 30, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        minus.OnClick = () =>
        {
            Config.Settings.SetProgressBarHeight(Config.Settings.ProgressBarHeight - 1);
            valueLbl.TextMesh.text = $"{Config.Settings.ProgressBarHeight} px";
        };
        plus.OnClick = () =>
        {
            Config.Settings.SetProgressBarHeight(Config.Settings.ProgressBarHeight + 1);
            valueLbl.TextMesh.text = $"{Config.Settings.ProgressBarHeight} px";
        };

        TooltipHover.Attach(row,
            $"Absolute pixel height for the XP/Weapon/Legacy progress bars when 'Scale bar with overlay' is off. Clamped {Config.Settings.PROGRESS_BAR_HEIGHT_MIN}..{Config.Settings.PROGRESS_BAR_HEIGHT_MAX}. Default 8.");

        // Companion toggle on the next line so the row doesn't get crowded.
        var relRow = UIFactory.CreateHorizontalGroup(parent, "ProgressBarRelativeRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(relRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var relToggle = UIFactory.CreateToggle(relRow, "ProgressBarRelativeToggle");
        UIFactory.SetLayoutElement(relToggle.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        relToggle.Text.text = "Scale bar height with overlay (pre-0.10.7 behavior)";
        relToggle.Text.fontSize = Theme.ScaledUI(12);
        relToggle.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(relToggle.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        relToggle.Toggle.isOn = Config.Settings.ProgressBarHeightRelative;
        TooltipHover.Attach(relToggle.GameObject,
            "When on, the bars stretch vertically as you grow the overlay. When off (default), the bars stay at the fixed pixel height above regardless of overlay size.");
        relToggle.OnValueChanged += v => Config.Settings.SetProgressBarHeightRelative(v);
    }

    // 0.10.8: per-overlay left/right edge padding. Friend-testing feedback
    // surfaced text sitting flush with overlay borders — especially the
    // Familiar Browser's row labels brushing the scrollbar gutter. One
    // setting, applied to every overlay's content area at construct time;
    // rebuild via overlay toggle to pick up changes live.
    private void AddOverlayEdgePaddingControls(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "OverlayEdgePadRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var label = UIFactory.CreateLabel(row, "OverlayEdgePadLabel",
            "Overlay edge padding:", TextAlignmentOptions.MidlineLeft,
            color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(label.GameObject,
            minWidth: 160, preferredWidth: 180, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var minus = UIFactory.CreateButton(row, "OverlayEdgePadMinus", "−");
        UIFactory.SetLayoutElement(minus.Component.gameObject,
            minWidth: 30, preferredWidth: 30, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var valueLbl = UIFactory.CreateLabel(row, "OverlayEdgePadValue",
            $"{Config.Settings.OverlayEdgePadding} px",
            TextAlignmentOptions.Center, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(valueLbl.GameObject,
            minWidth: 60, preferredWidth: 70, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var plus = UIFactory.CreateButton(row, "OverlayEdgePadPlus", "+");
        UIFactory.SetLayoutElement(plus.Component.gameObject,
            minWidth: 30, preferredWidth: 30, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        minus.OnClick = () =>
        {
            Config.Settings.SetOverlayEdgePadding(Config.Settings.OverlayEdgePadding - 1);
            valueLbl.TextMesh.text = $"{Config.Settings.OverlayEdgePadding} px";
            // Rebuild any currently-shown overlay so the new padding
            // takes effect live (same lifecycle as text-scale changes).
            Plugin.UIManager.RequestRebuildAllOverlays();
        };
        plus.OnClick = () =>
        {
            Config.Settings.SetOverlayEdgePadding(Config.Settings.OverlayEdgePadding + 1);
            valueLbl.TextMesh.text = $"{Config.Settings.OverlayEdgePadding} px";
            Plugin.UIManager.RequestRebuildAllOverlays();
        };

        TooltipHover.Attach(row,
            $"Inner left/right padding applied to every overlay (XP, Familiar, Familiar Browser, Daily Quest, Professions). Higher = more breathing room between text and panel edge / scrollbar. Clamped {Config.Settings.OVERLAY_EDGE_PADDING_MIN}..{Config.Settings.OVERLAY_EDGE_PADDING_MAX}. Default 6.");
    }

    // 0.10.7: Prestige sub-line on bars — Eclipse-style inset strip showing
    // progress toward next prestige tier inside the main bar.
    // 0.10.10: opt-in toggle for the V-Bloods tab's auto-scan-on-open
    // behavior. Pre-0.10.10 the scan fired unconditionally the first time
    // the user opened the tab; now it's manual by default.
    private void AddAutoScanVBloodsToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "AutoScanVBloodsRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, "AutoScanVBloodsToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = "Auto-scan V-Bloods when the V-Bloods tab opens";
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = Config.Settings.AutoScanVBloodsOnTabOpen;
        TooltipHover.Attach(t.GameObject,
            "When on, opening the V-Bloods tab with an empty collection automatically triggers a box-sweep scan (.fam boxes + .fam cb + .fam l for every box). Off by default — the scanner switches your active box ~10-15 times, so most users prefer the explicit Scan all button.");
        t.OnValueChanged += v => Config.Settings.SetAutoScanVBloodsOnTabOpen(v);
    }

    private void AddShowPrestigeSubLineToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "ShowPrestigeSubLineRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, "ShowPrestigeSubLineToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = "Show prestige-progress sub-line in progress bars (Eclipse-style)";
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = Config.Settings.ShowPrestigeSubLine;
        TooltipHover.Attach(t.GameObject,
            "Adds a slim inset fill at the bottom of each progress bar reflecting how close you are to the next prestige tier (Level / MaxLevel for that system). Mirrors Eclipse's overlay style. Requires Show Progress Bars to be on.");
        t.OnValueChanged += v => Config.Settings.SetShowPrestigeSubLine(v);
    }

    /// <summary>0.10.2: cycle button — text alignment for overlay rows
    /// (Left = default; Right = useful when the overlay is pinned to the
    /// right edge of the screen). Toggling rebuilds open overlays so the
    /// new alignment takes effect immediately — labels capture alignment
    /// at construct time, so a flag flip without rebuild wouldn't visually
    /// update existing labels.</summary>
    private void AddOverlayAlignmentToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "OverlayAlignRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var label = UIFactory.CreateLabel(row, "OverlayAlignLabel",
            "Overlay text alignment:", TextAlignmentOptions.MidlineLeft,
            color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(label.GameObject,
            minWidth: 200, preferredWidth: 240, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var btn = UIFactory.CreateButton(row, "OverlayAlignBtn", FormatOverlayAlignText());
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 80, preferredWidth: 100, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledUI(12); t.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(btn.GameObject,
            "Cycle between Left and Right text alignment for ALL overlays (XP, Familiar, Familiar Browser, Daily Quest, Professions). Right is handy when you've pinned an overlay to the right edge of the screen and want the values closer to the panel border.");
        btn.OnClick = () =>
        {
            var next = Config.Settings.OverlayTextAlignmentSetting == Config.Settings.OverlayAlignment.Left
                ? Config.Settings.OverlayAlignment.Right
                : Config.Settings.OverlayAlignment.Left;
            Config.Settings.SetOverlayTextAlignment(next);
            // Refresh the button label, then trigger overlay rebuilds so the
            // alignment baked into existing labels gets re-applied.
            var newTxt = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (newTxt != null) newTxt.text = FormatOverlayAlignText();
            Plugin.UIManager?.RequestRebuildAllOverlays();
        };
    }

    private static string FormatOverlayAlignText()
        => Config.Settings.OverlayTextAlignmentSetting == Config.Settings.OverlayAlignment.Right ? "Right" : "Left";

    /// <summary>0.9.1: opt-in toggle to suppress the chat confirmation lines
    /// Bloodcraft prints when the user bind / unbind / switch-box / move /
    /// smartbind / permanent-remove a familiar. The structured data pipes
    /// (.fam boxes, .fam l, Eclipse stream) are unaffected — the UI still
    /// updates normally. Friend-testing feedback: "is it possible to
    /// suppress all of the chat messages that come through as you were
    /// switching boxes and familiars". Default off so existing users see no
    /// change.</summary>
    private void AddSuppressActionChatterToggle(GameObject parent)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, "SuppressChatterRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var t = UIFactory.CreateToggle(row, "SuppressActionChatterToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        t.Text.text = "Hide familiar-action chat (bind/unbind/switch box/...)";
        t.Text.fontSize = Theme.ScaledUI(12);
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 320, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        t.Toggle.isOn = Config.Settings.SuppressFamiliarActionChatter;
        TooltipHover.Attach(t.GameObject,
            "When on, Bloodcraft's confirmation chat lines for .fam b / .fam ub / .fam t / .fam cb / .fam mb / .fam sb / .fam r get eaten so they don't clutter your chat box. " +
            "The UI continues to work normally — box list, contents, and overlays read from separate data feeds, not these confirmation lines. Off by default.");
        t.OnValueChanged += value =>
        {
            Config.Settings.SetSuppressFamiliarActionChatter(value);
        };
    }

    /// <summary>Renders a labeled row with three buttons (Small/Standard/Large).
    /// Each click writes the new multiplier and refreshes the in-row "(current: X)"
    /// hint so the user can confirm.</summary>
    private void AddTextScaleRow(GameObject parent, string label,
                                 System.Func<float> currentScaleSetting,
                                 System.Action<float> applyScale)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"DisplayRow_{label}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 30, preferredHeight: 32, flexibleHeight: 0);

        var lbl = UIFactory.CreateLabel(row, $"Lbl_{label}",
            $"{label}:",
            TMPro.TextAlignmentOptions.MidlineLeft, color: null, fontSize: 13);
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 140, preferredWidth: 160, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);

        var hint = UIFactory.CreateLabel(row, $"Hint_{label}",
            FormatScaleHint(currentScaleSetting()),
            TMPro.TextAlignmentOptions.MidlineLeft, color: null, fontSize: 11);
        UIFactory.SetLayoutElement(hint.GameObject,
            minWidth: 90, preferredWidth: 100, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        hint.TextMesh.fontStyle = TMPro.FontStyles.Italic;

        void Pick(float v)
        {
            applyScale(v);
            hint.TextMesh.text = FormatScaleHint(v);
        }
        AddScaleButton(row, "Small",    () => Pick(0.85f));
        AddScaleButton(row, "Standard", () => Pick(1.00f));
        AddScaleButton(row, "Large",    () => Pick(1.20f));
        AddScaleButton(row, "X-Large",  () => Pick(1.50f));
    }

    private static string FormatScaleHint(float v)
    {
        if (v <= 0.9f)  return "(current: Small)";
        if (v >= 1.35f) return "(current: X-Large)";
        if (v >= 1.1f)  return "(current: Large)";
        return "(current: Standard)";
    }

    private static void AddScaleButton(GameObject row, string text, System.Action onClick)
    {
        var btn = UIFactory.CreateButton(row, $"Btn_{text}", text);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 64, preferredWidth: 72, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.fontSize = 11;
        btn.OnClick = () => onClick();
    }

    /// <summary>Renders a labeled row with five buttons (0% / 25% / 50% / 75% / 100%)
    /// for a single overlay's background transparency.</summary>
    private void AddTransparencyRow(GameObject parent, string overlayLabel,
                                    System.Func<float> currentValue,
                                    System.Action<float> applyValue)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"OpacityRow_{overlayLabel}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        var lbl = UIFactory.CreateLabel(row, $"Lbl_{overlayLabel}",
            $"{overlayLabel}:",
            TMPro.TextAlignmentOptions.MidlineLeft, color: null, fontSize: 12);
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 130, preferredWidth: 150, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);

        var hint = UIFactory.CreateLabel(row, $"Hint_{overlayLabel}",
            FormatTransparencyHint(currentValue()),
            TMPro.TextAlignmentOptions.MidlineLeft, color: null, fontSize: 10);
        UIFactory.SetLayoutElement(hint.GameObject,
            minWidth: 60, preferredWidth: 70, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        hint.TextMesh.fontStyle = TMPro.FontStyles.Italic;

        void Pick(float v)
        {
            applyValue(v);
            hint.TextMesh.text = FormatTransparencyHint(v);
            // 0.9.2: push the new alpha to existing panel backgrounds so
            // the user sees the change without having to toggle the
            // overlay off and on.
            Plugin.UIManager.RefreshAllOpacities();
        }
        AddOpacityButton(row, "0%",   () => Pick(0.00f));
        AddOpacityButton(row, "25%",  () => Pick(0.25f));
        AddOpacityButton(row, "50%",  () => Pick(0.50f));
        AddOpacityButton(row, "75%",  () => Pick(0.75f));
        AddOpacityButton(row, "100%", () => Pick(1.00f));
    }

    private static string FormatTransparencyHint(float v)
    {
        // Snap to nearest preset for the display so floating-point drift
        // doesn't show "27%" for a freshly-clicked 25%.
        if (v < 0.13f) return "(0%)";
        if (v < 0.38f) return "(25%)";
        if (v < 0.63f) return "(50%)";
        if (v < 0.88f) return "(75%)";
        return "(100%)";
    }

    private static void AddOpacityButton(GameObject row, string text, System.Action onClick)
    {
        var btn = UIFactory.CreateButton(row, $"Op_{text}", text);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 36, preferredWidth: 42, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.fontSize = 10;
        btn.OnClick = () => onClick();
    }

    // 0.12.0: panel color preset sections. Two zones — "Panel background"
    // (outer chrome of every panel) and "Interior background" (scroll-view
    // wrappers + viewports inside the main panel and Familiar Browser).
    // Hex strings stored in Settings.{Panel,Inner}PanelBackgroundColorHex;
    // power users can hand-edit the .cfg for any specific color, this UI
    // surfaces a row of seven curated presets per zone.
    private TextMeshProUGUI _panelBgCurrentLabel;
    private TextMeshProUGUI _innerBgCurrentLabel;

    private void BuildPanelBackgroundColorSection(GameObject page)
    {
        // ── Outer (panel background, all panels) ────────────────────────
        AddSectionHeading(page, "Panel background color");

        AddPanelColorHelp(page, "PanelBgHelp",
            "Sets the OUTER background color of every panel BCH builds — the main panel, the Familiar Browser, and all five info overlays (XP, Familiar, Daily Quest, Profession, Shift Spell). " +
            "Light colors may reduce text legibility — labels assume a dark background. Transparency per panel is configured by the sliders above; this picker controls hue only.");

        AddPanelColorPresetRow(page, "PanelBgPresetRow", ApplyOuterPanelBgHex);

        _panelBgCurrentLabel = AddPanelColorInfoRow(page, "PanelBgInfoRow",
            FormatOuterBgCurrentText,
            resetHex: Config.Settings.DEFAULT_PANEL_BG_HEX,
            resetTooltip: "Restore the default panel background color (#121212 — near-black, the pre-0.12.0 look).",
            applyAction: ApplyOuterPanelBgHex);

        AddSpacer(page, 6);

        // ── Inner (scroll-view interior, main + familiar browser) ───────
        AddSectionHeading(page, "Interior background color");

        AddPanelColorHelp(page, "InnerBgHelp",
            "Sets the INTERIOR background — the scroll-view area where tab content shows in the main panel and where familiar rows show in the Familiar Browser. Pre-0.12.0 this was bright red by framework default (UIFactory.CreateScrollView used Theme.Level1). " +
            "Two palettes are offered — the dark row keeps the muted modern look, the bright row restores the saturation similar to that original red (Crimson Bright = #A30000 = the pre-0.12.0 default exactly). " +
            "Independent of the outer color above so you can build a two-tone theme. Smaller info overlays don't host scroll views so this picker doesn't affect them.");

        AddPanelColorSubHeading(page, "InnerBgDarkLabel", "Dark variants");
        AddPanelColorPresetRow(page, "InnerBgPresetDarkRow", ApplyInnerPanelBgHex, DefaultDarkPresets);

        AddPanelColorSubHeading(page, "InnerBgBrightLabel", "Bright variants");
        AddPanelColorPresetRow(page, "InnerBgPresetBrightRow", ApplyInnerPanelBgHex, DefaultBrightPresets);

        _innerBgCurrentLabel = AddPanelColorInfoRow(page, "InnerBgInfoRow",
            FormatInnerBgCurrentText,
            resetHex: Config.Settings.DEFAULT_INNER_BG_HEX,
            resetTooltip: "Restore the default interior background (#121212 near-black — masks the framework-default red.)",
            applyAction: ApplyInnerPanelBgHex);
    }

    // 0.12.1: thin italic sub-label for inside a color-picker section.
    // Smaller and quieter than AddSectionHeading so two preset rows visually
    // group together under "Interior background color" without looking like
    // a wall of headings. Uses DefaultText (white) rather than MutedBodyHex
    // for the same readability reasons documented on AddPanelColorHelp —
    // mid-luminance bright presets erase grey-on-grey contrast.
    private static void AddPanelColorSubHeading(GameObject page, string name, string text)
    {
        var lbl = UIFactory.CreateLabel(page, name, text,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Italic;
    }

    private static void AddPanelColorHelp(GameObject page, string name, string body)
    {
        // 0.12.1: render help paragraphs in italic + DefaultText (white).
        // Pre-fix this used <color=Theme.MutedBodyHex> (#8E8E8E mid-grey),
        // which lost contrast against the brighter interior presets
        // (Default Bright #666666, Forest Bright #2A6E2E, Crimson Bright
        // #A30000) — muted-on-mid-grey is unreadable. White italic stays
        // hierarchically distinct from the bold section heading above
        // while reading cleanly on every preset, dark and bright.
        var help = UIFactory.CreateLabel(page, name, body,
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(help.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 36, preferredHeight: 52, flexibleHeight: 0);
        help.TextMesh.enableWordWrapping = true;
        help.TextMesh.overflowMode = TextOverflowModes.Overflow;
        help.TextMesh.fontStyle = FontStyles.Italic;
    }

    // Seven-button preset row, parameterized over which Apply action it
    // calls AND which preset list it shows. Same row layout drives every
    // color picker — outer (one dark row) and inner (one dark + one bright).
    private void AddPanelColorPresetRow(GameObject page, string name, System.Action<string> applyAction,
        (string Label, string Hex)[] presets = null)
    {
        presets ??= DefaultDarkPresets;

        var row = UIFactory.CreateHorizontalGroup(page, name,
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        foreach (var p in presets)
            AddPanelBgPresetButton(row, p.Label, p.Hex, applyAction);
    }

    // 0.12.1: paired dark / bright preset palettes. The dark row is the
    // default (used by every color picker); the bright row is offered
    // additionally for the interior picker after user feedback that some
    // players prefer the pre-0.12.0 saturated look (the framework default
    // Theme.Level1 = (0.64, 0, 0) — actual bright red). Each bright entry
    // shares the hue of its dark sibling but lifts the max channel to
    // roughly 0.5–0.65 — Crimson Bright (#A30000) matches Theme.Level1
    // RGB exactly so the original framework red is one click away.
    private static readonly (string Label, string Hex)[] DefaultDarkPresets = new[]
    {
        ("Default", Config.Settings.DEFAULT_PANEL_BG_HEX),
        ("Black",   "#000000"),
        ("Slate",   "#1A1B25"),
        ("Wine",    "#1F0A10"),
        ("Forest",  "#0A1A0B"),
        ("Indigo",  "#0E0A1F"),
        ("Crimson", "#3B0B0F"),
    };
    private static readonly (string Label, string Hex)[] DefaultBrightPresets = new[]
    {
        ("Default", "#666666"),  // medium neutral grey
        ("Black",   "#404040"),  // dark grey — brighter neutral twin of #000
        ("Slate",   "#4A5070"),  // brighter blue-grey
        ("Wine",    "#8B1A2E"),  // burgundy
        ("Forest",  "#2A6E2E"),  // moss green
        ("Indigo",  "#3D2D80"),  // royal indigo
        ("Crimson", "#A30000"),  // exactly Theme.Level1 — the pre-0.12.0 framework red
    };

    // "Current: #hex" label + Reset button row. Returns the TMP_Text so the
    // caller can keep a reference and refresh it on each pick.
    private TextMeshProUGUI AddPanelColorInfoRow(GameObject page, string name,
        System.Func<string> currentText,
        string resetHex,
        string resetTooltip,
        System.Action<string> applyAction)
    {
        var info = UIFactory.CreateHorizontalGroup(page, name,
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(info,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var current = UIFactory.CreateLabel(info, $"{name}_Current",
            currentText(),
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(current.GameObject,
            minWidth: 200, preferredWidth: 260, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        current.TextMesh.fontStyle = FontStyles.Italic;

        var reset = UIFactory.CreateButton(info, $"{name}_Reset", "Reset");
        UIFactory.SetLayoutElement(reset.GameObject,
            minWidth: 54, preferredWidth: 60, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        var resetText = reset.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (resetText != null) resetText.fontSize = Theme.ScaledUI(11);
        reset.OnClick = () => applyAction(resetHex);
        TooltipHover.Attach(reset.GameObject, resetTooltip);

        return current.TextMesh;
    }

    private void AddPanelBgPresetButton(GameObject row, string label, string hex, System.Action<string> applyAction)
    {
        var btn = UIFactory.CreateButton(row, $"PanelBg_{row.name}_{label}", label);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 50, preferredWidth: 60, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.fontSize = Theme.ScaledUI(11);
        btn.OnClick = () => applyAction(hex);
        TooltipHover.Attach(btn.GameObject, $"Apply preset {label} ({hex}).");
    }

    private void ApplyOuterPanelBgHex(string hex)
    {
        Config.Settings.SetPanelBackgroundColorHex(hex);
        Plugin.UIManager?.RefreshAllPanelBackgrounds();
        if (_panelBgCurrentLabel != null) _panelBgCurrentLabel.text = FormatOuterBgCurrentText();
    }

    private void ApplyInnerPanelBgHex(string hex)
    {
        Config.Settings.SetInnerPanelBackgroundColorHex(hex);
        Plugin.UIManager?.RefreshScopedInnerBackgrounds();
        if (_innerBgCurrentLabel != null) _innerBgCurrentLabel.text = FormatInnerBgCurrentText();
    }

    private static string FormatOuterBgCurrentText()
        => $"Current: {Config.Settings.PanelBackgroundColorHex}";
    private static string FormatInnerBgCurrentText()
        => $"Current: {Config.Settings.InnerPanelBackgroundColorHex}";

    /// <summary>One row showing a label, the URL, and an "Open" button that
    /// hands the URL to <see cref="UnityEngine.Application.OpenURL"/> so the
    /// system browser opens it. Cleaner than wiring TMPro &lt;link&gt; click
    /// handlers under IL2CPP.</summary>
    private static void AddLinkRow(GameObject parent, string label, string url)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"LinkRow_{label}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(2, 2, 1, 1));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        var lbl = UIFactory.CreateLabel(row, "LinkLbl",
            $"{label}:  {url}",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 240, preferredWidth: 320, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Ellipsis;

        var btn = UIFactory.CreateButton(row, "OpenBtn", "Open");
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 56, preferredWidth: 64, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        var btnText = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null) btnText.fontSize = Theme.ScaledUI(12);
        btn.OnClick = () =>
        {
            try { UnityEngine.Application.OpenURL(url); }
            catch (System.Exception ex) { LogUtils.LogWarning($"OpenURL('{url}') threw: {ex.Message}"); }
        };
        TooltipHover.Attach(btn.GameObject, $"Open {url} in your default browser.");
    }

    // 0.12.1: Game Guide tab — V Rising itself (separate from the BCH-focused
    // QuickStartTab and the upcoming mod-mechanics help tab). Surface official
    // homepage + community-maintained resources so new players can find
    // mechanics docs, maps, and the official Discord without leaving the UI.
    private void BuildGameGuideTab(GameObject page)
    {
        AddGuideSection(page,
            "V Rising — quick reference",
            "BloodCraftHub is a UI mod for the Bloodcraft / Kindred V Rising " +
            "server mods. This tab links to resources for V Rising itself — " +
            "the official homepage, the community wiki, fan-maintained guides, " +
            "and the official Discord. Click 'Open' on any row to launch the " +
            "URL in your default browser.");

        AddSectionHeading(page, "Official");
        AddLinkRow(page, "Game homepage (Stunlock Studios)",
            "https://playvrising.com");

        AddSpacer(page, 6);
        AddSectionHeading(page, "Community resources");
        AddLinkRow(page, "V Rising Wiki (Fandom)",
            "https://vrising.fandom.com/wiki/V_Rising_Wiki");
        AddLinkRow(page, "CaDrift — community guides + tools",
            "https://www.cadrift.net/v-rising/");

        AddSpacer(page, 6);
        AddSectionHeading(page, "Discord");
        AddLinkRow(page, "V Rising official Discord",
            "https://discord.com/invite/vrising");

        AddSpacer(page, 8);
        AddGuideSection(page,
            "Suggest a resource",
            "Have another V Rising guide / map / Discord worth surfacing here? " +
            "Open an issue on the BloodCraftHub GitHub (link on the About tab) " +
            "and a future version can include it. Resources listed here are " +
            "user-suggested — they are not maintained by the mod author and " +
            "their content / availability may change.");
    }

    private void BuildQuickStartTab(GameObject page)
    {
        AddGuideSection(page,
            "Welcome to BloodCraftHub",
            "BloodCraftHub is the unified UI for the Bloodcraft V Rising mod. " +
            "Every chat command from Bloodcraft can be issued from this UI - " +
            "just open the matching tab and use the buttons or forms. The " +
            "left rail groups tabs into BLOODCRAFT / KINDRED / HELP - click " +
            "a group header to collapse or expand it. The footer toggles the " +
            "secondary overlays (XP, Familiar), auto-resize, and the input " +
            "block while typing.\n\n" +
            "Tip: this main panel and every overlay are BOTH draggable AND " +
            "resizable. Drag from anywhere inside to move; drag the bottom-" +
            "right (or any edge) to resize. The maximize button in the top-" +
            "right of this main panel toggles fullscreen, and the Settings " +
            "tab has size controls if you prefer click-to-resize.");

        AddGuideSection(page,
            "Leveling (passive)",
            "Your character earns XP automatically as you defeat enemies. " +
            "Current level, progress, and class show on the Levels tab and " +
            "in the XP overlay (toggle on the panel footer). Nothing to " +
            "click to gain XP - just play. When you hit the level cap, you " +
            "can prestige (see Prestige below) to reset level and earn " +
            "permanent bonuses.");

        AddGuideSection(page,
            "Familiars",
            "Familiars are summoned combat companions collected from random " +
            "drops when you defeat eligible mobs and V-Bloods. The drop " +
            "table and rate are server-configured.\n\n" +
            "Boxes hold your familiar collection (named buckets you can " +
            "switch between). The Boxes tab lists them: click a box to load " +
            "its contents, then click any familiar to bind it as your " +
            "active companion. Use the Familiars tab to unbind, toggle " +
            "combat mode, or prestige the active familiar.\n\n" +
            "Shiny familiars are rare visual+stat variants of normal " +
            "familiars - the server admin configures the rate. A shiny " +
            "drop is the same creature but with a glowing effect and " +
            "(usually) noticeably better stats.");

        AddGuideSection(page,
            "Classes",
            "A class specializes your character with weapon synergies, " +
            "stat bonuses, and a unique spell. Use Class -> List Classes " +
            "to see what's available on your server. To pick a class, type " +
            "`.class s <ClassName>` in chat - a UI selector lands in a " +
            "later phase. Most classes grant a spell that can occupy your " +
            "shift slot (see Unarmed + Shift below).");

        AddGuideSection(page,
            "Weapon Expertise",
            "Each weapon type (Sword, Axe, Mace, ...) tracks its own " +
            "expertise level. Switch to a weapon and use it - expertise " +
            "rises. The Weapon Expertise tab shows level, progress, " +
            "prestige, and the bonus stats you've chosen for the currently-" +
            "equipped weapon. Choose a stat with `.wep cst <Weapon> <Stat>` " +
            "(stat picker coming to UI in a later phase). Common choices: " +
            "PhysicalPower / SpellPower / one of the crit chances.");

        AddGuideSection(page,
            "Unarmed + Shift slot",
            "Two ways to gain extra ability slots:\n\n" +
            "Unarmed: when you have no weapon equipped, you use unarmed " +
            "expertise. Bloodcraft tracks it like any weapon. Unarmed " +
            "expertise unlocks extra spell slots so you can cast while " +
            "weaponless. Use `.wep locksp` after equipping the spells you " +
            "want to keep.\n\n" +
            "Shift slot: by default your shift ability is your travel " +
            "spell (wolf, bat, etc.). Bloodcraft can replace it with a " +
            "class spell instead. Toggle the override from Class -> " +
            "Toggle Shift. Pick which class spell goes in the slot with " +
            "`.class csp <#>`.");

        AddGuideSection(page,
            "Prestige",
            "At max level in any system (Experience, a weapon expertise, " +
            "a blood legacy, etc.) you can prestige. Prestiging resets the " +
            "level to 1 but grants permanent stat multipliers proportional " +
            "to your prestige count.\n\n" +
            "Use the Prestige tab: expand 'Prestige in a system', pick the " +
            "type from the dropdown, Submit. Quick actions: List shows what " +
            "exists; Sync Buffs re-applies your prestige buffs; Exoform " +
            "toggles the high-prestige shapeshift; Shroud is the " +
            "permanent stealth toggle if you qualify.");

        AddGuideSection(page,
            "Tips",
            "- Hover any control to see what it does (footer at panel bottom).\n" +
            "- Auto-resize ON: the panel grows to fit the active tab. Turn " +
            "off if you prefer manual sizing.\n" +
            "- The secondary overlays (XP, Familiar, Familiar Browser, Daily " +
            "Quest) are independent draggable panels - toggle them in the " +
            "footer.");
    }

    private static void AddGuideSection(GameObject parent, string title, string body)
    {
        AddSectionHeading(parent, title);

        var lbl = UIFactory.CreateLabel(parent, $"Guide_{title}", body,
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        // Guide body sized by TMP's actual rendered preferredHeight via a
        // ContentSizeFitter. Earlier versions estimated lines × 16 which
        // consistently over-shot, producing visible empty gaps between
        // sections. preferredWidth is fixed (panel content area) so wrap
        // is deterministic.
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 20, flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = true;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        lbl.TextMesh.fontStyle = FontStyles.Normal;
        var fitter = lbl.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
    }

    private static void AddAdminRefLine(GameObject parent, string command, string summary)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"AdminRef_{command}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 8, padding: new Vector4(2, 2, 0, 0));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);

        var cmd = UIFactory.CreateLabel(row, "Cmd", command,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(cmd.GameObject,
            minWidth: 200, preferredWidth: 220, flexibleWidth: 0,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        cmd.TextMesh.fontStyle = FontStyles.Bold;
        cmd.TextMesh.enableWordWrapping = false;
        cmd.TextMesh.overflowMode = TextOverflowModes.Overflow;

        var desc = UIFactory.CreateLabel(row, "Desc", summary,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(desc.GameObject,
            minWidth: 160, preferredWidth: 180, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        desc.TextMesh.enableWordWrapping = false;
        desc.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    // 0.10.7: tabular two-column row for the Vanilla Admin reference. Pre-0.10.7
    // each section packed all entries into a single multi-line label with
    // hand-tabbed alignment ("  • cmd — desc"); the tab spacing inside a
    // proportional font produced inconsistent column edges depending on the
    // longest command in each section. This helper renders bold command in
    // a fixed-width column + wrapped description in the flex column, so
    // every section ends up with the same crisp alignment regardless of
    // entry length.
    //
    // 0.10.8: the row now grows its height with the wrapped description.
    // Pre-0.10.8 the row had a fixed preferredHeight=22; the desc label
    // would wrap (correctly) but its rendered height stayed at the row's
    // fixed value, so the second/third wrapped line drew on top of the
    // next row's text. Fix: ContentSizeFitter on the row itself
    // (verticalFit=PreferredSize), and clear the row's preferredHeight so
    // the fitter uses the children's preferredHeight (HorizontalLayoutGroup
    // reports the max of its child heights, and desc has its own fitter).
    private static void AddCommandTableRow(GameObject parent, string command, string description)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"CmdRow_{command}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 10, padding: new Vector4(2, 2, 2, 2));
        // 0.10.8: minHeight floor only; flexibleHeight=0 + ContentSizeFitter
        // below grows the row when the description wraps. Setting
        // preferredHeight=-1 means "no opinion" — Unity uses the next
        // priority (child max from HorizontalLayoutGroup) which itself
        // comes from desc's ContentSizeFitter.
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 22, preferredHeight: -1, flexibleHeight: 0);
        var rowFitter = row.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        rowFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        rowFitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        var cmd = UIFactory.CreateLabel(row, "Cmd", command,
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(cmd.GameObject,
            minWidth: 220, preferredWidth: 220, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 22, flexibleHeight: 0);
        cmd.TextMesh.fontStyle = FontStyles.Bold;
        cmd.TextMesh.enableWordWrapping = false;
        cmd.TextMesh.overflowMode = TextOverflowModes.Overflow;

        var desc = UIFactory.CreateLabel(row, "Desc", description,
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        // 0.10.8: preferredHeight=-1 so the LayoutElement doesn't pin a
        // floor; the ContentSizeFitter pulls TMP's computed preferredHeight
        // (which is the actual wrapped height for this width).
        UIFactory.SetLayoutElement(desc.GameObject,
            minWidth: 160, preferredWidth: 200, flexibleWidth: 1,
            minHeight: 22, preferredHeight: -1, flexibleHeight: 0);
        desc.TextMesh.enableWordWrapping = true;
        desc.TextMesh.overflowMode = TextOverflowModes.Overflow;
        var fitter = desc.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
    }

    // 0.10.7: convenience — title heading + tabular row entries.
    private static void AddCommandTable(GameObject parent, string title, params (string command, string description)[] entries)
    {
        AddSectionHeading(parent, title);
        foreach (var (cmd, desc) in entries) AddCommandTableRow(parent, cmd, desc);
    }

    private void RenderExpertise(PlayerStateService.ExpertiseState s)
    {
        if (_wepTypeLabel == null) return;

        _wepTypeLabel.text = s.Type.ToString();
        _wepProgressLabel.text = s.Prestige > 0
            ? $"Level {s.Level}   ({s.Progress * 100f:0.#}%)   Prestige {s.Prestige}"
            : $"Level {s.Level}   ({s.Progress * 100f:0.#}%)";

        var stats = PlayerStateService.DecodeWeaponBonusStats(s.BonusStatsRaw);
        if (stats.Count == 0)
        {
            _wepBonusLabel.text = "Bonus Stats: (none yet — pick one via .wep cst)";
        }
        else
        {
            var named = new System.Collections.Generic.List<string>();
            foreach (var st in stats)
                if (st != PlayerStateService.WeaponStatType.None) named.Add(st.ToString());
            _wepBonusLabel.text = named.Count > 0
                ? $"Bonus Stats: {string.Join(", ", named)}"
                : "Bonus Stats: (none yet)";
        }
    }

    private void RenderFamiliar(PlayerStateService.FamiliarState s)
    {
        if (_famNameLabel == null) return;
        // 0.10.8: HasActive is the authoritative "is there a familiar bound"
        // signal sourced from the raw Eclipse protocol name field. The Name
        // field is masked to "Familiar" for display when no familiar is
        // bound (preserving pre-0.10.8 visual placeholder), so we can't
        // rely on string.IsNullOrEmpty(Name) anymore.
        _famNameLabel.text = s.HasActive ? s.Name : "(no familiar bound)";

        bool active = s.HasActive;
        _famProgressLabel.text = active
            ? (s.Prestige > 0
                ? $"Level {s.Level}   ({s.Progress * 100f:0.#}%)   Prestige {s.Prestige}"
                : $"Level {s.Level}   ({s.Progress * 100f:0.#}%)")
            : "—";

        _famStatsLabel.text = active
            ? $"HP {s.MaxHealth}   PP {s.PhysicalPower}   SP {s.SpellPower}"
            : "HP —   PP —   SP —";
    }

    // -----------------------------------------------------------------------
    // Small UI helpers (label rows, command buttons, section headings)
    // -----------------------------------------------------------------------

    /// <summary>
    /// 0.9.1: bump the per-character outline so an accent-colored label stays
    /// legible against bright / red in-game backdrops bleeding through a low-
    /// opacity panel. UIFactory.CreateLabel already applies a 0.15-width
    /// black outline by default; this helper widens it to 0.25 for labels
    /// the user has explicitly recolored (Bloodcraft red, weekly quest
    /// magenta, daily cyan, etc.). Friend-testing surfaced this as "pink or
    /// red text on the red background of the UI". Cheap — just a property
    /// set, no extra GameObjects.
    /// </summary>
    private static void ApplyStrongAccentOutline(TextMeshProUGUI t)
    {
        if (t == null) return;
        try
        {
            t.outlineColor = Color.black;
            t.outlineWidth = 0.25f;
        }
        catch { /* TMP can throw during application teardown */ }
    }

    private static TextMeshProUGUI AddInfoLabel(GameObject parent, string name, string initialText, FontStyles style, int fontSize)
    {
        var lbl = UIFactory.CreateLabel(parent, name, initialText,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: fontSize);
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(24), preferredHeight: Theme.ScaledHeight(26), flexibleHeight: 0);
        lbl.TextMesh.fontStyle = style;
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        return lbl.TextMesh;
    }

    private static void AddSectionHeading(GameObject parent, string text)
    {
        var lbl = UIFactory.CreateLabel(parent, $"Section_{text}", text,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(14));
        // 0.9.2: bumped minHeight 20→26 and preferredHeight 22→30. The old
        // values were tight even at Standard scale and clipped into the next
        // container at Large scale (Theme.ScaledUI(14) → 17pt with ~20px line
        // height needs >22px reserved). Friend-testing: "the text for available
        // boxes and the text for manage boxes is overlapping with the border of
        // the table". The container immediately below the heading (BoxList,
        // BoxContent etc.) typically had only 2px top padding so any
        // overflow from the heading drew into the list's edge. Bumping the
        // heading itself fixes the root cause.
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(26), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Bold | FontStyles.Italic;
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    private static void AddSpacer(GameObject parent, int height)
    {
        var spacer = UIFactory.CreateUIObject("Spacer", parent);
        UIFactory.SetLayoutElement(spacer, minHeight: height, preferredHeight: height, flexibleHeight: 0, flexibleWidth: 1);
    }

    // 0.10.9: VISUAL-POLISH HELPERS
    //
    // These power the cross-cutting design improvements from the v0.10.9
    // visual audit. Each is a small composable building block — pass an
    // arbitrary action that adds your section contents and the helper
    // wraps them in the polished shell. Used by Levels, Familiars,
    // V-Bloods, Prestige, Expertise, Legacy across the panel.

    /// <summary>0.10.9: wrap arbitrary content in an inset card with a
    /// subtle 13%-grey background. Replaces "naked text on the panel"
    /// pattern with discrete cards that read as grouped content. The
    /// optional <paramref name="tint"/> washes the card behind the
    /// content — pass one of <c>Theme.SystemTint*</c> for the XP /
    /// Legacy / Expertise / Familiar / Profession / Quest colors, or
    /// null for the neutral card background.
    ///
    /// 0.10.11: bumped default padding 6 → 10 so text labels inside the
    /// card never sit flush with the inset border. Friend-test 0.10.10
    /// surfaced this: "ensure that within containers ... there is at
    /// least a little left padding." Pre-0.10.11 a 6 px pad was tight
    /// at smaller font scales and several labels visually butted up
    /// against the card's left edge. Callers can still override.</summary>
    private static GameObject AddCard(GameObject parent, string name, Color? tint = null,
                                       int padding = 10, int innerSpacing = 4)
    {
        var card = UIFactory.CreateVerticalGroup(parent, name,
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: innerSpacing,
            padding: new Vector4(padding, padding, padding, padding),
            bgColor: Theme.CardBackground);
        UIFactory.SetLayoutElement(card,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(28), flexibleHeight: 0);
        // Optional system tint overlay — draws ON TOP of the card
        // background image so the wash modulates it down into the
        // theme-tinted hue. Uses a child Image with stretched anchors
        // so it tracks any future resize.
        if (tint.HasValue && tint.Value.a > 0.0001f)
        {
            var washObj = UIFactory.CreateUIObject("Tint", card);
            var washRt  = washObj.GetComponent<UnityEngine.RectTransform>();
            washRt.anchorMin = Vector2.zero;
            washRt.anchorMax = Vector2.one;
            washRt.offsetMin = Vector2.zero;
            washRt.offsetMax = Vector2.zero;
            var img = washObj.AddComponent<UnityEngine.UI.Image>();
            img.color = tint.Value;
            img.raycastTarget = false;
            // Force the wash to draw BEHIND siblings (which are added
            // AFTER it because parent is its first child). Setting
            // sibling index 0 keeps it behind anything appended later.
            washObj.transform.SetSiblingIndex(0);
            // Layout-element opt-out so the wash doesn't consume layout
            // space — it's pure decoration.
            UIFactory.SetLayoutElement(washObj, ignoreLayout: true);
        }
        return card;
    }

    /// <summary>0.10.9: a left-aligned label paired with a right-aligned
    /// value. Replaces the common pattern of stacking 4 separate
    /// AddInfoLabel calls with the data flowing left-to-right ("Type:
    /// Sword" / "Level: 42" / etc.) — instead, present as a compact
    /// table-style row inside a card. Value defaults to right-aligned so
    /// numeric values line up across stacked rows.</summary>
    private static (TextMeshProUGUI labelTmp, TextMeshProUGUI valueTmp) AddStatRow(
        GameObject parent, string label, string value, int fontSize = -1,
        FontStyles valueStyle = FontStyles.Bold)
    {
        int fs = fontSize > 0 ? fontSize : Theme.ScaledUI(12);
        var row = UIFactory.CreateHorizontalGroup(parent, $"StatRow_{label}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 8, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row,
            minWidth: 320, preferredWidth: 380, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);

        var lbl = UIFactory.CreateLabel(row, "Label",
            $"<color={Theme.MutedBodyHex}>{label}</color>",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: fs);
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 140, preferredWidth: 170, flexibleWidth: 0,
            minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;

        var val = UIFactory.CreateLabel(row, "Value", value,
            TextAlignmentOptions.MidlineRight, color: null, fontSize: fs);
        UIFactory.SetLayoutElement(val.GameObject,
            minWidth: 120, preferredWidth: 200, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(20), preferredHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
        val.TextMesh.fontStyle = valueStyle;
        val.TextMesh.enableWordWrapping = false;
        val.TextMesh.overflowMode = TextOverflowModes.Overflow;

        return (lbl.TextMesh, val.TextMesh);
    }

    /// <summary>0.10.9: a 1-px hairline used to separate logical groups
    /// inside a card / between cards. The color comes from Theme.DividerLine
    /// so it obeys the global opacity curve.
    ///
    /// 0.10.11: rewritten to compose spacer + line + spacer at the
    /// PARENT level instead of using an HLG wrap. The 0.10.9 implementation
    /// had the Vector4 padding axes swapped (this codebase's Vector4 is
    /// (top, bottom, left, right) — easy to get wrong, see UIFactory.cs:254),
    /// which put 12px of top/bottom padding inside a 7px wrap. The line
    /// rendered at an unexpected vertical position and visually overlapped
    /// the body text that followed the divider. The new approach has no
    /// such trap and produces a cleaner result.</summary>
    private static void AddDivider(GameObject parent, int verticalGap = 6)
    {
        int halfGap = UnityEngine.Mathf.Max(2, verticalGap / 2);
        AddSpacer(parent, halfGap);
        var line = UIFactory.CreateUIObject("DividerLine", parent);
        UIFactory.SetLayoutElement(line,
            minWidth: 100, preferredWidth: 320, flexibleWidth: 1,
            minHeight: 1, preferredHeight: 1, flexibleHeight: 0);
        var img = line.AddComponent<UnityEngine.UI.Image>();
        img.color = Theme.DividerLine;
        img.raycastTarget = false;
        AddSpacer(parent, halfGap);
    }

    /// <summary>0.10.9: muted prose label for "what this section does"
    /// explanatory text. Reads as secondary content vs. the bright
    /// section headings + primary data labels.
    ///
    /// 0.10.13: dropped italic styling and bumped default font size
    /// 11 → 13. Friend-test 0.10.12: "italic at standard text size is
    /// difficult to read." The MutedBodyHex color already does the
    /// "secondary content" job — italic was redundant emphasis that
    /// hurt legibility without adding meaning. The slight size bump
    /// brings prose hints in line with the 12-13 pt body text used
    /// for primary data labels.</summary>
    private static TextMeshProUGUI AddBodyText(GameObject parent, string text, int fontSize = -1)
    {
        int fs = fontSize > 0 ? fontSize : Theme.ScaledUI(13);
        var lbl = UIFactory.CreateLabel(parent, "BodyText",
            $"<color={Theme.MutedBodyHex}>{text}</color>",
            TextAlignmentOptions.TopLeft, color: null, fontSize: fs);
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 320, preferredWidth: 380, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(22), flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Normal;
        lbl.TextMesh.enableWordWrapping = true;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        var fitter = lbl.GameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        return lbl.TextMesh;
    }

    /// <summary>0.10.9: format a `.command` literal for inline display
    /// inside a body label. Wraps in AccentMono color so it visually
    /// pops as "this is a literal chat command." Use within string
    /// composition: <c>$"Use {Mono(".wep cst")} to pick a stat."</c>
    /// </summary>
    private static string Mono(string s)
        => string.IsNullOrEmpty(s) ? s : $"<color={Theme.AccentMonoHex}><b>{s}</b></color>";

    private static void AddCommandButton(GameObject parent, string label, string command,
        string tooltip = null, Color? color = null, bool confirm = false)
    {
        var b = UIFactory.CreateButton(parent, $"Cmd_{label}", label, color);
        UIFactory.SetLayoutElement(b.GameObject,
            minWidth: 70, preferredWidth: 110, flexibleWidth: 1,
            minHeight: Theme.ScaledHeight(28), preferredHeight: Theme.ScaledHeight(30), flexibleHeight: 0);
        // Action rows use childForceExpandWidth so buttons share the row; tell
        // their inner TMP text not to wrap, so labels render on one line and
        // overflow visually (which is fine - shorter than wrap) instead of
        // stacking one character per line.
        var t = b.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null)
        {
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.Center;
            t.fontSize = Theme.ScaledUI(13);
        }

        if (confirm && t != null)
        {
            // Two-click confirm: first click arms the button for 3s and changes
            // its text to "Confirm?". A second click within the window sends.
            // Outside the window, it reverts to the original label silently.
            // This avoids a modal dialog system for the one or two destructive
            // commands we expose (e.g. .fam ub permanently destroys familiar).
            float armUntil = -1f;
            string originalLabel = label;
            b.OnClick = () =>
            {
                float now = Time.realtimeSinceStartup;
                if (armUntil > 0 && now <= armUntil)
                {
                    armUntil = -1f;
                    t.text = originalLabel;
                    EnqueueOrWarn(command);
                }
                else
                {
                    armUntil = now + 3f;
                    t.text = "Confirm?";
                }
            };
        }
        else
        {
            b.OnClick = () => EnqueueOrWarn(command);
        }

        // Hover tooltip: default to the literal command text so even un-described
        // buttons surface what they'll send; per-call site can pass a richer
        // explanation if it's worth the words.
        TooltipHover.Attach(b.GameObject, tooltip ?? $"Sends '{command}' to the server.");
    }

    private static void EnqueueOrWarn(string command)
    {
        if (!MessageService.IsInitialized)
        {
            LogUtils.LogWarning($"Cannot send '{command}' — MessageService not yet bound to character/user.");
            return;
        }
        MessageService.EnqueueMessage(command);
        LogUtils.LogInfo($"Enqueued outbound: {command}");
    }

    // -----------------------------------------------------------------------
    // Overlay-toggle footer
    // -----------------------------------------------------------------------

    private void BuildOverlayFooter(GameObject parent)
    {
        // 0.10.13: reformatted as a labeled card-style container so the
        // user reads it as "Overlay visibility" rather than a loose row
        // of toggles. Single-column layout: a label prefix on the left
        // of the toggle row visually frames the group. The auto-resize
        // toggle stays on its own line below — it's not an overlay
        // toggle and shouldn't read as one.
        var footerWrap = UIFactory.CreateVerticalGroup(parent, "OverlayFooterWrap",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(10, 10, 6, 6),
            bgColor: Theme.CardBackground);
        UIFactory.SetLayoutElement(footerWrap, minHeight: 62, flexibleHeight: 0, flexibleWidth: 1);

        // Row 1: label + overlay toggles on one line. The label acts as
        // the container's section heading-in-line so the toggles read
        // as "Overlay visibility: [XP] [Familiar] [Browser] [Quest]
        // [Professions]" without sacrificing a full row of vertical
        // real estate for a separate heading.
        var row1 = UIFactory.CreateHorizontalGroup(footerWrap, "OverlayFooterRow1",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: false, childControlHeight: false,
            spacing: 10, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row1, minHeight: 26, flexibleHeight: 0, flexibleWidth: 1);

        var visLabel = UIFactory.CreateLabel(row1, "OverlayVisLabel",
            "Show overlays:",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(visLabel.GameObject,
            minWidth: 110, preferredWidth: 120, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        visLabel.TextMesh.fontStyle = FontStyles.Bold;
        visLabel.TextMesh.enableWordWrapping = false;
        visLabel.TextMesh.overflowMode = TextOverflowModes.Overflow;

        _xpOverlayToggle    = AddOverlayToggle(row1, "XP",                PanelType.ExperienceOverlay);
        _famOverlayToggle   = AddOverlayToggle(row1, "Familiar",          PanelType.FamiliarOverlay);
        _famBrowserToggle   = AddOverlayToggle(row1, "Familiar Browser",  PanelType.FamiliarBrowserOverlay);
        _dqOverlayToggle    = AddOverlayToggle(row1, "Daily quest",       PanelType.DailyQuestOverlay);
        _profOverlayToggle  = AddOverlayToggle(row1, "Professions",       PanelType.ProfessionOverlay);
        _shiftOverlayToggle = AddOverlayToggle(row1, "Shift spell",       PanelType.ShiftSpellOverlay);

        // Row 2: panel behavior — visually separated by the spacing in
        // the parent VLG, so it doesn't get confused with the visibility
        // toggles above.
        var row2 = UIFactory.CreateHorizontalGroup(footerWrap, "OverlayFooterRow2",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: false, childControlHeight: false,
            spacing: 12, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row2, minHeight: 26, flexibleHeight: 0, flexibleWidth: 1);

        AddAutoResizeToggle(row2);
        AddLockOverlaysToggle(row2);
    }

    /// <summary>0.10.14: "Lock overlays" toggle beside Auto-resize.
    /// When on, every overlay's IsPinned flag is set true, which makes
    /// PanelDragger ignore mouse interactions (no drag, no resize).
    /// Programmatic resize when settings or content change is unaffected
    /// — IsPinned only blocks the dragger, not direct Rect.sizeDelta
    /// mutations. Friend-test: "lock overlays so I don't accidentally
    /// drag them around while playing."</summary>
    private void AddLockOverlaysToggle(GameObject parent)
    {
        var t = UIFactory.CreateToggle(parent, "LockOverlaysToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 160, preferredWidth: 180, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        t.Text.text = "Lock overlays";
        t.Text.fontSize = Theme.ScaledUI(13);
        t.Text.enableWordWrapping = false;
        t.Text.overflowMode = TextOverflowModes.Overflow;
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 130, preferredWidth: 150, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);

        t.Toggle.isOn = Config.Settings.LockOverlays;
        TooltipHover.Attach(t.GameObject,
            "Lock the position and size of every overlay so they can't be dragged or resized by accident during play. Settings-driven resize (e.g. enabling progress bars on the XP overlay, or a V-Blood scan growing the list) still works.");
        t.OnValueChanged += value =>
        {
            Config.Settings.SetLockOverlays(value);
            // Apply to every live overlay immediately. Overlays not yet
            // constructed will read the setting in
            // ResizeablePanelBase.LateConstructUI when they're built.
            Plugin.UIManager?.ApplyOverlayLockState();
        };
    }

    private void AddAutoResizeToggle(GameObject parent)
    {
        var t = UIFactory.CreateToggle(parent, "AutoResizeToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 160, preferredWidth: 180, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        t.Text.text = "Auto-resize panel";
        t.Text.fontSize = Theme.ScaledUI(13);
        t.Text.enableWordWrapping = false;
        t.Text.overflowMode = TextOverflowModes.Overflow;
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 130, preferredWidth: 150, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);

        t.Toggle.isOn = Settings.IsPanelAutoResizeEnabled;
        TooltipHover.Attach(t.GameObject,
            "When on, the main panel grows to fit the active tab's content (capped at 90% of screen height).");
        t.OnValueChanged += value =>
        {
            Plugin.Instance.Config.Bind(Settings.UI_SETTINGS_GROUP, nameof(Settings.IsPanelAutoResizeEnabled), true, "").Value = value;
            AutoResizeIfEnabled();
        };
    }

    private void AutoResizeIfEnabled()
    {
        if (!Settings.IsPanelAutoResizeEnabled) return;
        // 0.11.2: bail in fullscreen mode. With stretched anchors (set by
        // SetFullscreen), assigning sizeDelta.y makes the panel that-many
        // pixels TALLER than the canvas — which is exactly the friend-
        // test bug ("UI scaled up larger than the screen"). Auto-resize
        // doesn't make sense when the panel is already canvas-sized;
        // skip it cleanly.
        if (_isFullscreen) return;
        if (!_tabContent.TryGetValue(ActiveTab, out var pageGo) || pageGo == null) return;

        // The visible tab GameObject is the ScrollView wrapper, but the actual
        // children live in the inner content. Walk the inner so AutoResize sees
        // the true height; if missing (legacy path), fall back to the wrapper.
        _tabInnerContent.TryGetValue(ActiveTab, out var innerGo);
        var measureGo = innerGo != null ? innerGo : pageGo;

        try
        {
            var pageRt = pageGo.GetComponent<RectTransform>();
            if (pageRt == null) return;

            // Force the layout to recalculate so preferredHeight is up to date.
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(pageRt);

            // Take the taller of the active page and the left tab strip - the
            // tab strip can be taller than the active page when many tabs are
            // expanded (BLOODCRAFT alone has 8 sub-tabs), and we don't want
            // sub-tabs hidden below the panel border.
            float pageHeight  = ComputeChildrenSumHeight(measureGo);
            float stripHeight = _tabStripGo != null ? ComputeChildrenSumHeight(_tabStripGo) : 0f;
            float contentHeight = Math.Max(pageHeight, stripHeight);
            // Chrome budget: panel title bar (~24) + OverlayFooter (32) + TooltipFooter (22) +
            // spacing/margins/safety buffer (~32) ≈ 110px. The earlier 76 was missing the title-
            // bar height, which manifested as the panel coming up short by a row or two when
            // switching to a long box-content view.
            float chrome = 110f;
            float desired = contentHeight + chrome;
            float screenCap = UnityEngine.Screen.height * 0.9f;
            float clamped = Math.Min(Math.Max(desired, MinHeight), screenCap);

            var size = Rect.sizeDelta;
            if (Math.Abs(size.y - clamped) > 1f)
            {
                Rect.sizeDelta = new Vector2(size.x, clamped);
                EnsureValidPosition();
                // 0.10.14: refresh the dragger's cached resize hit-area
                // after a programmatic resize. PanelDragger caches the
                // 10-px border mask at construction and ONLY refreshes it
                // when OnEndResize fires (manual drag-resize). Pre-0.10.14
                // an auto-resize would leave the cache pointing at the
                // OLD panel size — the user couldn't hit the new bottom
                // border because the mask still expected the panel's
                // initial bottom. Friend-test 0.10.13: "can't click and
                // expand the UI." Root cause was this cache staleness;
                // 0.10.13's layout changes made the auto-resize delta
                // bigger, which made the stale-cache offset large enough
                // for the user to notice.
                Dragger?.OnEndResize();
            }
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"AutoResizeIfEnabled failed: {ex}");
        }
    }

    /// <summary>
    /// Sum the visible direct children's preferred heights + spacing + padding.
    /// Honors VerticalLayoutGroup's spacing/padding when present. Uses
    /// LayoutUtility.GetPreferredHeight per child so children with their own
    /// VerticalLayoutGroup (e.g. CollapsibleSections) report the right value
    /// when expanded vs collapsed.
    /// </summary>
    private static float ComputeChildrenSumHeight(GameObject parent)
    {
        var rt = parent.GetComponent<RectTransform>();
        if (rt == null) return 0f;

        var vlg = parent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        float spacing = vlg != null ? vlg.spacing : 0f;
        float padTop = vlg != null ? vlg.padding.top : 0f;
        float padBot = vlg != null ? vlg.padding.bottom : 0f;

        float total = padTop + padBot;
        int visible = 0;
        for (int i = 0; i < rt.childCount; i++)
        {
            var child = rt.GetChild(i);
            if (!child.gameObject.activeInHierarchy) continue;
            visible++;

            var crt = child.GetComponent<RectTransform>();
            if (crt == null) continue;

            float ph = UnityEngine.UI.LayoutUtility.GetPreferredHeight(crt);
            if (ph < 0) ph = crt.rect.height;
            total += ph;
        }
        if (visible > 1) total += spacing * (visible - 1);
        return total;
    }

    private Toggle AddOverlayToggle(GameObject parent, string label, PanelType overlay)
    {
        var t = UIFactory.CreateToggle(parent, $"OverlayToggle_{overlay}");
        // Compact widths so 4 toggles fit in row 1 of the footer at the panel's
        // MinWidth=600 without spilling off the right edge.
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 130, preferredWidth: 150, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);

        t.Text.text = label;
        t.Text.fontSize = Theme.ScaledUI(13);
        t.Text.enableWordWrapping = false;
        t.Text.overflowMode = TextOverflowModes.Overflow;
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 100, preferredWidth: 120, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);

        t.Toggle.isOn = Plugin.UIManager.IsOverlayOpen(overlay);
        t.OnValueChanged += _ => Plugin.UIManager.ToggleOverlay(overlay);
        return t.Toggle;
    }

    // -----------------------------------------------------------------------
    // Tab switching
    // -----------------------------------------------------------------------

    public void ShowTab(PanelType tab)
    {
        if (!_tabContent.ContainsKey(tab)) return;
        foreach (var kv in _tabContent) kv.Value.SetActive(kv.Key == tab);
        ActiveTab = tab;

        // First-open auto-pull for tabs whose body depends on a server reply.
        // Boxes tab: send .fam boxes if we don't have any yet, so the user
        // doesn't have to click Refresh on every cold open. Cheap and idempotent
        // (the existing intercept-flag logic handles re-arming if pressed twice).
        if (tab == PanelType.BoxesTab
            && PlayerStateService.BoxList != null
            && PlayerStateService.BoxList.Count == 0
            && MessageService.IsInitialized)
        {
            if (_boxesStatusLabel != null) _boxesStatusLabel.text = "Loading boxes from the server…";
            EnqueueOrWarn(MessageService.BCCOM_FAM_BOXES);
        }

        // 0.9.6: auto-fire the structured-info fetch on Wep/Blood tab open
        // so the new stat-values header populates immediately. Subsequent
        // refreshes are driven by TickTabAutoRefresh (per-frame ticker).
        if (MessageService.IsInitialized)
        {
            if (tab == PanelType.ExpertiseTab)
            {
                FireWepInfoFetch();
            }
            else if (tab == PanelType.BloodLegacyTab)
            {
                FireBlInfoFetch();
            }
        }

        AutoResizeIfEnabled();
    }

    // 0.9.6: per-tab auto-refresh of the live "current X" info displays so
    // the stat-values stay current without the user needing to click Refresh.
    // Wired once in ConstructPanelContent as a CoreUpdateBehavior tick;
    // self-gates on ActiveTab + interval so it's a no-op for any other tab.
    private double _lastWepAutoFetchAt;
    private double _lastBlAutoFetchAt;
    // 0.10.2: type-change tracking. When the user equips a new weapon or
    // switches blood type, the bonus stats need to refresh ASAP rather than
    // waiting for the next 10s tick. Subscribe to ExpertiseChanged /
    // LegacyChanged in BuildExpertiseTab / BuildBloodLegacyTab; when a Type
    // delta is detected, reset the per-tab fetch timer so the ticker fires
    // next frame.
    private PlayerStateService.WeaponType _wepTabLastType;
    private PlayerStateService.BloodType  _blTabLastType;
    private bool _wepTabTypeBaseline;
    private bool _blTabTypeBaseline;
    private System.Action _tabAutoRefreshTicker;
    private const double TAB_AUTO_REFRESH_SECONDS = 10.0;

    private void TickTabAutoRefresh()
    {
        if (!MessageService.IsInitialized) return;
        // Only the panel-is-open path matters; tab tickers shouldn't run
        // while the main panel is hidden.
        if (!Enabled) return;
        var now = UnityEngine.Time.realtimeSinceStartupAsDouble;
        if (ActiveTab == PanelType.ExpertiseTab
            && now - _lastWepAutoFetchAt >= TAB_AUTO_REFRESH_SECONDS)
        {
            FireWepInfoFetch();
        }
        else if (ActiveTab == PanelType.BloodLegacyTab
              && now - _lastBlAutoFetchAt >= TAB_AUTO_REFRESH_SECONDS)
        {
            FireBlInfoFetch();
        }
    }

    private void FireWepInfoFetch()
    {
        _lastWepAutoFetchAt = UnityEngine.Time.realtimeSinceStartupAsDouble;
        // 0.10.2: silent enqueue — the wep tab already renders the reply in
        // the structured display, so the chat copy is redundant noise.
        if (MessageService.IsInitialized)
            MessageService.EnqueueMessageSilent(MessageService.BCCOM_WEP_GET);
        else
            EnqueueOrWarn(MessageService.BCCOM_WEP_GET);
    }

    private void FireBlInfoFetch()
    {
        _lastBlAutoFetchAt = UnityEngine.Time.realtimeSinceStartupAsDouble;
        var leg = PlayerStateService.Legacy;
        // .bl get with no arg doesn't arm AwaitingBloodInfo (see
        // MessageService_Processing.NoteOutboundForIntercept) — only the typed
        // form does. If the player has no current blood yet, skip the fetch.
        if ((int)leg.Type == 0) return;
        if (MessageService.IsInitialized)
            MessageService.EnqueueMessageSilent(string.Format(MessageService.BCCOM_BL_GET_FORMAT, leg.Type));
        else
            EnqueueOrWarn(string.Format(MessageService.BCCOM_BL_GET_FORMAT, leg.Type));
    }

    internal override void Reset()
    {
        if (_availabilitySubscribed)
        {
            Services.EclipseProtocolService.AvailabilityChanged -= OnBloodcraftAvailabilityChanged;
            _availabilitySubscribed = false;
        }
        if (_deferredAvailabilityRefresh != null)
        {
            BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Remove(_deferredAvailabilityRefresh);
            _deferredAvailabilityRefresh = null;
        }
        if (_tabAutoRefreshTicker != null)
        {
            BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Remove(_tabAutoRefreshTicker);
            _tabAutoRefreshTicker = null;
        }
        if (_sizePosReadoutTicker != null)
        {
            BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Remove(_sizePosReadoutTicker);
            _sizePosReadoutTicker = null;
        }
        _sizePosRefreshers.Clear();
        if (_wepLastResponseSubscribed)
        {
            PlayerStateService.LastResponseChanged -= OnLastResponseChangedForWep;
            _wepLastResponseSubscribed = false;
        }
        if (_vbSubscribed)
        {
            PlayerStateService.VBloodCollectionChanged -= OnVBloodCollectionChanged;
            Services.VBloodScannerService.ScanStateChanged -= OnVBloodScanStateChanged;
            _vbSubscribed = false;
        }
        if (_vbSummonStatusSubscribed)
        {
            Services.VBloodSummonService.StatusChanged -= OnVBSummonStatusChanged;
            _vbSummonStatusSubscribed = false;
        }
        if (_famSubscribed)
        {
            PlayerStateService.FamiliarChanged -= OnFamiliarChanged;
            _famSubscribed = false;
        }
        if (_famSearchSubscribed)
        {
            MessageService.FamSearchCompleted -= OnFamSearchCompletedForFamTab;
            _famSearchSubscribed = false;
        }
        if (_classSubscribed)
        {
            PlayerStateService.ExperienceChanged -= OnExperienceChangedForClass;
            _classSubscribed = false;
        }
        if (_wepSubscribed)
        {
            PlayerStateService.ExpertiseChanged -= OnExpertiseChanged;
            _wepSubscribed = false;
        }
        if (_shiftSubscribed)
        {
            PlayerStateService.ExpertiseChanged  -= OnExpertiseChangedForUnarmed;
            PlayerStateService.ShiftSpellChanged -= OnShiftSpellChanged;
            _shiftSubscribed = false;
        }
        if (_boxesSubscribed)
        {
            PlayerStateService.BoxListChanged     -= OnBoxListChanged;
            PlayerStateService.BoxContentsChanged -= OnBoxContentsChanged;
            PlayerStateService.ActiveBoxChanged   -= OnActiveBoxChanged;
            _boxesSubscribed = false;
        }
        if (_prestigeSubscribed)
        {
            PlayerStateService.ExperienceChanged -= OnAnyForPrestige;
            PlayerStateService.LegacyChanged     -= OnAnyForPrestige;
            PlayerStateService.ExpertiseChanged  -= OnAnyForPrestige;
            PlayerStateService.FamiliarChanged   -= OnAnyForPrestige;
            _prestigeSubscribed = false;
        }
        if (_lvlSubscribed)
        {
            PlayerStateService.ExperienceChanged -= OnAnyForLevels;
            PlayerStateService.LegacyChanged     -= OnAnyForLevels;
            PlayerStateService.ExpertiseChanged  -= OnAnyForLevels;
            PlayerStateService.FamiliarChanged   -= OnAnyForLevels;
            PlayerStateService.ProfessionChanged -= OnAnyForLevels;
            _lvlSubscribed = false;
        }
        if (_dqSubscribed)
        {
            PlayerStateService.QuestChanged -= OnQuestChangedForTab;
            _dqSubscribed = false;
        }
        if (_blSubscribed)
        {
            PlayerStateService.LegacyChanged -= OnLegacyChanged;
            _blSubscribed = false;
        }
        if (_blInfoSubscribed)
        {
            PlayerStateService.BloodInfoChanged -= OnBloodInfoChanged;
            _blInfoSubscribed = false;
        }
        if (_prestigeInfoSubscribed)
        {
            PlayerStateService.PrestigeInfoChanged -= OnPrestigeInfoChanged;
            _prestigeInfoSubscribed = false;
        }
        if (_collapsibleSubscribed)
        {
            CollapsibleSection.Toggled -= AutoResizeIfEnabled;
            _collapsibleSubscribed = false;
        }
        if (_lastResponseSubscribed)
        {
            PlayerStateService.LastResponseChanged -= OnLastResponseChanged;
            _lastResponseSubscribed = false;
        }
    }
}
