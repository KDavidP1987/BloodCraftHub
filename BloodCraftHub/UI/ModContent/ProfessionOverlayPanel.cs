using BloodCraftHub.Config;
using BloodCraftHub.Services;
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

// Secondary overlay: professions tracker.
//
// 0.9.0: added in response to friend-testing feedback ("they asked about
// having an additional overlay that shows all of the profession experience
// levels and prestige numbers alongside the weapon experience and prestige.
// This is an overlay that we missed").
//
// Data feed: Bloodcraft's signed Eclipse ProgressToClient stream carries 8
// professions × (progress%, level) at indices 19..34. EclipseProtocolService
// unpacks that into PlayerStateService.ProfessionState; this panel subscribes
// to ProfessionChanged and renders 8 rows.
//
// Bloodcraft does NOT currently surface a per-profession prestige level on
// the Eclipse channel (only the levels), so the overlay shows level + xp%
// per profession; prestige is omitted to avoid faking data we don't have.
public class ProfessionOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "ProfessionOverlay";
    public override PanelType PanelType => PanelType.ProfessionOverlay;

    public override int MinWidth  => 260;
    public override int MinHeight => 220;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    public override Vector2 DefaultPosition  => new(
        -Owner.Scaler.m_ReferenceResolution.x * 0.5f,
         Owner.Scaler.m_ReferenceResolution.y * 0.5f - 240f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.ProfessionOverlayTransparency);

    private LabelRef _enchantingLabel;
    private LabelRef _alchemyLabel;
    private LabelRef _harvestingLabel;
    private LabelRef _blacksmithingLabel;
    private LabelRef _tailoringLabel;
    private LabelRef _woodcuttingLabel;
    private LabelRef _miningLabel;
    private LabelRef _fishingLabel;
    private bool _subscribed;

    public ProfessionOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();

        AddHeader();
        _enchantingLabel    = AddRow("ProfEnchanting",    "Enchanting —");
        _alchemyLabel       = AddRow("ProfAlchemy",       "Alchemy —");
        _harvestingLabel    = AddRow("ProfHarvesting",    "Harvesting —");
        _blacksmithingLabel = AddRow("ProfBlacksmithing", "Blacksmithing —");
        _tailoringLabel     = AddRow("ProfTailoring",     "Tailoring —");
        _woodcuttingLabel   = AddRow("ProfWoodcutting",   "Woodcutting —");
        _miningLabel        = AddRow("ProfMining",        "Mining —");
        _fishingLabel       = AddRow("ProfFishing",       "Fishing —");

        Render(PlayerStateService.Profession);

        if (!_subscribed)
        {
            PlayerStateService.ProfessionChanged += OnProfessionChanged;
            _subscribed = true;
        }
    }

    private void AddHeader()
    {
        var lbl = UIFactory.CreateLabel(ContentRoot, "ProfHeader", "Professions",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledOverlay(15));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 240, preferredWidth: 260, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Bold | FontStyles.Italic;
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
    }

    private LabelRef AddRow(string name, string text)
    {
        var lbl = UIFactory.CreateLabel(ContentRoot, name, text,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledOverlay(13));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 240, preferredWidth: 260, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        return lbl;
    }

    private void OnProfessionChanged() => Render(PlayerStateService.Profession);

    private void Render(PlayerStateService.ProfessionState s)
    {
        if (_enchantingLabel == null) return;
        _enchantingLabel.TextMesh.text    = FormatRow("Enchanting",    s.EnchantingLevel,    s.EnchantingProgress);
        _alchemyLabel.TextMesh.text       = FormatRow("Alchemy",       s.AlchemyLevel,       s.AlchemyProgress);
        _harvestingLabel.TextMesh.text    = FormatRow("Harvesting",    s.HarvestingLevel,    s.HarvestingProgress);
        _blacksmithingLabel.TextMesh.text = FormatRow("Blacksmithing", s.BlacksmithingLevel, s.BlacksmithingProgress);
        _tailoringLabel.TextMesh.text     = FormatRow("Tailoring",     s.TailoringLevel,     s.TailoringProgress);
        _woodcuttingLabel.TextMesh.text   = FormatRow("Woodcutting",   s.WoodcuttingLevel,   s.WoodcuttingProgress);
        _miningLabel.TextMesh.text        = FormatRow("Mining",        s.MiningLevel,        s.MiningProgress);
        _fishingLabel.TextMesh.text       = FormatRow("Fishing",       s.FishingLevel,       s.FishingProgress);
    }

    private static string FormatRow(string name, int level, float progress) =>
        $"{name}: Lv {level} ({progress * 100f:0.#}%)";

    internal override void Reset()
    {
        if (_subscribed)
        {
            PlayerStateService.ProfessionChanged -= OnProfessionChanged;
            _subscribed = false;
        }
    }
}
