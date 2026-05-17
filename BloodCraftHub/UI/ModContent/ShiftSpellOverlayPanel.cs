using BloodCraftHub.Config;
using BloodCraftHub.Services;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.CustomLib.Util;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;

namespace BloodCraftHub.UI.ModContent;

// 0.11.0: visual-only shift-spell cooldown overlay.
// 0.11.1 redesigned per friend-test:
//   - Square button-style tile (was a wide panel with horizontal bar).
//   - Radial-360 cooldown sweep over the tile (was MiniBar).
//   - Detection now reads AbilityGroupSlotBuffer[3] instead of just the
//     transient AbilityBar_Shared.CastGroup (see ShiftCooldownService).
//
// Layout (approx 100×120, draggable):
//   ┌──────────────┐
//   │  ┌────────┐  │
//   │  │  ░░░   │  │  ← 80×80 tile: bg color + radial dark overlay
//   │  │  1.4s  │  │  ← countdown text centered over tile
//   │  │  ░░░   │  │
//   │  └────────┘  │
//   │   SHIFT      │  ← keybind label below tile
//   └──────────────┘
//
// Reads ShiftCooldownService at 10 Hz (the service is the one rate-limited;
// the per-frame ticker just paints the cached values). Visual-only — clicking
// does nothing; press the bound Shift key to actually cast.
//
// Inherits RespectsLockOverlays=true from ResizeablePanelBase so the global
// "Lock overlays" toggle pins it like every other overlay.
public class ShiftSpellOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "ShiftSpellOverlay";
    public override PanelType PanelType => PanelType.ShiftSpellOverlay;

    // 0.11.5: shrunk back down now that the diag line is opt-in. Existing
    // saved panel sizes (e.g. the wider one from 0.11.3-4) are unaffected
    // — these are floors, not the actual size on screen.
    public override int MinWidth  => 140;
    public override int MinHeight => 120;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // Lower-right of screen, clear of Bloodcraft's bottom-bar UI.
    public override Vector2 DefaultPosition  => new(
         Owner.Scaler.m_ReferenceResolution.x * 0.5f - 220f,
        -Owner.Scaler.m_ReferenceResolution.y * 0.5f + 220f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.ShiftSpellOverlayTransparency);
    // 0.12.0: overlay-wide color theme.
    public override bool UsesCustomBackgroundColor => true;

    // Cached widgets — driven each tick from ShiftCooldownService.
    private GameObject       _tileGo;
    private Image            _tileBg;
    private Image            _tileRadial;     // radial cooldown sweep overlay
    private TextMeshProUGUI  _tileText;       // centered countdown / "Ready"
    private TextMeshProUGUI  _labelText;      // "SHIFT" / "SHIFT 2/3"
    private TextMeshProUGUI  _diagText;       // tiny debug line under the label
    private System.Action    _ticker;

    // 0.11.1: shared 1×1 white sprite. UnityEngine's Image needs a sprite
    // before fillAmount/fillMethod do anything (sprite-less Image draws a
    // flat colored rect and ignores fill state). We don't need a circular
    // sprite — a 1×1 white pixel stretched to the Image's rect with
    // FillMethod.Radial360 gives a perfectly fine sweep over a square tile.
    private static Sprite _whiteSprite;
    private static Sprite GetWhiteSprite()
    {
        if (_whiteSprite != null) return _whiteSprite;
        try
        {
            _whiteSprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f));
        }
        catch
        {
            // If Texture2D.whiteTexture isn't reachable in this build's
            // IL2CPP bridge, return null — the panel falls back to a
            // sprite-less Image (flat color, no radial fill).
            _whiteSprite = null;
        }
        return _whiteSprite;
    }

    // Theme — kept inline since these are only used once.
    private static readonly Color TILE_BASE_READY  = new Color(0.32f, 0.55f, 0.85f, 0.95f); // bright cool-blue when ready
    private static readonly Color TILE_BASE_BUSY   = new Color(0.18f, 0.24f, 0.36f, 0.95f); // muted when cooling down
    private static readonly Color RADIAL_OVERLAY   = new Color(0.04f, 0.04f, 0.06f, 0.78f); // dark sweep over the tile

    public ShiftSpellOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();

        // Center the tile horizontally inside the panel by wrapping it in a
        // center-aligned horizontal row.
        var tileRow = UIFactory.CreateHorizontalGroup(ContentRoot, "ShiftTileRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: false, childControlHeight: false,
            spacing: 0, padding: new Vector4(0, 0, 0, 0),
            childAlignment: TextAnchor.MiddleCenter);
        UIFactory.SetLayoutElement(tileRow,
            minWidth: MinWidth - 12, preferredWidth: MinWidth - 12, flexibleWidth: 1,
            minHeight: 84, preferredHeight: 84, flexibleHeight: 0);

        // The tile itself — a plain UI GameObject sized 80×80 with
        // manually-anchored children (background image, radial overlay,
        // centered text). Doesn't use a layout group of its own so children
        // can stack on top of one another.
        _tileGo = UIFactory.CreateUIObject("ShiftTile", tileRow);
        var tileRt = _tileGo.GetComponent<RectTransform>();
        tileRt.sizeDelta = new Vector2(80, 80);
        UIFactory.SetLayoutElement(_tileGo,
            minWidth: 80, preferredWidth: 80, flexibleWidth: 0,
            minHeight: 80, preferredHeight: 80, flexibleHeight: 0);

        // ── Background (visible behind the radial overlay) ──
        _tileBg = _tileGo.AddComponent<Image>();
        _tileBg.color = TILE_BASE_READY;
        // No sprite needed — a sprite-less Image renders as a flat rect.
        var bgOutline = _tileGo.AddComponent<Outline>();
        bgOutline.effectColor = new Color(0.85f, 0.85f, 0.85f, 0.9f);
        bgOutline.effectDistance = new Vector2(1.2f, -1.2f);

        // ── Radial overlay child (drawn ON TOP of the background) ──
        var radialGo = UIFactory.CreateUIObject("ShiftRadial", _tileGo);
        var radialRt = radialGo.GetComponent<RectTransform>();
        radialRt.anchorMin = Vector2.zero;
        radialRt.anchorMax = Vector2.one;
        radialRt.offsetMin = Vector2.zero;
        radialRt.offsetMax = Vector2.zero;
        _tileRadial = radialGo.AddComponent<Image>();
        _tileRadial.color = RADIAL_OVERLAY;
        _tileRadial.raycastTarget = false;
        var ws = GetWhiteSprite();
        if (ws != null)
        {
            _tileRadial.sprite = ws;
            _tileRadial.type = Image.Type.Filled;
            _tileRadial.fillMethod = Image.FillMethod.Radial360;
            _tileRadial.fillOrigin = 2;          // Top — clock-style start
            _tileRadial.fillClockwise = true;    // sweep right (clockwise)
            _tileRadial.fillAmount = 0f;          // begin "Ready" until first poll
        }
        else
        {
            // Fall back to a flat overlay at 0 alpha — no radial sweep
            // possible without a sprite, but at least the tile still works.
            var c = _tileRadial.color; c.a = 0f; _tileRadial.color = c;
        }

        // ── Centered countdown text (drawn ON TOP of the radial) ──
        // 0.11.5: bumped font + bold + glyph-shadow outline so the digits
        // stay legible against both the bright ready-state tile AND the
        // dark radial sweep mid-cooldown.
        var textGo = UIFactory.CreateUIObject("ShiftCountdown", _tileGo);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        _tileText = textGo.AddComponent<TextMeshProUGUI>();
        _tileText.alignment = TextAlignmentOptions.Center;
        _tileText.fontSize  = Theme.ScaledOverlay(26);
        _tileText.fontStyle = FontStyles.Bold;
        _tileText.color     = Color.white;
        _tileText.enableWordWrapping = false;
        _tileText.overflowMode = TextOverflowModes.Overflow;
        _tileText.raycastTarget = false;
        // TMP outline material settings — soft dark halo around every glyph.
        _tileText.outlineWidth = 0.25f;
        _tileText.outlineColor = new Color32(0, 0, 0, 220);
        _tileText.text = "—";

        // ── "SHIFT" label below the tile ──
        _labelText = AddCenteredLabel("ShiftLabel", "SHIFT",
            FontStyles.Bold, Theme.ScaledOverlay(14));
        _labelText.outlineWidth = 0.18f;
        _labelText.outlineColor = new Color32(0, 0, 0, 200);

        // ── Diagnostic line (gated by Settings.ShiftSpellOverlayShowDiagnostics).
        // Default OFF — only construct when the user opts in via the
        // BepInEx config file. Flipping the setting takes effect on the
        // next overlay rebuild (panel close + reopen, or game restart).
        if (Settings.ShiftSpellOverlayShowDiagnostics)
        {
            _diagText = AddCenteredLabel("ShiftDiag", "",
                FontStyles.Italic, Theme.ScaledOverlay(9));
            var diagLe = _diagText.gameObject.GetComponent<UnityEngine.UI.LayoutElement>();
            if (diagLe != null) { diagLe.minHeight = 14; diagLe.preferredHeight = 14; }
            _diagText.color = new Color(0.7f, 0.7f, 0.7f, 0.95f);
        }

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
            minWidth: 90, preferredWidth: 100, flexibleWidth: 1,
            minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = style;
        lbl.TextMesh.enableWordWrapping = false;
        lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        return lbl.TextMesh;
    }

    private void OnTick()
    {
        if (_tileText == null) return;

        // Service self-throttles to 10 Hz, so this is a no-op most frames
        // even when called every render tick.
        ShiftCooldownService.Tick();

        // Always paint the diagnostic line — even when HasShiftSpell is
        // false. Pre-0.11.4 this update sat after an early return for the
        // "no shift" case, so the diag area stayed permanently empty and
        // told us nothing about why detection was failing.
        if (_diagText != null)
        {
            _diagText.text =
                  $"pf {ShiftCooldownService.DiagShiftPrefabHash}  "
                + $"cg {ShiftCooldownService.DiagCastGroupPrefabHash}  "
                + $"si {ShiftCooldownService.DiagCastGroupSlotIndex}  "
                + $"end {ShiftCooldownService.DiagLatchedEnd:F1}  "
                + $"srv {ShiftCooldownService.DiagServerNow:F1}  "
                + $"{ShiftCooldownService.DiagLastReadSource}";
        }

        if (!ShiftCooldownService.HasShiftSpell)
        {
            _labelText.text = "SHIFT";
            _tileText.text  = string.IsNullOrEmpty(ShiftCooldownService.LastError)
                ? "—"
                : "n/a";
            _tileBg.color = TILE_BASE_BUSY;
            if (_tileRadial != null) _tileRadial.fillAmount = 0f;
            return;
        }

        // Charges header.
        _labelText.text = ShiftCooldownService.MaxCharges > 1
            ? $"SHIFT  {ShiftCooldownService.CurrentCharges}/{ShiftCooldownService.MaxCharges}"
            : "SHIFT";

        float remaining = ShiftCooldownService.CooldownRemaining;
        float total     = ShiftCooldownService.CooldownTotal;
        bool ready      = remaining <= 0.05f;

        // Radial fill convention: fillAmount = remaining/total.
        float fill = total > 0.001f ? Mathf.Clamp01(remaining / total) : 0f;
        if (_tileRadial != null) _tileRadial.fillAmount = fill;

        _tileBg.color = ready ? TILE_BASE_READY : TILE_BASE_BUSY;

        if (ready)
            _tileText.text = "Ready";
        else if (remaining < 10f)
            _tileText.text = $"{remaining:0.0}s";
        else
            _tileText.text = $"{(int)remaining}s";
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
