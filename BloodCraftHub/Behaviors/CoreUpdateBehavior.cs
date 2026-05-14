using System;
using System.Collections.Generic;

namespace BloodCraftHub.Behaviors;

// Per-frame update host. Services register Actions here instead of each
// owning a MonoBehaviour (cheaper, simpler lifetime management).
//
// PORT FROM: LearningMods/BloodCraftUI-master/BloodCraftUI/Behaviors/CoreUpdateBehavior.cs
public class CoreUpdateBehavior
{
    private readonly List<Action> _onUpdate = new();

    public void Setup()
    {
        // TODO: hook into a Unity update source. BloodCraftUI uses a Harmony
        // patch on a per-frame system + invokes registered actions there.
    }

    public void Register(Action action)
    {
        if (action != null) _onUpdate.Add(action);
    }

    public void Unregister(Action action) => _onUpdate.Remove(action);

    public void Tick()
    {
        for (int i = 0; i < _onUpdate.Count; i++)
        {
            try { _onUpdate[i](); }
            catch (Exception ex) { Plugin.LogInstance?.LogError($"CoreUpdate handler threw: {ex}"); }
        }
    }
}
