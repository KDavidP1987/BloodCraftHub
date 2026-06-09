using System;
using BloodCraftHub.UI.Framework.CustomLib.Util;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Models;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AutoSliderScrollbar = BloodCraftHub.UI.Framework.UniverseLib.UI.Widgets.AutoSliderScrollbar;
using ButtonRef = BloodCraftHub.UI.Framework.UniverseLib.UI.Models.ButtonRef;
using ICell = BloodCraftHub.UI.Framework.UniverseLib.UI.Widgets.ScrollView.ICell;
using InputFieldRef = BloodCraftHub.UI.Framework.UniverseLib.UI.Models.InputFieldRef;

namespace BloodCraftHub.UI.Framework.UniverseLib.UI;

/// <summary>
/// Helper class to create Unity uGUI UI objects at runtime, as well as use some custom UniverseLib UI classes such as ScrollPool, InputFieldScroller and AutoSliderScrollbar.
/// </summary>
public static class UIFactory
{
    public static GameObject PlayerHUDCanvas { get; set; }
    public static TMP_FontAsset Font { get; set; }
    public static Material FontMaterial { get; set; }

    internal static Vector2 largeElementSize = new(100, 30);
    internal static Vector2 smallElementSize = new(25, 25);
    internal static Vector2 outlineDistance = new(2, 2);

    /// <summary>
    /// Create a simple UI object with a RectTransform. <paramref name="parent"/> can be null.
    /// </summary>
    public static GameObject CreateUIObject(string name, GameObject parent, Vector2 sizeDelta = default)
    {
        GameObject obj = new(name)
        {
            layer = 5,
            hideFlags = HideFlags.HideAndDontSave,
        };

        if (parent)
        {
            obj.transform.SetParent(parent.transform, false);
        }

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.sizeDelta = sizeDelta;
        return obj;
    }

    internal static void SetDefaultTextValues(TextMeshProUGUI text)
    {
        text.color = Theme.DefaultText;
        text.font = Font;
        text.fontSize = 14;
    }

    internal static void SetDefaultSelectableValues(Selectable selectable)
    {
        Navigation nav = selectable.navigation;
        nav.mode = Navigation.Mode.Explicit;
        selectable.navigation = nav;

        var colourBlock = new ColorBlock()
        {
            normalColor = Theme.SelectableNormal,
            highlightedColor = Theme.SelectableHighlighted,
            pressedColor = Theme.SelectablePressed,
            colorMultiplier = 1
        };

        selectable.colors = colourBlock;
    }


    #region Layout Helpers

    /// <summary>
    /// Get and/or Add a LayoutElement component to the GameObject, and set any of the values on it.
    /// </summary>
    public static LayoutElement SetLayoutElement(GameObject gameObject, int? minWidth = null, int? minHeight = null,
        int? flexibleWidth = null, int? flexibleHeight = null, int? preferredWidth = null, int? preferredHeight = null,
        bool? ignoreLayout = null)
    {
        LayoutElement layout = gameObject.GetComponent<LayoutElement>();
        if (!layout)
            layout = gameObject.AddComponent<LayoutElement>();

        if (minWidth != null)
            layout.minWidth = (int)minWidth;

        if (minHeight != null)
            layout.minHeight = (int)minHeight;

        if (flexibleWidth != null)
            layout.flexibleWidth = (int)flexibleWidth;

        if (flexibleHeight != null)
            layout.flexibleHeight = (int)flexibleHeight;

        if (preferredWidth != null)
            layout.preferredWidth = (int)preferredWidth;

        if (preferredHeight != null)
            layout.preferredHeight = (int)preferredHeight;

        if (ignoreLayout != null)
            layout.ignoreLayout = (bool)ignoreLayout;

        return layout;
    }

    /// <summary>
    /// Get and/or Add a HorizontalOrVerticalLayoutGroup (must pick one) to the GameObject, and set the values on it.
    /// </summary>
    public static T SetLayoutGroup<T>(GameObject gameObject, bool? forceWidth = null, bool? forceHeight = null,
        bool? childControlWidth = null, bool? childControlHeight = null, int? spacing = null, int? padTop = null,
        int? padBottom = null, int? padLeft = null, int? padRight = null, TextAnchor? childAlignment = null)
        where T : HorizontalOrVerticalLayoutGroup
    {
        T group = gameObject.GetComponent<T>();
        if (!group) group = gameObject.AddComponent<T>();

        return SetLayoutGroup(group, forceWidth, forceHeight, childControlWidth, childControlHeight, spacing, padTop,
            padBottom, padLeft, padRight, childAlignment);
    }

    /// <summary>
    /// Set the values on a HorizontalOrVerticalLayoutGroup.
    /// </summary>
    public static T SetLayoutGroup<T>(T group, bool? forceWidth = null, bool? forceHeight = null,
        bool? childControlWidth = null, bool? childControlHeight = null, int? spacing = null, int? padTop = null,
        int? padBottom = null, int? padLeft = null, int? padRight = null, TextAnchor? childAlignment = null)
        where T : HorizontalOrVerticalLayoutGroup
    {
        if (forceWidth != null)
            group.childForceExpandWidth = (bool)forceWidth;
        if (forceHeight != null)
            group.childForceExpandHeight = (bool)forceHeight;
        if (childControlWidth != null)
            group.childControlWidth = (bool)childControlWidth;
        if (childControlHeight != null)
            group.childControlHeight = (bool)childControlHeight;
        if (spacing != null)
            group.spacing = (int)spacing;
        if (padTop != null)
            group.padding.top = (int)padTop;
        if (padBottom != null)
            group.padding.bottom = (int)padBottom;
        if (padLeft != null)
            group.padding.left = (int)padLeft;
        if (padRight != null)
            group.padding.right = (int)padRight;
        if (childAlignment != null)
            group.childAlignment = (TextAnchor)childAlignment;

        return group;
    }

    #endregion


    #region Layout Objects

    /// <summary>
    /// Create a simple UI Object with a VerticalLayoutGroup and an Image component.
    /// <br /><br />See also: <see cref="PanelBase"/>
    /// </summary>
    /// <param name="name">The name of the panel GameObject, useful for debugging purposes</param>
    /// <param name="parent">The parent GameObject to attach this to</param>
    /// <param name="bgColor">The background color of your panel. Defaults to dark grey if null.</param>
    /// <param name="contentHolder">The GameObject which you should add your actual content on to.</param>
    /// <param name="opacity"></param>
    /// <returns>The base panel GameObject (not for adding content to).</returns>
    public static GameObject CreatePanel(string name, GameObject parent, out GameObject contentHolder, Color? bgColor = null, float opacity = 1.0f)
    {
        GameObject panelObj = CreateUIObject(name, parent);
        SetLayoutGroup<VerticalLayoutGroup>(panelObj, true, true, true, true);

        RectTransform rect = panelObj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;

        contentHolder = CreateUIObject("Content", panelObj);
        SetLayoutGroup<VerticalLayoutGroup>(contentHolder, true, true, true, true);

        Image bgImage = contentHolder.AddComponent<Image>();
        bgImage.type = Image.Type.Filled;
        // 0.9.2: actually apply the `opacity` parameter. Pre-0.9.2 the
        // parameter was accepted but immediately discarded — the per-panel
        // Opacity override on overlays had no visible effect because the
        // image kept whatever alpha came packed into Theme.DarkBackground
        // (the static global, ~0.8). Replace the alpha with the caller's
        // requested value so each overlay's user-chosen transparency actually
        // takes effect. Stored as a named child so PanelBase can find it again
        // for live refresh on settings changes.
        var color = bgColor ?? Theme.DarkBackground;
        color.a = opacity;
        bgImage.color = color;

        var panelOutline = contentHolder.AddComponent<Outline>();
        panelOutline.effectColor = Theme.DarkBackground;
        panelOutline.effectDistance = outlineDistance;

        return panelObj;
    }

