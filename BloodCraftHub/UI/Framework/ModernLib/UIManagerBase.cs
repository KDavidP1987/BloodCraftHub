using BloodCraftHub.UI.Framework.UniverseLib.UI;
using UnityEngine;

namespace BloodCraftHub.UI.Framework.ModernLib;

public abstract class UIManagerBase
{
    protected UIBase UiBase { get; set; }
    public GameObject UIRoot => UiBase?.RootObject;
    public bool IsInitialized { get; protected set; }

    public virtual void SetupAndShowUI()
    {
        if (IsInitialized) return;
        UniversalUI.Init();

        if (UiBase == null)
        {
            UiBase = UniversalUI.RegisterUI(MyPluginInfo.PLUGIN_GUID, UiUpdate);
            AddMainContentPanel();
        }
        SetActive(true);

        IsInitialized = true;
    }

    public abstract void SetActive(bool active);

    public virtual void Reset()
    {
        IsInitialized = false;
        SetActive(false);
    }

    protected abstract void AddMainContentPanel();
    /// <summary>
    /// Called once per frame when your UI is being displayed.
    /// </summary>
    protected virtual void UiUpdate()
    {
    }
}