using System;
using System.Collections.Generic;
using System;
using BloodCraftHub.Config;
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

namespace BloodCraftHub.UI.ModContent;

// The primary tabbed UI. See the matching ASCII diagram in docs/MOD_DESIGN.md
// for the layout. Each tab's body is built by a dedicated BuildXxxTab method
// dispatched in BuildContentArea; tabs that need live data subscribe to
// PlayerStateService events and unsubscribe in Reset.
public class MainPanel : ResizeablePanelBase
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
    private readonly Dictionary<PanelType, ButtonRef> _tabButtons = new();
    private Toggle _xpOverlayToggle;
    private Toggle _famOverlayToggle;

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

    // Boxes-tab live state
    private TextMeshProUGUI _boxesActiveBoxLabel;
    private TextMeshProUGUI _boxesContentHeading;
    private TextMeshProUGUI _boxesStatusLabel;
    private GameObject _boxesPickerSection;       // parent wrapping picker heading + list
    private GameObject _boxesContentSection;      // parent wrapping content heading + list
    private GameObject _boxesListContainer;       // box-name buttons go here
    private GameObject _boxesContentContainer;    // familiar-name buttons go here
    private bool _boxesShowingContents;
    private bool _boxesSubscribed;

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
                (PanelType.UnarmedShiftTab, "Unarmed + Shift"),
                (PanelType.PrestigeTab,     "Prestige"),
                (PanelType.LevelsTab,       "Levels"),
                (PanelType.AdminTab,        "Admin"),
            },
        },
        new TabGroupDef
        {
            Title = "Kindred",
            StartExpanded = false,
            Tabs = System.Array.Empty<(PanelType, string)>(),
        },
        new TabGroupDef
        {
            Title = "Help",
            StartExpanded = false,
            Tabs = System.Array.Empty<(PanelType, string)>(),
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

    public MainPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        var body = UIFactory.CreateHorizontalGroup(ContentRoot, "Body",
            forceExpandWidth: true, forceExpandHeight: true,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(6, 6, 6, 6));
        UIFactory.SetLayoutElement(body, flexibleHeight: 1, flexibleWidth: 1);

        BuildTabStrip(body);
        BuildContentArea(body);
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
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: 12);
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 400, preferredWidth: 600, flexibleWidth: 1,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Italic;
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;

        // Wire the static Sink so the per-frame TooltipHover.TickAll updates this label,
        // and register the tick with CoreUpdateBehavior (idempotent).
        TooltipHover.Sink = lbl.TextMesh;
        TooltipHover.EnsureTicking();
    }

    // -----------------------------------------------------------------------
    // Tab strip (left rail)
    // -----------------------------------------------------------------------

    private void BuildTabStrip(GameObject parent)
    {
        var strip = UIFactory.CreateVerticalGroup(parent, "TabStrip",
            forceWidth: false, forceHeight: false,
            childControlWidth: true, childControlHeight: false,
            spacing: 2, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(strip, minWidth: 150, flexibleWidth: 0, flexibleHeight: 1);

        foreach (var group in TabGroups)
            BuildTabGroup(strip, group);
    }

    private void BuildTabGroup(GameObject parent, TabGroupDef group)
    {
        _groupExpanded[group.Title] = group.StartExpanded;

        // Header button - clicking toggles the group's content visibility.
        var header = UIFactory.CreateButton(parent, $"GroupHeader_{group.Title}",
            FormatGroupHeader(group.Title, group.StartExpanded));
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
            headerText.fontSize = 12;
            _groupHeaderText[group.Title] = headerText;
        }
        TooltipHover.Attach(header.GameObject,
            $"Show / hide the {group.Title} tab list.");

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
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: 11);
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
                    t.fontSize = 13;
                }
                var captured = tab;
                b.OnClick = () => ShowTab(captured);
                _tabButtons[tab] = b;
            }
        }

        content.SetActive(group.StartExpanded);
        header.OnClick = () => ToggleGroup(group.Title);
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

    private static string FormatGroupHeader(string title, bool expanded) =>
        expanded ? $"▼  {title.ToUpper()}" : $"▶  {title.ToUpper()}";

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
            var page = CreateTabPage(content);
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
                default:
                    AddComingSoonBody(page, label);
                    break;
            }

            page.SetActive(false);
            _tabContent[tab] = page;
        }
    }

    private GameObject CreateTabPage(GameObject parent)
    {
        var page = UIFactory.CreateVerticalGroup(parent, "TabPage",
            forceWidth: true, forceHeight: true,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(8, 8, 8, 8));
        UIFactory.SetLayoutElement(page,
            minWidth: 380, preferredWidth: 420, flexibleWidth: 1,
            minHeight: 280, preferredHeight: 320, flexibleHeight: 1);
        return page;
    }

    private static void AddTabHeading(GameObject page, string text)
    {
        var heading = UIFactory.CreateLabel(page, "TabHeading", text,
            TextAlignmentOptions.TopLeft, color: null, fontSize: 20);
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
            TextAlignmentOptions.TopLeft, color: null, fontSize: 14);
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

        _famNameLabel     = AddInfoLabel(page, "FamName",     "—", FontStyles.Bold,   fontSize: 18);
        _famProgressLabel = AddInfoLabel(page, "FamProgress", "Level — ", FontStyles.Normal, fontSize: 14);
        _famStatsLabel    = AddInfoLabel(page, "FamStats",    "HP —  PP —  SP —", FontStyles.Normal, fontSize: 14);

        AddSpacer(page, 4);
        AddSectionHeading(page, "Actions");

        var actions = UIFactory.CreateHorizontalGroup(page, "FamActions",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(actions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(actions, "Unbind",   MessageService.BCCOM_FAM_UNBIND,
            "Dismiss the currently bound familiar (.fam u). Your familiar returns to its box.");
        AddCommandButton(actions, "Toggle",   MessageService.BCCOM_FAM_TOGGLE,
            "Toggle the familiar visibility on/off (.fam toggle). Hidden familiars stop following you.");
        AddCommandButton(actions, "Combat",   MessageService.BCCOM_FAM_COMBAT,
            "Toggle combat mode for the active familiar (.fam combat). Off = passive, won't engage enemies.");
        AddCommandButton(actions, "Prestige", MessageService.BCCOM_FAM_PRESTIGE,
            "Prestige the active familiar (.fam pr). Requires max level; resets level and grants permanent bonuses.");

        AddSpacer(page, 4);
        var note = UIFactory.CreateLabel(page, "FamNote",
            "Switch to the Boxes tab to browse your familiar boxes and click-to-bind.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: 12);
        UIFactory.SetLayoutElement(note.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 28, flexibleHeight: 0);
        note.TextMesh.enableWordWrapping = true;
        note.TextMesh.overflowMode = TextOverflowModes.Overflow;

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
            FontStyles.Bold, fontSize: 14);

        AddSpacer(page, 4);

        // ---------------- Picker section (visible when no box selected) ----------------
        // childControlHeight: true is crucial here - without it the layout group
        // does NOT enforce children's heights, so the action row, section heading,
        // and list container all draw at their default (0) sizeDelta and overlap.
        _boxesPickerSection = UIFactory.CreateVerticalGroup(page, "BoxPickerSection",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_boxesPickerSection,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 80, preferredHeight: 200, flexibleHeight: 1);

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
            refreshText.fontSize = 13;
        }
        TooltipHover.Attach(refreshBtn.GameObject,
            "Re-fetch your familiar boxes from the server (.fam boxes). The server reply can take a few seconds.");
        refreshBtn.OnClick = () =>
        {
            if (_boxesStatusLabel != null) _boxesStatusLabel.text = "Loading boxes from the server…";
            EnqueueOrWarn(MessageService.BCCOM_FAM_BOXES);
        };

        _boxesStatusLabel = AddInfoLabel(_boxesPickerSection, "BoxesStatus", "",
            FontStyles.Italic, fontSize: 11);

        AddSectionHeading(_boxesPickerSection, "Available Boxes");
        _boxesListContainer = UIFactory.CreateVerticalGroup(_boxesPickerSection, "BoxListContainer",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(_boxesListContainer,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 60, preferredHeight: 200, flexibleHeight: 1);

        // ---------------- Content section (visible when a box is selected) ----------------
        _boxesContentSection = UIFactory.CreateVerticalGroup(page, "BoxContentSection",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_boxesContentSection,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 80, preferredHeight: 200, flexibleHeight: 1);

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
            backText.fontSize = 13;
        }
        backBtn.OnClick = OnBackToBoxesClicked;
        TooltipHover.Attach(backBtn.GameObject, "Return to the box list without changing your active box.");

        AddCommandButton(contentActions, "Reload", MessageService.BCCOM_FAM_LIST_CURRENT_BOX,
            "Re-fetch the familiars in the currently-active box (sends .fam l).");

        _boxesContentHeading = AddInfoLabel(_boxesContentSection, "ContentHeading",
            "Familiars in (none)", FontStyles.Italic, fontSize: 13);

        _boxesContentContainer = UIFactory.CreateVerticalGroup(_boxesContentSection, "BoxContentContainer",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(_boxesContentContainer,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 60, preferredHeight: 200, flexibleHeight: 1);

        AddSpacer(page, 4);
        var note = UIFactory.CreateLabel(page, "BoxesNote",
            "Click Refresh to pull your box list. Click a box to see its familiars; click a familiar to bind it. Use ← Back to return to the box list.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: 11);
        UIFactory.SetLayoutElement(note.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 32, flexibleHeight: 0);
        note.TextMesh.enableWordWrapping = true;
        note.TextMesh.overflowMode = TextOverflowModes.Overflow;

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
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: 12);
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
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: 12);
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
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: 12);
            UIFactory.SetLayoutElement(pending.GameObject,
                minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
                minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
            pending.TextMesh.fontStyle = FontStyles.Italic;
            return;
        }

        foreach (var entry in entries)
        {
            var idx = entry.Index;
            var label = $"{entry.Index:00}  —  {entry.Name}";
            var b = UIFactory.CreateButton(_boxesContentContainer, $"FamBtn_{entry.Index}", label);
            UIFactory.SetLayoutElement(b.GameObject,
                minWidth: 340, preferredWidth: 380, flexibleWidth: 1,
                minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
            b.OnClick = () => OnFamiliarClicked(idx);
        }
    }

    private void OnBoxClicked(string boxName)
    {
        PlayerStateService.SetActiveBox(boxName);
        // .fam cb selects the box server-side, .fam l lists its contents.
        // Both fire immediately (no queue throttle) so the user sees the
        // contents view populate within ~1s of the click.
        EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_SWITCH_BOX_FORMAT, boxName));
        EnqueueOrWarn(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);

        _boxesShowingContents = true;
        UpdateBoxesSectionVisibility();
    }

    private static void OnFamiliarClicked(int index)
    {
        EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_BIND_BY_INDEX_FORMAT, index));
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

        _classNameLabel  = AddInfoLabel(page, "ClassName",  "—",       FontStyles.Bold,   fontSize: 18);
        _classLevelLabel = AddInfoLabel(page, "ClassLevel", "Level —", FontStyles.Normal, fontSize: 14);

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

        AddSpacer(page, 4);
        var note = UIFactory.CreateLabel(page, "ClassNote",
            "Class info responses appear in chat. Selecting/changing classes by argument arrives later — use `.class s <Class>` or `.class c <Class>` in chat for now.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: 12);
        UIFactory.SetLayoutElement(note.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 40, preferredHeight: 50, flexibleHeight: 0);
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

        _wepTypeLabel     = AddInfoLabel(page, "WepType",     "—",                  FontStyles.Bold,   fontSize: 18);
        _wepProgressLabel = AddInfoLabel(page, "WepProgress", "Level —",            FontStyles.Normal, fontSize: 14);
        _wepBonusLabel    = AddInfoLabel(page, "WepBonus",    "Bonus Stats: —",     FontStyles.Normal, fontSize: 13);

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

        AddSpacer(page, 4);
        var note = UIFactory.CreateLabel(page, "WepNote",
            "Choosing a specific bonus stat (`.wep cst <Weapon> <Stat>`) takes arguments — for now use chat. A stat picker arrives in a later phase.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: 12);
        UIFactory.SetLayoutElement(note.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 40, flexibleHeight: 0);
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
    // Unarmed + Shift Skill tab
    // -----------------------------------------------------------------------

    private void BuildUnarmedShiftTab(GameObject page)
    {
        AddSectionHeading(page, "Shift Spell");

        _shiftSpellLabel = AddInfoLabel(page, "ShiftSpell",
            "Equipped: —", FontStyles.Normal, fontSize: 14);

        AddSpacer(page, 4);
        AddSectionHeading(page, "Unarmed Expertise");

        _unarmedStatusLabel = AddInfoLabel(page, "UnarmedStatus",
            "Equip your fists (no weapon) to inspect unarmed expertise.",
            FontStyles.Normal, fontSize: 14);
        _unarmedBonusLabel = AddInfoLabel(page, "UnarmedBonus",
            "Bonus Stats: —", FontStyles.Normal, fontSize: 13);

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
            TextAlignmentOptions.TopLeft, color: null, fontSize: 12);
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

        _prestigeXpLabel        = AddInfoLabel(page, "PrestigeXp",        "Experience prestige: —", FontStyles.Normal, fontSize: 13);
        _prestigeLegacyLabel    = AddInfoLabel(page, "PrestigeLegacy",    "Blood legacy prestige: —", FontStyles.Normal, fontSize: 13);
        _prestigeExpertiseLabel = AddInfoLabel(page, "PrestigeExpertise", "Weapon expertise prestige: —", FontStyles.Normal, fontSize: 13);
        _prestigeFamLabel       = AddInfoLabel(page, "PrestigeFam",       "Familiar prestige: —", FontStyles.Normal, fontSize: 13);

        AddSpacer(page, 4);
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
            tooltip: "Expand to display detailed info about your prestige in a specific system. Chat receives the response.",
            buildContent: c => FormBuilder.Build(c,
                title: "Show prestige info",
                commandTemplate: ".prestige get {type}",
                new EnumField<PlayerStateService.PrestigeType>("type", "Prestige type",
                    defaultValue: PlayerStateService.PrestigeType.Experience)));

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
        _lvlXpLabel = AddInfoLabel(page, "LvlXp", "—", FontStyles.Normal, fontSize: 13);

        AddSpacer(page, 4);
        AddSectionHeading(page, "Blood Legacy");
        _lvlLegacyLabel = AddInfoLabel(page, "LvlLegacy", "—", FontStyles.Normal, fontSize: 13);

        AddSpacer(page, 4);
        AddSectionHeading(page, "Weapon Expertise");
        _lvlExpertiseLabel      = AddInfoLabel(page, "LvlExpertise",      "—", FontStyles.Normal, fontSize: 13);
        _lvlExpertiseBonusLabel = AddInfoLabel(page, "LvlExpertiseBonus", "Bonus stats: —", FontStyles.Italic, fontSize: 12);

        AddSpacer(page, 4);
        AddSectionHeading(page, "Familiar (active)");
        _lvlFamLabel      = AddInfoLabel(page, "LvlFam",      "—", FontStyles.Normal, fontSize: 13);
        _lvlFamStatsLabel = AddInfoLabel(page, "LvlFamStats", "HP —   PP —   SP —", FontStyles.Italic, fontSize: 12);

        AddSpacer(page, 4);
        AddSectionHeading(page, "Professions");
        _lvlProfessions1Label = AddInfoLabel(page, "LvlProf1", "—", FontStyles.Normal, fontSize: 12);
        _lvlProfessions2Label = AddInfoLabel(page, "LvlProf2", "—", FontStyles.Normal, fontSize: 12);
        _lvlProfessions3Label = AddInfoLabel(page, "LvlProf3", "—", FontStyles.Normal, fontSize: 12);
        _lvlProfessions4Label = AddInfoLabel(page, "LvlProf4", "—", FontStyles.Normal, fontSize: 12);

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
    // Admin tab
    // -----------------------------------------------------------------------

    private void BuildAdminTab(GameObject page)
    {
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

        AddSpacer(page, 6);
        AddSectionHeading(page, "Admin commands (use chat — args required)");

        AddAdminRefLine(page, ".prestige set [Player] [PrestigeType] [Level]",
            "Set a player's prestige in a system. PrestigeType is e.g. Experience / Expertise / Legacy.");
        AddAdminRefLine(page, ".prestige r [Player] [PrestigeType]",
            "Reset a prestige for the player.");
        AddAdminRefLine(page, ".bl set [Player] [Blood] [Level]",
            "Set a player's blood legacy level.");
        AddAdminRefLine(page, ".wep set [Player] [Weapon] [Level]",
            "Set a player's weapon expertise level.");
        AddAdminRefLine(page, ".prof set [Player] [Profession] [Level]",
            "Set a player's profession level.");
        AddAdminRefLine(page, ".fam sl [Player] [Level]",
            "Set a player's familiar level.");
        AddAdminRefLine(page, ".quest rf [Player]",
            "Refresh daily/weekly quests for a player.");
        AddAdminRefLine(page, ".quest c [Player] [QuestType]",
            "Forcibly complete a quest for a player. QuestType is Daily or Weekly.");

        AddSpacer(page, 6);
        var note = UIFactory.CreateLabel(page, "AdminNote",
            "Phase 5b: the first admin form (Set player level above) is live. " +
            "Remaining commands move to forms in Phase 5e. " +
            "If you aren't an admin on this server, commands will return a permission error.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: 12);
        UIFactory.SetLayoutElement(note.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 40, preferredHeight: 60, flexibleHeight: 0);
        note.TextMesh.enableWordWrapping = true;
        note.TextMesh.overflowMode = TextOverflowModes.Overflow;
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
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: 12);
        UIFactory.SetLayoutElement(cmd.GameObject,
            minWidth: 200, preferredWidth: 220, flexibleWidth: 0,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        cmd.TextMesh.fontStyle = FontStyles.Bold;
        cmd.TextMesh.enableWordWrapping = false;
        cmd.TextMesh.overflowMode = TextOverflowModes.Overflow;

        var desc = UIFactory.CreateLabel(row, "Desc", summary,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: 12);
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
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: 14);
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Bold | FontStyles.Italic;
        lbl.TextMesh.enableWordWrapping = false;
    }

    private static void AddSpacer(GameObject parent, int height)
    {
        var spacer = UIFactory.CreateUIObject("Spacer", parent);
        UIFactory.SetLayoutElement(spacer, minHeight: height, preferredHeight: height, flexibleHeight: 0, flexibleWidth: 1);
    }

    private static void AddCommandButton(GameObject parent, string label, string command, string tooltip = null)
    {
        var b = UIFactory.CreateButton(parent, $"Cmd_{label}", label);
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
            t.fontSize = 13;
        }
        b.OnClick = () => EnqueueOrWarn(command);

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
        var footer = UIFactory.CreateHorizontalGroup(parent, "OverlayFooter",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: false, childControlHeight: false,
            spacing: 12, padding: new Vector4(8, 8, 4, 4));
        UIFactory.SetLayoutElement(footer, minHeight: 32, flexibleHeight: 0, flexibleWidth: 1);

        _xpOverlayToggle  = AddOverlayToggle(footer, "XP overlay",       PanelType.ExperienceOverlay);
        _famOverlayToggle = AddOverlayToggle(footer, "Familiar overlay", PanelType.FamiliarOverlay);
        AddAutoResizeToggle(footer);
        AddInputBlockToggle(footer);
    }

    private void AddInputBlockToggle(GameObject parent)
    {
        var t = UIFactory.CreateToggle(parent, "InputBlockToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 200, preferredWidth: 220, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        t.Text.text = "Suspend game input when typing";
        t.Text.fontSize = 13;
        t.Text.enableWordWrapping = false;
        t.Text.overflowMode = TextOverflowModes.Overflow;
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 170, preferredWidth: 190, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);

        t.Toggle.isOn = Settings.SuspendGameInputWhileTyping;
        TooltipHover.Attach(t.GameObject,
            "When on: WASD typed into UI fields stays in the form (game input paused). Off: " +
            "game keeps reading input even while a field is focused (typing also moves your character).");
        t.OnValueChanged += value =>
        {
            Plugin.Instance.Config.Bind(Settings.UI_SETTINGS_GROUP,
                nameof(Settings.SuspendGameInputWhileTyping), true, "").Value = value;
        };
    }

    private void AddAutoResizeToggle(GameObject parent)
    {
        var t = UIFactory.CreateToggle(parent, "AutoResizeToggle");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 160, preferredWidth: 180, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        t.Text.text = "Auto-resize panel";
        t.Text.fontSize = 13;
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

        try
        {
            var pageRt = pageGo.GetComponent<RectTransform>();
            if (pageRt == null) return;

            // Force the layout to recalculate so preferredHeight is up to date.
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(pageRt);

            float contentHeight = UnityEngine.UI.LayoutUtility.GetPreferredHeight(pageRt);
            // Chrome budget: title bar + tab strip already share width; we account for
            // the OverlayFooter (32) + TooltipFooter (22) + spacing (~16) ~= 70px.
            float chrome = 76f;
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

    private Toggle AddOverlayToggle(GameObject parent, string label, PanelType overlay)
    {
        var t = UIFactory.CreateToggle(parent, $"OverlayToggle_{overlay}");
        UIFactory.SetLayoutElement(t.GameObject,
            minWidth: 200, preferredWidth: 220, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);

        t.Text.text = label;
        t.Text.fontSize = 14;
        t.Text.enableWordWrapping = false;
        t.Text.overflowMode = TextOverflowModes.Overflow;
        t.Text.alignment = TextAlignmentOptions.MidlineLeft;
        UIFactory.SetLayoutElement(t.Text.gameObject,
            minWidth: 160, preferredWidth: 180, flexibleWidth: 1,
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
    }
}
