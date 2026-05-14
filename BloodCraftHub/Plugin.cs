using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BloodCraftHub.Behaviors;
using BloodCraftHub.Config;
using BloodCraftHub.UI;
using BloodCraftHub.Utils;
using HarmonyLib;
using UnityEngine;

namespace BloodCraftHub;

[BepInProcess("VRising.exe")]
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    public static Plugin Instance { get; private set; }
    public static ManualLogSource LogInstance => Instance.Log;
    public static Settings Settings { get; private set; }
    public static BCHubUIManager UIManager { get; private set; }
    public static CoreUpdateBehavior CoreUpdateBehavior { get; private set; }

    public static bool IsClient { get; private set; }
    public static bool IsInitialized { get; private set; }

    // Set true to surface a test UI populated with dummy data on load.
    // Do not ship as true.
    public const bool IS_TESTING = false;

    private Harmony _harmony;

    public override void Load()
    {
        Instance = this;
        IsClient = Application.productName != "VRisingServer";
        LogUtils.Init(Log);

        if (!IsClient)
        {
            Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME}[{MyPluginInfo.PLUGIN_VERSION}] is a client mod — not loading on server ({Application.productName})");
            return;
        }

        Settings = new Settings().InitConfig();

        UIManager = new BCHubUIManager();
        CoreUpdateBehavior = new CoreUpdateBehavior();
        CoreUpdateBehavior.Setup();

        // Patch everything in this assembly that carries Harmony attributes.
        // Individual patch classes live under BloodCraftHub.Patches.
        _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), MyPluginInfo.PLUGIN_GUID);

        IsInitialized = true;
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} loaded.");

        if (IS_TESTING)
            UIManager.SetupAndShowUI();
    }

    public override bool Unload()
    {
        _harmony?.UnpatchSelf();
        return true;
    }
}
