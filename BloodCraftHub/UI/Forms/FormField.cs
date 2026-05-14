using System;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Models;
using TMPro;
using UnityEngine;

namespace BloodCraftHub.UI.Forms;

// Form-field hierarchy used by FormBuilder. Each field is responsible for:
//   1. Building its input widget into a parent row (Build)
//   2. Returning its current value as a string for command-template substitution (GetValueString)
//   3. Optionally validating the value before submit (IsValid)
//
// Adding a new field type = subclass FormField and implement those three.
//
// All fields use Name for {token} substitution in the command template,
// Label for the row's text label, and Tooltip for hover help on the input.
public abstract class FormField
{
    public string Name    { get; set; }
    public string Label   { get; set; }
    public string Tooltip { get; set; }

    public abstract void   Build(GameObject row);
    public abstract string GetValueString();
    public virtual  bool   IsValid() => !string.IsNullOrEmpty(GetValueString());
}

// ---------------------------------------------------------------------------
// TextField - bare TMP_InputField wrapper.
// ---------------------------------------------------------------------------
public class TextField : FormField
{
    public string Placeholder { get; set; } = "";
    protected InputFieldRef Input;

    public TextField() { }
    public TextField(string name, string label, string placeholder = "", string tooltip = null)
    {
        Name = name; Label = label; Placeholder = placeholder; Tooltip = tooltip;
    }

    public override void Build(GameObject row)
    {
        Input = UIFactory.CreateInputField(row, $"Field_{Name}", Placeholder);
        UIFactory.SetLayoutElement(Input.GameObject,
            minWidth: 100, preferredWidth: 200, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        if (!string.IsNullOrEmpty(Tooltip))
            TooltipHover.Attach(Input.GameObject, Tooltip);
    }

    public override string GetValueString() => Input?.Text ?? "";
}

// ---------------------------------------------------------------------------
// IntField - text input that validates as integer in [Min, Max].
// ---------------------------------------------------------------------------
public class IntField : TextField
{
    public int Min { get; set; } = int.MinValue;
    public int Max { get; set; } = int.MaxValue;

    public IntField() { }
    public IntField(string name, string label, int min = int.MinValue, int max = int.MaxValue, string tooltip = null)
        : base(name, label, placeholder: "0", tooltip: tooltip)
    {
        Min = min; Max = max;
    }

    public override bool IsValid()
    {
        if (Input == null || string.IsNullOrEmpty(Input.Text)) return false;
        if (!int.TryParse(Input.Text, out var v)) return false;
        return v >= Min && v <= Max;
    }
}

// ---------------------------------------------------------------------------
// PlayerNameField - free-text input wired to Services.PlayerNameCacheService.
// Autocomplete UI will be a later iteration; this stub already feeds names
// the player types into the cache so future autocomplete can suggest them.
// ---------------------------------------------------------------------------
public class PlayerNameField : TextField
{
    public PlayerNameField() : base() { Placeholder = "Player name"; }
    public PlayerNameField(string name, string label, string tooltip = null)
        : base(name, label, "Player name", tooltip) { }
}

// ---------------------------------------------------------------------------
// EnumField<T> - TMP_Dropdown populated from Enum.GetNames(typeof(T)).
// Value sent to the server is the enum NAME (not the integer index), which is
// what most Bloodcraft / Kindred commands expect (e.g. ".prestige set X Experience 5").
// ---------------------------------------------------------------------------
public class EnumField<T> : FormField where T : struct, Enum
{
    public T Default { get; set; }
    protected TMP_Dropdown Dropdown;
    protected string[] OptionNames;

    public EnumField() { }
    public EnumField(string name, string label, T defaultValue = default, string tooltip = null)
    {
        Name = name; Label = label; Default = defaultValue; Tooltip = tooltip;
    }

    public override void Build(GameObject row)
    {
        OptionNames = Enum.GetNames(typeof(T));
        var go = UIFactory.CreateDropdown(row, $"Field_{Name}", out var dropdown,
            OptionNames.Length > 0 ? OptionNames[0] : "",
            itemFontSize: 13,
            onValueChanged: null,
            defaultOptions: OptionNames);
        Dropdown = dropdown;
        try { Dropdown.value = Convert.ToInt32(Default); }
        catch { Dropdown.value = 0; }

        UIFactory.SetLayoutElement(go,
            minWidth: 100, preferredWidth: 200, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        if (!string.IsNullOrEmpty(Tooltip))
            TooltipHover.Attach(go, Tooltip);
    }

    public override string GetValueString()
    {
        if (Dropdown == null || OptionNames == null || OptionNames.Length == 0) return "";
        int idx = Dropdown.value;
        if (idx < 0 || idx >= OptionNames.Length) return OptionNames[0];
        return OptionNames[idx];
    }
}

// ---------------------------------------------------------------------------
// BoolField - toggle. Emits "true"/"false" for commands that take a boolean arg.
// ---------------------------------------------------------------------------
public class BoolField : FormField
{
    public bool Default { get; set; }
    protected ToggleRef Toggle;

    public BoolField() { }
    public BoolField(string name, string label, bool defaultValue = false, string tooltip = null)
    {
        Name = name; Label = label; Default = defaultValue; Tooltip = tooltip;
    }

    public override void Build(GameObject row)
    {
        Toggle = UIFactory.CreateToggle(row, $"Field_{Name}");
        Toggle.Toggle.isOn = Default;
        Toggle.Text.text = "";
        UIFactory.SetLayoutElement(Toggle.GameObject,
            minWidth: 60, preferredWidth: 80, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        if (!string.IsNullOrEmpty(Tooltip))
            TooltipHover.Attach(Toggle.GameObject, Tooltip);
    }

    public override string GetValueString() => (Toggle?.Toggle != null && Toggle.Toggle.isOn) ? "true" : "false";
    public override bool   IsValid() => Toggle != null;
}
