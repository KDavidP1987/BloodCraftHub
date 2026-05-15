using System;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Models;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using BloodCraftHub.UI.Framework.CustomLib.Util;

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
// FormDropdownRegistry - non-generic registry of every active TMP_Dropdown
// created by an EnumField<T>. Lifted out of EnumField<T> because static fields
// on a generic class are per-T (so each EnumField<BloodType>, EnumField<Foo>,
// etc. would have its own list and the per-frame outside-click checker would
// only see dropdowns of one specific T).
// ---------------------------------------------------------------------------
public static class FormDropdownRegistry
{
    private static readonly System.Collections.Generic.List<TMP_Dropdown> _active = new();

    internal static void Register(TMP_Dropdown dd)
    {
        if (dd != null) _active.Add(dd);
    }

    // Reusable scratch list for EventSystem.RaycastAll. V Rising runs IL2CPP,
    // so the RaycastAll overload expects Il2CppSystem.Collections.Generic.List —
    // a managed System.Collections.Generic.List won't satisfy the bridge.
    // Reusing one list across frames also avoids per-click allocations.
    private static readonly Il2CppSystem.Collections.Generic.List<RaycastResult> _raycastHits
        = new Il2CppSystem.Collections.Generic.List<RaycastResult>();

    /// <summary>Per-frame: close any open dropdown when the user clicks outside it.
    /// Registered with CoreUpdateBehavior in Plugin.Load.
    ///
    /// 0.8.2: switched from rect-containment to EventSystem.RaycastAll + ancestry
    /// check. The old approach checked whether the click landed inside the
    /// "Dropdown List" RectTransform, which excluded the scrollbar — clicking
    /// the scrollbar handle counted as "outside" and dismissed the dropdown
    /// mid-scroll. The raycast approach asks "did the click hit anything
    /// rendered by this dropdown's hierarchy?", which catches the scrollbar,
    /// its handle, the items, and any future child widgets.</summary>
    public static void TickCloseOnOutsideClick()
    {
        if (!UnityEngine.Input.GetMouseButtonDown(0)) return;
        if (_active.Count == 0) return;

        var es = EventSystem.current;
        Vector2 mouse = UnityEngine.Input.mousePosition;

        // Raycast once; reuse the hit list across every open dropdown this frame.
        _raycastHits.Clear();
        if (es != null)
        {
            var ped = new PointerEventData(es) { position = mouse };
            es.RaycastAll(ped, _raycastHits);
        }

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var dd = _active[i];
            if (dd == null) { _active.RemoveAt(i); continue; }

            // The runtime-spawned options panel is a child of the dropdown
            // named "Dropdown List" while expanded; absent when collapsed.
            var listTransform = dd.transform.Find("Dropdown List");
            if (listTransform == null) continue;

            // Pass 1: ancestry check via raycast. If the user clicked anything
            // rendered as a descendant of the dropdown or its popup, leave it
            // open. This covers the scrollbar + handle + items naturally.
            bool hitDropdown = false;
            for (int h = 0; h < _raycastHits.Count; h++)
            {
                var go = _raycastHits[h].gameObject;
                if (go == null) continue;
                var t = go.transform;
                while (t != null)
                {
                    if (t == dd.transform || t == listTransform) { hitDropdown = true; break; }
                    t = t.parent;
                }
                if (hitDropdown) break;
            }
            if (hitDropdown) continue;

            // Pass 2: fall back to rect-containment in case the canvas isn't
            // raycast-registered (rare but possible during scene transitions).
            var listRt = listTransform as RectTransform;
            var ddRt   = dd.transform as RectTransform;
            bool overList = listRt != null && UnityEngine.RectTransformUtility.RectangleContainsScreenPoint(listRt, mouse, null);
            bool overDd   = ddRt   != null && UnityEngine.RectTransformUtility.RectangleContainsScreenPoint(ddRt,   mouse, null);
            if (!overList && !overDd) dd.Hide();
        }
    }
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
            itemFontSize: Theme.ScaledUI(13),
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

        FormDropdownRegistry.Register(dropdown);
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
// EnumIndexField<T> - same dropdown UX as EnumField<T>, but emits the 1-based
// dropdown index (e.g. "5") instead of the enum NAME (e.g. "PhysicalPower").
//
// Bloodcraft's `.wep cst <Weapon> <StatIndex>` and `.bl cst <Blood> <StatIndex>`
// commands use a 1-based integer for the stat picker (the command body does
// `--statType` to convert it back to 0-based). Wrapping the dropdown lets the
// user pick a NAMED stat while the form template still substitutes the integer
// the server expects. Pair with picker enums (WeaponBonusStat / BloodBonusStat)
// that mirror Bloodcraft's enum order so dropdown index 0+1=1 maps to enum 1.
// ---------------------------------------------------------------------------
public class EnumIndexField<T> : EnumField<T> where T : struct, Enum
{
    public EnumIndexField() : base() { }
    public EnumIndexField(string name, string label, T defaultValue = default, string tooltip = null)
        : base(name, label, defaultValue, tooltip) { }

    public override string GetValueString()
    {
        if (Dropdown == null || OptionNames == null || OptionNames.Length == 0) return "";
        return (Dropdown.value + 1).ToString();
    }
}

// ---------------------------------------------------------------------------
// BoolField - toggle. Emits "true"/"false" for commands that take a boolean arg.
// ---------------------------------------------------------------------------
public class BoolField : FormField
{
    public bool Default { get; set; }
    /// <summary>If true, IsValid() requires the toggle to be checked. Use for
    /// confirmation gates on destructive commands (.fam r, etc.).</summary>
    public bool RequireTrue { get; set; }
    protected ToggleRef Toggle;

    public BoolField() { }
    public BoolField(string name, string label, bool defaultValue = false, string tooltip = null, bool requireTrue = false)
    {
        Name = name; Label = label; Default = defaultValue; Tooltip = tooltip; RequireTrue = requireTrue;
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
    public override bool   IsValid()
    {
        if (Toggle == null) return false;
        if (RequireTrue && (Toggle.Toggle == null || !Toggle.Toggle.isOn)) return false;
        return true;
    }
}
