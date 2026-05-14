using BloodCraftHub.UI.Framework.UniverseLib.UI.ObjectPool;
using UnityEngine;

namespace BloodCraftHub.UI.Framework.UniverseLib.UI.Widgets.ScrollView;

public interface ICell : IPooledObject
{
    bool Enabled { get; }

    RectTransform Rect { get; set; }

    void Enable();
    void Disable();
}