    /// <summary>0.9.2: re-apply a panel's current Opacity to its background
    /// Image at runtime. Called by overlay panels from RefreshOpacity() after
    /// the user changes a per-overlay transparency setting — without this
    /// the panel keeps the alpha it was constructed with and the setting
    /// only takes effect on next game launch. Walks the content holder for
    /// the Image component added in CreatePanel.</summary>
    public static void ApplyOpacityToPanel(GameObject panelObj, float opacity)
    {
        if (panelObj == null) return;
        var content = panelObj.transform.Find("Content");
        if (content == null) return;
        var img = content.GetComponent<Image>();
        if (img == null) return;
        var c = img.color;
        c.a = opacity;
        img.color = c;
    }

    /// <summary>0.12.0: apply a custom RGB background to every structural
    /// layer of a panel built by CreatePanel + nested CreateVerticalGroup /
    /// CreateHorizontalGroup calls. Preserves each Image's existing alpha
    /// (per-panel transparency continues to flow through ApplyOpacityToPanel).
    ///
    /// Why a walker, not just the outer Content Image: BCH panels are
    /// stacked VerticalLayoutGroup containers, each layer adding its own
    /// Image with Theme.PanelBackground. The child Images completely
    /// cover the outer one, so updating only the outer Content was
    /// visually a no-op — the inner TitleBar / Body / LastResponse /
    /// OverlayFooter / TooltipFooter Images all stayed dark grey.
    ///
    /// Heuristic: an Image is "structural" if its GameObject also hosts
    /// a LayoutGroup (the UIFactory.Create*Group pattern). Buttons hold
    /// an Image + Button + no LayoutGroup; sliders / progress bars use
    /// their own Image without a LayoutGroup; tinted accents and cards
    /// with explicit non-theme colors are addressed below. This catches
    /// every panel-bg layer without disturbing accent visuals.
    ///
    /// Excluded by explicit color check: Images whose current RGB is
    /// noticeably brighter than the theme dark-grey (e.g. CardBackground
    /// 0.13 vs PanelBackground 0.07) — those use deliberate non-bg tones
    /// and stay untouched. The check uses a generous epsilon so a custom
    /// color the user picked previously is still recolored on the next
    /// refresh (we don't track per-Image originals).</summary>
    public static void ApplyBackgroundColorRgbToPanel(GameObject panelObj, Color rgb)
    {
        if (panelObj == null) return;
        foreach (var img in panelObj.GetComponentsInChildren<Image>(true))
        {
            if (img == null) continue;
            if (img.gameObject.GetComponent<LayoutGroup>() == null) continue;

            var c = img.color;
            // Preserve mid-grey "card-style" accents (Theme.CardBackground
            // = ~0.13 grey) so section grouping survives a recolor. The
            // panel base background (Theme.PanelBackground = ~0.07 grey)
            // is BELOW the 0.10 floor so it still recolors. Once the user
            // picks a colored preset the panel-bg Images have channel
            // variation > 0.02 so they no longer look "card-ish" and DO
            // recolor on the next preset click.
            bool isCardish = c.r > 0.10f
                          && c.r < 0.40f
                          && System.Math.Abs(c.r - c.g) < 0.02f
                          && System.Math.Abs(c.g - c.b) < 0.02f
                          && System.Math.Abs(c.r - c.b) < 0.02f;
            if (isCardish) continue;

            img.color = new Color(rgb.r, rgb.g, rgb.b, c.a);
        }
    }

    /// <summary>0.12.0: companion to ApplyBackgroundColorRgbToPanel that
    /// targets the OTHER half of the panel's visible surfaces — the
    /// scroll-view wrapper Images and viewports. CreateScrollView paints
    /// the wrapper bright red (Theme.Level1 — pre-0.12.0 default) and the
    /// viewport dark grey (Theme.ViewportBackground); both lack a
    /// LayoutGroup so the outer walker leaves them untouched. The user-
    /// perceived "red inside the panel" comes from the wrapper, which
    /// shows around the viewport edges.
    ///
    /// Heuristic: an Image is "inner-scroll" if its GameObject hosts a
    /// ScrollRect (scroll-view wrapper) or a Mask (viewport). Excludes
    /// every LayoutGroup-attached Image (those are handled by the outer
    /// walker, so we double-skip to keep responsibilities clean).</summary>
    public static void ApplyInnerBackgroundColorToPanel(GameObject panelObj, Color rgb)
    {
        if (panelObj == null) return;
        foreach (var img in panelObj.GetComponentsInChildren<Image>(true))
        {
            if (img == null) continue;
            var go = img.gameObject;
            if (go.GetComponent<LayoutGroup>() != null) continue;
            bool isScrollWrapper = go.GetComponent<ScrollRect>() != null;
            bool isViewport       = go.GetComponent<Mask>() != null;
            if (!isScrollWrapper && !isViewport) continue;

            var c = img.color;
            img.color = new Color(rgb.r, rgb.g, rgb.b, c.a);
        }
    }

    /// <summary>
    /// Create a VerticalLayoutGroup object with an Image component. Use SetLayoutGroup to create one without an image.
    /// </summary>
    public static GameObject CreateVerticalGroup(GameObject parent, string name, bool forceWidth, bool forceHeight,
        bool childControlWidth, bool childControlHeight, int spacing = 0, Vector4 padding = default, Color? bgColor = null,
        TextAnchor? childAlignment = null, float opacity = 1.0f)
    {
        GameObject groupObj = CreateUIObject(name, parent);

        SetLayoutGroup<VerticalLayoutGroup>(groupObj, forceWidth, forceHeight, childControlWidth, childControlHeight,
            spacing, (int)padding.x, (int)padding.y, (int)padding.z, (int)padding.w, childAlignment);

        groupObj.AddComponent<Image>().color = bgColor ?? Theme.PanelBackground;

        return groupObj;
    }

    /// <summary>
    /// Create a HorizontalLayoutGroup object with an Image component. Use SetLayoutGroup to create one without an image.
    /// </summary>
    public static GameObject CreateHorizontalGroup(GameObject parent, string name, bool forceExpandWidth, bool forceExpandHeight,
        bool childControlWidth, bool childControlHeight, int spacing = 0, Vector4 padding = default, Color? bgColor = null,
        TextAnchor? childAlignment = null, float opacity = 1.0f)
    {
        GameObject groupObj = CreateUIObject(name, parent);

        SetLayoutGroup<HorizontalLayoutGroup>(groupObj, forceExpandWidth, forceExpandHeight, childControlWidth, childControlHeight,
            spacing, (int)padding.x, (int)padding.y, (int)padding.z, (int)padding.w, childAlignment);

        groupObj.AddComponent<Image>().color = bgColor ?? Theme.PanelBackground;

        return groupObj;
    }

    /// <summary>
    /// Create a GridLayoutGroup object with an Image component. 
    /// </summary>
    public static GameObject CreateGridGroup(GameObject parent, string name, Vector2 cellSize, Vector2 spacing, Color? bgColor = null)
    {
        GameObject groupObj = CreateUIObject(name, parent);

        GridLayoutGroup gridGroup = groupObj.AddComponent<GridLayoutGroup>();
        gridGroup.childAlignment = TextAnchor.UpperLeft;
        gridGroup.cellSize = cellSize;
        gridGroup.spacing = spacing;

        groupObj.AddComponent<Image>().color = bgColor ?? Theme.PanelBackground;

        return groupObj;
    }

