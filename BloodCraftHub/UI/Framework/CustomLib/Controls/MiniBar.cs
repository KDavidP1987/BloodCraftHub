using UnityEngine;
using UnityEngine.UI;
using UIFactory = BloodCraftHub.UI.Framework.UniverseLib.UI.UIFactory;

namespace BloodCraftHub.UI.Framework.CustomLib.Controls;

// 0.9.2: dead-simple inline progress bar for the XP / Prestige progress
// visualization toggle. Two stacked Image components inside a slim
// horizontal row — a dark background and a colored fill that's stretched
// via anchorMax.x.
//
// Why not reuse ProgressBar.cs? That class includes flash animations,
// fade-out timers, alert tooltips, change-text deltas — none of which the
// "show progress as bar" toggle needs. A thin bar in the overlay HUD
// should be silent and lightweight.
public static class MiniBar
{
    public const int DefaultHeight = 8;

    /// <summary>Create a horizontal bar row. Returns the row GameObject (so
    /// callers can SetActive it) and writes the fill's RectTransform to the
    /// out parameter (so the caller can later stretch it via SetProgress).</summary>
    public static GameObject Create(GameObject parent, string name, out RectTransform fillRect,
                                    Color fillColor, int height = DefaultHeight)
    {
        var row = UIFactory.CreateUIObject(name, parent);
        UIFactory.SetLayoutElement(row,
            minHeight: height, preferredHeight: height,
            flexibleWidth: 1, flexibleHeight: 0);

        // Background — sits beneath the fill. Slightly darker than the
        // panel so the bar is visible even when the panel itself is solid.
        var bg = row.AddComponent<Image>();
        bg.color = new Color(0.10f, 0.10f, 0.10f, 0.85f);

        // Fill — anchored stretched. Width is controlled by anchorMax.x in
        // SetProgress (0.0 = empty, 1.0 = full).
        var fillObj = UIFactory.CreateUIObject(name + "_Fill", row);
        var rt = fillObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f); // starts empty
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;

        var fill = fillObj.AddComponent<Image>();
        fill.color = fillColor;

        fillRect = rt;
        return row;
    }

    /// <summary>Stretch the fill rect's right anchor to the clamped progress
    /// value. Pure anchor manipulation — works at any panel size without
    /// caring about the parent's rect width.</summary>
    public static void SetProgress(RectTransform fillRect, float progress01)
    {
        if (fillRect == null) return;
        var clamped = Mathf.Clamp01(progress01);
        var max = fillRect.anchorMax;
        max.x = clamped;
        fillRect.anchorMax = max;
    }
}
