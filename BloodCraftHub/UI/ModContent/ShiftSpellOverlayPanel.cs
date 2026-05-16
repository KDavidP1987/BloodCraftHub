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

// 0.11.0: visual-only shift-spell cooldown overlay.
//
// Friend-test ask: "a button on my window that shows the shift command with
// a loading circle for the cooldown." We don't fire the spell from a click
// (V Rising's input bus doesn't accept that path; pressing the bound Shift
// key remains the cast trigger), but we DO surface the cooldown live so the
// player can monitor it without taking their eyes off the action.
//
// Layout:
//   ┌──────────────────────┐
//   │       SHIFT          │  ← keybind label (bold)
//   │  ▓▓▓▓▓▓▓░░░░░░  78%  │  ← horizontal cooldown bar
//   │       1.4s           │  ← remaining cooldown / "Ready" / "—"
//   └──────────────────────┘
//
// Polls ShiftCooldownService (in turn polls game-side AbilityCooldownState)
// every frame; cheap thanks to the 10 Hz throttle inside the service.
//
// Coexists with Eclipse's shift slot — they read the same game state, so
// the numbers will agree. The two overlays live in different positions
// (Eclipse pins to the bottom ability bar; BCH is freely draggable).
public class ShiftSpellOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "ShiftSpellOverlay";
    public override PanelType PanelType => PanelType.ShiftSpellOverlay;

    public override int MinWidth  => 140;
    public override int MinHeight => 90;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // Default to lower-right area, well clear of Bloodcraft's bottom-bar UI
    // (so even if the user installs both Eclipse and BCH, the two don't
    // overlap right out of the box).
    public override Vector2 DefaultPosition  => new(
         Owner.Scaler.m_ReferenceResolution.x * 0.5f - 220f,
        -Owner.Scaler.m_ReferenceResolution.y * 0.5f + 220f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.ShiftSpellOverlayTransparency);

    private TextMeshProUGUI _labelText;     // "SHIFT" / "SHIFT  2/3"
    private TextMeshProUGUI _statusText;    // "Ready" / "1.4s" / "—"
    private GameObject      _cooldownBar;
    private RectTransform   _cooldownFill;
    private System.Action   _ticker;

    public ShiftSpellOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();

        _labelText = AddCenteredLabel("ShiftLabel", "SHIFT",
            FontStyles.Bold, Theme.ScaledOverlay(15));

        // Cooldown bar — reused MiniBar pattern so visual style matches the
        // XP / Familiar overlay bars.
        _cooldownBar = MiniBar.Create(ContentRoot, "ShiftCdBar", out _cooldownFill,
            fillColor: new Color(0.45f, 0.75f, 1.0f, 0.95f)); // ice-blue, distinct from XP/legacy bars
        if (_cooldownBar != null)
        {
            var le = _cooldownBar.GetComponent<UnityEngine.UI.LayoutElement>();
            if (le != null)
            {
                int h = 14;
                le.minHeight = h;
                le.preferredHeight = h;
                le.minWidth = MinWidth - 24;
            }
        }

        _statusText = AddCenteredLabel("ShiftStatus", "—",
            FontStyles.Normal, Theme.ScaledOverlay(13));

        if (_ticker == null)
        {
            _ticker = OnTick;
            BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Add(_ticker);
        }
    }

    private TextMeshProUGUI AddCenteredLabel(string name, string text, FontStyles style, int fontSize)
    {
        var lbl = UIFactory.CreateLabel(ContentRoot, name, text,
            TextAlignmentOptions.Center, color: null, fontSize: fontSize);
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 120, preferredWidth: 140, flexibleWidth: 1,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = style;
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        return lbl.TextMesh;
    }

    private void OnTick()
    {
        if (_labelText == null) return;

        // The service is fed by Plugin's CoreUpdateBehavior tick, but call it
        // here too — Tick() throttles internally to 0.1s so the extra call is
        // a no-op most frames. This keeps the overlay live even if some
        // future refactor unhooks the global ticker.
        ShiftCooldownService.Tick();

        if (!ShiftCooldownService.HasShiftSpell)
        {
            _labelText.text  = "SHIFT";
            _statusText.text = string.IsNullOrEmpty(ShiftCooldownService.LastError)
                ? "(no shift spell equipped)"
                : "(unavailable)";
            MiniBar.SetProgress(_cooldownFill, 0f);
            return;
        }

        // Charges display when more than one is possible (e.g., Reaper class).
        if (ShiftCooldownService.MaxCharges > 1)
            _labelText.text = $"SHIFT  {ShiftCooldownService.CurrentCharges}/{ShiftCooldownService.MaxCharges}";
        else
            _labelText.text = "SHIFT";

        float remaining = ShiftCooldownService.CooldownRemaining;
        float total     = ShiftCooldownService.CooldownTotal;

        // Bar fill matches Eclipse's "1 - remaining/total" convention so a
        // full bar = ready, empty bar = freshly used and recharging.
        float ready = total > 0.001f
            ? 1f - Mathf.Clamp01(remaining / total)
            : 1f;
        MiniBar.SetProgress(_cooldownFill, ready);

        if (remaining <= 0.05f)
            _statusText.text = "Ready";
        else if (remaining < 10f)
            _statusText.text = $"{remaining:0.0}s";
        else
            _statusText.text = $"{(int)remaining}s";
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
