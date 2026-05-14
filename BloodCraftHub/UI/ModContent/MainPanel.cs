using System;
using System.Collections.Generic;
using BloodCraftHub.Config;
using BloodCraftHub.Services;
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

    private static readonly (PanelType Tab, string Label)[] Tabs =
    {
        (PanelType.FamiliarsTab,    "Familiars"),
        (PanelType.BoxesTab,        "Boxes"),
        (PanelType.ClassTab,        "Class"),
        (PanelType.ExpertiseTab,    "Weapon Expertise"),
        (PanelType.UnarmedShiftTab, "Unarmed + Shift"),
        (PanelType.AdminTab,        "Admin"),
    };

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

        ShowTab(ActiveTab);
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
        UIFactory.SetLayoutElement(strip, minWidth: 140, flexibleWidth: 0, flexibleHeight: 1);

        foreach (var (tab, label) in Tabs)
        {
            var b = UIFactory.CreateButton(strip, $"TabBtn_{tab}", label);
            UIFactory.SetLayoutElement(b.GameObject, minWidth: 130, minHeight: 28, flexibleWidth: 1, flexibleHeight: 0);
            var captured = tab;
            b.OnClick = () => ShowTab(captured);
            _tabButtons[tab] = b;
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

        foreach (var (tab, label) in Tabs)
        {
            var page = CreateTabPage(content);
            AddTabHeading(page, label);

            switch (tab)
            {
                case PanelType.FamiliarsTab:
                    BuildFamiliarsTab(page);
                    break;
                case PanelType.ClassTab:
                    BuildClassTab(page);
                    break;
                case PanelType.ExpertiseTab:
                    BuildExpertiseTab(page);
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
            childControlWidth: false, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(actions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(actions, "Unbind",   MessageService.BCCOM_FAM_UNBIND);
        AddCommandButton(actions, "Toggle",   MessageService.BCCOM_FAM_TOGGLE);
        AddCommandButton(actions, "Combat",   MessageService.BCCOM_FAM_COMBAT);
        AddCommandButton(actions, "Prestige", MessageService.BCCOM_FAM_PRESTIGE);

        AddSpacer(page, 4);
        AddSectionHeading(page, "Box browsing");

        var note = UIFactory.CreateLabel(page, "BoxNote",
            "Full box browser arrives in the next phase. For now, use the button below to request the list — the response lands in your chat window.",
            TextAlignmentOptions.TopLeft, color: null, fontSize: 12);
        UIFactory.SetLayoutElement(note.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 40, preferredHeight: 50, flexibleHeight: 0);
        note.TextMesh.enableWordWrapping = true;
        note.TextMesh.overflowMode = TextOverflowModes.Overflow;

        var boxesBtn = UIFactory.CreateButton(page, "GetBoxesBtn",
            $"Request: {MessageService.BCCOM_FAM_BOXES}");
        UIFactory.SetLayoutElement(boxesBtn.GameObject,
            minWidth: 240, preferredWidth: 260, flexibleWidth: 0,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        boxesBtn.OnClick = () => EnqueueOrWarn(MessageService.BCCOM_FAM_BOXES);

        RenderFamiliar(PlayerStateService.Familiar);
        if (!_famSubscribed)
        {
            PlayerStateService.FamiliarChanged += OnFamiliarChanged;
            _famSubscribed = true;
        }
    }

    private void OnFamiliarChanged() => RenderFamiliar(PlayerStateService.Familiar);

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
            childControlWidth: false, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(actions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(actions, "List Classes",  MessageService.BCCOM_CLASS_LIST);
        AddCommandButton(actions, "List Spells",   MessageService.BCCOM_CLASS_LIST_SPELLS);
        AddCommandButton(actions, "List Stats",    MessageService.BCCOM_CLASS_LIST_STATS);
        AddCommandButton(actions, "Toggle Shift",  MessageService.BCCOM_CLASS_TOGGLE_SHIFT);

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
            childControlWidth: false, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(actions,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 32, preferredHeight: 32, flexibleHeight: 0);
        AddCommandButton(actions, "Refresh",     MessageService.BCCOM_WEP_GET);
        AddCommandButton(actions, "List Weps",   MessageService.BCCOM_WEP_LIST);
        AddCommandButton(actions, "List Stats",  MessageService.BCCOM_WEP_LIST_STATS);
        AddCommandButton(actions, "Reset Stats", MessageService.BCCOM_WEP_RESET_STATS);
        AddCommandButton(actions, "Lock Spells", MessageService.BCCOM_WEP_LOCK_SPELLS);

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

    private static void AddCommandButton(GameObject parent, string label, string command)
    {
        var b = UIFactory.CreateButton(parent, $"Cmd_{label}", label);
        UIFactory.SetLayoutElement(b.GameObject,
            minWidth: 80, preferredWidth: 90, flexibleWidth: 0,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        b.OnClick = () => EnqueueOrWarn(command);
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
    }
}
