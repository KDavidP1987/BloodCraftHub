using BloodCraftHub.Config;
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

// 0.26: "Nearby Public Storage" overlay — a small on-screen list of Uriel-SHARED containers / prison
// cells around you, detected entirely CLIENT-SIDE (SharedContainerDetector reads replicated ECS state;
// no server query). This is the "shared-container badges" feature in a draggable-panel form (a true
// world-anchored badge would need per-entity world→screen tracking; this is the pragmatic v1).
//
// Refreshes on a throttled ticker (~every 1.5s) while visible — cheap, and the detector self-disables
// on repeated faults so the overlay can never become a crash vector (it just shows "unavailable").
public class UrielSharedOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "UrielSharedOverlay";
    public override PanelType PanelType => PanelType.UrielSharedOverlay;

    public override int MinWidth  => 160;
    public override int MinHeight => 70;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // Lower-left, offset from the Beelz overlays so they don't stack.
    public override Vector2 DefaultPosition  => new(
        -Owner.Scaler.m_ReferenceResolution.x * 0.5f + 220f,
        -Owner.Scaler.m_ReferenceResolution.y * 0.5f + 460f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.UrielSharedOverlayTransparency);
    public override bool UsesCustomBackgroundColor => true;

    private const float SCAN_RADIUS = 15f;
    private const float REFRESH_INTERVAL = 1.5f;
    private float _lastRefreshAt = -999f;

    private LabelRef _header;
    private GameObject _rowContainer;
    private System.Action _ticker;

    public UrielSharedOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();
        SetTitle("Public Storage");

        _header = UIFactory.CreateLabel(ContentRoot, "UrielSharedHeader", "Scanning…",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(_header.GameObject,
            minWidth: 150, preferredWidth: 200, flexibleWidth: 1, minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        _header.TextMesh.enableWordWrapping = false;
        _header.TextMesh.overflowMode = TextOverflowModes.Overflow;

        _rowContainer = UIFactory.CreateVerticalGroup(ContentRoot, "UrielSharedRows",
            forceWidth: true, forceHeight: false, childControlWidth: true, childControlHeight: true,
            spacing: 1, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(_rowContainer, minWidth: 150, preferredWidth: 200, flexibleWidth: 1, minHeight: 20, flexibleHeight: 1);

        _ticker = Tick;
        BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Add(_ticker);
        Refresh();
    }

    private void Tick()
    {
        if (!Enabled) return;
        float now = Time.realtimeSinceStartup;
        if (now - _lastRefreshAt < REFRESH_INTERVAL) return;
        _lastRefreshAt = now;
        Refresh();
    }

    private void Refresh()
    {
        if (_rowContainer == null || _header == null) return;
        ClearRows();

        if (!SharedContainerDetector.Available)
        {
            _header.TextMesh.text ="<color=#FFB070>Detection unavailable</color>";
            return;
        }

        var found = SharedContainerDetector.ScanNearby(SCAN_RADIUS);
        if (found.Count == 0)
        {
            _header.TextMesh.text ="<color=#A0A0A0>No public storage nearby</color>";
            return;
        }

        _header.TextMesh.text = $"<color=#90EE90>{found.Count} public nearby</color>";
        int shown = 0;
        foreach (var c in found)
        {
            if (shown++ >= 8) break;
            string kind = c.Kind == SharedContainerDetector.ContainerKind.PrisonCell ? "cell" : "chest";
            var row = UIFactory.CreateLabel(_rowContainer, $"UrielSharedRow_{shown}",
                $"• {c.Name} <color={Theme.MutedBodyHex}>({kind}, {c.Distance:0.#}m)</color>",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
            UIFactory.SetLayoutElement(row.GameObject,
                minWidth: 150, preferredWidth: 220, flexibleWidth: 1, minHeight: 16, preferredHeight: 18, flexibleHeight: 0);
            row.TextMesh.enableWordWrapping = false;
            row.TextMesh.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    private void ClearRows()
    {
        if (_rowContainer == null) return;
        var t = _rowContainer.transform;
        for (int i = t.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
    }

    internal override void Reset()
    {
        if (_ticker != null)
        {
            BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Remove(_ticker);
            _ticker = null;
        }
    }
}
