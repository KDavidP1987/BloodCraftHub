using UnityEngine;
using UnityEngine.UI;
using UIFactory = BloodCraftHub.UI.Framework.UniverseLib.UI.UIFactory;

namespace BloodCraftHub.UI.Framework.CustomLib.Controls;

// 0.9.2/0.9.3: dead-simple inline progress bar for the XP/Prestige/Profession
// progress visualization toggle. Three stacked Image elements inside a slim
// horizontal row — fully-opaque dark "container" + colored fill stretched
// via anchorMax.x + thin outline so the container's right edge is always
// visible against transparent overlay panels.
//
// 0.9.3 visibility fix: the original (0.10, 0.10, 0.10, 0.85) bg with no
// outline blended into transparent overlay backdrops and the user couldn't
// tell where the bar ENDED. Now the bg is fully opaque and outlined.
//
// Why not reuse ProgressBar.cs? That class includes flash animations,
// fade-out timers, alert tooltips, change-text deltas — none of which the
// "show progress as bar" toggle needs.
public static class MiniBar
{
    public const int DefaultHeight = 12;

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

        // Background — fully opaque so the bar's container is visible against
        // any panel transparency. Slightly lighter than pure black so the
        // fill color remains distinguishable when the fill is dark.
        var bg = row.AddComponent<Image>();
        bg.color = new Color(0.07f, 0.07f, 0.07f, 1f);

        // Outline gives the container a clear right-edge marker (matters most
        // when transparency is at 100% and the panel chrome is barely there
        // — without this, "where the bar ends" is invisible).
        var outline = row.AddComponent<Outline>();
        outline.effectColor = new Color(0.55f, 0.55f, 0.55f, 0.9f);
        outline.effectDistance = new Vector2(1f, -1f);

        // Fill — anchored stretched horizontally; sized by anchorMax.x in
        // SetProgress (0.0 = empty, 1.0 = full). Inset by 1px on each side
        // so the fill sits inside the outline border instead of crashing
        // through it.
        var fillObj = UIFactory.CreateUIObject(name + "_Fill", row);
        var rt = fillObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f); // starts empty
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(1f, 0f);
        rt.offsetMin = new Vector2(1f, 1f);
        rt.offsetMax = new Vector2(-1f, -1f);

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