    #endregion


    #region Control and Graphic Components

    /// <summary>
    /// Create a Text component.
    /// </summary>
    /// <param name="parent">The parent object to build onto</param>
    /// <param name="name">The GameObject name of your label</param>
    /// <param name="defaultText">The default text of the label</param>
    /// <param name="alignment">The alignment of the Text component</param>
    /// <param name="color">The Text color (default is White)</param>
    /// <param name="fontSize">The default font size</param>
    /// <param name="outlineWidth"></param>
    /// <param name="outlineColor"></param>
    /// <returns>Your new Text component</returns>
    public static LabelRef CreateLabel(GameObject parent, string name, string defaultText, TextAlignmentOptions alignment = TextAlignmentOptions.Center,
        Color? color = null, int fontSize = 14, float outlineWidth = 0.15f, Color? outlineColor = null)
    {
        var obj = CreateUIObject(name, parent);
        var textComp = obj.AddComponent<TextMeshProUGUI>();


        textComp.color = color ?? Theme.DefaultText;
        textComp.font = Font;

        textComp.text = defaultText;
        textComp.alignment = alignment;
        textComp.fontSize = fontSize;

        try
        {
            textComp.outlineWidth = outlineWidth;
            textComp.outlineColor = outlineColor ?? Color.black;
        }
        catch (Exception)
        {
            // This can throw if the mod is attempting to run this when exiting the application.
        }

        return new LabelRef
        {
            GameObject = obj,
            TextMesh = textComp
        };
    }

    /// <summary>
    /// Create a ButtonRef wrapper and a Button component, providing only the default Color (highlighted and pressed colors generated automatically).
    /// </summary>
    /// <param name="parent">The parent object to build onto</param>
    /// <param name="name">The GameObject name of your button</param>
    /// <param name="text">The default button text</param>
    /// <param name="normalColor">The base color for your button, with the highlighted and pressed colors generated from this.</param>
    /// <returns>A ButtonRef wrapper for your Button component.</returns>
    public static ButtonRef CreateButton(GameObject parent, string name, string text, Color? normalColor = null)
    {
        // 0.18.4: a button created WITHOUT an explicit color follows the user's
        // Settings.ButtonBackgroundColor (default = the old Theme.SliderFill grey) and is registered
        // as "themed" so the Settings button-color picker can recolor it live. A button created WITH
        // an explicit color (Danger red, etc.) keeps that color and is not registered.
        bool themed = !normalColor.HasValue;
        var baseColour = normalColor ?? ThemedButtonBaseColor();

        var buttonRef = CreateButton(parent, name, text, MakeButtonColorBlock(baseColour));
        if (themed && buttonRef.Component != null)
        {
            if (ThemedButtons.Count > 400) PruneThemedButtons();
            ThemedButtons.Add(buttonRef.Component);
        }
        return buttonRef;
    }

    // 0.18.4: themed-button support. ThemedButtons holds every button that follows the user's button
    // color (created via the no-explicit-color overload above). ApplyThemedButtonColor recolors them
    // all live when the Settings picker changes — no panel rebuild needed (matches the panel-color
    // picker's live UX). Destroyed buttons are pruned lazily (bounded on add + fully on each apply).
    internal static readonly System.Collections.Generic.List<Button> ThemedButtons = new();

    // Current themed base color = the user's RGB with the historical Theme.SliderFill alpha, so the
    // default look is unchanged but the hue is user-controllable.
    private static Color ThemedButtonBaseColor()
    {
        var rgb = BloodCraftHub.Config.Settings.ButtonBackgroundColor;
        return new Color(rgb.r, rgb.g, rgb.b, Theme.SliderFill.a);
    }

    private static ColorBlock MakeButtonColorBlock(Color baseColour) => new ColorBlock()
    {
        normalColor      = baseColour,
        highlightedColor = (baseColour * 1.2f),
        selectedColor    = (baseColour * 1.1f),
        pressedColor     = (baseColour * 0.7f),
        disabledColor    = (baseColour * 0.4f),
        colorMultiplier  = 1,
    };

    private static void PruneThemedButtons()
    {
        for (int i = ThemedButtons.Count - 1; i >= 0; i--)
            if (ThemedButtons[i] == null) ThemedButtons.RemoveAt(i);
    }

    /// <summary>0.18.4: recolor every registered themed button to the current
    /// Settings.ButtonBackgroundColor. Called by the Settings button-color picker (via
    /// BCHubUIManager.RefreshAllButtonColors). Prunes destroyed buttons as it goes.</summary>
    public static void ApplyThemedButtonColor()
    {
        var cb = MakeButtonColorBlock(ThemedButtonBaseColor());
        for (int i = ThemedButtons.Count - 1; i >= 0; i--)
        {
            var b = ThemedButtons[i];
            if (b == null) { ThemedButtons.RemoveAt(i); continue; }
            try { b.colors = cb; }
            catch { ThemedButtons.RemoveAt(i); }
        }
    }

    /// <summary>
    /// Create a ButtonRef wrapper and a Button component.
    /// </summary>
    /// <param name="parent">The parent object to build onto</param>
    /// <param name="name">The GameObject name of your button</param>
    /// <param name="text">The default button text</param>
    /// <param name="colors">The ColorBlock used for your Button component</param>
    /// <returns>A ButtonRef wrapper for your Button component.</returns>
    public static ButtonRef CreateButton(GameObject parent, string name, string text, ColorBlock colors)
    {
        GameObject buttonObj = CreateUIObject(name, parent, smallElementSize);

        GameObject textObj = CreateUIObject("Text", buttonObj);

        // Setting the background to white, so that the colour block can tint it correctly
        Image image = buttonObj.AddComponent<Image>();
        image.type = Image.Type.Sliced;
        image.color = Theme.White;

        var outline = buttonObj.AddComponent<Outline>();
        outline.effectColor = Theme.DarkBackground;
        outline.effectDistance = outlineDistance;

        Button button = buttonObj.AddComponent<Button>();
        SetDefaultSelectableValues(button);

        colors.colorMultiplier = 1;
        button.colors = colors;

        TextMeshProUGUI textComp = textObj.AddComponent<TextMeshProUGUI>();
        textComp.text = text;
        SetDefaultTextValues(textComp);
        textComp.alignment = TextAlignmentOptions.Center;

        RectTransform rect = textObj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;

        SetButtonDeselectListener(button);

        return new ButtonRef(button);
    }

    // Automatically deselect buttons when clicked.
    internal static void SetButtonDeselectListener(Button button)
    {
        button.onClick.AddListener(() =>
        {
            button.OnDeselect(null);
        });
    }

    /// <summary>
    /// Create a Slider control component.
    /// </summary>
    /// <param name="parent">The parent object to build onto</param>
    /// <param name="name">The GameObject name of your slider</param>
    /// <param name="slider">Returns the created Slider component</param>
    /// <returns>The root GameObject for your Slider</returns>
    public static GameObject CreateSlider(GameObject parent, string name, out Slider slider)
    {
        GameObject sliderObj = CreateUIObject(name, parent, smallElementSize);

        GameObject bgObj = CreateUIObject("Background", sliderObj);
        GameObject fillAreaObj = CreateUIObject("Fill Area", sliderObj);
        GameObject fillObj = CreateUIObject("Fill", fillAreaObj);
        GameObject handleSlideAreaObj = CreateUIObject("Handle Slide Area", sliderObj);
        GameObject handleObj = CreateUIObject("Handle", handleSlideAreaObj);

        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.type = Image.Type.Sliced;
        bgImage.color = Theme.PanelBackground;

        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0f, 0.25f);
        bgRect.anchorMax = new Vector2(1f, 0.75f);
        bgRect.sizeDelta = new Vector2(0f, 0f);

