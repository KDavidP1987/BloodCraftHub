using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BloodCraftHub.UI.Framework.CustomLib.Util;
using BloodCraftHub.UI.ModContent.Data;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ButtonRef = BloodCraftHub.UI.Framework.UniverseLib.UI.Models.ButtonRef;
using Object = UnityEngine.Object;

namespace BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;

public abstract class PanelBase : UIBehaviourModel, IPanelBase
{
    public UIBase Owner { get; }
    public abstract PanelType PanelType { get; }

    public abstract string PanelId { get; }

    public abstract int MinWidth { get; }
    public abstract int MinHeight { get; }
    public virtual int MaxWidth { get; }

    public abstract Vector2 DefaultAnchorMin { get; }
    public abstract Vector2 DefaultAnchorMax { get; }
    public virtual Vector2 DefaultPivot => Vector2.one * 0.5f;
    public virtual Vector2 DefaultPosition { get; }
    public virtual float Opacity { get; set; } = 1.0f;

    public virtual bool CanDrag { get; protected set; } = true;
    public virtual PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public PanelDragger Dragger { get; internal set; }

    public override GameObject UIRoot => uiRoot;
    protected GameObject uiRoot;
    public RectTransform Rect { get; private set; }
    public GameObject ContentRoot { get; protected set; }

    public GameObject TitleBar { get; private set; }
    private LabelRef TitleLabel { get; set; }
    public GameObject CloseButton { get; private set; }
    protected Toggle PinPanelToggleControl;

    // 0.16: transient accent frame shown while the cursor is over the
    // drag-to-resize border. Built lazily on first hover. See SetResizeHighlight.
    private GameObject _resizeHighlight;

    // 0.10.14: setter widened to public so the main panel's "Lock
    // overlays" toggle can drive IsPinned on every overlay from one
    // place. Pre-0.10.14 IsPinned was protected-set and only flipped
    // via the dormant per-panel PinPanelToggleControl (which is null
    // in the current overlay implementations) or via ApplySaveData.
    public virtual bool IsPinned { get; set; }

    public PanelBase(UIBase owner)
    {
        Owner = owner;

        ConstructUI();

        // Add to owner
        Owner.Panels.AddPanel(this);
    }

    /// <summary>0.9.2: re-read the current Opacity value and apply it to the
    /// panel's background Image. Lets per-overlay transparency settings take
    /// effect without rebuilding the whole panel. Called from
    /// BCHubUIManager.RefreshOverlayOpacity after the user clicks a
    /// transparency segmented-button on the Settings tab.</summary>
    public void RefreshOpacity()
    {
        UIFactory.ApplyOpacityToPanel(uiRoot, Opacity);
    }

    /// <summary>0.12.0: apply Settings.PanelBackgroundColor to this panel's
    /// structural backgrounds (LayoutGroup-attached Images). No-op when the
    /// panel opts out by keeping <see cref="UsesCustomBackgroundColor"/>=false.
    /// Pushed by BCHubUIManager.RefreshAllPanelBackgrounds on user pick.</summary>
    public void RefreshBackgroundColor()
    {
        if (!UsesCustomBackgroundColor) return;
        UIFactory.ApplyBackgroundColorRgbToPanel(uiRoot, Config.Settings.PanelBackgroundColor);
    }

    /// <summary>0.12.0: apply Settings.InnerPanelBackgroundColor to this
    /// panel's scroll-view wrappers / viewports — the framework-default
    /// red surfaces inside CreateScrollView. No-op when the panel opts
    /// out by keeping <see cref="UsesCustomInnerBackgroundColor"/>=false.</summary>
    public void RefreshInnerBackgroundColor()
    {
        if (!UsesCustomInnerBackgroundColor) return;
        UIFactory.ApplyInnerBackgroundColorToPanel(uiRoot, Config.Settings.InnerPanelBackgroundColor);
    }

    /// <summary>0.12.0: opt-in flag — when true, this panel applies the
    /// user's Settings.PanelBackgroundColorHex preference at construct
    /// time and on live refresh. Pre-0.12.1 only MainPanel + FamiliarBrowserOverlayPanel
    /// opted in; the five info overlays were preserved on default. v0.12.0
    /// friend test wanted the color theme to span every panel — now flipped
    /// true on every overlay too.</summary>
    public virtual bool UsesCustomBackgroundColor => false;

