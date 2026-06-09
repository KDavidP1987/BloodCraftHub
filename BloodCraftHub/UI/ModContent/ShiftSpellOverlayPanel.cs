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

// Visual-only shift-spell cooldown overlay.
//
// 0.18.4 REDESIGN (friend-feedback): a single compact ability-button TILE that FILLS the panel,
// roughly mirroring V Rising's action-bar slot look (the Eclipse shift-slot a tester shared). The
// pre-0.18.4 version drew a FIXED 80×80 tile floating inside a larger 140×120 panel with a "SHIFT"
// label on its own row below — so resizing the panel just added whitespace and it never read as a
// game ability button. Now:
//   - The tile is the sole laid-out child of ContentRoot's VerticalLayoutGroup (TitleBar hidden) with
//     flexible 9999×9999, so it FILLS the panel and SCALES when you resize → the button itself grows
//     and shrinks. Default size is small (MinWidth×MinHeight) and thematic.
//   - Dark beveled frame (outer Image + Outline) → tinted slot interior → slotted-spell icon → radial
//     cooldown sweep → centered countdown → "Shift" keybind on a strip at the bottom of the tile →
//     charges badge in the top-right. The countdown / keybind fonts scale with the tile height.
//
// We can't use Eclipse's exact look — Eclipse CLONES the game's real AbilityBarEntry prefab into the
// game HUD canvas; BCH deliberately keeps its UI on its own canvas (HUD manipulation is the 0.16
// crash class), so this is a hand-rolled approximation that reads the slotted-spell icon (already
// resolved from the game by ShiftCooldownService) inside a thematic frame.
//
// Reads ShiftCooldownService at 10 Hz (the service self-throttles; the per-frame ticker just paints
// the cached values). Visual-only — clicking does nothing; press the bound Shift key to actually cast.
// Inherits RespectsLockOverlays=true so the global "Lock overlays" toggle pins it like every overlay.
public class ShiftSpellOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "ShiftSpellOverlay";
    public override PanelType PanelType => PanelType.ShiftSpellOverlay;

    // 0.18.4: small, ability-slot-sized default (was 140×120). These are floors AND — since the panel
    // has no explicit default size — the on-screen default. A saved size from a prior version still
    // wins (it's above this floor); such users can drag the corner down to ~44px or use Settings →
    // Size & Positioning → reset to get the new compact default. The tile scales to whatever size.
    public override int MinWidth  => 44;
    public override int MinHeight => 48;

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
    public override bool UsesCustomBackgroundColor => true;

    // Cached widgets — driven each tick from ShiftCooldownService.
    private RectTransform    _tileRt;          // the tile's transform (read for font-scaling)
    private GameObject       _tileGo;
    private Image            _frameImg;        // outer dark frame (the "border")
    private Image            _tileBg;          // slot interior — flips ready/busy color
    private Image            _tileIcon;        // slotted-spell icon (under radial + text)
    private int              _tileIconHash;    // prefab hash of the icon currently shown (0 = none)
    private Image            _tileRadial;      // radial cooldown sweep overlay
    private Image            _keyStripBg;      // semi-dark strip behind the keybind label
    private TextMeshProUGUI  _tileText;        // centered countdown / "Ready"
    private TextMeshProUGUI  _labelText;       // "Shift" keybind label (bottom strip)
    private TextMeshProUGUI  _chargesText;     // "2/3" charges badge (top-right)
    private TextMeshProUGUI  _diagText;        // tiny debug line below the tile (opt-in)
    private System.Action    _ticker;
    private float            _lastOpacity = -1f;

    // Shared 1×1 white sprite for the radial fill. An Image needs a sprite before fillAmount/fillMethod
    // do anything; a white pixel stretched with FillMethod.Radial360 sweeps fine over a square tile.
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
        catch { _whiteSprite = null; }
        return _whiteSprite;
    }

    // Theme — ability-slot palette.
    // 0.18.4 follow-up: the ready/busy color lives on the FRAME (border), and the interior behind the
    // icon is a DARK slot bg. Previously the interior itself was a bright blue "ready" fill drawn behind
    // the icon — which bled through the icon sprite's transparent areas as a "blue film" over the spell
    // art (tester report). A dark slot bg lets the icon read clean; the colored border is the ready cue.
    private static readonly Color SLOT_BG         = new Color(0.07f, 0.08f, 0.11f, 0.98f); // dark slot interior (behind icon)
    private static readonly Color FRAME_EDGE      = new Color(0.62f, 0.66f, 0.78f, 0.85f); // cool steel edge highlight
    private static readonly Color FRAME_READY     = new Color(0.32f, 0.55f, 0.85f, 0.98f); // cool-blue — ONLY before the icon resolves
    private static readonly Color FRAME_BUSY      = new Color(0.20f, 0.24f, 0.32f, 0.98f); // muted — no-icon placeholder while not ready
    private static readonly Color FRAME_NEUTRAL   = new Color(0.13f, 0.15f, 0.20f, 0.98f); // dark neutral — once the icon IS showing (no blue)
    private static readonly Color RADIAL_OVERLAY  = new Color(0.04f, 0.04f, 0.06f, 0.80f); // dark sweep = time remaining
    private static readonly Color KEYBIND_STRIP   = new Color(0f, 0f, 0f, 0.55f);          // backdrop behind "Shift"

    public ShiftSpellOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();

        // ── The tile: sole laid-out child, fills + scales with the panel ──
        _tileGo = UIFactory.CreateUIObject("ShiftTile", ContentRoot);
        _tileRt = _tileGo.GetComponent<RectTransform>();
        UIFactory.SetLayoutElement(_tileGo,
            minWidth: 32, preferredWidth: 56, flexibleWidth: 9999,
            minHeight: 32, preferredHeight: 56, flexibleHeight: 9999);

        // Outer frame = the border, and the READY/BUSY accent (set each tick). Crisp steel edge.
        _frameImg = _tileGo.AddComponent<Image>();
        _frameImg.color = FRAME_READY;
        var frameOutline = _tileGo.AddComponent<Outline>();
        frameOutline.effectColor = FRAME_EDGE;
        frameOutline.effectDistance = new Vector2(1.4f, -1.4f);

        // Slot interior = DARK backing behind the icon (inset inside the frame so the frame reads as a
        // colored border). Constant dark so the icon reads clean — no color bleeds through the icon's
        // transparent areas (the old "blue film" bug).
        var interiorGo = UIFactory.CreateUIObject("ShiftInterior", _tileGo);
        StretchFill(interiorGo.GetComponent<RectTransform>(), 2.5f);
        _tileBg = interiorGo.AddComponent<Image>();
        _tileBg.color = SLOT_BG;
        _tileBg.raycastTarget = false;

        // Slotted-spell icon (0.16): over the dark interior, under the radial + text. Sits on dark so
        // the spell art is fully visible. Sprite assigned each tick once resolved.
        var iconGo = UIFactory.CreateUIObject("ShiftIcon", _tileGo);
        StretchFill(iconGo.GetComponent<RectTransform>(), 4f);
        _tileIcon = iconGo.AddComponent<Image>();
        _tileIcon.raycastTarget = false;
        _tileIcon.preserveAspect = true;
        _tileIcon.enabled = false; // no sprite yet

        // Radial cooldown sweep, over the interior.
        var radialGo = UIFactory.CreateUIObject("ShiftRadial", _tileGo);
        StretchFill(radialGo.GetComponent<RectTransform>(), 2.5f);
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
            _tileRadial.fillClockwise = true;
            _tileRadial.fillAmount = 0f;
        }
        else
        {
            var c = _tileRadial.color; c.a = 0f; _tileRadial.color = c;
        }
        // 0.18.4 follow-up: start the cooldown layer fully OFF. It's only enabled while a cooldown is
        // actually counting down (OnTick) — so when Shift is ready the icon shows with NOTHING over it
        // (a faint fill=0 sliver / film no longer lingers; the layer is genuinely disabled).
        _tileRadial.enabled = false;

        // Centered countdown text (on top of the radial). Font scales with the tile in OnTick.
        var textGo = UIFactory.CreateUIObject("ShiftCountdown", _tileGo);
        StretchFill(textGo.GetComponent<RectTransform>(), 0f);
        _tileText = textGo.AddComponent<TextMeshProUGUI>();
        _tileText.alignment = TextAlignmentOptions.Center;
        _tileText.fontSize  = 22;
        _tileText.fontStyle = FontStyles.Bold;
        _tileText.color     = Color.white;
        _tileText.enableWordWrapping = false;
        _tileText.overflowMode = TextOverflowModes.Overflow;
        _tileText.raycastTarget = false;
        _tileText.outlineWidth = 0.25f;
        _tileText.outlineColor = new Color32(0, 0, 0, 220);
        _tileText.text = "—";

        // "Shift" keybind on a strip across the bottom ~26% of the tile (like a slot's key hint).
        var keyStripGo = UIFactory.CreateUIObject("ShiftKeyStrip", _tileGo);
        var ksRt = keyStripGo.GetComponent<RectTransform>();
        ksRt.anchorMin = new Vector2(0f, 0f);
        ksRt.anchorMax = new Vector2(1f, 0.28f);
        ksRt.offsetMin = new Vector2(2.5f, 2.5f);
        ksRt.offsetMax = new Vector2(-2.5f, 0f);
        _keyStripBg = keyStripGo.AddComponent<Image>();
        _keyStripBg.color = KEYBIND_STRIP;
        _keyStripBg.raycastTarget = false;
        var keyLblGo = UIFactory.CreateUIObject("ShiftKeyLabel", keyStripGo);
        StretchFill(keyLblGo.GetComponent<RectTransform>(), 0f);
        _labelText = keyLblGo.AddComponent<TextMeshProUGUI>();
        _labelText.alignment = TextAlignmentOptions.Center;
        _labelText.fontSize  = 11;
        _labelText.fontStyle = FontStyles.Bold;
        _labelText.color     = Color.white;
        _labelText.enableWordWrapping = false;
        _labelText.overflowMode = TextOverflowModes.Overflow;
        _labelText.raycastTarget = false;
        _labelText.outlineWidth = 0.2f;
        _labelText.outlineColor = new Color32(0, 0, 0, 220);
        _labelText.text = "Shift";

        // Charges badge in the top-right corner (only shown when MaxCharges > 1).
        var chargesGo = UIFactory.CreateUIObject("ShiftCharges", _tileGo);
        var chRt = chargesGo.GetComponent<RectTransform>();
        chRt.anchorMin = new Vector2(0.45f, 0.66f);
        chRt.anchorMax = new Vector2(1f, 1f);
        chRt.offsetMin = new Vector2(0f, 0f);
        chRt.offsetMax = new Vector2(-3f, -2f);
        _chargesText = chargesGo.AddComponent<TextMeshProUGUI>();
        _chargesText.alignment = TextAlignmentOptions.TopRight;
        _chargesText.fontSize  = 11;
        _chargesText.fontStyle = FontStyles.Bold;
        _chargesText.color     = new Color(1f, 0.95f, 0.7f, 1f);
        _chargesText.enableWordWrapping = false;
        _chargesText.overflowMode = TextOverflowModes.Overflow;
        _chargesText.raycastTarget = false;
        _chargesText.outlineWidth = 0.2f;
        _chargesText.outlineColor = new Color32(0, 0, 0, 220);
        _chargesText.text = "";
        _chargesText.gameObject.SetActive(false);

        // Diagnostic line (opt-in via Settings.ShiftSpellOverlayShowDiagnostics). A normal ContentRoot
        // child so it's inactive-by-default → takes NO layout space and the tile fills the whole panel;
        // when the tester enables it, it appears below the tile (the tile shares the space). Not tied to
        // the general Diagnostic mode, so it never clutters the overlay during normal play.
        _diagText = UIFactory.CreateLabel(ContentRoot, "ShiftDiag", "",
            TextAlignmentOptions.Center, color: new Color(0.7f, 0.7f, 0.7f, 0.95f),
            fontSize: Theme.ScaledOverlay(9)).TextMesh;
        UIFactory.SetLayoutElement(_diagText.gameObject,
            minWidth: 90, preferredWidth: 120, flexibleWidth: 1,
            minHeight: 14, preferredHeight: 14, flexibleHeight: 0);
        _diagText.fontStyle = FontStyles.Italic;
        _diagText.enableWordWrapping = false;
        _diagText.overflowMode = TextOverflowModes.Overflow;
        _diagText.gameObject.SetActive(false);

        ApplyTileOpacity(Opacity);

        if (_ticker == null)
        {
            _ticker = OnTick;
            BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Add(_ticker);
        }
    }

    // Stretch a child to fill its parent with a uniform inset (px).
    private static void StretchFill(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    // 0.18.4: the panel's own background (what ApplyOpacityToPanel dims) is now fully covered by the
    // tile, so re-apply the transparency setting to the tile's frame / radial / icon / keybind strip
    // here. The interior (_tileBg) alpha is applied per-tick in OnTick (its color flips ready/busy).
    // Text stays fully opaque for legibility (matches the prior behavior — panel opacity never dimmed
    // the digits). Called at construct + whenever Opacity changes (detected in OnTick).
    private void ApplyTileOpacity(float o)
    {
        // _frameImg AND _tileBg colors are owned by OnTick (which picks frame/interior per icon-state
        // and applies o each tick), so they're not set here.
        if (_keyStripBg != null) _keyStripBg.color = WithAlpha(KEYBIND_STRIP, o);
        if (_tileRadial != null && _tileRadial.sprite != null)
        { var c = RADIAL_OVERLAY; c.a *= o; _tileRadial.color = c; }
        if (_tileIcon   != null) { var c = _tileIcon.color; c.a = o; _tileIcon.color = c; }
    }

    // 0.18.4 follow-up: blue appears ONLY before the spell icon is identified (loading / no icon).
    // Once the icon is showing, the frame + interior go dark-neutral so NOTHING tints the spell art —
    // cooldown is conveyed solely by the radial sweep + countdown. (Tester: a blue film stayed over the
    // icon because the frame was blue whenever the cooldown was "ready", and with transparency it bled
    // through the slot.)
    private void SetTileStateColors(bool iconShown, bool ready, float op)
    {
        Color frame, interior;
        if (iconShown) { frame = FRAME_NEUTRAL; interior = SLOT_BG; }       // icon visible → no blue
        else           { frame = interior = ready ? FRAME_READY : FRAME_BUSY; } // placeholder (blue when ready)
        if (_frameImg != null) _frameImg.color = WithAlpha(frame, op);
        if (_tileBg   != null) _tileBg.color   = WithAlpha(interior, op);
    }

    private static Color WithAlpha(Color c, float aMul) => new Color(c.r, c.g, c.b, c.a * aMul);

    private void OnTick()
    {
        if (_tileText == null) return;

        // Service self-throttles to 10 Hz, so this is a no-op most frames even when called every tick.
        ShiftCooldownService.Tick();

        // Re-apply transparency to the tile only when the setting actually changes.
        float op = Opacity;
        if (Mathf.Abs(op - _lastOpacity) > 0.001f) { _lastOpacity = op; ApplyTileOpacity(op); }

        // Scale the countdown + keybind fonts with the tile so the button reads cleanly at any size.
        float h = _tileRt != null ? _tileRt.rect.height : 56f;
        int cdSize = Mathf.Clamp(Mathf.RoundToInt(h * 0.32f), 9, 64);
        int kbSize = Mathf.Clamp(Mathf.RoundToInt(h * 0.16f), 7, 26);
        if (Mathf.Abs(_tileText.fontSize    - cdSize) > 0.5f) _tileText.fontSize    = cdSize;
        if (Mathf.Abs(_labelText.fontSize   - kbSize) > 0.5f) _labelText.fontSize   = kbSize;
        if (Mathf.Abs(_chargesText.fontSize - kbSize) > 0.5f) _chargesText.fontSize = kbSize;

        // Diagnostic line (dedicated cfg flag only; default off → stays out of normal play).
        if (_diagText != null)
        {
            bool showDiag = Settings.ShiftSpellOverlayShowDiagnostics;
            if (_diagText.gameObject.activeSelf != showDiag) _diagText.gameObject.SetActive(showDiag);
            if (showDiag)
                _diagText.text =
                      $"pf {ShiftCooldownService.DiagShiftPrefabHash}  "
                    + $"slot {ShiftCooldownService.DiagSlotGroupPrefabHash}  "
                    + $"ic {(ShiftCooldownService.ShiftIcon != null ? 1 : 0)}  "
                    + $"cg {ShiftCooldownService.DiagCastGroupPrefabHash}  "
                    + $"si {ShiftCooldownService.DiagCastGroupSlotIndex}  "
                    + $"ch {ShiftCooldownService.CurrentCharges}/{ShiftCooldownService.MaxCharges}  "
                    + $"end {ShiftCooldownService.DiagLatchedEnd:F1}  "
                    + $"slotcd {ShiftCooldownService.DiagSlotCooldownEnd:F1}  "
                    + $"srv {ShiftCooldownService.DiagServerNow:F1}  "
                    + $"{ShiftCooldownService.DiagLastReadSource}";
        }

        UpdateIconWidget();
        bool iconShown = _tileIcon != null && _tileIcon.enabled;

        // Charges badge: only when the spell has multiple charges.
        bool showCharges = ShiftCooldownService.HasShiftSpell && ShiftCooldownService.MaxCharges > 1;
        if (_chargesText.gameObject.activeSelf != showCharges) _chargesText.gameObject.SetActive(showCharges);
        if (showCharges)
            _chargesText.text = $"{ShiftCooldownService.CurrentCharges}/{ShiftCooldownService.MaxCharges}";

        if (!ShiftCooldownService.HasShiftSpell)
        {
            _tileText.text   = string.IsNullOrEmpty(ShiftCooldownService.LastError) ? "—" : "n/a";
            SetTileStateColors(iconShown: false, ready: false, op); // muted placeholder, no spell
            if (_tileRadial != null && _tileRadial.enabled) _tileRadial.enabled = false;
            return;
        }

        float remaining = ShiftCooldownService.CooldownRemaining;
        float total     = ShiftCooldownService.CooldownTotal;
        bool ready      = remaining <= 0.05f;

        // Radial fill convention: fillAmount = remaining/total (dark wedge = time left). The cooldown
        // layer is ENABLED only while actually cooling down — otherwise it's disabled (100% transparent)
        // so a ready Shift shows just the icon, like the action bar (no lingering film over the art).
        float fill = total > 0.001f ? Mathf.Clamp01(remaining / total) : 0f;
        bool cooling = !ready && fill > 0.0025f;
        if (_tileRadial != null)
        {
            if (_tileRadial.enabled != cooling) _tileRadial.enabled = cooling;
            if (cooling) _tileRadial.fillAmount = fill;
        }

        // Icon showing → dark neutral tile (no blue); only the placeholder (no icon yet) shows blue.
        SetTileStateColors(iconShown, ready, op);

        if (ready)
            // With the icon showing, a "Ready" label over it is clutter — let the icon read clean and
            // only surface the countdown digits while cooling down (the standard ability-button look).
            _tileText.text = (_tileIcon != null && _tileIcon.enabled) ? "" : "Ready";
        else if (remaining < 10f)
            _tileText.text = $"{remaining:0.0}s";
        else
            _tileText.text = $"{(int)remaining}s";
    }

    // 0.16: keep the slotted-spell icon in sync with ShiftCooldownService. Only (re)assigns the sprite
    // when the slotted spell actually changes — the service resolves + caches the icon, so most ticks
    // this is a no-op. Re-applies the current transparency to a freshly-assigned sprite.
    private void UpdateIconWidget()
    {
        if (_tileIcon == null) return;

        var icon = ShiftCooldownService.ShiftIcon;
        bool show = Settings.ShowShiftSpellIcon
                    && ShiftCooldownService.HasShiftSpell
                    && icon != null;

        if (!show)
        {
            if (_tileIcon.enabled) _tileIcon.enabled = false;
            _tileIconHash = 0;
            return;
        }

        int hash = ShiftCooldownService.ShiftIconPrefabHash;
        if (hash != _tileIconHash)
        {
            _tileIcon.sprite = icon;
            _tileIconHash = hash;
            var c = _tileIcon.color; c.a = Opacity; _tileIcon.color = c;
        }
        if (!_tileIcon.enabled) _tileIcon.enabled = true;
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
