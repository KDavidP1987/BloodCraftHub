using System.Collections.Generic;
using BloodCraftHub.Config;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Models;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;

namespace BloodCraftHub.UI.ModContent;

// The primary tabbed UI. Layout:
//
//   +-------------------------------------+
//   | Title bar / close                   |
//   +----------+--------------------------+
//   | Familiars|                          |
//   | Boxes    |   <active tab content>   |
//   | Class    |                          |
//   | Expertise|                          |
//   | Unarmed  |                          |
//   | Admin    |                          |
//   +----------+--------------------------+
//   | [ ] XP overlay   [ ] Familiar overl.|
//   +-------------------------------------+
//
// Tabs are GameObjects under the content area. Switching tabs just SetActive's
// the right one. Real per-tab content gets filled in during Phase 4; for now
// each tab is just a label so the routing is visible.
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
    public override bool ResizeWholePanel => false; // we want a real title bar to drag from

    public PanelType ActiveTab { get; private set; } = PanelType.FamiliarsTab;

    private readonly Dictionary<PanelType, GameObject> _tabContent = new();
    private readonly Dictionary<PanelType, ButtonRef> _tabButtons = new();
    private Toggle _xpOverlayToggle;
    private Toggle _famOverlayToggle;

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
        // ----- Top-level vertical split: body row + footer toggle row -----
        var body = UIFactory.CreateHorizontalGroup(ContentRoot, "Body",
            forceExpandWidth: true, forceExpandHeight: true,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(6, 6, 6, 6));
        UIFactory.SetLayoutElement(body, flexibleHeight: 1, flexibleWidth: 1);

        BuildTabStrip(body);
        BuildContentArea(body);
        BuildOverlayFooter(ContentRoot);

        // Default visible tab.
        ShowTab(ActiveTab);
    }

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

    private void BuildContentArea(GameObject parent)
    {
        var content = UIFactory.CreateVerticalGroup(parent, "TabContent",
            forceWidth: true, forceHeight: true,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(6, 6, 6, 6));
        UIFactory.SetLayoutElement(content, flexibleWidth: 1, flexibleHeight: 1);

        foreach (var (tab, label) in Tabs)
        {
            var page = UIFactory.CreateVerticalGroup(content, $"Tab_{tab}",
                forceWidth: true, forceHeight: true,
                childControlWidth: true, childControlHeight: true,
                spacing: 6, padding: new Vector4(8, 8, 8, 8));
            UIFactory.SetLayoutElement(page,
                minWidth: 380, preferredWidth: 420, flexibleWidth: 1,
                minHeight: 280, preferredHeight: 320, flexibleHeight: 1);

            var heading = UIFactory.CreateLabel(page, "TabHeading", label,
                TextAlignmentOptions.TopLeft, color: null, fontSize: 20);
            UIFactory.SetLayoutElement(heading.GameObject,
                minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
                minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
            heading.TextMesh.fontStyle = FontStyles.Bold;
            heading.TextMesh.enableWordWrapping = false;
            heading.TextMesh.overflowMode = TextOverflowModes.Overflow;

            var placeholder = UIFactory.CreateLabel(page, "Placeholder",
                "Coming soon — this tab will surface the matching Bloodcraft commands.",
                TextAlignmentOptions.TopLeft, color: null, fontSize: 14);
            UIFactory.SetLayoutElement(placeholder.GameObject,
                minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
                minHeight: 40, preferredHeight: 80, flexibleHeight: 1);
            placeholder.TextMesh.enableWordWrapping = true;
            placeholder.TextMesh.overflowMode = TextOverflowModes.Overflow;

            page.SetActive(false);
            _tabContent[tab] = page;
        }
    }

    private void BuildOverlayFooter(GameObject parent)
    {
        var footer = UIFactory.CreateHorizontalGroup(parent, "OverlayFooter",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: false, childControlHeight: false,
            spacing: 12, padding: new Vector4(8, 8, 4, 4));
        UIFactory.SetLayoutElement(footer, minHeight: 32, flexibleHeight: 0, flexibleWidth: 1);

        _xpOverlayToggle = AddOverlayToggle(footer, "XP overlay", PanelType.ExperienceOverlay);
        _famOverlayToggle = AddOverlayToggle(footer, "Familiar overlay", PanelType.FamiliarOverlay);
    }

    private Toggle AddOverlayToggle(GameObject parent, string label, PanelType overlay)
    {
        var t = UIFactory.CreateToggle(parent, $"OverlayToggle_{overlay}");
        UIFactory.SetLayoutElement(t.GameObject, minWidth: 180, minHeight: 24, flexibleWidth: 0, flexibleHeight: 0);
        t.Text.text = label;
        t.Toggle.isOn = Plugin.UIManager.IsOverlayOpen(overlay);
        t.OnValueChanged += _ => Plugin.UIManager.ToggleOverlay(overlay);
        return t.Toggle;
    }

    public void ShowTab(PanelType tab)
    {
        if (!_tabContent.ContainsKey(tab)) return;
        foreach (var kv in _tabContent) kv.Value.SetActive(kv.Key == tab);
        ActiveTab = tab;
    }

    internal override void Reset() { /* nothing for now */ }
}
