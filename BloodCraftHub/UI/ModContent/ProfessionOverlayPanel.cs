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
    // 0.9.3: optional progress bars per profession, paired with the label rows.
    private GameObject _enchantingBar, _alchemyBar, _harvestingBar, _blacksmithingBar;
    private GameObject _tailoringBar, _woodcuttingBar, _miningBar, _fishingBar;
    private RectTransform _enchantingFill, _alchemyFill, _harvestingFill, _blacksmithingFill;
    private RectTransform _tailoringFill, _woodcuttingFill, _miningFill, _fishingFill;
    private bool _subscribed;

    public ProfessionOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();

        AddHeader();
        _enchantingLabel    = AddRow("ProfEnchanting",    "Enchanting —");
        _enchantingBar      = AddBar("ProfEnchantingBar",    out _enchantingFill);
        _alchemyLabel       = AddRow("ProfAlchemy",       "Alchemy —");
        _alchemyBar         = AddBar("ProfAlchemyBar",       out _alchemyFill);
        _harvestingLabel    = AddRow("ProfHarvesting",    "Harvesting —");
        _harvestingBar      = AddBar("ProfHarvestingBar",    out _harvestingFill);
        _blacksmithingLabel = AddRow("ProfBlacksmithing", "Blacksmithing —");
        _blacksmithingBar   = AddBar("ProfBlacksmithingBar", out _blacksmithingFill);
        _tailoringLabel     = AddRow("ProfTailoring",     "Tailoring —");
        _tailoringBar       = AddBar("ProfTailoringBar",     out _tailoringFill);
        _woodcuttingLabel   = AddRow("ProfWoodcutting",   "Woodcutting —");
        _woodcuttingBar     = AddBar("ProfWoodcuttingBar",   out _woodcuttingFill);
        _miningLabel        = AddRow("ProfMining",        "Mining —");
        _miningBar          = AddBar("ProfMiningBar",        out _miningFill);
        _fishingLabel       = AddRow("ProfFishing",       "Fishing —");
        _fishingBar         = AddBar("ProfFishingBar",       out _fishingFill);

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

    /// <summary>0.9.3: optional progress bar paired with each profession's
    /// label row. Hidden by default; visibility tied to Settings.ShowProgressBars
    /// and refreshed on every Render. Uses a warm amber fill so the 8 bars
    /// don't compete with the XP overlay's cyan or the Familiar overlay's
    /// orange when they're all visible together.</summary>
    private GameObject AddBar(string name, out UnityEngine.RectTransform fillRect)
    {
        var bar = Framework.CustomLib.Controls.MiniBar.Create(ContentRoot, name, out fillRect,
            fillColor: new Color(0.95f, 0.75f, 0.35f, 0.95f), height: 10);
        bar.SetActive(false);
        return bar;
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

        // 0.9.3: progress-bar visibility per the Settings.ShowProgressBars
        // toggle. Re-read every render so the toggle takes effect live.
        bool showBars = Settings.ShowProgressBars;
        SyncBar(_enchantingBar,    _enchantingFill,    s.EnchantingProgress,    showBars);
        SyncBar(_alchemyBar,       _alchemyFill,       s.AlchemyProgress,       showBars);
        SyncBar(_harvestingBar,    _harvestingFill,    s.HarvestingProgress,    showBars);
        SyncBar(_blacksmithingBar, _blacksmithingFill, s.BlacksmithingProgress, showBars);
        SyncBar(_tailoringBar,     _tailoringFill,     s.TailoringProgress,     showBars);
        SyncBar(_woodcuttingBar,   _woodcuttingFill,   s.WoodcuttingProgress,   showBars);
        SyncBar(_miningBar,        _miningFill,        s.MiningProgress,        showBars);
        SyncBar(_fishingBar,       _fishingFill,       s.FishingProgress,       showBars);
    }

    private static void SyncBar(GameObject bar, UnityEngine.RectTransform fill, float progress, bool show)
    {
        if (bar == null) return;
        if (bar.activeSelf != show) bar.SetActive(show);
        if (show) Framework.CustomLib.Controls.MiniBar.SetProgress(fill, progress);
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
