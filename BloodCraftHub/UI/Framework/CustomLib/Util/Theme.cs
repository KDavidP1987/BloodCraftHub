using System;
using BloodCraftHub.Utils;
using TMPro;
using UnityEngine;

namespace BloodCraftHub.UI.Framework.CustomLib.Util;

public static class Theme
{
    // 0.10.2: helper that reads the OverlayTextAlignment setting and returns
    // the matching TMP alignment constant. Used by every overlay's row-
    // creation path so cycling the setting + rebuilding overlays propagates
    // the new alignment uniformly. Default is MidlineLeft (pre-0.10.2 behavior).
    public static TextAlignmentOptions OverlayMidlineAlignment()
        => BloodCraftHub.Config.Settings.OverlayTextAlignmentSetting == BloodCraftHub.Config.Settings.OverlayAlignment.Right
            ? TextAlignmentOptions.MidlineRight
            : TextAlignmentOptions.MidlineLeft;

    // Base colour palette
    public static Color Level1 {get; private set; }
    public static Color Level2 {get; private set; }
    public static Color Level3 {get; private set; }
    public static Color Level4 {get; private set; }
    public static Color Level5 { get; private set; }

    // Colour constants
    public static Color DefaultBar = Level4;
    public static Color Highlight = Color.yellow;
    public static Color PositiveChange = Color.yellow;
    public static Color NegativeChange = Color.red;

    public static Color DarkBackground        {get; private set; }
    public static Color PanelBackground       {get; private set; }
    public static Color SliderFill            {get; private set; }
    public static Color SliderHandle          {get; private set; }
    public static Color SliderNormal          {get; private set; }
    public static Color SliderHighlighted     {get; private set; }
    public static Color SliderPressed         {get; private set; }
    public static Color DefaultText           {get; private set; }
    public static Color PlaceHolderText       {get; private set; }
    public static Color SelectableNormal      {get; private set; }
    public static Color SelectableHighlighted {get; private set; }
    public static Color SelectablePressed     {get; private set; }
    public static Color ScrollbarNormal       {get; private set; }
    public static Color ScrollbarHighlighted  {get; private set; }
    public static Color ScrollbarPressed      {get; private set; }
    public static Color ScrollbarDisabled     {get; private set; }
    public static Color InputFieldNormal      {get; private set; }
    public static Color InputFieldHighlighted {get; private set; }
    public static Color InputFieldPressed     {get; private set; }
    public static Color ViewportBackground    {get; private set; }
    public static Color White                 {get; private set; }
    public static Color ToggleNormal          {get; private set; }
    public static Color ToggleCheckMark { get; private set; }
    // 0.15.0: 1-px outline applied to the toggle checkbox so it remains
    // visible against the panel background on low-contrast monitors.
    // Applied in UIFactory.CreateToggle as an UI.Outline component.
    public static Color ToggleOutline { get; private set; }

    public static Color DropDownScrollBarNormal     {get; private set; }
    public static Color DropDownScrollbarHighlighted{get; private set; }
    public static Color DropDownScrollbarPressed    {get; private set; }
    public static Color DropDownToggleNormal        {get; private set; }
    public static Color DropDownToggleHighlighted { get; private set; }

    // 0.10.9: visual-polish palette.
    //
    // CardBackground is a subtle inset background applied behind grouped
    // sections via UIFactory.AddCard. Pre-0.10.9 every tab was a single
    // wall of labels on the dark-grey panel background — the user
    // described it as "basic HTML on a red background." The card surface
    // is ~50% lighter than PanelBackground but stays well below
    // SelectableHighlighted so it reads as "section grouping" rather
    // than "interactable element."
    //
    // MutedBody is for prose-hint labels (italic body text that explains
    // what a section does). Muted enough to recede behind the bright
    // primary labels but legible against PanelBackground.
    //
    // AccentMono is the inline-code color for command-name spans like
    // `.wep cst` inside body text — picks a faint gold/cyan so they
    // visually pop as "look here, this is the literal command" while
    // staying readable in any TMPro fallback font (we ship no monospace
    // font; the color does the emphasis work instead).
    //
    // DividerLine is for 1-px AddDivider hairlines between logical
    // groups. Low-opacity so it reads as a subtle separator, not chrome.
    //
    // SystemTint* are 5%-opacity washes that color-code the four
    // progression systems (XP / Legacy / Expertise / Familiar) on the
    // Prestige and Levels tabs. The bright base tones come from Level1-5
    // above; these are intentionally dim so labels stay readable on top.
    public static Color CardBackground { get; private set; }
    public static Color MutedBody      { get; private set; }
    public static Color AccentMono     { get; private set; }
    public static Color DividerLine    { get; private set; }
    public static Color SystemTintXP        { get; private set; }
    public static Color SystemTintLegacy    { get; private set; }
    public static Color SystemTintExpertise { get; private set; }
    public static Color SystemTintFamiliar  { get; private set; }
    public static Color SystemTintProfession{ get; private set; }
    public static Color SystemTintQuest     { get; private set; }

