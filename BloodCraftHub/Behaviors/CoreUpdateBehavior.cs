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
    private GameObject _obj;

    public void Setup()
    {
        ClassInjector.RegisterTypeInIl2Cpp<CoreUpdateBehavior>();
        _obj = new GameObject("BloodCraftHubCoreUpdateBehavior");
        DontDestroyOnLoad(_obj);
        _obj.hideFlags = HideFlags.HideAndDontSave;
        _obj.AddComponent<CoreUpdateBehavior>();
    }

    public void Dispose()
    {
        if (_obj) Destroy(_obj);
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
