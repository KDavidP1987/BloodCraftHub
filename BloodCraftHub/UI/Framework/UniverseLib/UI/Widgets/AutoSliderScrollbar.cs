using System;
using System.Collections.Generic;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Models;
using UnityEngine;
using UnityEngine.UI;

namespace BloodCraftHub.UI.Framework.UniverseLib.UI.Widgets;

/// <summary>
/// Tracks every slider created by <see cref="UIFactory.CreateSliderScrollbar"/>
/// so a per-frame click-on-track handler (registered with CoreUpdateBehavior in
/// Plugin.Load) can jump the slider value when the user clicks anywhere on the
/// track. Unity's built-in Slider.OnPointerDown should do this, but our
/// canvas/scrollview hierarchy interferes (only the handle drag works, and the
/// mouse wheel via ScrollRect — clicks on the empty bar area do nothing). This
/// gives users back the standard scrollbar interaction.
/// </summary>
public static class SliderClickRegistry
{
    private static readonly List<Slider> _sliders = new();

    internal static void Register(Slider slider)
    {
        if (slider != null) _sliders.Add(slider);
    }

    /// <summary>Per-frame: if the user just mouse-downed on a registered slider's
    /// track (anywhere outside the handle), set its value to the click position.</summary>
    public static void TickClickOnTrack()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        if (_sliders.Count == 0) return;

        Vector2 mouse = Input.mousePosition;
        for (int i = _sliders.Count - 1; i >= 0; i--)
        {
            var s = _sliders[i];
            if (s == null) { _sliders.RemoveAt(i); continue; }
            if (!s.IsInteractable() || !s.gameObject.activeInHierarchy) continue;

            var sliderRt = s.transform as RectTransform;
            if (sliderRt == null) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(sliderRt, mouse, null)) continue;

            // Skip if click was on the handle itself — Unity's drag handler
            // covers that case, and we don't want to jump the value AND start a
            // drag at the same time.
            var handleRt = s.handleRect;
            if (handleRt != null && RectTransformUtility.RectangleContainsScreenPoint(handleRt, mouse, null))
                continue;

            // Convert mouse to slider-local space and lerp to a 0..1 value.
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(sliderRt, mouse, null, out local))
                continue;

            var rect = sliderRt.rect;
            float t;
            if (s.direction == Slider.Direction.TopToBottom)
                t = Mathf.Clamp01(1f - Mathf.InverseLerp(rect.yMin, rect.yMax, local.y));
            else if (s.direction == Slider.Direction.BottomToTop)
                t = Mathf.Clamp01(Mathf.InverseLerp(rect.yMin, rect.yMax, local.y));
            else if (s.direction == Slider.Direction.LeftToRight)
                t = Mathf.Clamp01(Mathf.InverseLerp(rect.xMin, rect.xMax, local.x));
            else
                t = Mathf.Clamp01(1f - Mathf.InverseLerp(rect.xMin, rect.xMax, local.x));

            s.value = Mathf.Lerp(s.minValue, s.maxValue, t);
        }
    }
}

/// <summary>
/// A scrollbar which automatically resizes itself (and its handle) depending on the size of the content and viewport.
/// </summary>
public class AutoSliderScrollbar : UIBehaviourModel
{
    public override GameObject UIRoot
    {
        get
        {
            if (Slider)
                return Slider.gameObject;
            return null;
        }
    }

    public Slider Slider { get; }
    public Scrollbar Scrollbar { get; }
    public RectTransform ContentRect { get; }
    public RectTransform ViewportRect { get; }

    public AutoSliderScrollbar(Scrollbar scrollbar, Slider slider, RectTransform contentRect, RectTransform viewportRect)
    {
        Scrollbar = scrollbar;
        Slider = slider;
        ContentRect = contentRect;
        ViewportRect = viewportRect;

        Scrollbar.onValueChanged.AddListener(OnScrollbarValueChanged);
        Slider.onValueChanged.AddListener(OnSliderValueChanged);

        Slider.Set(0f, false);

        UpdateSliderHandle();
    }

    private float lastAnchorPosition;
    private float lastContentHeight;
    private float lastViewportHeight;
    private bool _refreshWanted;

    public override void Update()
    {
        if (!Enabled)
            return;

        _refreshWanted = false;
        if (Math.Abs(ContentRect.localPosition.y - lastAnchorPosition) > 0.0001)
        {
            lastAnchorPosition = ContentRect.localPosition.y;
            _refreshWanted = true;
        }
        if (Math.Abs(ContentRect.rect.height - lastContentHeight) > 0.0001)
        {
            lastContentHeight = ContentRect.rect.height;
            _refreshWanted = true;
        }
        if (Math.Abs(ViewportRect.rect.height - lastViewportHeight) > 0.0001)
        {
            lastViewportHeight = ViewportRect.rect.height;
            _refreshWanted = true;
        }

        if (_refreshWanted)
            UpdateSliderHandle();
    }

    public void UpdateSliderHandle()
    {
        // calculate handle size based on viewport / total data height
        float totalHeight = ContentRect.rect.height;
        float viewportHeight = ViewportRect.rect.height;

        if (totalHeight <= viewportHeight)
        {
            Slider.handleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 0f);
            Slider.value = 0f;
            Slider.interactable = false;
            return;
        }

        float handleHeight = viewportHeight * Math.Min(1, viewportHeight / totalHeight);
        handleHeight = Math.Max(15f, handleHeight);

        // resize the handle container area for the size of the handle (bigger handle = smaller container)
        RectTransform container = Slider.m_HandleContainerRect;
        container.offsetMax = new Vector2(container.offsetMax.x, -(handleHeight * 0.5f));
        container.offsetMin = new Vector2(container.offsetMin.x, handleHeight * 0.5f);

        // set handle size
        Slider.handleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, handleHeight);

        // if slider is 100% height then make it not interactable
        Slider.interactable = !Mathf.Approximately(handleHeight, viewportHeight);

        float val = 0f;
        if (totalHeight > 0f)
            val = (float)((decimal)ContentRect.localPosition.y / (decimal)(totalHeight - ViewportRect.rect.height));

        Slider.Set(val);
    }

    public void OnScrollbarValueChanged(float value)
    {
        value = 1f - value;
        // Don't update the value if it is the same, as we don't want to loop setting the values
        if (Math.Abs(Slider.value - value) > 0.0001)
        {
            // Make sure we send the callback to correctly propagate the value
            Slider.Set(value, true);
        }
    }

    public void OnSliderValueChanged(float value)
    {
        value = 1f - value;
        // Don't update the value if it is the same, as we don't want to loop setting the values
        if (Math.Abs(Scrollbar.value - value) > 0.0001)
        {
            // Make sure we send the callback to correctly propagate the value
            Scrollbar.Set(value, true);
        }
    }
}