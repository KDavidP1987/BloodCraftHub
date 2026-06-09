using System;
using System.Collections.Generic;
using BloodCraftHub.Config;
using BloodCraftHub.Services;
using BloodCraftHub.Services.Uriel;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.CustomLib.Util;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Models;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using BloodCraftHub.Utils;
using TMPro;
using UnityEngine;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;

namespace BloodCraftHub.UI.ModContent;

// 0.29: draggable Uriel object-spawn PALETTE overlay — a compact on-screen list of the objects you've
// unlocked, grouped by category, each with a Spawn button, so you can move around your base and place
// objects WITHOUT opening the bulky main panel (which forces the camera to the right and lands objects
// off to the side). Built for active building sessions: pop it open, build, close it.
//
// Modeled on the FamiliarBrowser overlay the user referenced: a ← / → category cycler at the top, a
// PAGED list below (Prev/Next — NOT a scroll view, because the mouse wheel over a scroll view zooms the
// game camera), and Despawn / Rotate quick-actions in the header (those act on the nearest placed object).
// The spawn DURABILITY / RESPAWN choice is local to this overlay (the full controls live on the Object
// Spawning tab) so a quick-build session doesn't have to round-trip through the main panel.
//
// Data comes from UrielState.Unlocked (populated by UrielProtocolService). On first show it auto-pulls
// the unlocked list if it's empty, so opening the overlay alone is enough to populate it.
public class UrielObjectSpawnerOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "UrielObjectSpawnerOverlay";
    public override PanelType PanelType => PanelType.UrielObjectSpawnerOverlay;

    public override int MinWidth  => 240;
    public override int MinHeight => 240;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // Upper-left-ish, clear of the shared-storage overlay (lower-left).
    public override Vector2 DefaultPosition  => new(
        -Owner.Scaler.m_ReferenceResolution.x * 0.5f + 240f,
         Owner.Scaler.m_ReferenceResolution.y * 0.5f - 230f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.UrielObjectSpawnerOverlayTransparency);
    public override bool UsesCustomBackgroundColor => true;

    // Category cycler state. "" = All. _categories is rebuilt from the unlocked set each render; the
    // SELECTED category (by value) is the source of truth so a list change doesn't shift the selection.
    private readonly List<string> _categories = new();
    private string _selectedCategory = "";
    private int _page;
    private const int PAGE_SIZE = 8;

    // Spawn lifecycle (local to the overlay; full controls on the Object Spawning tab).
    private string _durability = UrielClient.DUR_INDESTRUCTIBLE;
    private bool _respawn;

    private TextMeshProUGUI _catLabel;
    private TextMeshProUGUI _statusLabel;
    private ButtonRef _durBtn;
    private ButtonRef _respawnBtn;
    private GameObject _rowContainer;   // rows are added here (cleared + re-added per page)
    private GameObject _pagerRow;       // Prev / page x/y / Next selector

    private bool _subscribed;
    private bool _autoPullDone;

    public UrielObjectSpawnerOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();
        SetTitle("Spawn Objects");

        // Header / footer rows keep their natural height; the list (flex=1) absorbs resize.
        var rootVlg = ContentRoot.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (rootVlg != null) rootVlg.childForceExpandHeight = false;

        // ── Row 1: category cycler  ←  [Category (N)]  → ──
        // Glyphs: ← → are the confirmed-OK TMPro fallback arrows; ◄ ► render as squares (see
        // docs/V0.26_URIEL_NOTES.md + LESSONS_LEARNED) — the familiar-browser overlay uses ← → for the
        // same reason.
        var catRow = MakeRow("UrielSpawnCatRow", 30);
        AddArrowButton(catRow, "UrielSpawnCatPrev", "←", "Previous category.", () => CycleCategory(-1));
        _catLabel = UIFactory.CreateLabel(catRow, "UrielSpawnCatLabel", "All",
            TextAlignmentOptions.Center, color: null, fontSize: Theme.ScaledOverlay(13)).TextMesh;
        UIFactory.SetLayoutElement(_catLabel.gameObject, minWidth: 100, preferredWidth: 150, flexibleWidth: 1, minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        _catLabel.fontStyle = FontStyles.Bold;
        _catLabel.enableWordWrapping = false;
        _catLabel.overflowMode = TextOverflowModes.Ellipsis;
        AddArrowButton(catRow, "UrielSpawnCatNext", "→", "Next category.", () => CycleCategory(+1));

        // ── Row 2: quick actions (act on the nearest placed object) ──
        var actRow = MakeRow("UrielSpawnActRow", 28);
        AddSmallButton(actRow, "UrielSpawnDespawn", "Despawn", "Remove the spawned object nearest you (.uriel despawn).", () => UrielClient.Despawn(), 74);
        AddSmallButton(actRow, "UrielSpawnRotate", "Rotate", "Rotate the spawned object nearest you 90° (.uriel rotate).", () => UrielClient.Rotate(), 64);
        AddSmallButton(actRow, "UrielSpawnRefresh", "Refresh", "Re-pull your unlocked objects from the server (.uriel api unlocked).", () => UrielProtocolService.StartUnlockedScan(), 72);

        // ── Row 3: spawn-as durability + respawn ──
        var modeRow = MakeRow("UrielSpawnModeRow", 28);
        _durBtn = AddSmallButton(modeRow, "UrielSpawnDurability", DurabilityLabel(),
            "Cycle spawned-object durability: Indestructible → Breakable → Smashable.", CycleDurability, 132);
        _respawnBtn = AddSmallButton(modeRow, "UrielSpawnRespawn", RespawnLabel(),
            "Toggle auto-respawn for breakable/smashable objects.", ToggleRespawn, 96);

        // ── Status line ──
        _statusLabel = UIFactory.CreateLabel(ContentRoot, "UrielSpawnStatus", "—",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledOverlay(10)).TextMesh;
        UIFactory.SetLayoutElement(_statusLabel.gameObject, minWidth: 200, preferredWidth: 240, flexibleWidth: 1, minHeight: 16, preferredHeight: 18, flexibleHeight: 0);
        _statusLabel.enableWordWrapping = false;
        _statusLabel.overflowMode = TextOverflowModes.Ellipsis;

        // ── Object rows (PAGED — Prev/Next selector below) ──
        // No scroll view on purpose: the mouse wheel over a scroll view zooms the GAME camera (V Rising
        // captures the wheel), so a page selector is the reliable control. Rows are parented to _rowContainer
        // and cleared + re-added each page — the 0.29.0 "list grows forever" bug was MakeRow parenting rows to
        // ContentRoot (so ClearRows, which clears _rowContainer, never removed them); fixed by MakeRow(parent:).
        _rowContainer = UIFactory.CreateVerticalGroup(ContentRoot, "UrielSpawnRows",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_rowContainer, minWidth: 200, preferredWidth: 240, flexibleWidth: 1, minHeight: 40, flexibleHeight: 1);

        // ── Page selector ──
        _pagerRow = UIFactory.CreateHorizontalGroup(ContentRoot, "UrielSpawnPager",
            forceExpandWidth: true, forceExpandHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_pagerRow, minWidth: 200, preferredWidth: 240, flexibleWidth: 1, minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        if (!_subscribed)
        {
            UrielState.UnlockedChanged += Render;
            UrielState.PresenceChanged += Render;
            _subscribed = true;
        }

        // Auto-pull the unlocked list on first show (once MessageService + Uriel are ready).
        Behaviors.CoreUpdateBehavior.Actions.Add(TickDeferredAutoPull);

        Render();
    }

    private void TickDeferredAutoPull()
    {
        if (_autoPullDone) return;
        if (!MessageService.IsInitialized || !UrielState.Present) return;
        _autoPullDone = true;
        if (!UrielState.ObjectSpawnEnabled) return;
        if (UrielState.UnlockedLoaded && UrielState.Unlocked.Count > 0) return;
        UrielProtocolService.StartUnlockedScan();
    }

    // ── category cycling ──
    private void BuildCategories()
    {
        _categories.Clear();
        _categories.Add("");   // All
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in UrielState.Unlocked)
            if (!string.IsNullOrEmpty(o.Category)) set.Add(o.Category);
        foreach (var c in set) _categories.Add(c);
        // Keep the selection valid if its category disappeared.
        if (!_categories.Contains(_selectedCategory)) _selectedCategory = "";
    }

    private void CycleCategory(int dir)
    {
        BuildCategories();
        if (_categories.Count == 0) return;
        int idx = _categories.IndexOf(_selectedCategory);
        if (idx < 0) idx = 0;
        idx = (idx + dir + _categories.Count) % _categories.Count;
        _selectedCategory = _categories[idx];
        _page = 0;
        Render();
    }

    private string DurabilityLabel() => "As: " + _durability switch
    {
        UrielClient.DUR_BREAKABLE => "Breakable",
        UrielClient.DUR_SMASHABLE => "Smashable",
        _                         => "Indestructible",
    };
    private string RespawnLabel() => _respawn ? "Respawn: ON" : "Respawn: off";

    private void CycleDurability()
    {
        _durability = _durability switch
        {
            UrielClient.DUR_INDESTRUCTIBLE => UrielClient.DUR_BREAKABLE,
            UrielClient.DUR_BREAKABLE      => UrielClient.DUR_SMASHABLE,
            _                              => UrielClient.DUR_INDESTRUCTIBLE,
        };
        SetButtonText(_durBtn, DurabilityLabel());
    }
    private void ToggleRespawn() { _respawn = !_respawn; SetButtonText(_respawnBtn, RespawnLabel()); }

    private void Render()
    {
        if (_rowContainer == null || _statusLabel == null) return;
        BuildCategories();

        // Category label: "All (N)" or "<cat> (N)".
        int catCount = 0;
        foreach (var o in UrielState.Unlocked) if (MatchesSelected(o)) catCount++;
        string catName = string.IsNullOrEmpty(_selectedCategory) ? "All" : _selectedCategory;
        if (_catLabel != null) _catLabel.text = $"{catName} ({catCount})";

        ClearRows();
        ClearPager();

        if (!UrielState.Present)
        {
            _statusLabel.text = "<color=#FFB070>Uriel not detected on this server.</color>";
            return;
        }
        if (!UrielState.ObjectSpawnEnabled)
        {
            _statusLabel.text = "<color=#A0A0A0>Object spawning is disabled on this server.</color>";
            return;
        }

        var filtered = new List<UrielObject>();
        foreach (var o in UrielState.Unlocked) if (MatchesSelected(o)) filtered.Add(o);

        if (filtered.Count == 0)
        {
            _statusLabel.text = UrielState.Unlocked.Count == 0
                ? (UrielState.UnlockedLoaded ? "<color=#A0A0A0>No objects unlocked yet.</color>" : "<color=#A0A0A0>Loading… (Refresh)</color>")
                : "<color=#A0A0A0>No unlocked objects in this category.</color>";
            return;
        }

        int pages = (filtered.Count + PAGE_SIZE - 1) / PAGE_SIZE;
        if (_page < 0) _page = 0; else if (_page >= pages) _page = pages - 1;

        string scope = string.IsNullOrEmpty(_selectedCategory) ? "unlocked" : $"in {_selectedCategory}";
        _statusLabel.text = $"<color=#90EE90>{filtered.Count}</color> {scope}  ·  page {_page + 1}/{pages}";

        int start = _page * PAGE_SIZE;
        int end = Math.Min(start + PAGE_SIZE, filtered.Count);
        for (int i = start; i < end; i++) AddObjectRow(filtered[i]);

        if (pages > 1) BuildPager(pages);
    }

    private bool MatchesSelected(UrielObject o)
        => string.IsNullOrEmpty(_selectedCategory)
           || string.Equals(o.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase);

    private void AddObjectRow(UrielObject o)
    {
        var row = MakeRow($"UrielSpawnRow_{o.Guid}", 24, _rowContainer);
        var name = UIFactory.CreateLabel(row, "Name", o.DisplayName,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledOverlay(11)).TextMesh;
        UIFactory.SetLayoutElement(name.gameObject, minWidth: 110, preferredWidth: 160, flexibleWidth: 1, minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        name.enableWordWrapping = false;
        name.overflowMode = TextOverflowModes.Ellipsis;

        AddSmallButton(row, $"UrielSpawnBtn_{o.Guid}", "Spawn",
            $"Spawn {o.DisplayName} at your location ({_durability}{(_respawn ? " + respawn" : "")}). Move it with the Rotate / build-hotkeys.",
            () => UrielClient.Spawn(o.Guid, _durability, _respawn), 66);
    }

    private void BuildPager(int pages)
    {
        if (_page > 0)
            AddSmallButton(_pagerRow, "UrielSpawnPrev", "← Prev", "Previous page.", () => { _page--; Render(); }, 70);
        var lbl = UIFactory.CreateLabel(_pagerRow, "UrielSpawnPageLbl", $"{_page + 1}/{pages}",
            TextAlignmentOptions.Center, color: null, fontSize: Theme.ScaledOverlay(11)).TextMesh;
        UIFactory.SetLayoutElement(lbl.gameObject, minWidth: 50, preferredWidth: 80, flexibleWidth: 1, minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        lbl.enableWordWrapping = false;
        lbl.overflowMode = TextOverflowModes.Overflow;
        if (_page < pages - 1)
            AddSmallButton(_pagerRow, "UrielSpawnNext", "Next →", "Next page.", () => { _page++; Render(); }, 70);
    }

    // ── small UI builders ──
    private GameObject MakeRow(string name, int height, GameObject parent = null)
    {
        var row = UIFactory.CreateHorizontalGroup(parent ?? ContentRoot, name,
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(row, minWidth: 200, preferredWidth: 240, flexibleWidth: 1,
            minHeight: height, preferredHeight: height + 2, flexibleHeight: 0);
        return row;
    }

    private ButtonRef AddArrowButton(GameObject parent, string name, string glyph, string tooltip, Action onClick)
    {
        var btn = UIFactory.CreateButton(parent, name, glyph);
        UIFactory.SetLayoutElement(btn.GameObject, minWidth: 30, preferredWidth: 32, flexibleWidth: 0, minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledOverlay(16); t.fontStyle = FontStyles.Bold; t.alignment = TextAlignmentOptions.Center; }
        if (!string.IsNullOrEmpty(tooltip)) UI.TooltipHover.Attach(btn.GameObject, tooltip);
        btn.OnClick = () => { try { onClick(); } catch (Exception ex) { LogUtils.LogError($"[Uriel] spawn-overlay '{name}' threw: {ex}"); } };
        return btn;
    }

    private ButtonRef AddSmallButton(GameObject parent, string name, string label, string tooltip, Action onClick, int width = 70)
    {
        var btn = UIFactory.CreateButton(parent, name, label);
        UIFactory.SetLayoutElement(btn.GameObject, minWidth: width, preferredWidth: width, flexibleWidth: 0, minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) { t.fontSize = Theme.ScaledOverlay(11); t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Overflow; }
        if (!string.IsNullOrEmpty(tooltip)) UI.TooltipHover.Attach(btn.GameObject, tooltip);
        btn.OnClick = () => { try { onClick(); } catch (Exception ex) { LogUtils.LogError($"[Uriel] spawn-overlay '{name}' threw: {ex}"); } };
        return btn;
    }

    private static void SetButtonText(ButtonRef btn, string text)
    {
        if (btn?.Component == null) return;
        var t = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = text;
    }

    private void ClearRows() => ClearChildren(_rowContainer);
    private void ClearPager() => ClearChildren(_pagerRow);

    private static void ClearChildren(GameObject go)
    {
        if (go == null) return;
        var t = go.transform;
        for (int i = t.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
    }

    internal override void Reset()
    {
        Behaviors.CoreUpdateBehavior.Actions.Remove(TickDeferredAutoPull);
        if (_subscribed)
        {
            UrielState.UnlockedChanged -= Render;
            UrielState.PresenceChanged -= Render;
            _subscribed = false;
        }
    }
}