    /// <summary>0.12.0: opt-in flag for the SECOND color picker (Interior
    /// background). True on MainPanel + FamiliarBrowserOverlayPanel only —
    /// the smaller info overlays don't host scroll views worth recoloring.</summary>
    public virtual bool UsesCustomInnerBackgroundColor => false;

    protected void ForceRecalculateBasePanelWidth(List<GameObject> data = null)
    {
        float contentWidth = 0;
        if(data != null)
        {
            foreach (var obj in data)
            {
                var child = obj.GetComponent<RectTransform>();
                LayoutRebuilder.ForceRebuildLayoutImmediate(child);
                var width = LayoutUtility.GetPreferredWidth(child);
                contentWidth = Math.Max(contentWidth, width);
            }
        }
        else
        {
            foreach (var child in uiRoot.transform)
            {
                var childRect = child as RectTransform;
                if (childRect != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(childRect);
                    var width = LayoutUtility.GetPreferredWidth(childRect.GetComponent<RectTransform>());
                    contentWidth = Math.Max(contentWidth, width);
                }
            }
        }

        Rect.sizeDelta = new Vector2(contentWidth, Rect.sizeDelta.y);
    }

    public void SetTitle(string label)
    {
        TitleLabel.TextMesh.SetText(label);
    }

    public override void Destroy()
    {
        Owner.Panels.RemovePanel(this);
        base.Destroy();
    }

    public virtual void OnFinishResize()
    {
    }

    public virtual void OnFinishDrag()
    {
    }

    /// <summary>0.16: show/hide a thin accent frame just inside the panel border
    /// while the cursor is over the drag-to-resize grip. Friend-test feedback was
    /// that the resize edge was invisible and hard to find ("had to click the
    /// perfect spot") — this makes "the edge is draggable" obvious alongside the
    /// directional resize cursor. Built lazily on first hover; purely cosmetic
    /// (raycastTarget off, so it never eats clicks).</summary>
    public void SetResizeHighlight(bool on)
    {
        if (_resizeHighlight == null)
        {
            if (!on) return;            // nothing built yet, nothing to hide
            BuildResizeHighlight();
        }
        if (_resizeHighlight != null && _resizeHighlight.activeSelf != on)
            _resizeHighlight.SetActive(on);
    }

