using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BloodCraftHub.UI;

// Static hover-tooltip service. Each attached binding is a (RectTransform, string)
// pair. Every frame TickAll checks the mouse position against each rect; the
// first match writes its tooltip text into the shared Sink (a TextMeshProUGUI
// at the bottom of the MainPanel).
//
// Why not use IPointerEnterHandler/IPointerExitHandler on a subclassed
// MonoBehaviour: under Il2CppInterop those interfaces are exposed as wrapped
// classes, not interfaces, so a managed class can't implement them - the C#
// compiler reports CS1721 "multiple base classes". Polling avoids the issue
// entirely and runs at ~zero cost for the ~30 tooltips we have.
public static class TooltipHover
{
    private struct Binding
    {
        public RectTransform Rt;
        public string Text;
    }

    private static readonly List<Binding> _bindings = new();

    /// <summary>Shared display target. <see cref="ModContent.MainPanel"/> sets this on construction.</summary>
    public static TextMeshProUGUI Sink;

    public static string IdlePlaceholder = "Hover any button for a hint.";

    private static bool _ticking;

    /// <summary>
    /// One-time wiring: registers TickAll with the per-frame loop. Safe to call
    /// multiple times (idempotent).
    /// </summary>
    public static void EnsureTicking()
    {
        if (_ticking) return;
        if (Plugin.CoreUpdateBehavior == null) return;
        Behaviors.CoreUpdateBehavior.Actions.Add(TickAll);
        _ticking = true;
    }

    /// <summary>Attach a tooltip to a UI GameObject. No-op if target lacks a RectTransform.</summary>
    public static void Attach(GameObject target, string text)
    {
        if (target == null || string.IsNullOrEmpty(text)) return;
        var rt = target.GetComponent<RectTransform>();
        if (rt == null) return;
        _bindings.Add(new Binding { Rt = rt, Text = text });
    }

    /// <summary>Per-frame: walks bindings, updates Sink to the topmost hovered tooltip text.</summary>
    public static void TickAll()
    {
        if (Sink == null) return;

        Vector2 mouse = Input.mousePosition;
        string hit = null;

        // Iterate in reverse so removal of stale (destroyed) bindings is O(1)-cheap.
        for (int i = _bindings.Count - 1; i >= 0; i--)
        {
            var b = _bindings[i];
            if (b.Rt == null)
            {
                _bindings.RemoveAt(i);
                continue;
            }
            if (!b.Rt.gameObject.activeInHierarchy) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(b.Rt, mouse, null))
            {
                hit = b.Text;
                // Don't break - children registered after parents end up later in the list,
                // and we want the most-recently-registered (typically deepest/inner) to win.
            }
        }

        Sink.text = hit ?? IdlePlaceholder;
    }
}