        RectTransform fillAreaRect = fillAreaObj.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.25f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.75f);
        fillAreaRect.anchoredPosition = new Vector2(-5f, 0f);
        fillAreaRect.sizeDelta = new Vector2(-20f, 0f);

        Image fillImage = fillObj.AddComponent<Image>();
        fillImage.type = Image.Type.Sliced;
        fillImage.color = Theme.SliderFill;

        fillObj.GetComponent<RectTransform>().sizeDelta = new Vector2(10f, 0f);

        RectTransform handleSlideRect = handleSlideAreaObj.GetComponent<RectTransform>();
        handleSlideRect.sizeDelta = new Vector2(-20f, 0f);
        handleSlideRect.anchorMin = new Vector2(0f, 0f);
        handleSlideRect.anchorMax = new Vector2(1f, 1f);

        Image handleImage = handleObj.AddComponent<Image>();
        handleImage.color = Theme.SliderHandle;

        var outline = handleObj.AddComponent<Outline>();
        outline.effectColor = Theme.DarkBackground;
        outline.effectDistance = outlineDistance;

        handleObj.GetComponent<RectTransform>().sizeDelta = new Vector2(20f, 0f);

        slider = sliderObj.AddComponent<Slider>();
        slider.fillRect = fillObj.GetComponent<RectTransform>();
        slider.handleRect = handleObj.GetComponent<RectTransform>();
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;

        // Start from the engine default so selectedColor / disabledColor / fadeDuration are sane, then
        // override the visible states. (Fix: a bare `new ColorBlock{}` left selectedColor at transparent
        // (0,0,0,0); after a drag the EventSystem SELECTS the slider → the transparent selectedColor tint
        // made the handle vanish until something deselected it. selectedColor must be visible.)
        var colourBlock = ColorBlock.defaultColorBlock;
        colourBlock.normalColor = Theme.SliderNormal;
        colourBlock.highlightedColor = Theme.SliderHighlighted;
        colourBlock.pressedColor = Theme.SliderPressed;
        colourBlock.selectedColor = Theme.SliderHighlighted;
        colourBlock.disabledColor = Theme.SliderNormal;
        colourBlock.colorMultiplier = 1;
        slider.colors = colourBlock;

        return sliderObj;
    }

    /// <summary>
    /// Create a standard Unity Scrollbar component.
    /// </summary>
    /// <param name="parent">The parent object to build onto</param>
    /// <param name="name">The GameObject name of your scrollbar</param>
    /// <param name="scrollbar">Returns the created Scrollbar component</param>
    /// <returns>The root GameObject for your Scrollbar</returns>
    public static GameObject CreateScrollbar(GameObject parent, string name, out Scrollbar scrollbar)
    {
        GameObject scrollObj = CreateUIObject(name, parent, smallElementSize);

        GameObject slideAreaObj = CreateUIObject("Sliding Area", scrollObj);
        GameObject handleObj = CreateUIObject("Handle", slideAreaObj);

        Image scrollImage = scrollObj.AddComponent<Image>();
        scrollImage.type = Image.Type.Sliced;
        scrollImage.color = Theme.DarkBackground;

        Image handleImage = handleObj.AddComponent<Image>();
        handleImage.type = Image.Type.Sliced;
        handleImage.color = Theme.SliderHandle;

        RectTransform slideAreaRect = slideAreaObj.GetComponent<RectTransform>();
        slideAreaRect.sizeDelta = new Vector2(-20f, -20f);
        slideAreaRect.anchorMin = Vector2.zero;
        slideAreaRect.anchorMax = Vector2.one;

        RectTransform handleRect = handleObj.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(20f, 20f);

        scrollbar = scrollObj.AddComponent<Scrollbar>();
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;

        SetDefaultSelectableValues(scrollbar);

        return scrollObj;
    }

    /// <summary>
    /// Create a Toggle control component.
    /// </summary>
    /// <param name="parent">The parent object to build onto</param>
    /// <param name="name">The GameObject name of your toggle</param>
    /// <param name="text">Returns the Text component for your Toggle</param>
    /// <param name="bgColor">The background color of the checkbox</param>
    /// <param name="checkWidth">The width of your checkbox</param>
    /// <param name="checkHeight">The height of your checkbox</param>
    /// <returns>The root GameObject for your Toggle control</returns>
    public static ToggleRef CreateToggle(GameObject parent, string name, Color bgColor = default,
        int checkWidth = 20, int checkHeight = 20, string text = "")
    {
        var result = new ToggleRef();
        // Main obj
        result.GameObject = CreateUIObject(name, parent, smallElementSize);
        SetLayoutGroup<HorizontalLayoutGroup>(result.GameObject, false, false, true, true, 5, 0, 0, 0, 0, childAlignment: TextAnchor.MiddleLeft);
        result.Toggle = result.GameObject.AddComponent<Toggle>();
        result.Toggle.isOn = true;

        // 0.15.0 friend-test v4: third Frame-border attempt. v1 used Unity
        // Outline (4 corner specks — invisible at scale). v2 used HLG-
        // based 1-px Frame (user still couldn't see the border). v3 used
        // HLG-based 2-px Frame with near-white color — STILL invisible per
        // the user's third report.
        //
        // Two root causes diagnosed for v4:
        //
        //   (a) HLG-driven sizing for the Background inside the Frame was
        //       unreliable across the various nested layouts BCH uses.
        //       The Background's flexibleWidth/Height could overrun the
        //       Frame's content area in some HLG contexts, covering the
        //       entire Frame and leaving zero visible border. v4 uses
        //       explicit anchored-stretch positioning (Background pinned
        //       to Frame's edges with offsetMin/Max = border thickness),
        //       which is mathematically guaranteed to leave the border
        //       visible regardless of any parent layout choices.
        //
        //   (b) Unity's Selectable component overwrites targetGraphic.color
        //       every frame to match colors.normalColor (which inherits
        //       panel opacity via Theme.SelectableNormal). At 60% panel
        //       opacity the Background fill rendered at 60% alpha, making
        //       the entire toggle ghost into the panel — and a dark fill
        //       at 60% alpha against a 0.07 panel looks practically
        //       identical to the panel itself. v4 assigns a custom
        //       ColorBlock with FULL-ALPHA colors specifically for
        //       toggles so the fill is opaque at every panel opacity
        //       setting.
        //
        // Hierarchy (anchored throughout, no nested HLGs):
        //
        //   Frame  (LayoutElement 28x28, Image = Theme.ToggleOutline)
        //     └ Background (anchored stretch offset 3 px, Image = inner
        //                    fill — color managed by Selectable but with
        //                    custom ColorBlock so FILL is opaque)
        //         └ Checkmark (anchored stretch offset 3 px, Image)
        //
        // Border ring = 3 px on all four sides. Inner fill area = 22x22,
        // visually identical to the v0.14 checkbox before any border
        // additions. Outer footprint = 28x28, 8 px larger than v0.14 on
        // each side — large enough that the border reads as a clear ring
        // and not a sliver.

        // 0.15.0 friend-test v5: trimmed border from 3 px to 2 px and
        // dropped the extra +2 outer padding from v4. Reader feedback:
        // "borders are now too thick" after the v4 jump. 2 px stays
        // unmistakable on every monitor tested but doesn't dominate the
        // toggle visually. Outer footprint = checkWidth + 4 (e.g. 24×24
        // for the default 20×20 callers), inner fill stays 20×20.
        const int BORDER_PX = 2;
        const int CHECK_INSET_PX = 3;
        int frameW = checkWidth + 2 * BORDER_PX;
        int frameH = checkHeight + 2 * BORDER_PX;

        // Custom ColorBlock — full-alpha brighter slate so the toggle fill
        // is opaque + visible at every panel opacity. Compare to
        // Theme.SelectableNormal (0.2,0.2,0.2, Opacity) which ghosts at
        // low panel opacity. The fill still gets the standard hover/press
        // tint, just from a higher floor so it never becomes invisible.
        var toggleColors = new ColorBlock
        {
            normalColor      = new Color(0.30f, 0.30f, 0.34f, 1.0f),
            highlightedColor = new Color(0.45f, 0.45f, 0.50f, 1.0f),
            pressedColor     = new Color(0.22f, 0.22f, 0.26f, 1.0f),
            selectedColor    = new Color(0.35f, 0.35f, 0.40f, 1.0f),
            disabledColor    = new Color(0.16f, 0.16f, 0.18f, 0.5f),
            colorMultiplier  = 1f,
            fadeDuration     = 0.1f,
        };
        result.Toggle.colors = toggleColors;
        var nav = result.Toggle.navigation;
        nav.mode = Navigation.Mode.Explicit;
        result.Toggle.navigation = nav;

        // Frame — outer border. Locked to frameW x frameH by LayoutElement
        // so the toggle's outer HLG can't squeeze it.
        var frameObj = CreateUIObject("Frame", result.GameObject);
        var frameImage = frameObj.AddComponent<Image>();
        frameImage.color = Theme.ToggleOutline;
        frameImage.raycastTarget = true; // Selectable needs a raycast target on the click area
        SetLayoutElement(frameObj,
            minWidth: frameW, preferredWidth: frameW, flexibleWidth: 0,
            minHeight: frameH, preferredHeight: frameH, flexibleHeight: 0);

        // Background — anchored-stretched to fill Frame minus BORDER_PX
        // on each side. THIS IS THE KEY DIFFERENCE FROM v2/v3: no HLG,
        // no LayoutElement on Background. Anchored stretching is
        // mathematically guaranteed to inset by exactly BORDER_PX
        // regardless of any layout-group quirks.
        var checkBgObj = CreateUIObject("Background", frameObj);
        var bgRect = checkBgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = new Vector2(BORDER_PX, BORDER_PX);
        bgRect.offsetMax = new Vector2(-BORDER_PX, -BORDER_PX);
        var bgImage = checkBgObj.AddComponent<Image>();
        bgImage.color = bgColor == default ? toggleColors.normalColor : bgColor;
        bgImage.raycastTarget = true;

        // Checkmark — anchored-stretched to fill Background minus
        // CHECK_INSET_PX on each side. Visible only when isOn=true (the
        // Toggle component manages this via the graphic property below).
        var checkMarkObj = CreateUIObject("Checkmark", checkBgObj);
        var ckRect = checkMarkObj.GetComponent<RectTransform>();
        ckRect.anchorMin = Vector2.zero;
        ckRect.anchorMax = Vector2.one;
        ckRect.offsetMin = new Vector2(CHECK_INSET_PX, CHECK_INSET_PX);
        ckRect.offsetMax = new Vector2(-CHECK_INSET_PX, -CHECK_INSET_PX);
        var checkImage = checkMarkObj.AddComponent<Image>();
        checkImage.color = Theme.ToggleCheckMark;
        checkImage.raycastTarget = false;

        // Label

        var labelObj = CreateUIObject("Label", result.GameObject);
        result.Text = labelObj.AddComponent<TextMeshProUGUI>();
        result.Text.text = text;
        result.Text.alignment = TextAlignmentOptions.MidlineLeft;
        SetDefaultTextValues(result.Text);
        SetLayoutElement(labelObj, minWidth: 0, flexibleWidth: 0, minHeight: frameH, flexibleHeight: 0);

        // References. targetGraphic = inner Background; Selectable's
        // hover/press tint applies to the fill while the Frame's bright
        // border stays a constant color regardless of state.
        result.Toggle.graphic = checkImage;
        result.Toggle.targetGraphic = bgImage;

        return result;
    }

    /// <summary>
    /// Create a standard InputField control and an InputFieldRef wrapper for it.
    /// </summary>
    /// <param name="parent">The parent object to build onto</param>
    /// <param name="name">The GameObject name of your InputField</param>
    /// <param name="placeHolderText">The placeholder text for your InputField component</param>
    /// <returns>An InputFieldRef wrapper for your InputField</returns>
    public static InputFieldRef CreateInputField(GameObject parent, string name, string placeHolderText)
    {
        GameObject mainObj = CreateUIObject(name, parent);

        Image mainImage = mainObj.AddComponent<Image>();
        mainImage.type = Image.Type.Sliced;
        // Clearly lighter than the panel/form background (Theme.DarkBackground ≈ 0.07) and kept nearly
        // opaque on its OWN so a field reads as an input box even when its panel is dimmed — earlier
        // builds tied the field alpha to the panel and used a darker fill, so fields vanished into the
        // background ("grey text on grey" was unrecognizable as a field).
        mainImage.color = new Color(0.22f, 0.22f, 0.27f, 0.98f);

        // Bright, slightly thicker border so the field's edge is unmistakable at any panel opacity.
        var fieldOutline = mainObj.AddComponent<Outline>();
        fieldOutline.effectColor = new Color(0.72f, 0.72f, 0.80f, 1f);
        fieldOutline.effectDistance = new Vector2(1.5f, -1.5f);

        TMP_InputField inputField = mainObj.AddComponent<TMP_InputField>();
        Navigation nav = inputField.navigation;
        nav.mode = Navigation.Mode.None;
        inputField.navigation = nav;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.interactable = true;
        // Transition.None (not ColorTint): ColorTint MULTIPLIES the fill by the state color whose alpha
        // is the theme Opacity, which dragged the field back to translucent grey-on-grey at lower panel
        // opacity (the recurring "can't tell it's an input field" report). With None, the solid fill +
        // bright outline set above stand on their own, like the chat input field does.
        inputField.transition = Selectable.Transition.None;
        inputField.targetGraphic = mainImage;

        // Visible blinking caret. TMP_InputField defaults to no custom caret color, which renders as an
        // invisible/zero-width caret on these dark fields ("I can't tell where I'm typing"). Force a
        // bright, 2px blinking caret on every BCH field.
        inputField.customCaretColor = true;
        inputField.caretColor = new Color(0.95f, 0.95f, 1f, 1f);
        inputField.caretWidth = 2;
        inputField.caretBlinkRate = 0.85f;
        // Don't SELECT-ALL on focus. TMP_InputField defaults onFocusSelectAll=true, so clicking into a
        // field that ALREADY HAS a value highlights the whole value instead of dropping a blinking caret —
        // and the selection highlight is near-invisible on our dark fill, so it reads as "no cursor."
        // (Empty fields show the caret fine because there's nothing to select — the exact split the user
        // observed: prefilled fields = no caret, empty fields = caret.) With this off, focusing a prefilled
        // field places the blinking caret at the click position like the empty fields do.
        inputField.onFocusSelectAll = false;

        // Focus indicator: brighten + thicken the outline (and lighten the fill a touch) while the field
        // is selected, so it's obvious which field you're typing in. Done via onSelect/onDeselect rather
        // than the framework ColorTint (which faded the fill — see the comment above).
        var normalOutlineCol = fieldOutline.effectColor;
        var normalOutlineDist = fieldOutline.effectDistance;
        var normalFill = mainImage.color;
        var focusOutlineCol = new Color(0.40f, 0.85f, 1f, 1f);
        inputField.onSelect.AddListener((UnityEngine.Events.UnityAction<string>)((string _) =>
        {
            if (fieldOutline != null) { fieldOutline.effectColor = focusOutlineCol; fieldOutline.effectDistance = new Vector2(2f, -2f); }
            mainImage.color = new Color(0.27f, 0.27f, 0.33f, 1f);
            // Engage the keyboard-lock the instant this field is focused (mirrors the chat input's
            // OnChatSelect) so typing can't leak movement/menu/ability keys into the game — instead of
            // relying solely on the AnyFocused() poll, which can miss main-panel fields in nested
            // scroll-view canvases (the TMP isFocused=false quirk). The per-frame TickChatFocus poll
            // remains the authoritative RELEASE (clears it when no BCH field is focused). Gated by the
            // same default-on setting the poll uses.
            try { if (BloodCraftHub.Config.Settings.LockKeyboardInFormFields) BloodCraftHub.Patches.InputSuppression.ChatInputActive = true; } catch { }
            // Force REAL activation. Fields in scroll views / dynamically-rebuilt containers often get
            // SELECTED without ACTIVATING — so isFocused stays false → the caret never renders AND the
            // keyboard-lock poll (AnyFocused → isFocused) doesn't latch, letting keystrokes leak into the
            // game (menus open in the background). Activating makes isFocused true → caret shows + the
            // lock engages. Event-driven (fires once on select), so it can't pin focus the way the
            // reverted per-frame re-activation did (ESC still deselects normally).
            try { if (!inputField.isFocused) inputField.ActivateInputField(); } catch { }
        }));
        inputField.onDeselect.AddListener((UnityEngine.Events.UnityAction<string>)((string _) =>
        {
            if (fieldOutline != null) { fieldOutline.effectColor = normalOutlineCol; fieldOutline.effectDistance = normalOutlineDist; }
            mainImage.color = normalFill;
        }));

        GameObject textArea = CreateUIObject("TextArea", mainObj);
        textArea.AddComponent<RectMask2D>();

        RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        // Left/right inset so the text/placeholder isn't jammed against the border.
        textAreaRect.offsetMin = new Vector2(6f, 0f);
        textAreaRect.offsetMax = new Vector2(-4f, 0f);

        GameObject placeHolderObj = CreateUIObject("Placeholder", textArea);
        TextMeshProUGUI placeholderText = placeHolderObj.AddComponent<TextMeshProUGUI>();
        SetDefaultTextValues(placeholderText);
        placeholderText.text = placeHolderText ?? "...";
        // Explicit readable placeholder grey (Theme.PlaceHolderText = SliderHandle can read dim on the
        // lighter fill). Typed text stays Theme.DefaultText (white) below.
        placeholderText.color = new Color(0.72f, 0.72f, 0.78f, 1f);
        placeholderText.enableWordWrapping = true;
        placeholderText.alignment = TextAlignmentOptions.MidlineLeft;
        placeholderText.fontSize = 14;

        RectTransform placeHolderRect = placeHolderObj.GetComponent<RectTransform>();
        placeHolderRect.anchorMin = Vector2.zero;
        placeHolderRect.anchorMax = Vector2.one;
        placeHolderRect.offsetMin = Vector2.zero;
        placeHolderRect.offsetMax = Vector2.zero;

        inputField.placeholder = placeholderText;

        GameObject inputTextObj = CreateUIObject("Text", textArea);
        TextMeshProUGUI inputText = inputTextObj.AddComponent<TextMeshProUGUI>();
        SetDefaultTextValues(inputText);
        inputText.text = "";
        inputText.color = Theme.DefaultText;
        inputText.enableWordWrapping = true;
        inputText.alignment = TextAlignmentOptions.MidlineLeft;
        inputText.fontSize = 14;

        RectTransform inputTextRect = inputTextObj.GetComponent<RectTransform>();
        inputTextRect.anchorMin = Vector2.zero;
        inputTextRect.anchorMax = Vector2.one;
        inputTextRect.offsetMin = Vector2.zero;
        inputTextRect.offsetMax = Vector2.zero;

        inputField.textComponent = inputText;
        // REQUIRED: TMP_InputField positions its caret + builds its selection-highlight mesh relative to
        // the text VIEWPORT. It was never assigned, so the caret couldn't render (invisible cursor) and
        // selecting text threw a per-frame NullReferenceException. Point it at the masked TextArea.
        inputField.textViewport = textAreaRect;
        inputField.characterLimit = UniversalUI.MAX_INPUTFIELD_CHARS;

        return new InputFieldRef(inputField);
    }

    /// <summary>
    /// Create a standard DropDown control.
    /// </summary>
    /// <param name="parent">The parent object to build onto</param>
    /// <param name="name">The GameObject name of your Dropdown</param>
    /// <param name="dropdown">Returns your created Dropdown component</param>
    /// <param name="defaultItemText">The default displayed text (suggested is 14)</param>
    /// <param name="itemFontSize">The font size of the displayed text</param>
    /// <param name="onValueChanged">Invoked when your Dropdown value is changed</param>
    /// <param name="defaultOptions">Optional default options for the dropdown</param>
    /// <returns>The root GameObject for your Dropdown control</returns>
    public static GameObject CreateDropdown(GameObject parent, string name, out TMP_Dropdown dropdown, string defaultItemText, int itemFontSize,
        Action<int> onValueChanged, string[] defaultOptions = null)
    {
        GameObject dropdownObj = CreateUIObject(name, parent, largeElementSize);

        GameObject labelObj = CreateUIObject("Label", dropdownObj);
        GameObject arrowObj = CreateUIObject("Arrow", dropdownObj);
        GameObject templateObj = CreateUIObject("Template", dropdownObj);
        GameObject viewportObj = CreateUIObject("Viewport", templateObj);
        GameObject contentObj = CreateUIObject("Content", viewportObj);
        GameObject itemObj = CreateUIObject("Item", contentObj);
        GameObject itemBgObj = CreateUIObject("Item Background", itemObj);
        GameObject itemCheckObj = CreateUIObject("Item Checkmark", itemObj);
        GameObject itemLabelObj = CreateUIObject("Item Label", itemObj);

        GameObject scrollbarObj = CreateScrollbar(templateObj, "DropdownScroll", out Scrollbar scrollbar);
        scrollbar.SetDirection(Scrollbar.Direction.BottomToTop, true);

        var scrollbarColours = new ColorBlock()
        {
            normalColor = Theme.DropDownScrollBarNormal,
            highlightedColor = Theme.DropDownScrollbarHighlighted,
            pressedColor = Theme.DropDownScrollbarPressed,
            colorMultiplier = 1
        };
        scrollbar.colors = scrollbarColours;


        RectTransform scrollRectTransform = scrollbarObj.GetComponent<RectTransform>();
        scrollRectTransform.anchorMin = Vector2.right;
        scrollRectTransform.anchorMax = Vector2.one;
        scrollRectTransform.pivot = Vector2.one;
        scrollRectTransform.sizeDelta = new Vector2(scrollRectTransform.sizeDelta.x, 0f);

        TextMeshProUGUI itemLabelText = itemLabelObj.AddComponent<TextMeshProUGUI>();
        SetDefaultTextValues(itemLabelText);
        itemLabelText.alignment = TextAlignmentOptions.MidlineLeft;
        itemLabelText.text = defaultItemText;
        itemLabelText.fontSize = itemFontSize;

        TextMeshProUGUI arrowText = arrowObj.AddComponent<TextMeshProUGUI>();
        SetDefaultTextValues(arrowText);
        arrowText.text = "▼";
        RectTransform arrowRect = arrowObj.GetComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(1f, 0.5f);
        arrowRect.anchorMax = new Vector2(1f, 0.5f);
        arrowRect.sizeDelta = new Vector2(20f, 20f);
        arrowRect.anchoredPosition = new Vector2(-15f, 0f);

        Image itemBgImage = itemBgObj.AddComponent<Image>();
        itemBgImage.color = Theme.SliderFill;

        Toggle itemToggle = itemObj.AddComponent<Toggle>();
        itemToggle.targetGraphic = itemBgImage;
        itemToggle.isOn = true;

        var itemToggleColors = new ColorBlock()
        {
            normalColor = Theme.DropDownToggleNormal,
            highlightedColor = Theme.DropDownToggleHighlighted,
            colorMultiplier = 1
        };
        itemToggle.colors = itemToggleColors;

        itemToggle.onValueChanged.AddListener(_ => { itemToggle.OnDeselect(null); });
        Image templateImage = templateObj.AddComponent<Image>();
        templateImage.type = Image.Type.Sliced;
        templateImage.color = Color.black;

        ScrollRect scrollRect = templateObj.AddComponent<ScrollRect>();
        scrollRect.scrollSensitivity = 35;
        scrollRect.content = contentObj.GetComponent<RectTransform>();
        scrollRect.viewport = viewportObj.GetComponent<RectTransform>();
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        scrollRect.verticalScrollbarSpacing = -3f;

        viewportObj.AddComponent<Mask>().showMaskGraphic = false;

        Image viewportImage = viewportObj.AddComponent<Image>();
        viewportImage.type = Image.Type.Sliced;

        TextMeshProUGUI labelText = labelObj.AddComponent<TextMeshProUGUI>();
        SetDefaultTextValues(labelText);
        labelText.alignment = TextAlignmentOptions.MidlineLeft;

        Image dropdownImage = dropdownObj.AddComponent<Image>();
        // Same lighter shade + outline as input fields so dropdowns are also
        // visibly distinct from the panel background.
        dropdownImage.color = new Color(0.18f, 0.18f, 0.21f, Theme.DarkBackground.a);
        dropdownImage.type = Image.Type.Sliced;

        var dropdownOutline = dropdownObj.AddComponent<Outline>();
        dropdownOutline.effectColor = new Color(0.55f, 0.55f, 0.6f, 0.85f);
        dropdownOutline.effectDistance = new Vector2(1f, -1f);

        dropdown = dropdownObj.AddComponent<TMP_Dropdown>();
        dropdown.targetGraphic = dropdownImage;
        dropdown.template = templateObj.GetComponent<RectTransform>();
        dropdown.captionText = labelText;
        dropdown.itemText = itemLabelText;
        //itemLabelText.text = "DEFAULT";

        dropdown.RefreshShownValue();

        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(10f, 2f);
        labelRect.offsetMax = new Vector2(-28f, -2f);

        RectTransform templateRect = templateObj.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0f, 0f);
        templateRect.anchorMax = new Vector2(1f, 0f);
        templateRect.pivot = new Vector2(0.5f, 1f);
        templateRect.anchoredPosition = new Vector2(0f, 2f);
        // 0.8.2: bumped from 150 → 250 so long enum lists (BloodType, stat
        // indices, KindredCommands player lookups) show ~10 items before
        // needing scroll instead of ~6. Item height stays 25px; if a future
        // release adds a UI-text-scale toggle, this should also scale.
        templateRect.sizeDelta = new Vector2(0f, 250f);

        RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0f, 0f);
        viewportRect.anchorMax = new Vector2(1f, 1f);
        viewportRect.sizeDelta = new Vector2(-18f, 0f);
        viewportRect.pivot = new Vector2(0f, 1f);

        RectTransform contentRect = contentObj.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = new Vector2(0f, 0f);
        contentRect.sizeDelta = new Vector2(0f, 28f);

        RectTransform itemRect = itemObj.GetComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0f, 0.5f);
        itemRect.anchorMax = new Vector2(1f, 0.5f);
        itemRect.sizeDelta = new Vector2(0f, 25f);

        RectTransform itemBgRect = itemBgObj.GetComponent<RectTransform>();
        itemBgRect.anchorMin = Vector2.zero;
        itemBgRect.anchorMax = Vector2.one;
        itemBgRect.sizeDelta = Vector2.zero;

        RectTransform itemLabelRect = itemLabelObj.GetComponent<RectTransform>();
        itemLabelRect.anchorMin = Vector2.zero;
        itemLabelRect.anchorMax = Vector2.one;
        itemLabelRect.offsetMin = new Vector2(20f, 1f);
        itemLabelRect.offsetMax = new Vector2(-10f, -2f);
        templateObj.SetActive(false);

        if (onValueChanged != null)
            dropdown.onValueChanged.AddListener(onValueChanged);

        if (defaultOptions != null)
        {
            foreach (string option in defaultOptions)
                dropdown.options.Add(new TMP_Dropdown.OptionData(option));
        }

        return dropdownObj;
    }


    #endregion


    #region Custom Scroll Components

    /// <summary>
    /// Create a ScrollPool for the <typeparamref name="T"/> ICell. You should call scrollPool.Initialize(handler) after this.
    /// </summary>
    /// <typeparam name="T">The ICell type which will be used for the ScrollPool.</typeparam>
    /// <param name="parent">The parent GameObject which the ScrollPool will be built on to.</param>
    /// <param name="name">The GameObject name for your ScrollPool</param>
    /// <param name="uiRoot">Returns the root GameObject for your ScrollPool</param>
    /// <param name="content">Returns the content GameObject for your ScrollPool (where cells will be populated)</param>
    /// <param name="bgColor">The background color for your ScrollPool. If default, it will be dark grey.</param>
    /// <returns>Your created ScrollPool instance.</returns>
    public static Widgets.ScrollView.ScrollPool<T> CreateScrollPool<T>(GameObject parent, string name, out GameObject uiRoot,
        out GameObject content, Color? bgColor = null) where T : ICell
    {
        GameObject mainObj = CreateUIObject(name, parent, new Vector2(1, 1));
        mainObj.AddComponent<Image>().color = bgColor ?? Theme.DarkBackground;
        SetLayoutGroup<HorizontalLayoutGroup>(mainObj, false, true, true, true);
        SetLayoutElement(mainObj, flexibleHeight: 9999, flexibleWidth: 9999);

        GameObject viewportObj = CreateUIObject("Viewport", mainObj);
        SetLayoutElement(viewportObj, flexibleWidth: 9999, flexibleHeight: 9999);
        RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.pivot = new Vector2(0.0f, 1.0f);
        viewportRect.sizeDelta = new Vector2(0f, 0.0f);
        viewportRect.offsetMax = new Vector2(-10.0f, 0.0f);
        viewportObj.AddComponent<RectMask2D>();
        viewportObj.AddComponent<Image>().color = Theme.ViewportBackground;
        viewportObj.AddComponent<Mask>();

        content = CreateUIObject("Content", viewportObj);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = new Vector2(0f, 0f);
        contentRect.offsetMax = new Vector2(0f, 0f);
        SetLayoutGroup<VerticalLayoutGroup>(content, true, false, true, true, 0, 2, 2, 2, 2, TextAnchor.UpperCenter);
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Slider

        GameObject scrollBarObj = CreateUIObject("AutoSliderScrollbar", mainObj);
        RectTransform scrollBarRect = scrollBarObj.GetComponent<RectTransform>();
        scrollBarRect.anchorMin = new Vector2(1, 0);
        scrollBarRect.anchorMax = Vector2.one;
        scrollBarRect.offsetMin = new Vector2(-25, 0);
        SetLayoutGroup<VerticalLayoutGroup>(scrollBarObj, false, true, true, true);
        scrollBarObj.AddComponent<Image>().color = Theme.PanelBackground;
        scrollBarObj.AddComponent<Mask>().showMaskGraphic = false;

        GameObject hiddenBar = CreateScrollbar(scrollBarObj, "HiddenScrollviewScroller", out Scrollbar hiddenScrollbar);
        hiddenScrollbar.SetDirection(Scrollbar.Direction.BottomToTop, true);

        for (int i = 0; i < hiddenBar.transform.childCount; i++)
        {
            Transform child = hiddenBar.transform.GetChild(i);
            child.gameObject.SetActive(false);
        }

        CreateSliderScrollbar(scrollBarObj, out Slider scrollSlider);

        new AutoSliderScrollbar(hiddenScrollbar, scrollSlider, contentRect, viewportRect);

        // Set up the ScrollRect component

        ScrollRect scrollRect = mainObj.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.verticalScrollbar = hiddenScrollbar;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 35;
        scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;


        // finalize and create ScrollPool

        uiRoot = mainObj;
        Widgets.ScrollView.ScrollPool<T> scrollPool = new(scrollRect);

        return scrollPool;
    }

    /// <summary>
    /// Create a SliderScrollbar, using a Slider to mimic a Scrollbar. This fixes several issues with Unity's Scrollbar implementation.<br/><br/>
    /// 
    /// Note that this will not have any actual functionality. Use this along with an <see cref="AutoSliderScrollbar"/> to automate the functionality.
    /// </summary>
    /// <param name="parent">The parent to create on to.</param>
    /// <param name="slider">Your created Slider component</param>
    /// <returns>The root GameObject for your SliderScrollbar.</returns>
    public static GameObject CreateSliderScrollbar(GameObject parent, out Slider slider)
    {
        GameObject mainObj = CreateUIObject("SliderScrollbar", parent, smallElementSize);
        //mainObj.AddComponent<Mask>().showMaskGraphic = false;
        mainObj.AddComponent<Image>().color = Theme.DarkBackground;

        //GameObject bgImageObj = CreateUIObject("Background", mainObj);
        GameObject handleSlideAreaObj = CreateUIObject("Handle Slide Area", mainObj);
        GameObject handleObj = CreateUIObject("Handle", handleSlideAreaObj);

        RectTransform handleSlideRect = handleSlideAreaObj.GetComponent<RectTransform>();
        handleSlideRect.anchorMin = Vector3.zero;
        handleSlideRect.anchorMax = Vector3.one;
        handleSlideRect.pivot = new Vector3(0.5f, 0.5f);

        Image handleImage = handleObj.AddComponent<Image>();
        handleImage.color = Theme.SliderHandle;

        RectTransform handleRect = handleObj.GetComponent<RectTransform>();
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        SetLayoutElement(handleObj, minWidth: 21, flexibleWidth: 0);

        LayoutElement sliderBarLayout = mainObj.AddComponent<LayoutElement>();
        sliderBarLayout.minWidth = 25;
        sliderBarLayout.flexibleWidth = 0;
        sliderBarLayout.minHeight = 30;
        sliderBarLayout.flexibleHeight = 9999;

        slider = mainObj.AddComponent<Slider>();
        slider.handleRect = handleRect;
        slider.fillRect = handleRect;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.TopToBottom;

        // Register so the per-frame click-on-track handler in
        // BloodCraftHub.UI.Framework.UniverseLib.UI.Widgets.SliderClickRegistry
        // can move the value when the user clicks anywhere on the bar (Unity's
        // built-in OnPointerDown only fires on the handle in our hierarchy).
        Widgets.SliderClickRegistry.Register(slider);

        SetLayoutElement(mainObj, minWidth: 25, flexibleWidth: 0, flexibleHeight: 9999);

        slider.colors = new ColorBlock()
        {
            normalColor = Theme.ScrollbarNormal,
            highlightedColor = Theme.ScrollbarHighlighted,
            pressedColor = Theme.ScrollbarPressed,
            disabledColor = Theme.ScrollbarDisabled,
            colorMultiplier = 1
        };

        return mainObj;
    }

    /// <summary>
    /// Create a ScrollView and a SliderScrollbar for non-pooled content.
    /// </summary>
    /// <param name="parent">The parent GameObject to build on to.</param>
    /// <param name="name">The GameObject name for your ScrollView.</param>
    /// <param name="content">The GameObject for your content to be placed on.</param>
    /// <param name="autoScrollbar">A created AutoSliderScrollbar instance for your ScrollView.</param>
    /// <param name="color">The background color, defaults to grey.</param>
    /// <returns>The root GameObject for your ScrollView.</returns>
    public static GameObject CreateScrollView(GameObject parent, string name, out GameObject content, out AutoSliderScrollbar autoScrollbar,
        Color color = default)
    {
        GameObject mainObj = CreateUIObject(name, parent);
        RectTransform mainRect = mainObj.GetComponent<RectTransform>();
        mainRect.anchorMin = Vector2.zero;
        mainRect.anchorMax = Vector2.one;
        Image mainImage = mainObj.AddComponent<Image>();
        mainImage.type = Image.Type.Filled;
        mainImage.color = color == default ? Theme.Level1 : color;

        SetLayoutElement(mainObj, flexibleHeight: 9999, flexibleWidth: 9999);

        GameObject viewportObj = CreateUIObject("Viewport", mainObj);
        RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.pivot = new Vector2(0.0f, 1.0f);
        viewportRect.offsetMax = new Vector2(-28, 0);
        // Need both <Image> and <Mask> to ensure the viewport masks correctly (even if viewport image isn't visible)
        viewportObj.AddComponent<Image>().color = Theme.ViewportBackground;
        viewportObj.AddComponent<Mask>().showMaskGraphic = false;

        content = CreateUIObject("Content", viewportObj);
        SetLayoutGroup<VerticalLayoutGroup>(content, true, false, true, true, childAlignment: TextAnchor.UpperLeft);
        SetLayoutElement(content, flexibleHeight: 9999);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.pivot = new Vector2(0.0f, 1.0f);
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Slider

        GameObject scrollBarObj = CreateUIObject("AutoSliderScrollbar", mainObj);
        RectTransform scrollBarRect = scrollBarObj.GetComponent<RectTransform>();
        scrollBarRect.anchorMin = new Vector2(1, 0);
        scrollBarRect.anchorMax = Vector2.one;
        scrollBarRect.offsetMin = new Vector2(-25, 0);
        SetLayoutGroup<VerticalLayoutGroup>(scrollBarObj, false, true, true, true);
        scrollBarObj.AddComponent<Image>().color = Theme.PanelBackground;
        scrollBarObj.AddComponent<Mask>().showMaskGraphic = false;

        GameObject hiddenBar = CreateScrollbar(scrollBarObj, "HiddenScrollviewScroller", out Scrollbar hiddenScrollbar);
        hiddenScrollbar.SetDirection(Scrollbar.Direction.BottomToTop, true);

        for (int i = 0; i < hiddenBar.transform.childCount; i++)
        {
            Transform child = hiddenBar.transform.GetChild(i);
            child.gameObject.SetActive(false);
        }

        CreateSliderScrollbar(scrollBarObj, out Slider scrollSlider);

        autoScrollbar = new AutoSliderScrollbar(hiddenScrollbar, scrollSlider, contentRect, viewportRect);

        // Set up the ScrollRect component

        ScrollRect scrollRect = mainObj.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.verticalScrollbar = hiddenScrollbar;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 35;
        scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;

        return mainObj;
    }
    #endregion
}