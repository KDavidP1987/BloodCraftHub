using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace BloodCraftHub.Behaviors;

// IL2CPP-registered MonoBehaviour that hosts our per-frame work.
// Ported from LearningMods/BloodCraftUI-master/BloodCraftUI/Behaviors/CoreUpdateBehavior.cs.
//
// Anyone wanting per-frame work adds to the static Actions list:
//
//     CoreUpdateBehavior.Actions.Add(MyService.OnUpdate);
//     CoreUpdateBehavior.Actions.Remove(MyService.OnUpdate);  // when unloading
//
// Setup() registers the type with IL2CPP and attaches it to a DontDestroyOnLoad
// GameObject so it survives scene transitions.
public class CoreUpdateBehavior : MonoBehaviour
{
    public static List<Action> Actions = new();

    // 0.15.1: STATIC host-object guard. Pre-0.15.1 Setup() was invoked
    // twice — once from Plugin.Load, once from UniversalUI.Init — each
    // creating a new GameObject + CoreUpdateBehavior MonoBehaviour. Both
    // MonoBehaviours ran Update() every frame iterating the SAME static
    // Actions list, so every registered Action fired 2× per frame.
    //
    // Most pre-0.15 callers were idempotent (drain-queues, polling
    // checks, etc.) so the double-call was invisible. v0.15.0's hotkey
    // listener exposed the bug: Input.GetKeyDown returns true for the
    // ENTIRE frame after the key transitions Up->Down, so calling
    // TickHotkeys twice in one frame double-toggled the panel
    // (open->close->open in one frame, net visible effect = nothing).
    // Friend-test diagnostic logs showed the panel state flipping
    // back-to-back per keypress.
    //
    // The static guard makes Setup idempotent across instances —
    // subsequent calls become no-ops, so we still have exactly one
    // GameObject + MonoBehaviour running Update regardless of how many
    // places allocate a CoreUpdateBehavior instance.
    private static GameObject _hostObject;

    public void Setup()
    {
        if (_hostObject != null) return; // already registered globally
        ClassInjector.RegisterTypeInIl2Cpp<CoreUpdateBehavior>();
        _hostObject = new GameObject("BloodCraftHubCoreUpdateBehavior");
        DontDestroyOnLoad(_hostObject);
        _hostObject.hideFlags = HideFlags.HideAndDontSave;
        _hostObject.AddComponent<CoreUpdateBehavior>();
    }

    public void Dispose()
    {
        if (_hostObject) Destroy(_hostObject);
        _hostObject = null;
    }

    protected void Update()
    {
        if (!Plugin.IsInitialized) return;

        foreach (var action in Actions.ToList())
        {
            try { action?.Invoke(); }
            catch (Exception ex) { Plugin.LogInstance?.LogError($"CoreUpdate handler threw: {ex}"); }
        }
    }
}
