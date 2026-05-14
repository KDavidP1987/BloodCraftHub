using System;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using TMPro;
using UnityEngine;

namespace BloodCraftHub.UI.Forms;

// Accordion-style section. Click the header to toggle visibility of the
// content GameObject. Headers persist their state in-memory until the panel
// is destroyed.
//
// Usage:
//   CollapsibleSection.Build(parent, "Set player level (.lvl set)",
//       startExpanded: false,
//       content => FormBuilder.Build(content, ..., fields...));
//
// Why this exists: the Admin tab will accumulate many forms once Phase 5e
// migrates the chat-reference list. With all forms expanded the page
// massively overflows the panel's available height (and the parent layout
// group then squishes/overlaps them). Collapsing by default keeps the tab
// compact; users expand only the form they need.
public static class CollapsibleSection
{
    public static GameObject Build(
        GameObject parent,
        string title,
        bool startExpanded,
        Action<GameObject> buildContent,
        string tooltip = null)
    {
        var group = UIFactory.CreateVerticalGroup(parent, $"Collapsible_{title}",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(group,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 28, flexibleHeight: 0);

        // Header button - looks like a heading row with a leading triangle.
        var header = UIFactory.CreateButton(group, "Header", BuildHeaderText(title, startExpanded));
        UIFactory.SetLayoutElement(header.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 26, flexibleHeight: 0);
        var headerText = header.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (headerText != null)
        {
            headerText.enableWordWrapping = false;
            headerText.overflowMode = TextOverflowModes.Overflow;
            headerText.alignment = TextAlignmentOptions.MidlineLeft;
            headerText.fontSize = 13;
            headerText.fontStyle = FontStyles.Bold;
        }
        if (!string.IsNullOrEmpty(tooltip))
            TooltipHover.Attach(header.GameObject, tooltip);

        // Content - hidden when collapsed.
        var content = UIFactory.CreateVerticalGroup(group, "Content",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(8, 4, 4, 8));
        UIFactory.SetLayoutElement(content,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 40, preferredHeight: 80, flexibleHeight: 0);
        content.SetActive(startExpanded);

        // Build user-supplied content into the content GameObject.
        try { buildContent?.Invoke(content); }
        catch (Exception ex) { Utils.LogUtils.LogError($"CollapsibleSection content builder threw: {ex}"); }

        bool expanded = startExpanded;
        header.OnClick = () =>
        {
            expanded = !expanded;
            content.SetActive(expanded);
            if (headerText != null)
                headerText.text = BuildHeaderText(title, expanded);
        };

        return group;
    }

    private static string BuildHeaderText(string title, bool expanded) =>
        expanded ? $"▼  {title}" : $"▶  {title}";
}
