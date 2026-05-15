using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BloodCraftHub.Behaviors;
using BloodCraftHub.Config;
using BloodCraftHub.Services;
using BloodCraftHub.UI;
using BloodCraftHub.UI.Forms;
using BloodCraftHub.Utils;
using HarmonyLib;
using Unity.Entities;
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
    public static bool IsGameDataInitialized { get; set; }

    // Set true to surface UI immediately on load with dummy data; do not ship as true.
    public const bool IS_TESTING = false;

    // Client-world handles populated by GameManagerPatch / InitializationPatch.
    private static World _client;
    public static EntityManager EntityManager => _client.EntityManager;
    public static bool IsClientNull() => _client == null;
    public static Entity LocalCharacter { get; set; } = Entity.Null;

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

        // 0.9.0: sync the Theme font multipliers from saved Settings before
        // any panel constructs. ScaledUI / ScaledOverlay read these to size
        // labels at build time, so the synchronization has to happen before
        // SetupAndShowUI runs.
        UI.Framework.CustomLib.Util.Theme.UIFontMultiplier      = BloodCraftHub.Config.Settings.UITextScale;
        UI.Framework.CustomLib.Util.Theme.OverlayFontMultiplier = BloodCraftHub.Config.Settings.OverlayTextScale;

        EclipseProtocolService.Initialize();

        UIManager = new BCHubUIManager();
        CoreUpdateBehavior = new CoreUpdateBehavior();
        CoreUpdateBehavior.Setup();

        // Tick the outbound chat queue every frame. ProcessAllMessages no-ops
        // until MessageService.SetCharacter/SetUser get called (by InitializationPatch
        // once the player is in-world), so this is safe at Load time.
        CoreUpdateBehavior.Actions.Add(MessageService.ProcessAllMessages);

        // Per-frame timeout flush for the inbound intercept buffers (box list,
        // box content). Without this the parsed list sits in a buffer until
        // some other system message arrives to act as a terminator.
        CoreUpdateBehavior.Actions.Add(MessageService.TickInterceptTimeouts);

        // Tooltip hover loop. TickAll no-ops until the MainPanel sets
        // TooltipHover.Sink (during BuildTooltipFooter), so this is safe at
        // Load time. Registering here (not lazily on first MainPanel build)
        // sidesteps a load-order bug where the lazy EnsureTicking saw a null
        // CoreUpdateBehavior under some path and the loop never started.
        CoreUpdateBehavior.Actions.Add(TooltipHover.TickAll);

        // Outside-click closes any open EnumField dropdown. TMP_Dropdown's own
        // Blocker doesn't fire in our canvas setup, so we close it ourselves.
        CoreUpdateBehavior.Actions.Add(FormDropdownRegistry.TickCloseOnOutsideClick);

        // Click-on-track handler for our scroll-view sliders. Unity's built-in
        // Slider.OnPointerDown only fires on the handle in our hierarchy; this
        // adds the standard "click anywhere on the track to jump there" UX.
        CoreUpdateBehavior.Actions.Add(UI.Framework.UniverseLib.UI.Widgets.SliderClickRegistry.TickClickOnTrack);

        _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), MyPluginInfo.PLUGIN_GUID);

        IsInitialized = true;
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} loaded.");

#pragma warning disable CS0162 // IS_TESTING is a compile-time feature flag; unreachability here is intentional.
        if (IS_TESTING)
            UIOnInitialize();
#pragma warning restore CS0162
    }

    public override bool Unload()
    {
        _harmony?.UnpatchSelf();
        return true;
    }

    /// <summary>Called from InitializationPatch once the player is in-world.</summary>
    public static void UIOnInitialize()
    {
        if (UIManager.IsInitialized) return;
        UIManager.SetupAndShowUI();
        // Bring back any overlays the user had visible at last logout. Wired
        // in 0.6.0 — pre-0.6.0 every overlay defaulted to off on every login
        // even if the user had toggled them on.
        UIManager.RestoreOverlaysFromSettings();
        LogUtils.LogInfo("UI Manager initialized.");
    }

    /// <summary>Called from GameManagerPatch once the client World is available.</summary>
    public static void GameDataOnInitialize(World world)
    {
        if (IsGameDataInitialized || !IsClient) return;
        _client = world;
        IsGameDataInitialized = true;
        LogUtils.LogInfo("Client world bound; game data initialized.");
    }
}