    private void BuildResizeHighlight()
    {
        try
        {
            _resizeHighlight = UIFactory.CreateUIObject("ResizeHighlight", uiRoot);
            // uiRoot carries a VerticalLayoutGroup (UIFactory.CreatePanel). Without
            // ignoreLayout the group overrides our anchors/size and the highlight
            // never appears where we place it — that's why the v0.16.0 glow didn't
            // show in testing. ignoreLayout lets the free anchors below stand.
            var hlLayout = _resizeHighlight.AddComponent<LayoutElement>();
            hlLayout.ignoreLayout = true;
            var rt = _resizeHighlight.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling(); // draw over the panel content

            // Four flat edge strips just INSIDE the panel border (kept inside so
            // they're never clipped by a parent mask). No sprite needed — a
            // sprite-less Image renders a flat colored rect.
            // 0.16.x: softened from the original bright cyan to a semi-transparent
            // warm gold that matches the UI's yellow accent (Theme.Highlight) —
            // friend-test found the bright blue offsetting on hover.
            Color glow = new(0.93f, 0.80f, 0.40f, 0.55f);
            const float t = 3f;
            AddHighlightStrip("HL_Top",    new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -t), new Vector2(0, 0), glow);
            AddHighlightStrip("HL_Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0),  new Vector2(0, t), glow);
            AddHighlightStrip("HL_Left",   new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0),  new Vector2(t, 0), glow);
            AddHighlightStrip("HL_Right",  new Vector2(1, 0), new Vector2(1, 1), new Vector2(-t, 0), new Vector2(0, 0), glow);

            _resizeHighlight.SetActive(false);
        }
        catch
        {
            _resizeHighlight = null; // fall back to no highlight; resize still works
        }
    }

    private void AddHighlightStrip(string name, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, Color c)
    {
        var go = UIFactory.CreateUIObject(name, _resizeHighlight);
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = aMin; r.anchorMax = aMax;
        r.offsetMin = offMin; r.offsetMax = offMax;
        var img = go.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
    }

    public override void SetActive(bool active)
    {
        if (Enabled != active)
            base.SetActive(active);

        if (!active)
        {
            Dragger.WasDragging = false;
        }
        else
        {
            UIRoot.transform.SetAsLastSibling();
            Owner.Panels.InvokeOnPanelsReordered();
        }
    }

    public void SetActiveOnly(bool active)
    {
        if (Enabled != active)
            base.SetActive(active);

        if (!active)
        {
            Dragger.WasDragging = false;
        }
        else
        {
            UIRoot.transform.SetAsLastSibling();
            Owner.Panels.InvokeOnPanelsReordered();
        }
    }

    protected virtual void OnClosePanelClicked()
    {
        SetActive(false);
    }

    // Setting size and position

    public virtual void SetDefaultSizeAndPosition()
    {
        Rect.localPosition = DefaultPosition;
        Rect.pivot = DefaultPivot;

        Rect.anchorMin = DefaultAnchorMin;
        Rect.anchorMax = DefaultAnchorMax;

        LayoutRebuilder.ForceRebuildLayoutImmediate(Rect);

        // 0.11.2: size FIRST, then position. EnsureValidPosition's clamp
        // requires Rect.rect.width/height to be ≤ screen dimensions — if
        // the panel is oversized at this point the old order would throw
        // ArgumentException ("3399.5 cannot be greater than -3399.5") from
        // Math.Clamp. Capping size first guarantees valid bounds before
        // position is computed.
        EnsureValidSize();
        EnsureValidPosition();

        Dragger.OnEndResize();
    }

    public virtual void EnsureValidSize()
    {
        if (Rect.rect.width < MinWidth)
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, MinWidth);
        if (MaxWidth > 0 && Rect.rect.width > MaxWidth)
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, MaxWidth);

        if (Rect.rect.height < MinHeight)
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, MinHeight);

        // 0.11.2: hard screen-size cap. No panel may exceed the canvas — not
        // the main panel, not any overlay, regardless of MaxWidth. Without
        // this clamp, a corrupted save OR the fullscreen-then-auto-resize
        // bug (v0.11.1 friend-test) could blow the panel up bigger than the
        // screen. If the panel is also pinned/locked at that point the user
        // has no way to recover via the UI itself. SetSizeWithCurrentAnchors
        // handles both centered and stretched anchor modes correctly.
        var maxAllowed = GetMaxAllowedSize();
        if (Rect.rect.width  > maxAllowed.x)
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, maxAllowed.x);
        if (Rect.rect.height > maxAllowed.y)
            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,   maxAllowed.y);

        Dragger.OnEndResize();
    }

    /// <summary>0.11.2: largest size a panel is allowed to occupy, derived
    /// from the canvas reference resolution divided by the current UI scale,
    /// with a small margin so the resize-by-edge grip stays inside the
    /// visible canvas. Used as the upper bound in EnsureValidSize.</summary>
    private Vector2 GetMaxAllowedSize()
    {
        float scale = 1f;
        try
        {
            scale = UniversalUI.uiBases.First().Panels.PanelHolder.GetComponent<RectTransform>().localScale.x;
            if (scale <= 0.001f) scale = 1f;
        }
        catch { /* uiBases may be empty mid-init — fall back to scale=1 */ }
        Vector2 dim = Owner.Scaler.referenceResolution / scale;
        const float margin = 10f;
        return new Vector2(
            Math.Max(MinWidth,  dim.x - margin),
            Math.Max(MinHeight, dim.y - margin));
    }

    public virtual void EnsureValidPosition()
    {
        float scale = 1f;
        try
        {
            scale = UniversalUI.uiBases.First().Panels.PanelHolder.GetComponent<RectTransform>().localScale.x;
            if (scale <= 0.001f) scale = 1f;
        }
        catch { /* fall through with scale=1 */ }

        Vector2 pos = Rect.anchoredPosition;
        Vector2 dimensions = Owner.Scaler.referenceResolution / scale;
        float halfW = dimensions.x * 0.5f;
        float halfH = dimensions.y * 0.5f;

        float scaledWidth = Rect.rect.width;
        float scaledHeight = Rect.rect.height;

        float minPosX = -halfW + scaledWidth * 0.5f;
        float maxPosX = halfW - scaledWidth * 0.5f;
        float minPosY = -halfH + scaledHeight * 0.5f;
        float maxPosY = halfH - scaledHeight * 0.5f;

        // 0.11.2: when the panel is larger than the screen on an axis,
        // minPos > maxPos and Math.Clamp throws ArgumentException. In that
        // case the panel can't fit, so we center it on the axis instead.
        // Combined with the screen-size cap in EnsureValidSize, this means
        // an oversized panel always gets shrunk THEN centered — no more
        // un-recoverable "panel covers the whole screen and can't be
        // moved" situations.
        pos.x = (minPosX > maxPosX) ? 0f : Math.Clamp(pos.x, minPosX, maxPosX);
        pos.y = (minPosY > maxPosY) ? 0f : Math.Clamp(pos.y, minPosY, maxPosY);
        Rect.anchoredPosition = pos;
    }

    // UI Construction

    protected abstract void ConstructPanelContent();

    protected virtual PanelDragger CreatePanelDragger() => new(this);

    public virtual void ConstructUI()
    {
        // create core canvas 
        uiRoot = UIFactory.CreatePanel(PanelId, Owner.Panels.PanelHolder, out GameObject contentRoot, opacity: Opacity);
        ContentRoot = contentRoot;
        Rect = uiRoot.GetComponent<RectTransform>();

        UIFactory.SetLayoutElement(ContentRoot, 0, 0, flexibleWidth: 9999, flexibleHeight: 9999);

        // Title bar
        TitleBar = UIFactory.CreateHorizontalGroup(ContentRoot, "TitleBar", false, true, true, true, 2,
            new Vector4(2, 2, 2, 2), Theme.PanelBackground);
        UIFactory.SetLayoutElement(TitleBar, minHeight: 25, flexibleHeight: 0);

        // Title text
        TitleLabel = UIFactory.CreateLabel(TitleBar, "TitleBar", PanelId, TextAlignmentOptions.Center, Theme.DefaultText, outlineWidth: 0.05f, fontSize: 16);
        UIFactory.SetLayoutElement(TitleLabel.GameObject, 50, 25, 9999, 0);

        // close button

        CloseButton = UIFactory.CreateUIObject("CloseHolder", TitleBar);
        UIFactory.SetLayoutElement(CloseButton, minHeight: 25, flexibleHeight: 0, minWidth: 30, flexibleWidth: 9999);
        UIFactory.SetLayoutGroup<HorizontalLayoutGroup>(CloseButton, false, false, true, true, 3, childAlignment: TextAnchor.MiddleRight);
        ButtonRef closeBtn = UIFactory.CreateButton(CloseButton, "CloseButton", "—");
        // Remove the button outline
        Object.Destroy(closeBtn.Component.gameObject.GetComponent<Outline>());
        UIFactory.SetLayoutElement(closeBtn.Component.gameObject, minHeight: 25, minWidth: 25, flexibleWidth: 0);
        closeBtn.Component.colors = new ColorBlock()
        {
            normalColor = Theme.SliderHandle,
            colorMultiplier = 1
        };

        closeBtn.OnClick += OnClosePanelClicked;

        if (!(CanDrag || CanResize > 0)) TitleBar.SetActive(false);
       
        // Panel dragger

        Dragger = CreatePanelDragger();
        Dragger.OnFinishResize += OnFinishResize;
        Dragger.OnFinishDrag += OnFinishDrag;

        // content (abstract)

        ConstructPanelContent();

        // 0.12.0: now that every Image in the panel subtree exists, paint
        // them with the user's chosen colors. No-op for panels that opted
        // out via UsesCustomBackgroundColor / UsesCustomInnerBackgroundColor.
        // Done here in the base so individual panels don't have to remember
        // to call this from ConstructPanelContent.
        RefreshBackgroundColor();
        RefreshInnerBackgroundColor();

        SetDefaultSizeAndPosition();

        CoroutineUtility.StartCoroutine(LateSetupCoroutine());
    }

    private IEnumerator LateSetupCoroutine()
    {
        yield return null;

        LateConstructUI();
    }

    protected virtual void LateConstructUI()
    {
        SetDefaultSizeAndPosition();
    }
}