    /// <summary>0.10.9: hex string for AccentMono usable inside TMPro
    /// `&lt;color=#...&gt;` rich-text spans. Avoids per-callsite hex
    /// duplication — change here, propagate everywhere.</summary>
    public const string AccentMonoHex = "#9AC8D9"; // muted cyan, legible on dark grey
    public const string MutedBodyHex  = "#8E8E8E"; // mid-grey, recedes vs DefaultText

    private static float _opacity;
    public static float Opacity
    {
        get => _opacity;
        set
        {
            _opacity = value;
            UpdateColors();
        }
    }

    // 0.9.0: explicit two-axis font scale. The UI multiplier scales fontSize
    // values on the main panel (forms, tabs, headers); the overlay multiplier
    // is independent so the player can have small overlays + large panel text
    // or vice versa. Each scale is applied at label-construction time via the
    // ScaledUI / ScaledOverlay helpers below — changing the multiplier at
    // runtime does not retroactively resize already-built labels, so the user
    // needs to close + reopen the panel (or toggle an overlay) for the change
    // to take effect. Live re-render would require a full panel rebuild path,
    // which is more invasive than this feature warrants.
    public static float UIFontMultiplier { get; set; } = 1.0f;
    public static float OverlayFontMultiplier { get; set; } = 1.0f;

    /// <summary>Scale a baseline UI font size by the current UIFontMultiplier.
    /// Rounded to int so TMP doesn't render sub-pixel mismatches across
    /// adjacent labels.</summary>
    public static int ScaledUI(int baseSize) =>
        UnityEngine.Mathf.Max(8, UnityEngine.Mathf.RoundToInt(baseSize * UIFontMultiplier));

    /// <summary>Scale a baseline overlay font size by the current
    /// OverlayFontMultiplier. Separate axis from ScaledUI so overlays can be
    /// adjusted without changing the main panel.</summary>
    public static int ScaledOverlay(int baseSize) =>
        UnityEngine.Mathf.Max(8, UnityEngine.Mathf.RoundToInt(baseSize * OverlayFontMultiplier));

    // 0.11.0: layout-height helpers. Font scaling alone clips labels when
    // the multiplier crosses ~1.3 — the text grows but the row's minHeight
    // doesn't. ScaledHeight / ScaledOverlayHeight scale layout heights in
    // lockstep with the font multiplier so cards / labels / buttons stay
    // legible at X-Large (1.5×) without manually retuning every layout
    // value. Applied inside the shared helpers (AddInfoLabel, AddCard,
    // AddSectionHeading, AddBodyText) so the dominant cases are covered;
    // tab-local custom layouts may still need per-tab tweaks at X-Large.
    //
    // Scaling stays at 1.0 below Standard so Small/Standard layouts are
    // pixel-identical to pre-0.11.0; only Large+ stretches heights.
    public static int ScaledHeight(int baseHeight) =>
        UnityEngine.Mathf.Max(baseHeight,
            UnityEngine.Mathf.RoundToInt(baseHeight * UnityEngine.Mathf.Max(1.0f, UIFontMultiplier)));

    public static int ScaledOverlayHeight(int baseHeight) =>
        UnityEngine.Mathf.Max(baseHeight,
            UnityEngine.Mathf.RoundToInt(baseHeight * UnityEngine.Mathf.Max(1.0f, OverlayFontMultiplier)));

    // 0.24.8: width counterpart to ScaledHeight, for fixed-width buttons whose
    // captions scale with the UI font. Without it, a Large+ font multiplier
    // grows the text but not the button, and TMP word-wraps short captions
    // VERTICALLY (one letter per line) — first reported on the Loadout
    // assign-row slot buttons. Same convention: 1.0 below Standard so
    // Small/Standard layouts stay pixel-identical; only Large+ widens.
    public static int ScaledWidth(int baseWidth) =>
        UnityEngine.Mathf.Max(baseWidth,
            UnityEngine.Mathf.RoundToInt(baseWidth * UnityEngine.Mathf.Max(1.0f, UIFontMultiplier)));

    static Theme()
    {
        Opacity = 0.8f;
    }

