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

    // B8 (0.19): the main fill and the prestige sub-line fill MUST share identical horizontal insets so
    // they line up width-wise (left edge + right-edge inset) at any progress. They were equal already,
    // but were two separate literals that could drift; these named constants lock them together. The
    // fills can still END at different x because they show DIFFERENT progress values (main = current-level
    // XP %, sub = prestige Level/MaxLevel) — that's by design, not misalignment.
    private const float FillInsetLeftX  = 1f;   // px from the container's left edge (inside the 1px outline)
    private const float FillInsetRightX = -1f;  // px from the progress mark (inside the 1px outline)

    /// <summary>Create a horizontal bar row. Returns the row GameObject (so
    /// callers can SetActive it) and writes the fill's RectTransform to the
    /// out parameter (so the caller can later stretch it via SetProgress).</summary>
    public static GameObject Create(GameObject parent, string name, out RectTransform fillRect,
                                    Color fillColor, int height = DefaultHeight)
        => CreateWithSubLine(parent, name, out fillRect, out _, fillColor, height);

    /// <summary>0.10.7: Create a bar with an OPTIONAL inset sub-line for
    /// secondary progress (e.g. prestige tier toward next tier). The sub-line
    /// fill is a slim ~25%-height strip pinned to the bottom edge of the
    /// container; consumers SetActive its parent ("_SubLine") to show/hide
    /// and call <see cref="SetProgress"/> on the sub fill RT to update.
    /// Both fills use the same Image-fill technique so progress drawing is
    /// consistent. Returns the row container.</summary>
    public static GameObject CreateWithSubLine(GameObject parent, string name,
                                               out RectTransform fillRect, out RectTransform subFillRect,
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

        // Main fill — anchored stretched vertically (full height) and sized
        // by anchorMax.x in SetProgress. Inset by 1px on each side so the
        // fill sits inside the outline border instead of crashing through it.
        var fillObj = UIFactory.CreateUIObject(name + "_Fill", row);
        var rt = fillObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f); // starts empty
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(1f, 0f);
        rt.offsetMin = new Vector2(FillInsetLeftX, 1f);
        rt.offsetMax = new Vector2(FillInsetRightX, -1f);

        var fill = fillObj.AddComponent<Image>();
        fill.color = fillColor;

        fillRect = rt;

        // 0.10.7: prestige sub-line. Slim strip across the bottom 25% of the
        // bar height, slightly lighter than the main fill so it reads as a
        // secondary indicator without competing for attention. Anchored
        // bottom-stretched-horizontal so it scales cleanly when the main bar
        // gets thinner. Initially hidden — consumer activates when needed.
        var subObj = UIFactory.CreateUIObject(name + "_SubLine", row);
        var subRt = subObj.GetComponent<RectTransform>();
        subRt.anchorMin = new Vector2(0f, 0f);
        subRt.anchorMax = new Vector2(0f, 0.30f);
        subRt.pivot = new Vector2(0f, 0.5f);
        subRt.anchoredPosition = new Vector2(1f, 0f);
        // B8: SAME horizontal insets as the main fill so the two align width-wise.
        subRt.offsetMin = new Vector2(FillInsetLeftX, 1f);
        subRt.offsetMax = new Vector2(FillInsetRightX, 0f);

        var subFill = subObj.AddComponent<Image>();
        // Slightly desaturated white-ish overlay so it works for any base fill color.
        subFill.color = new Color(1f, 1f, 1f, 0.55f);

        subObj.SetActive(false);
        subFillRect = subRt;

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

    /// <summary>0.10.7: Resize the bar's container height. Used when the
    /// user toggles Settings.ProgressBarHeight while a bar is already
    /// constructed. Idempotent — safe to call every render.</summary>
    public static void SetHeight(GameObject row, int height)
    {
        if (row == null) return;
        var le = row.GetComponent<UnityEngine.UI.LayoutElement>();
        if (le == null) return;
        if (le.minHeight == height && le.preferredHeight == height) return;
        le.minHeight = height;
        le.preferredHeight = height;
    }
}
