using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BloodCraftHub.UI.Framework.UniverseLib.UI.Models;

/// <summary>
/// A simple wrapper class for working with InputFields, with some helpers and performance improvements.
/// </summary>
public class InputFieldRef : UIModel
{
    // Static

    internal static readonly HashSet<InputFieldRef> inputsPendingUpdate = new();

    internal static void UpdateInstances()
    {
        while (inputsPendingUpdate.Any())
        {
            var inputField = inputsPendingUpdate.First();
            LayoutRebuilder.MarkLayoutForRebuild(inputField.Transform);
            inputField.OnValueChanged?.Invoke(inputField.Component.text);

            inputsPendingUpdate.Remove(inputField);
        }
    }

    // -------------------------------------------------------------------------
    // 0.18.2: registry of every live BCH input field so InputSuppression can lock the
    // game keyboard while ANY of them is focused (not just the chat window) — so a keystroke
    // meant for a text box never moves the character, casts, opens a menu, or fires a bound
    // hotkey (e.g. an admin's destructive keybind).
    //
    // Focus is read from each field's OWN `isFocused` every frame — the chat window's
    // proven-reliable pattern. We deliberately do NOT use onSelect/onDeselect (DeactivateInputField
    // doesn't always raise onDeselect → the flag could stick true → post-chat freeze) and do NOT
    // poll the global EventSystem selection (it flickers/goes stale → the 0.17.2 "character stuck
    // looping actions" regression). Reading real per-field focus each call means the lock can never
    // stick on after a field is defocused or destroyed.
    // -------------------------------------------------------------------------
    internal static readonly HashSet<InputFieldRef> LiveInputs = new();

    /// <summary>True if any live BCH input field currently holds keyboard focus. Prunes
    /// destroyed fields as it goes; never throws.</summary>
    internal static bool AnyFocused()
    {
        List<InputFieldRef> dead = null;
        bool any = false;
        GameObject selectedGo = null;
        try { var es = UnityEngine.EventSystems.EventSystem.current; selectedGo = es != null ? es.currentSelectedGameObject : null; }
        catch { selectedGo = null; }

        foreach (var r in LiveInputs)
        {
            try
            {
                if (r?.Component == null) { (dead ??= new List<InputFieldRef>()).Add(r); continue; }
                // Primary signal: the field's own isFocused (chat-window-proven, never sticks on).
                if (r.Component.isFocused) any = true;
                // 0.18.2 fallback: some MAIN-PANEL fields are the EventSystem's active text selection
                // yet report isFocused=false (TMP quirk in nested/scroll-view canvases) — the chat
                // field doesn't, which is why chat locked but forms didn't. Treat "this registered BCH
                // field is the selected object" as focused too. Restricted to OUR fields (no global
                // EventSystem flicker) and smoothed by the release-grace in TickChatFocus → safe.
                else if (selectedGo != null && r.Component.gameObject == selectedGo) any = true;
            }
            catch { (dead ??= new List<InputFieldRef>()).Add(r); }
        }
        if (dead != null) foreach (var r in dead) LiveInputs.Remove(r);
        return any;
    }

    /// <summary>0.18.2: if a registered BCH field is the EventSystem's selected object but reports
    /// isFocused=false, ACTIVATE it so it becomes truly focused. The log showed main-panel fields get
    /// selected without isFocused=true; V Rising's NATIVE input suppression (what actually locks the
    /// keyboard while the chat field is focused — my BlockInputState code is a no-op) only engages for a
    /// genuinely-focused field, so forms leaked. Forcing activation makes a form behave like the chat
    /// input. No-op when already focused, so it can't loop. Returns true if it activated something.</summary>
    internal static bool EnsureSelectedFocused()
    {
        try
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null) return false;
            foreach (var r in LiveInputs)
            {
                try
                {
                    if (r?.Component != null && r.Component.gameObject == go && !r.Component.isFocused)
                    {
                        r.Component.ActivateInputField();
                        return true;
                    }
                }
                catch { }
            }
        }
        catch { }
        return false;
    }

    /// <summary>Escape hatch: deactivate every focused BCH field and clear the EventSystem
    /// selection (the proven anti-freeze), so the keyboard can always be freed immediately.</summary>
    internal static void ReleaseAllFocused()
    {
        foreach (var r in LiveInputs)
        {
            try { if (r?.Component != null && r.Component.isFocused) r.Component.DeactivateInputField(); }
            catch { }
        }
        try
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null) es.SetSelectedGameObject(null);
        }
        catch { }
    }

    // Instance

    /// <summary>
    /// Invoked at most once per frame, if the input was changed in the previous frame.
    /// </summary>
    public event Action<string> OnValueChanged;

    /// <summary>
    /// The actual InputField component which this object is a reference to.
    /// </summary>
    public TMP_InputField Component { get; }

    /// <summary>
    /// The placeholder Text component.
    /// </summary>
    public TextMeshProUGUI PlaceholderText { get; }

    /// <summary>
    /// The GameObject which the InputField is attached to.
    /// </summary>
    public override GameObject UIRoot => Component.gameObject;

    /// <summary>
    /// The GameObject which the InputField is attached to.
    /// </summary>
    public GameObject GameObject => Component.gameObject;

    /// <summary>
    /// The RectTransform for this InputField.
    /// </summary>
    public RectTransform Transform { get; }

    /// <summary>
    /// The Text set to the InputField.
    /// </summary>
    public string Text
    {
        get => Component.text;
        set => Component.text = value;
    }

    public InputFieldRef(TMP_InputField component)
    {
        Component = component;
        Transform = component.GetComponent<RectTransform>();
        PlaceholderText = component.placeholder.TryCast<TextMeshProUGUI>();
        component.onValueChanged.AddListener(OnInputChanged);
        // 0.18.2: track for the keyboard-lock focus poll (see LiveInputs / AnyFocused).
        LiveInputs.Add(this);
    }

    private void OnInputChanged(string value)
    {
        if (!inputsPendingUpdate.Contains(this))
            inputsPendingUpdate.Add(this);
    }
}