    private static void UpdateColors()
    {
        PanelBackground = new Color(0.07f, 0.07f, 0.07f, Opacity);
        DarkBackground = new Color(0.07f, 0.07f, 0.07f, Opacity);
        SliderFill = new Color(0.3f, 0.3f, 0.3f, Opacity);
        SliderHandle = new Color(0.5f, 0.5f, 0.5f, Opacity);
        SliderNormal = new Color(0.4f, 0.4f, 0.4f, Opacity);
        SliderHighlighted = new Color(0.55f, 0.55f, 0.55f, Opacity);
        SliderPressed = new Color(0.3f, 0.3f, 0.3f, Opacity);
        SelectableNormal = new Color(0.2f, 0.2f, 0.2f, Opacity);
        SelectableHighlighted = new Color(0.3f, 0.3f, 0.3f, Opacity);
        SelectablePressed = new Color(0.15f, 0.15f, 0.15f, Opacity);
        DefaultText = Color.white;
        White = Color.white;
        PlaceHolderText = SliderHandle;

        InputFieldNormal = new Color(1f, 1f, 1f, Opacity);
        InputFieldHighlighted = new Color(0.95f, 0.95f, 0.95f, Opacity);
        InputFieldPressed = new Color(0.78f, 0.78f, 0.78f, Opacity);
        // 0.15.0: lifted toggle background from pure black to a dark slate.
        // Pre-0.15 was (0,0,0,Opacity) which rendered as a "void hole" on
        // a 0.07 panel — friend-test surfaced users on certain laptop
        // monitors literally couldn't see the checkbox at all even with
        // a check mark drawn on top. Paired with the Frame-image border
        // added in UIFactory.CreateToggle, the checkbox now reads as a
        // tangible button on every monitor tested.
        ToggleNormal = new Color(0.18f, 0.18f, 0.21f, Opacity);
        // 0.15.0 friend-test v3: bumped to near-white (0.92,0.92,0.95).
        // v1 used Unity Outline at low alpha — invisible at checkbox
        // scale. v2 used a 1-px Frame Image at (0.78,0.78,0.82) — still
        // too subtle on the user's monitor. v3 doubles the border to
        // 2 px (UIFactory.CreateToggle now uses HLG padding 2 + grows
        // the outer Frame by 4 px so the inner fill stays close to the
        // v0.14 visible size) and runs the color at near-white full
        // alpha. The result is an unmistakable bright ring on every
        // monitor at every panel-opacity setting.
        ToggleOutline = new Color(0.92f, 0.92f, 0.95f, 1.0f);
        ToggleCheckMark = new Color(0.6f, 0.7f, 0.6f, Opacity);
        ViewportBackground = new Color(0.07f, 0.07f, 0.07f, Opacity);
        ScrollbarNormal = new Color(0.4f, 0.4f, 0.4f, Opacity);
        ScrollbarHighlighted = new Color(0.5f, 0.5f, 0.5f, Opacity);
        ScrollbarPressed = new Color(0.3f, 0.3f, 0.3f, Opacity);
        ScrollbarDisabled = new Color(0.5f, 0.5f, 0.5f, Opacity);

        Level1 = new Color(0.64f, 0, 0, Opacity);
        Level2 = new Color(0.72f, 0.43f, 0, Opacity);
        Level3 = new Color(1, 0.83f, 0.45f, Opacity);
        Level4 = new Color(0.47f, 0.74f, 0.38f, Opacity);
        Level5 = new Color(0.18f, 0.53f, 0.67f, Opacity);

        DropDownScrollBarNormal = new Color(0.45f, 0.45f, 0.45f, Opacity);
        DropDownScrollbarHighlighted = new Color(0.6f, 0.6f, 0.6f, Opacity);
        DropDownScrollbarPressed = new Color(0.4f, 0.4f, 0.4f, Opacity);
        DropDownToggleNormal = new Color(0.35f, 0.35f, 0.35f, Opacity);
        DropDownToggleHighlighted = new Color(0.25f, 0.25f, 0.25f, Opacity);

        // 0.10.9 visual-polish colors. All tied to Opacity so they obey
        // the same transparency curve as the rest of the panel chrome.
        CardBackground = new Color(0.13f, 0.13f, 0.13f, Opacity);
        MutedBody      = new Color(0.56f, 0.56f, 0.56f, 1f);   // body text — full alpha so it doesn't get re-multiplied by panel transparency
        AccentMono     = new Color(0.60f, 0.78f, 0.85f, 1f);   // muted cyan for inline-command emphasis
        DividerLine    = new Color(0.40f, 0.40f, 0.40f, 0.55f * Opacity);

        // System-progress tints — drawn from the Level1..Level5 base
        // palette, then multiplied down to ~5% alpha so the wash sits
        // BEHIND text without competing for attention.
        Color Wash(float r, float g, float b) => new Color(r, g, b, 0.06f * Opacity);
        SystemTintXP        = Wash(0.18f, 0.53f, 0.67f); // cyan-blue (Level5)
        SystemTintLegacy    = Wash(0.64f, 0.10f, 0.10f); // crimson  (Level1)
        SystemTintExpertise = Wash(0.92f, 0.55f, 0.18f); // copper   (Level2-ish)
        SystemTintFamiliar  = Wash(0.55f, 0.30f, 0.65f); // violet
        SystemTintProfession= Wash(0.30f, 0.70f, 0.35f); // green    (Level4)
        SystemTintQuest     = Wash(0.85f, 0.75f, 0.30f); // gold
    }
}