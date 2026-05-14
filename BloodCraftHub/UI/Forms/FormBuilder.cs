using System;
using BloodCraftHub.Services;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.Utils;
using TMPro;
using UnityEngine;

namespace BloodCraftHub.UI.Forms;

// Builds a small "form panel" inside any GameObject parent:
//
//   ┌─────────────────────────────────────────┐
//   │ Title (italic bold)                     │
//   │ [Label1:] [input widget]                │
//   │ [Label2:] [input widget]                │
//   │ ...                                     │
//   │ [Submit]   (right-aligned)              │
//   └─────────────────────────────────────────┘
//
// Submit substitutes {field.Name} tokens in the command template with each
// field's value, validates, then sends through MessageService.EnqueueMessage.
//
// Call sites (e.g. MainPanel.BuildAdminTab) write a single FormBuilder.Build
// per command instead of constructing widgets by hand.
public static class FormBuilder
{
    /// <summary>Build a form into <paramref name="parent"/>. Returns the form's root GameObject.</summary>
    public static GameObject Build(
        GameObject parent,
        string title,
        string commandTemplate,
        params FormField[] fields)
    {
        if (fields == null) fields = Array.Empty<FormField>();

        var form = UIFactory.CreateVerticalGroup(parent, $"Form_{title}",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(4, 4, 4, 4));
        UIFactory.SetLayoutElement(form,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 60, preferredHeight: 80 + fields.Length * 32, flexibleHeight: 0);

        // Title row
        var titleLbl = UIFactory.CreateLabel(form, "FormTitle", title,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: 13);
        UIFactory.SetLayoutElement(titleLbl.GameObject,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        titleLbl.TextMesh.fontStyle = FontStyles.Bold | FontStyles.Italic;
        titleLbl.TextMesh.enableWordWrapping = false;
        titleLbl.TextMesh.overflowMode = TextOverflowModes.Overflow;

        // Field rows: [Label:] [input widget]
        foreach (var field in fields)
        {
            var row = UIFactory.CreateHorizontalGroup(form, $"Row_{field.Name}",
                forceExpandWidth: true, forceExpandHeight: false,
                childControlWidth: true, childControlHeight: true,
                spacing: 6, padding: new Vector4(0, 0, 0, 0));
            UIFactory.SetLayoutElement(row,
                minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
                minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

            var lbl = UIFactory.CreateLabel(row, "Label", field.Label + ":",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: 12);
            UIFactory.SetLayoutElement(lbl.GameObject,
                minWidth: 90, preferredWidth: 100, flexibleWidth: 0,
                minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
            lbl.TextMesh.enableWordWrapping = false;
            lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;

            field.Build(row);
        }

        // Submit row (Submit button right-aligned, status label left-flexes)
        var submitRow = UIFactory.CreateHorizontalGroup(form, "SubmitRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 2, 2));
        UIFactory.SetLayoutElement(submitRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 30, preferredHeight: 32, flexibleHeight: 0);

        var statusLbl = UIFactory.CreateLabel(submitRow, "FormStatus", "",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: 11);
        UIFactory.SetLayoutElement(statusLbl.GameObject,
            minWidth: 100, preferredWidth: 220, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        statusLbl.TextMesh.fontStyle = FontStyles.Italic;
        statusLbl.TextMesh.enableWordWrapping = false;
        statusLbl.TextMesh.overflowMode = TextOverflowModes.Overflow;

        var submit = UIFactory.CreateButton(submitRow, "Submit", "Submit");
        UIFactory.SetLayoutElement(submit.GameObject,
            minWidth: 90, preferredWidth: 110, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var submitText = submit.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (submitText != null)
        {
            submitText.enableWordWrapping = false;
            submitText.overflowMode = TextOverflowModes.Overflow;
            submitText.alignment = TextAlignmentOptions.Center;
            submitText.fontSize = 13;
        }
        TooltipHover.Attach(submit.GameObject, $"Send the command: {commandTemplate}");

        submit.OnClick = () => HandleSubmit(commandTemplate, fields, statusLbl.TextMesh);

        return form;
    }

    private static void HandleSubmit(string template, FormField[] fields, TextMeshProUGUI status)
    {
        // Validate every field; surface the first failure in the status label.
        foreach (var f in fields)
        {
            if (!f.IsValid())
            {
                var msg = $"Invalid: {f.Label}";
                if (status != null) status.text = msg;
                LogUtils.LogWarning($"Form submit blocked - {f.Name} value '{f.GetValueString()}' failed validation.");
                return;
            }
        }

        if (!MessageService.IsInitialized)
        {
            if (status != null) status.text = "Not connected to server yet.";
            return;
        }

        // Token substitution: every {field.Name} in the template becomes the field's value.
        string command = template;
        foreach (var f in fields)
            command = command.Replace("{" + f.Name + "}", f.GetValueString());

        // Feed the player-name cache if any field looks like a player name.
        foreach (var f in fields)
            if (f is PlayerNameField) PlayerNameCacheService.Add(f.GetValueString());

        MessageService.EnqueueMessage(command);
        if (status != null) status.text = $"Sent: {command}";
        LogUtils.LogInfo($"Form sent: {command}");
    }
}
