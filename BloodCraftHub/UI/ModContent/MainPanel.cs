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

    // Familiars-tab live labels
    private TextMeshProUGUI _famNameLabel;
    private TextMeshProUGUI _famProgressLabel;
    private TextMeshProUGUI _famStatsLabel;
    private bool _famSubscribed;

    // Class-tab live labels
    private TextMeshProUGUI _classNameLabel;
    private TextMeshProUGUI _classLevelLabel;
    private bool _classSubscribed;

    // Expertise-tab live labels
    private TextMeshProUGUI _wepTypeLabel;
    private TextMeshProUGUI _wepProgressLabel;
    private TextMeshProUGUI _wepBonusLabel;
    private bool _wepSubscribed;

    // Blood-Legacy-tab live labels
    private TextMeshProUGUI _blTypeLabel;
    private TextMeshProUGUI _blProgressLabel;
    private TextMeshProUGUI _blBonusLabel;
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
            Title = "Help",
            StartExpanded = false,
            Tabs = new[]
            {
                (PanelType.QuickStartTab,    "Quick Start"),
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
    private GameObject _tabStripGo;

    public MainPanel(UIBase owner) : base(owner) { }

    // The "—" button in the title-bar's top-right used to be inert because we
    // skip ResizeablePanelBase.ConstructPanelContent (which would hide the
    // title bar entirely) — and PanelBase wires it to OnClosePanelClicked,
    // which the base class no-ops. Hide the panel here so the button does
    // what users expect: close the UI. The floating button stays visible so
    // the user can re-open.
    protected override void OnClosePanelClicked() => SetActive(false);

    protected override void ConstructPanelContent()
    {
        var body = UIFactory.CreateHorizontalGroup(ContentRoot, "Body",
            forceExpandWidth: true, forceExpandHeight: true,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(6, 6, 6, 6));
        UIFactory.SetLayoutElement(body, flexibleHeight: 1, flexibleWidth: 1);

        BuildTabStrip(body);
        BuildContentArea(body);
        BuildLastResponsePanel(ContentRoot);
        BuildOverlayFooter(ContentRoot);
        BuildTooltipFooter(ContentRoot);

        ShowTab(ActiveTab);
    }

    private void BuildTooltipFooter(GameObject parent)
    {
        var footer = UIFactory.CreateHorizontalGroup(parent, "TooltipFooter",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(8, 8, 2, 2));
        UIFactory.SetLayoutElement(footer, minHeight: 22, preferredHeight: 22, flexibleHeight: 0, flexibleWidth: 1);

        var lbl = UIFactory.CreateLabel(footer, "TooltipText",
            TooltipHover.IdlePlaceholder,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 400, preferredWidth: 600, flexibleWidth: 1,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Italic;
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;

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

    private void BuildTabStrip(GameObject parent)
    {
        // childControlHeight: true is required - the strip stacks group headers
        // and group-content blocks of varying heights, and without it the layout
        // group leaves children at default sizeDelta (~0px) so KINDRED/HELP
        // headers overlap the BLOODCRAFT sub-tab list.
        var strip = UIFactory.CreateVerticalGroup(parent, "TabStrip",
            forceWidth: false, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(strip, minWidth: 150, flexibleWidth: 0, flexibleHeight: 1);
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
                    _ => Services.EclipseProtocolService.UserRegistered,
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
        AddSectionHeading(page, "Active Familiar");

        _famNameLabel     = AddInfoLabel(page, "FamName",     "—", FontStyles.Bold,   fontSize: Theme.ScaledUI(18));
        _famProgressLabel = AddInfoLabel(page, "FamProgress", "Level — ", FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        _famStatsLabel    = AddInfoLabel(page, "FamStats",    "HP —  PP —  SP —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        AddSpacer(page, 4);
        AddSectionHeading(page, "Actions");

        // Row 1 — common, safe actions.
        var row1 = UIFactory.CreateHorizontalGroup(page, "FamActionsRow1",
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

        AddSpacer(page, 4);

        // Row 2 — irreversible actions. Destroy is red and requires a second click.
        var row2 = UIFactory.CreateHorizontalGroup(page, "FamActionsRow2",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row2,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(row2, "Prestige", MessageService.BCCOM_FAM_PRESTIGE,
            "Prestige the active familiar (.fam pr). Requires max level; resets level and grants permanent bonuses.");
        AddCommandButton(row2, "Unbind", MessageService.BCCOM_FAM_UNBIND,
            "Unbind the active familiar (.fam ub). The in-world entity is released but the familiar STAYS in your box — you can re-bind it from the Boxes tab any time. Use this to free the bind slot so you can summon a different familiar. To permanently delete a familiar from your collection, use the Boxes tab → Permanently Delete form.");

        AddSpacer(page, 4);
        var note = UIFactory.CreateLabel(page, "FamNote",
            "Switch to the Boxes tab to browse your familiar boxes and click-to-bind.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(note.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 28, flexibleHeight: 0);
        note.TextMesh.enableWordWrapping = true;
        note.TextMesh.overflowMode = TextOverflowModes.Overflow;

        AddSpacer(page, 6);
        AddSectionHeading(page, "Emote Bindings (perform these in-world)");
        var emoteRef = UIFactory.CreateLabel(page, "FamEmoteRef",
            "Bloodcraft binds these emotes to familiar actions. Trigger by performing the emote in-world (e.g. /clap), NOT via this UI — there's no chat command to invoke an emote programmatically. Toggle Emotes (above) enables/disables the whole system.\n\n" +
            "  • Wave   →  Recall / Dismiss\n" +
            "  • Salute →  Toggle Combat Mode\n" +
            "  • Clap   →  Bind / Unbind active familiar\n" +
            "  • Beckon →  Interact (opens familiar's inventory, equipment, name & settings)",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(emoteRef.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 110, preferredHeight: 130, flexibleHeight: 0);
        emoteRef.TextMesh.enableWordWrapping = true;
        emoteRef.TextMesh.overflowMode = TextOverflowModes.Overflow;

        // ---- 0.6.0: extra .fam commands the audit caught ----
        AddSpacer(page, 6);
        AddSectionHeading(page, "More Familiar Actions");

        CollapsibleSection.Build(page,
            title: "Search boxes by name (.fam s)",
            startExpanded: false,
            tooltip: "Search across ALL your boxes for familiars whose name matches the text. Reply appears in chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Search familiars",
                commandTemplate: ".fam s {name}",
                new TextField("name", "Name (substring)", placeholder: "Wolf",
                    tooltip: "Substring of the familiar's display name. Bloodcraft does case-insensitive matching across boxes.")));

        CollapsibleSection.Build(page,
            title: "Smart bind by name (.fam sb)",
            startExpanded: false,
            tooltip: "Search + bind in one step. If multiple matches are found Bloodcraft returns the list for clarification (no destructive action). Will fail if you already have a familiar bound.",
            buildContent: c => FormBuilder.Build(c,
                title: "Smart bind",
                commandTemplate: ".fam sb {name}",
                new TextField("name", "Name (substring)", placeholder: "Wolf",
                    tooltip: "Substring of the familiar's display name to bind.")));

        CollapsibleSection.Build(page,
            title: "Make active familiar shiny (.fam shiny)",
            startExpanded: false,
            tooltip: "Spends vampiric dust to permanently mark your CURRENT active familiar with a shiny buff of the chosen school. Requires an active familiar bound first.",
            buildContent: c => FormBuilder.Build(c,
                title: "Apply shiny",
                commandTemplate: ".fam shiny {school}",
                new EnumField<PlayerStateService.FamiliarShinySchoolChoice>("school", "Spell school",
                    defaultValue: PlayerStateService.FamiliarShinySchoolChoice.Storm,
                    tooltip: "The shiny element to apply. Each school has a flavour (Storm = stun, Blood = leech, etc.).")));

        CollapsibleSection.Build(page,
            title: "Toggle a familiar setting (.fam option)",
            startExpanded: false,
            tooltip: "Flips one of Bloodcraft's per-player familiar settings. Common settings: 'shiny' (apply shiny visuals), 'vbloodemotes' (familiar plays VBlood emotes). Bloodcraft's reply tells you what's now on/off.",
            buildContent: c => FormBuilder.Build(c,
                title: "Toggle option",
                commandTemplate: ".fam option {setting}",
                new TextField("setting", "Setting name", placeholder: "shiny",
                    tooltip: "Name of the setting to toggle. Server replies with the new state in chat.")));

        CollapsibleSection.Build(page,
            title: "Buy V-Blood echoes (.fam echoes)",
            startExpanded: false,
            tooltip: "Spend V-Blood essence to purchase the exo reward tied to the named V-Blood unit. Cost scales with unit tier.",
            buildContent: c => FormBuilder.Build(c,
                title: "Buy echoes",
                commandTemplate: ".fam echoes {vblood}",
                new TextField("vblood", "V-Blood name", placeholder: "Quincey the Bandit King",
                    tooltip: "Exact display name of the V-Blood whose echo reward you want.")));

        CollapsibleSection.Build(page,
            title: "Reset all familiar entities (.fam reset) — DESTRUCTIVE",
            startExpanded: false,
            tooltip: "Destroys every entity in your follower buffer and clears your familiar-actives state. Use to recover from a bugged or stuck familiar bind. Required confirm checkbox.",
            buildContent: c => FormBuilder.Build(c,
                title: "Reset familiars",
                commandTemplate: ".fam reset",
                new BoolField("confirm", "Yes, destroy active follower entities",
                    tooltip: "Required. Box records and unlock data are NOT touched — this only clears in-world entities + active state. Re-bind from a box to summon again.",
                    requireTrue: true)));

        // ---- 0.6.0: battle group system ----
        AddSpacer(page, 6);
        AddSectionHeading(page, "Battle Groups");
        var bgIntro = UIFactory.CreateLabel(page, "FamBgIntro",
            "Battle groups are pre-built lineups of familiars for PvP challenges. List shows the groups you've made; create one, slot familiars into it, then challenge another player.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(bgIntro.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 40, flexibleHeight: 0);
        bgIntro.TextMesh.fontStyle = FontStyles.Italic;
        bgIntro.TextMesh.enableWordWrapping = true;
        bgIntro.TextMesh.overflowMode = TextOverflowModes.Overflow;

        var bgRow1 = UIFactory.CreateHorizontalGroup(page, "FamBgRow1",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(bgRow1,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(bgRow1, "List Groups", MessageService.BCCOM_FAM_BG_LIST,
            "List your battle groups (.fam bgs).");

        CollapsibleSection.Build(page,
            title: "Show battle group details (.fam bg)",
            startExpanded: false,
            tooltip: "Show the contents of a battle group. Leave blank to inspect your active group.",
            buildContent: c => FormBuilder.Build(c,
                title: "Show battle group",
                commandTemplate: ".fam bg {group}",
                new TextField("group", "Group name (blank = active)")));

        CollapsibleSection.Build(page,
            title: "Choose active battle group (.fam cbg)",
            startExpanded: false,
            tooltip: "Sets which battle group is your active one (used by .fam challenge).",
            buildContent: c => FormBuilder.Build(c,
                title: "Choose battle group",
                commandTemplate: ".fam cbg {group}",
                new TextField("group", "Group name", placeholder: "MyTeam")));

        CollapsibleSection.Build(page,
            title: "Create battle group (.fam abg)",
            startExpanded: false,
            tooltip: "Create a new (empty) battle group. Use Slot Familiar below to fill it.",
            buildContent: c => FormBuilder.Build(c,
                title: "Create battle group",
                commandTemplate: ".fam abg {group}",
                new TextField("group", "New group name", placeholder: "MyTeam")));

        CollapsibleSection.Build(page,
            title: "Slot active familiar into group (.fam sbg)",
            startExpanded: false,
            tooltip: "Assigns your CURRENTLY-bound familiar to a slot in the named group. Bind the familiar you want to slot first.",
            buildContent: c => FormBuilder.Build(c,
                title: "Slot familiar",
                commandTemplate: ".fam sbg {group} {slot}",
                new TextField("group", "Group name", placeholder: "MyTeam"),
                new IntField("slot", "Slot (1-3)", min: 1, max: 3,
                    tooltip: "Which slot in the group to put the familiar.")));

        CollapsibleSection.Build(page,
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

        CollapsibleSection.Build(page,
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
    }

    private void OnFamiliarChanged() => RenderFamiliar(PlayerStateService.Familiar);

    // -----------------------------------------------------------------------
    // Boxes tab
    // -----------------------------------------------------------------------

    private void BuildBoxesTab(GameObject page)
    {
        // Compact active-box label always shown at the top.
        _boxesActiveBoxLabel = AddInfoLabel(page, "ActiveBox",
            "Active Box: (none selected)",
            FontStyles.Bold, fontSize: Theme.ScaledUI(14));

        // Hint text near the top so it can never be overlapped by a long box
        // list further down. Phrased as a one-liner so it doesn't dominate.
        var note = UIFactory.CreateLabel(page, "BoxesNote",
            "Tip: click Refresh to pull your box list, click a box to see its familiars, click a familiar to bind it. Use ← Back to return.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(note.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 32, flexibleHeight: 0);
        note.TextMesh.enableWordWrapping = true;
        note.TextMesh.overflowMode = TextOverflowModes.Overflow;
        note.TextMesh.fontStyle = FontStyles.Italic;

        AddSpacer(page, 4);

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
                new TextField("boxName", "Box name", placeholder: "MyBox",
                    tooltip: "Name of the box to delete. Server will reject if it's not empty.")));

        CollapsibleSection.Build(_boxesPickerSection,
            title: "Rename box (.fam rb)",
            startExpanded: false,
            tooltip: "Renames an existing box.",
            buildContent: content => FormBuilder.Build(content,
                title: "Rename box",
                commandTemplate: ".fam rb {current} {newName}",
                onSubmitted: refreshBoxes,
                new TextField("current", "Current name", placeholder: "OldName",
                    tooltip: "The box's current name."),
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
                new TextField("boxName", "Destination box", placeholder: "TargetBox",
                    tooltip: "Name of the box to move the active familiar into. Must already exist; create one with the box management form on the picker view.")));

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
        AddSectionHeading(page, "Active Class");

        _classNameLabel  = AddInfoLabel(page, "ClassName",  "—",       FontStyles.Bold,   fontSize: Theme.ScaledUI(18));
        _classLevelLabel = AddInfoLabel(page, "ClassLevel", "Level —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        AddSpacer(page, 4);
        AddSectionHeading(page, "Actions");

        var actions = UIFactory.CreateHorizontalGroup(page, "ClassActions",
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
        AddSectionHeading(page, "Change Class");

        CollapsibleSection.Build(page,
            title: "Select / change your class (.class s)",
            startExpanded: false,
            tooltip: "Pick a class from the dropdown and Submit. Some servers may rate-limit class changes or require a cost — Bloodcraft replies in chat with success or the rejection reason.",
            buildContent: c => FormBuilder.Build(c,
                title: "Select class",
                commandTemplate: ".class s {class}",
                new EnumField<PlayerStateService.BloodcraftClassChoice>("class", "Class",
                    defaultValue: PlayerStateService.BloodcraftClassChoice.BloodKnight,
                    tooltip: "The class you want active. Bloodcraft's six built-in classes are listed.")));

        CollapsibleSection.Build(page,
            title: "Choose class shift spell (.class csp)",
            startExpanded: false,
            tooltip: "Set which of your class's spells occupies your shift slot. Use 'List Spells' above to see the available spells (numbered) for your current class, then enter the spell's 1-based index here.",
            buildContent: c => FormBuilder.Build(c,
                title: "Choose shift spell",
                commandTemplate: ".class csp {index}",
                new IntField("index", "Spell #", min: 1, max: 32,
                    tooltip: "1-based index of the class spell. Run 'List Spells' to see what each number maps to before submitting.")));

        AddSpacer(page, 2);
        var note = UIFactory.CreateLabel(page, "ClassNote",
            "Tip: List Spells / List Stats above describe what each class grants before you commit.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(note.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 26, flexibleHeight: 0);
        note.TextMesh.fontStyle = FontStyles.Italic;
        note.TextMesh.enableWordWrapping = true;
        note.TextMesh.overflowMode = TextOverflowModes.Overflow;

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

    private void BuildExpertiseTab(GameObject page)
    {
        AddSectionHeading(page, "Current Weapon Expertise");

        _wepTypeLabel     = AddInfoLabel(page, "WepType",     "—",                  FontStyles.Bold,   fontSize: Theme.ScaledUI(18));
        _wepProgressLabel = AddInfoLabel(page, "WepProgress", "Level —",            FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        _wepBonusLabel    = AddInfoLabel(page, "WepBonus",    "Bonus Stats: —",     FontStyles.Normal, fontSize: Theme.ScaledUI(13));

        AddSpacer(page, 4);
        AddSectionHeading(page, "Actions");

        var actions = UIFactory.CreateHorizontalGroup(page, "WepActions",
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
        AddSectionHeading(page, "Choose Bonus Stat");

        CollapsibleSection.Build(page,
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

        AddSpacer(page, 4);
        var note = UIFactory.CreateLabel(page, "WepNote",
            "Bloodcraft only streams the EQUIPPED weapon's expertise to the client — there's no command to query stats for weapons you're not currently holding. Switch weapons to see each one's level + chosen stats above.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(note.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 44, flexibleHeight: 0);
        note.TextMesh.fontStyle = FontStyles.Italic;
        note.TextMesh.enableWordWrapping = true;
        note.TextMesh.overflowMode = TextOverflowModes.Overflow;

        RenderExpertise(PlayerStateService.Expertise);
        if (!_wepSubscribed)
        {
            PlayerStateService.ExpertiseChanged += OnExpertiseChanged;
            _wepSubscribed = true;
        }
    }

    private void OnExpertiseChanged() => RenderExpertise(PlayerStateService.Expertise);

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
        AddSectionHeading(page, "Current Blood Legacy");

        _blTypeLabel     = AddInfoLabel(page, "BlType",     "—",                  FontStyles.Bold,   fontSize: Theme.ScaledUI(18));
        _blTypeLabel.color = new Color(1f, 0.4f, 0.4f); // Bloodcraft uses red for blood headings
        ApplyStrongAccentOutline(_blTypeLabel);
        _blProgressLabel = AddInfoLabel(page, "BlProgress", "Level —",            FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        _blBonusLabel    = AddInfoLabel(page, "BlBonus",    "Bonus Stats: —",     FontStyles.Normal, fontSize: Theme.ScaledUI(13));

        AddSpacer(page, 4);
        AddSectionHeading(page, "Actions");

        var actions = UIFactory.CreateHorizontalGroup(page, "BlActions",
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
        AddSectionHeading(page, "Choose Bonus Stat");

        CollapsibleSection.Build(page,
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

        CollapsibleSection.Build(page,
            title: "Show info for a specific blood (.bl get [Blood])",
            startExpanded: false,
            tooltip: "Query any blood type's level + chosen stats — not just the one you currently have. Result is parsed and shown in the panel below; chat is also updated unless you've enabled 'Clear server messages'.",
            buildContent: c => FormBuilder.Build(c,
                title: "Show blood info",
                commandTemplate: ".bl get {blood}",
                new EnumField<PlayerStateService.BloodTypeChoice>("blood", "Blood",
                    defaultValue: PlayerStateService.BloodTypeChoice.Warrior,
                    tooltip: "Which blood type to inspect.")));

        AddSpacer(page, 4);
        BuildBloodInfoDisplay(page);

        AddSpacer(page, 4);
        var note = UIFactory.CreateLabel(page, "BlNote",
            "Unlike weapon expertise, .bl get accepts a blood-type argument — so the 'Show info for a specific blood' form above can inspect ANY blood you've leveled, not just your current one.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(note.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 44, flexibleHeight: 0);
        note.TextMesh.fontStyle = FontStyles.Italic;
        note.TextMesh.enableWordWrapping = true;
        note.TextMesh.overflowMode = TextOverflowModes.Overflow;

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
        AutoResizeIfEnabled();
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

    private void OnLegacyChanged() => RenderBloodLegacy(PlayerStateService.Legacy);

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
        AddSectionHeading(page, "Shift Spell");

        _shiftSpellLabel = AddInfoLabel(page, "ShiftSpell",
            "Equipped: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

        AddSpacer(page, 4);
        AddSectionHeading(page, "Unarmed Expertise");

        _unarmedStatusLabel = AddInfoLabel(page, "UnarmedStatus",
            "Equip your fists (no weapon) to inspect unarmed expertise.",
            FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        _unarmedBonusLabel = AddInfoLabel(page, "UnarmedBonus",
            "Bonus Stats: —", FontStyles.Normal, fontSize: Theme.ScaledUI(13));

        AddSpacer(page, 4);
        AddSectionHeading(page, "Actions");

        var actions = UIFactory.CreateHorizontalGroup(page, "UnarmedShiftActions",
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

        AddSpacer(page, 4);
        var note = UIFactory.CreateLabel(page, "ShiftNote",
            "Choosing which class spell goes in the shift slot takes a number (`.class csp <#>`). " +
            "Use chat for now; a spell picker arrives in a later phase.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(note.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 40, preferredHeight: 50, flexibleHeight: 0);
        note.TextMesh.enableWordWrapping = true;
        note.TextMesh.overflowMode = TextOverflowModes.Overflow;

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
        AddSectionHeading(page, "Current Prestige");

        // 0.8.2: wrap the 4 prestige info rows in their own padded VLG so they
        // don't crowd against the section heading or each other. Pre-0.8.2 they
        // were direct children of the page (spacing inherited from the page's
        // VLG, which is 2-3px) — friend-testing surfaced this as "crammed".
        var prestigeSummary = UIFactory.CreateVerticalGroup(page, "PrestigeSummary",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(8, 8, 6, 6));
        UIFactory.SetLayoutElement(prestigeSummary,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 130, flexibleHeight: 0);

        _prestigeXpLabel        = AddInfoLabel(prestigeSummary, "PrestigeXp",        "Experience prestige: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        _prestigeLegacyLabel    = AddInfoLabel(prestigeSummary, "PrestigeLegacy",    "Blood legacy prestige: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        _prestigeExpertiseLabel = AddInfoLabel(prestigeSummary, "PrestigeExpertise", "Weapon expertise prestige: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));
        _prestigeFamLabel       = AddInfoLabel(prestigeSummary, "PrestigeFam",       "Familiar prestige: —", FontStyles.Normal, fontSize: Theme.ScaledUI(14));

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
        AddSectionHeading(page, "Player Experience");
        _lvlXpLabel = AddInfoLabel(page, "LvlXp", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(13));

        AddSpacer(page, 4);
        AddSectionHeading(page, "Blood Legacy");
        _lvlLegacyLabel = AddInfoLabel(page, "LvlLegacy", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(13));

        AddSpacer(page, 4);
        AddSectionHeading(page, "Weapon Expertise (active weapon)");
        _lvlExpertiseLabel      = AddInfoLabel(page, "LvlExpertise",      "—", FontStyles.Normal, fontSize: Theme.ScaledUI(13));
        _lvlExpertiseBonusLabel = AddInfoLabel(page, "LvlExpertiseBonus", "Bonus stats: —", FontStyles.Italic, fontSize: Theme.ScaledUI(12));

        // Bloodcraft's Eclipse protocol only streams the currently-equipped
        // weapon's expertise level - no per-weapon snapshot. So the in-panel
        // "all weapons" view the user asked for needs server-side support we
        // don't have yet. Surface a "List Weapons" button (sends .wep l, reply
        // appears in chat) and a one-liner explaining the gap, so the user
        // isn't left wondering why the UI only shows one weapon.
        var allWepRow = UIFactory.CreateHorizontalGroup(page, "AllWepRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(allWepRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        AddCommandButton(allWepRow, "List Weapon Types (chat)", MessageService.BCCOM_WEP_LIST,
            "Sends .wep l. Bloodcraft replies in chat with the list of weapon types you can level. Per-weapon levels for all weapons aren't streamed by the server, so they can't be shown in this panel — switch weapons and the active level above updates.");

        AddSpacer(page, 4);
        AddSectionHeading(page, "Familiar (active)");
        _lvlFamLabel      = AddInfoLabel(page, "LvlFam",      "—", FontStyles.Normal, fontSize: Theme.ScaledUI(13));
        _lvlFamStatsLabel = AddInfoLabel(page, "LvlFamStats", "HP —   PP —   SP —", FontStyles.Italic, fontSize: Theme.ScaledUI(12));

        AddSpacer(page, 4);
        AddSectionHeading(page, "Professions");
        _lvlProfessions1Label = AddInfoLabel(page, "LvlProf1", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(12));
        _lvlProfessions2Label = AddInfoLabel(page, "LvlProf2", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(12));
        _lvlProfessions3Label = AddInfoLabel(page, "LvlProf3", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(12));
        _lvlProfessions4Label = AddInfoLabel(page, "LvlProf4", "—", FontStyles.Normal, fontSize: Theme.ScaledUI(12));

        // ---- 0.7.0: profession (.prof) commands the re-audit caught ----
        AddSpacer(page, 8);
        AddSectionHeading(page, "Profession Tools");
        var profRow = UIFactory.CreateHorizontalGroup(page, "ProfRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(profRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(profRow, "List Professions", MessageService.BCCOM_PROF_LIST,
            "List the professions Bloodcraft tracks (.prof l). Reply in chat.");
        AddCommandButton(profRow, "Toggle Prof Log",  MessageService.BCCOM_PROF_LOG_TOGGLE,
            "Toggle in-chat profession-progress logging (.prof log). SERVER-side toggle.");

        AddSpacer(page, 4);
        CollapsibleSection.Build(page,
            title: "Show profession progress (.prof get)",
            startExpanded: false,
            tooltip: "Displays your current level + progress for the chosen profession in chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Show profession",
                commandTemplate: ".prof get {profession}",
                new EnumField<PlayerStateService.BloodcraftProfession>("profession", "Profession",
                    defaultValue: PlayerStateService.BloodcraftProfession.Enchanting,
                    tooltip: "Which profession to inspect.")));

        // ---- 0.6.0: .lvl + .misc utilities the audit caught ----
        AddSpacer(page, 8);
        AddSectionHeading(page, "Player Tools");
        var toolsRow1 = UIFactory.CreateHorizontalGroup(page, "PlayerToolsRow1",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(toolsRow1,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(toolsRow1, "User Stats",     MessageService.BCCOM_MISC_USER_STATS,
            "Print a summary of player stats in chat (.misc userstats).");
        AddCommandButton(toolsRow1, "Toggle XP Log",  MessageService.BCCOM_LVL_LOG_TOGGLE,
            "Toggle in-chat logging of leveling-progress messages (.lvl log). SERVER-side toggle — Bloodcraft replies with the new state in chat.");
        AddCommandButton(toolsRow1, "Reminders",      MessageService.BCCOM_MISC_REMINDERS,
            "Toggle general feature reminders (.misc remindme). SERVER-side toggle.");
        AddCommandButton(toolsRow1, "Silence Music",  MessageService.BCCOM_MISC_SILENCE,
            "Reset stuck combat music if it won't stop (.misc silence).");

        var toolsRow2 = UIFactory.CreateHorizontalGroup(page, "PlayerToolsRow2",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(toolsRow2,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(toolsRow2, "Starter Kit",    MessageService.BCCOM_MISC_KIT_ME,
            "Claim the server's starter kit (.misc kitme). One-time on most servers.");
        AddCommandButton(toolsRow2, "Prepare Hunt",   MessageService.BCCOM_MISC_PREPARE,
            "Auto-complete the GettingReadyForTheHunt quest if it's stuck (.misc prepare).");

        AddSpacer(page, 4);
        CollapsibleSection.Build(page,
            title: "Toggle scrolling combat text (.misc sct)",
            startExpanded: false,
            tooltip: "Enable or disable a specific scrolling-combat-text element. Bloodcraft replies with the new state in chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Toggle SCT",
                commandTemplate: ".misc sct {type}",
                new TextField("type", "SCT element type",
                    tooltip: "Element name (e.g. 'damage', 'heal'). Bloodcraft's reply tells you the new state.")));

        AddSpacer(page, 4);
        var toolsNote = UIFactory.CreateLabel(page, "PlayerToolsNote",
            "Heads up: most of these are server-side TOGGLES — Bloodcraft flips a flag and reports the new state in chat. The client can't 'remember' the new state across sessions because the server is the source of truth (same with Toggle Emotes / Toggle Shift / etc. on other tabs).",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(toolsNote.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 50, flexibleHeight: 0);
        toolsNote.TextMesh.fontStyle = FontStyles.Italic;
        toolsNote.TextMesh.enableWordWrapping = true;
        toolsNote.TextMesh.overflowMode = TextOverflowModes.Overflow;

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

        bool famActive = fam.Level > 0 || !string.IsNullOrEmpty(fam.Name);
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
        AddSectionHeading(page, "Daily Quest");
        _dqDailyTargetLabel   = AddInfoLabel(page, "DQDailyTarget",   "—", FontStyles.Bold,   fontSize: Theme.ScaledUI(15));
        _dqDailyTargetLabel.color = new Color(0f, 1f, 1f); // Bloodcraft cyan #00FFFF
        ApplyStrongAccentOutline(_dqDailyTargetLabel);
        _dqDailyProgressLabel = AddInfoLabel(page, "DQDailyProgress", "—", FontStyles.Italic, fontSize: Theme.ScaledUI(13));

        AddSpacer(page, 4);
        var dailyRow = UIFactory.CreateHorizontalGroup(page, "DQDailyActions",
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

        AddSpacer(page, 8);
        AddSectionHeading(page, "Weekly Quest");
        _dqWeeklyTargetLabel   = AddInfoLabel(page, "DQWeeklyTarget",   "—", FontStyles.Bold,   fontSize: Theme.ScaledUI(15));
        // 0.9.2: dropped the pink family entirely. v0.9.1 tried brightening
        // Bloodcraft's #BF40BF magenta to (1, 0.55, 1) but it still reads as
        // pink against red in-game backdrops. Switched to gold/yellow — well
        // outside the red wavelength so contrast survives any backdrop, and
        // still visually distinct from the cyan daily-quest target.
        _dqWeeklyTargetLabel.color = new Color(1f, 0.85f, 0.3f);
        ApplyStrongAccentOutline(_dqWeeklyTargetLabel);
        _dqWeeklyProgressLabel = AddInfoLabel(page, "DQWeeklyProgress", "—", FontStyles.Italic, fontSize: Theme.ScaledUI(13));

        AddSpacer(page, 4);
        var weeklyRow = UIFactory.CreateHorizontalGroup(page, "DQWeeklyActions",
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

        AddSpacer(page, 8);
        AddSectionHeading(page, "Settings");
        var setRow = UIFactory.CreateHorizontalGroup(page, "DQSettings",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(setRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(setRow, "Toggle Quest Log", MessageService.BCCOM_QUEST_LOG_TOGGLE,
            "Toggle in-chat progress logging (.quest log). When on, Bloodcraft prints a message each time you progress an objective.");

        AddSpacer(page, 6);
        var note = UIFactory.CreateLabel(page, "DQNote",
            "Toggle the Daily Quest overlay from the panel footer to track progress in a small movable HUD.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(note.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 26, flexibleHeight: 0);
        note.TextMesh.fontStyle = FontStyles.Italic;
        note.TextMesh.enableWordWrapping = true;
        note.TextMesh.overflowMode = TextOverflowModes.Overflow;

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
            ? $"⚔  {s.TargetName}  (V Blood)"
            : $"⚔  {s.TargetName}";
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
        RenderAdminInfoNote(page, "Bloodcraft admin");

        AddSectionHeading(page, "Server Diagnostics");

        var diagRow = UIFactory.CreateHorizontalGroup(page, "AdminDiag",
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
        var note = UIFactory.CreateLabel(page, "AdminNote",
            "All admin commands now have forms. If you aren't an admin on this server, commands return a permission error.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(note.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 32, flexibleHeight: 0);
        note.TextMesh.enableWordWrapping = true;
        note.TextMesh.overflowMode = TextOverflowModes.Overflow;
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
        var intro = UIFactory.CreateLabel(page, "KLIntro",
            "Requires the KindredLogistics server mod. Personal toggles affect only your character; admin globals affect the whole server (admin only).",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(intro.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 36, flexibleHeight: 0);
        intro.TextMesh.enableWordWrapping = true;
        intro.TextMesh.overflowMode = TextOverflowModes.Overflow;

        // ---- Personal toggles (.l ...) -----------------------------------
        AddSpacer(page, 4);
        AddSectionHeading(page, "Personal Toggles (.l)");

        var pr1 = AddKLRow(page, "KLPersonal1");
        AddCommandButton(pr1, "Sort Stash",     MessageService.BCCOM_KL_SORT_STASH,
            "Toggle auto-stash on double-click of the sort button (.l ss).");
        AddCommandButton(pr1, "Craft Pull",     MessageService.BCCOM_KL_CRAFT_PULL,
            "Toggle right-click on a recipe pulling missing ingredients (.l cr).");
        AddCommandButton(pr1, "Don't Pull Last", MessageService.BCCOM_KL_DONT_PULL_LAST,
            "Toggle never pulling the last item from a container (.l dpl).");
        AddCommandButton(pr1, "Servant Stash",  MessageService.BCCOM_KL_AUTOSTASH_MISSION,
            "Toggle auto-stash of servant mission rewards (.l asm).");

        var pr2 = AddKLRow(page, "KLPersonal2");
        AddCommandButton(pr2, "Conveyor",      MessageService.BCCOM_KL_CONVEYOR,
            "Toggle named sender/receiver chests routing items between them (.l co).");
        AddCommandButton(pr2, "Salvage",       MessageService.BCCOM_KL_SALVAGE,
            "Toggle chests named 'salvage' auto-salvaging their contents (.l sal).");
        AddCommandButton(pr2, "Unit Spawner",  MessageService.BCCOM_KL_UNIT_SPAWNER,
            "Toggle chests named 'spawner' auto-filling unit stations (.l us).");
        AddCommandButton(pr2, "Brazier",       MessageService.BCCOM_KL_BRAZIER,
            "Toggle chests named 'brazier' auto-fueling braziers (.l bz).");

        var pr3 = AddKLRow(page, "KLPersonal3");
        AddCommandButton(pr3, "Silent Pull",   MessageService.BCCOM_KL_SILENT_PULL,
            "Toggle suppressing chat messages when pulling items (.l sp).");
        AddCommandButton(pr3, "Silent Stash",  MessageService.BCCOM_KL_SILENT_STASH,
            "Toggle suppressing chat messages when stashing items (.l ssh).");
        AddCommandButton(pr3, "Show Settings", MessageService.BCCOM_KL_SETTINGS,
            "Print your current personal Logistics settings into chat (.l s).");

        // ---- Utility -----------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Utility");

        var util = AddKLRow(page, "KLUtility");
        AddCommandButton(util, "Stash All",    MessageService.BCCOM_KL_STASH_ALL,
            "Stash all items in your inventory into nearby chests (.stash).");

        CollapsibleSection.Build(page,
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

        CollapsibleSection.Build(page,
            title: "Find item (.fi)",
            startExpanded: false,
            tooltip: "Locates the specified item in nearby chests and prints which chest holds it.",
            buildContent: c => FormBuilder.Build(c,
                title: "Find item",
                commandTemplate: ".fi {item}",
                new TextField("item", "Item name",
                    tooltip: "Item to search for. Exact match against the item's prefab name.")));

        CollapsibleSection.Build(page,
            title: "Find chest by name (.fc)",
            startExpanded: false,
            tooltip: "Locates chests with the specified custom name.",
            buildContent: c => FormBuilder.Build(c,
                title: "Find chest",
                commandTemplate: ".fc {name}",
                new TextField("name", "Chest name",
                    tooltip: "The custom name written on the chest's sign (e.g. 'salvage', 'spawner', 'brazier').")));

        // Admin globals (.lg ...) live on the dedicated KindredLogisticsAdminTab.
        // Pointer left here so anyone reading BuildKindredLogisticsTab knows
        // where the rest of the surface went. Admin tabs are not gated client-
        // side as of 0.8.2 — the server enforces permissions.
    }

    private void BuildKindredLogisticsAdminTab(GameObject page)
    {
        RenderAdminInfoNote(page, "Kindred Logistics admin");

        var intro = UIFactory.CreateLabel(page, "KLAdminIntro",
            "Server-wide toggles for the KindredLogistics features. These affect every player on the server. Requires admin permission server-side.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(intro.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 36, flexibleHeight: 0);
        intro.TextMesh.enableWordWrapping = true;
        intro.TextMesh.overflowMode = TextOverflowModes.Overflow;

        AddSpacer(page, 4);
        AddSectionHeading(page, "Admin Globals (.lg)");

        var ar1 = AddKLRow(page, "KLAdmin1");
        AddCommandButton(ar1, "Sort Stash",      MessageService.BCCOM_KL_ADMIN_SORT_STASH,
            "Server-wide: enable auto-stash on sort double-click (.lg ss).");
        AddCommandButton(ar1, "Pull",            MessageService.BCCOM_KL_ADMIN_PULL,
            "Server-wide: enable the .pull command for all players (.lg p).");
        AddCommandButton(ar1, "Craft Pull",      MessageService.BCCOM_KL_ADMIN_CRAFT_PULL,
            "Server-wide: enable right-click-recipe ingredient pulling (.lg cr).");
        AddCommandButton(ar1, "Servant Stash",   MessageService.BCCOM_KL_ADMIN_AUTOSTASH_MISSION,
            "Server-wide: enable auto-stash for servant mission rewards (.lg asm).");

        var ar2 = AddKLRow(page, "KLAdmin2");
        AddCommandButton(ar2, "Conveyor",        MessageService.BCCOM_KL_ADMIN_CONVEYOR,
            "Server-wide: enable sender/receiver conveyor chests (.lg co).");
        AddCommandButton(ar2, "Salvage",         MessageService.BCCOM_KL_ADMIN_SALVAGE,
            "Server-wide: enable 'salvage' chests (.lg sal).");
        AddCommandButton(ar2, "Unit Spawner",    MessageService.BCCOM_KL_ADMIN_UNIT_SPAWNER,
            "Server-wide: enable 'spawner' chests filling unit stations (.lg us).");
        AddCommandButton(ar2, "Brazier",         MessageService.BCCOM_KL_ADMIN_BRAZIER,
            "Server-wide: enable 'brazier' chests auto-fueling braziers (.lg bz).");

        var ar3 = AddKLRow(page, "KLAdmin3");
        AddCommandButton(ar3, "Named Brazier",   MessageService.BCCOM_KL_ADMIN_NAMED_BRAZIER,
            "Server-wide: enable night/proximity-controlled named braziers (.lg nam).");
        AddCommandButton(ar3, "Trash",           MessageService.BCCOM_KL_ADMIN_TRASH,
            "Server-wide: allow 'trash' chests to delete their contents (.lg trash).");
        AddCommandButton(ar3, "Show Settings",   MessageService.BCCOM_KL_ADMIN_SETTINGS,
            "Print the current server-wide Logistics settings into chat (.lg s).");
        AddCommandButton(ar3, "Empty Trash",     MessageService.BCCOM_KL_ADMIN_EMPTY_TRASH,
            "Empty all trash containers in your current territory (.emptytrash).");

        AddSpacer(page, 6);
        CollapsibleSection.Build(page,
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
        var intro = UIFactory.CreateLabel(page, "KCPlayerIntro",
            "Requires the KindredCommands server mod. Player-facing commands only - admin commands land in their own tab.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(intro.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 32, flexibleHeight: 0);
        intro.TextMesh.enableWordWrapping = true;
        intro.TextMesh.overflowMode = TextOverflowModes.Overflow;

        // ---- Self ---------------------------------------------------------
        AddSpacer(page, 4);
        AddSectionHeading(page, "Self");

        var selfRow = AddKLRow(page, "KCSelf");
        AddCommandButton(selfRow, "AFK",   MessageService.BCCOM_KC_AFK,
            "Toggle AFK animation - locks WASD movement until you run .afk again (.afk).");
        AddCommandButton(selfRow, "Ping",  MessageService.BCCOM_KC_PING,
            "Show your latency in chat (.ping).");
        AddCommandButton(selfRow, "Pace",  MessageService.BCCOM_KC_PACE,
            "Pace at the closest NPC near you - a cosmetic walk loop (.pace).");

        // ---- Server info --------------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Server info");

        var infoRow1 = AddKLRow(page, "KCInfo1");
        AddCommandButton(infoRow1, "Server Time", MessageService.BCCOM_KC_TIME,
            "Print the current server time into chat (.time).");
        AddCommandButton(infoRow1, "Online Staff", MessageService.BCCOM_KC_STAFF,
            "List staff members currently online (.staff).");
        AddCommandButton(infoRow1, "Open Plots",   MessageService.BCCOM_KC_CASTLE_OPEN_PLOTS,
            "Report territories with open or decaying castle plots (.openplots — alias .op). Reply appears in chat.");
        AddCommandButton(infoRow1, "Soulshards",   MessageService.BCCOM_KC_GEAR_SOULSHARD_STATUS,
            "Print the status of soulshards on the server (.gear soulshardstatus).");

        var infoRow2 = AddKLRow(page, "KCInfo2");
        AddCommandButton(infoRow2, "Boss List",   MessageService.BCCOM_KC_BOSS_LIST,
            "List all locked bosses on the server (.boss list).");
        AddCommandButton(infoRow2, "Region List", MessageService.BCCOM_KC_REGION_LIST,
            "List all locked and gated regions on the server (.region list).");

        // Stateful clan-list pagination - prev/current-page/next replace the
        // old static "Clan List" button so users can flip through pages.
        BuildClanListPager(infoRow2);

        // ---- Lookups (forms) ---------------------------------------------
        AddSpacer(page, 6);
        AddSectionHeading(page, "Lookups");

        CollapsibleSection.Build(page,
            title: "Check player level (.checklevel)",
            startExpanded: false,
            tooltip: "Print a player's current level into chat.",
            buildContent: c => FormBuilder.Build(c,
                title: "Check player level",
                commandTemplate: ".checklevel {player}",
                new PlayerNameField("player", "Player",
                    tooltip: "Player whose level you want to look up. Exact character-name match.")));

        // (clan list pagination state - lives on the panel instance so the
        // current-page label keeps its value across re-renders.)

        CollapsibleSection.Build(page,
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

        AddGuideSection(page,
            "Authentication",
            "  • adminauth                — grant yourself admin powers (needed before any other vanilla admin command)\n" +
            "  • adminderegister          — drop admin powers for the current session");

        AddGuideSection(page,
            "Player management",
            "  • Kick <CharacterName>     — kick a player by name\n" +
            "  • BanUser <SteamID>        — ban a player by SteamID\n" +
            "  • Banhammer <SteamID>      — ban + delete the player's characters\n" +
            "  • Unban <UserIndex>        — unban (use BanList to find the index)\n" +
            "  • BanList                  — list current bans\n" +
            "  • Connectinfo              — print connection info for diagnostics\n" +
            "  • Mute <SteamID> <minutes> — silence a player (vanilla)\n\n" +
            "Chat-command equivalents already in BCH (KINDRED → Admin: Players):\n" +
            "  → .kick, .ban (via Kindred or vanilla), .unban, etc.");

        AddGuideSection(page,
            "Item / character spawning",
            "  • give <PrefabName>          — give yourself an item by prefab name\n" +
            "  • giveset                    — open the giveset menu (sets of armor/weapons)\n" +
            "  • SpawnUnit <PrefabName>     — spawn an NPC at your position\n" +
            "  • teleporttowaypoint <name>  — teleport to a waypoint\n\n" +
            "Chat-command equivalents already in BCH (KINDRED → Admin: World):\n" +
            "  → .give {item} {qty}\n" +
            "  → .spawnnpc / .customspawn / .customspawnat\n" +
            "  → .teleport {x} {y} {z} {player}\n" +
            "Use the Lookups section on the same tab to find prefab names.");

        AddGuideSection(page,
            "Server",
            "  • List                  — list every console command\n" +
            "  • Help <command>        — detailed help for a command\n" +
            "  • Save                  — force a server save\n" +
            "  • Disconnect            — disconnect yourself from the server\n" +
            "  • Quit                  — close the V Rising client");

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
        // 0.9.2: Display settings + Chat noise moved to their own Settings tab.
        // About tab is now purely acknowledgements + community links.
        AddGuideSection(page,
            "Server-side mods this UI talks to",
            "BloodCraftHub is a CLIENT mod — it doesn't change the server. " +
            "Everything you see here is wrapping the chat-command surface of " +
            "two server-side mods made by other developers. Big thanks to:");

        AddGuideSection(page,
            "Bloodcraft  —  by zfolmt",
            "Leveling, expertise, legacies, professions, familiars, classes, quests! " +
            "The bulk of what BloodCraftHub surfaces (every BLOODCRAFT-group tab) " +
            "would not exist without zfolmt's mod.");
        AddLinkRow(page, "Bloodcraft on Thunderstore",
            "https://thunderstore.io/c/v-rising/p/zfolmt/Bloodcraft/");

        AddGuideSection(page,
            "KindredCommands  —  by odjit",
            "Commands to expand administration efforts and provide information. " +
            "The KINDRED admin tabs (Players / Server / World) and the Logistics " +
            "section all call into odjit's mods.");
        AddLinkRow(page, "KindredCommands on Thunderstore",
            "https://thunderstore.io/c/v-rising/p/odjit/KindredCommands/");

        AddGuideSection(page,
            "About me",
            "Player Name: Chaos\n" +
            "V Rising Server: The Shadow Realm  (Brutal, PvE)\n\n" +
            "Want to support development? Use the Open buttons below.");

        AddLinkRow(page, "Server Discord",   "https://discord.gg/usC9QgBrXK");
        AddLinkRow(page, "PayPal (support)", "https://www.paypal.com/paypalme/KrisPenland");
        AddLinkRow(page, "SkillEra.IO",      "https://SkillEra.IO");

        AddGuideSection(page,
            "About this UI",
            "BloodCraftHub is open source (MIT). Bug reports, feature ideas, " +
            "and pull requests welcome:");

        AddLinkRow(page, "GitHub repo", "https://github.com/KDavidP1987/BloodCraftHub");
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
            "Display settings  (0.9.0)",
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
        AddSectionHeading(page, "HUD extras");
        AddShowProgressBarsToggle(page);
        AddSpacer(page, 8);
        AddSectionHeading(page, "Chat noise");
        AddSuppressActionChatterToggle(page);
        AddSpacer(page, 8);
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
    }

    private static string FormatScaleHint(float v)
    {
        if (v <= 0.9f)  return "(current: Small)";
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
            "block while typing.");

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
        _famNameLabel.text = string.IsNullOrEmpty(s.Name) ? "(no familiar bound)" : s.Name;

        bool active = s.Level > 0 || !string.IsNullOrEmpty(s.Name);
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
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
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
            minHeight: 26, preferredHeight: 30, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Bold | FontStyles.Italic;
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    private static void AddSpacer(GameObject parent, int height)
    {
        var spacer = UIFactory.CreateUIObject("Spacer", parent);
        UIFactory.SetLayoutElement(spacer, minHeight: height, preferredHeight: height, flexibleHeight: 0, flexibleWidth: 1);
    }

    private static void AddCommandButton(GameObject parent, string label, string command,
        string tooltip = null, Color? color = null, bool confirm = false)
    {
        var b = UIFactory.CreateButton(parent, $"Cmd_{label}", label, color);
        UIFactory.SetLayoutElement(b.GameObject,
            minWidth: 70, preferredWidth: 110, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
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
        // Two-row layout so toggles never overflow when the panel is narrow.
        // Earlier 0.4.1 added a 6th toggle (Familiar Browser) that, plus the
        // long "Suspend game input when typing" label, made the row spill off
        // the right edge at the panel's MinWidth=600.
        var footerWrap = UIFactory.CreateVerticalGroup(parent, "OverlayFooterWrap",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(8, 8, 4, 4));
        UIFactory.SetLayoutElement(footerWrap, minHeight: 56, flexibleHeight: 0, flexibleWidth: 1);

        var row1 = UIFactory.CreateHorizontalGroup(footerWrap, "OverlayFooterRow1",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: false, childControlHeight: false,
            spacing: 12, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row1, minHeight: 26, flexibleHeight: 0, flexibleWidth: 1);

        var row2 = UIFactory.CreateHorizontalGroup(footerWrap, "OverlayFooterRow2",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: false, childControlHeight: false,
            spacing: 12, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row2, minHeight: 26, flexibleHeight: 0, flexibleWidth: 1);

        // Row 1 — overlay toggles
        _xpOverlayToggle   = AddOverlayToggle(row1, "XP overlay",        PanelType.ExperienceOverlay);
        _famOverlayToggle  = AddOverlayToggle(row1, "Familiar overlay",  PanelType.FamiliarOverlay);
        _famBrowserToggle  = AddOverlayToggle(row1, "Familiar Browser",  PanelType.FamiliarBrowserOverlay);
        _dqOverlayToggle   = AddOverlayToggle(row1, "Daily quest",       PanelType.DailyQuestOverlay);
        _profOverlayToggle = AddOverlayToggle(row1, "Professions",       PanelType.ProfessionOverlay);

        // Row 2 — panel behavior toggles
        AddAutoResizeToggle(row2);
        // SuspendGameInputWhileTyping was removed in 0.8.2 — its Harmony prefix
        // on InputActionSystem.OnUpdate wedged the entire game (UI + input
        // alike). SuspendGameInputWhileUIOpen was removed in 0.1.2 for the same
        // reason. A proper fix needs a different patch target.
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

        AutoResizeIfEnabled();
    }

    internal override void Reset()
    {
        if (_famSubscribed)
        {
            PlayerStateService.FamiliarChanged -= OnFamiliarChanged;
            _famSubscribed = false;
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
