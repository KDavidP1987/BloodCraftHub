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
        // 0.10.3 fix: VBloodScannerService.Initialize used to run here.
        // Subscribing to MessageService.FamSearchCompleted from Plugin.Load
        // triggered MessageService's cctor before V Rising's ECS World was
        // up, and the cctor's ComponentType.ReadOnly calls NREd inside
        // Unity.Entities.TypeManager.FindTypeIndex — aborting plugin load
        // entirely. Defer to UIOnInitialize which fires once LocalCharacter
        // is bound (i.e. the World is ready). MessageService is also made
        // lazy as a defense-in-depth measure, but the deferral keeps the
        // initialization order matching Eclipse-main's same pattern.

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

        // 0.10.0: V-Blood scanner per-frame pump. No-op unless a scan is
        // actively running; cheap when idle (one bool check + return).
        CoreUpdateBehavior.Actions.Add(VBloodScannerService.Tick);

        // 0.11.0: shift-spell cooldown polling. Self-throttles to 10 Hz; cheap
        // when no shift overlay is open (the service runs but the panel just
        // never reads the values).
        CoreUpdateBehavior.Actions.Add(ShiftCooldownService.Tick);

        // 0.16.1: custom-recipe application is scheduled (not run inline in the
        // Eclipse config handler) and applied here a few seconds after login, on a
        // quiet frame — keeps its ECS structural-change burst out of the volatile
        // login window. No-op until ScheduleApply arms it; self-disarms after apply.
        CoreUpdateBehavior.Actions.Add(RecipeService.Tick);

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

        // 0.15.0: hotkey listener. Polls the two configurable shortcuts every
        // frame and fires the same actions as clicking the floating BCH / OV
        // buttons. Hotkeys default to KeyboardShortcut.Empty so the listener
        // is effectively a single bool check (IsEmpty) per frame until the
        // user binds something — cheap when unused.
        CoreUpdateBehavior.Actions.Add(TickHotkeys);

        // 0.17.0: drive the tabbed-chat input-focus suppression flag every frame
        // (was polled from ClientChatSystem.OnUpdate, which doesn't tick reliably,
        // so menu hotkeys leaked through while typing). Cheap when chat is closed.
        CoreUpdateBehavior.Actions.Add(Patches.InputSuppression.TickChatFocus);

        // 0.17.2: safe menu-open suppression while typing — replaces the 3 menu Harmony
        // patches that caused the V-Blood-tracking load crash. Drains menu-open request
        // entities only while menus should be blocked; no detour on the hot menu systems.
        CoreUpdateBehavior.Actions.Add(Patches.InputSuppression.DrainMenuOpenRequests);

        // 0.17.2: drive the deferred overlay restore (armed by UIOnInitialize). Pushes
        // overlay construction off the volatile login frame onto a quiet one. No-op
        // until armed and the UiBuildDelaySeconds window elapses.
        CoreUpdateBehavior.Actions.Add(UIManager.TickDeferredRestore);

        // 0.17.3: keep the HIDDEN native chat from ever trapping input under takeover
        // (e.g. the P-key social menu's right-click "Whisper" focusing it). No-op unless
        // native-chat hide is active and the native chat somehow grabbed focus.
        CoreUpdateBehavior.Actions.Add(UIManager.TickNativeChatGuard);

        // 0.17.2: selective patch manifest (was CreateAndPatchAll over the whole
        // assembly). Lets an affected player drop individual patch GROUPS via the
        // Compatibility config section to bisect the intermittent 0.16.x load crash.
        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        ApplyPatches(_harmony);

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
        // SetupAndShowUI is light (canvas + floating launcher) and sets IsInitialized,
        // which CommonClientDataSystem_OnUpdate_Postfix needs to begin capturing
        // LocalCharacter/LocalUser — so it stays synchronous on the spawn frame.
        UIManager.SetupAndShowUI();
        // 0.17.2: the overlay restore (rebuild every overlay the user had visible at
        // last logout) and the V-Blood scanner init used to run synchronously right
        // here, on the same frame the player spawns. For users with several overlays
        // enabled that's a burst of IL2CPP-wrapper allocation in the volatile login
        // window — combined with other client mods' churn it helped tip the 0.16.x
        // GC-finalizer crash. ScheduleOverlayRestore defers both onto a quiet frame
        // (Settings.UiBuildDelaySeconds); a delay of 0 runs them immediately (legacy).
        // (The scanner deferral also keeps the old 0.10.3 invariant — by the time it
        // fires, LocalCharacter is bound and the ECS World is fully available.)
        UIManager.ScheduleOverlayRestore();
        LogUtils.LogInfo("UI Manager initialized.");
    }

    // 0.17.2: selective Harmony patch manifest. Replaces CreateAndPatchAll over the
    // whole assembly so each always-on patch GROUP can be dropped independently via
    // the Compatibility config section. This is the bisect lever for the 0.16.x load
    // crash: a prior "all features off" diagnostic still crashed, which points at the
    // always-on patches (applied at load regardless of any feature setting) rather
    // than a toggleable feature. Skipping a group means the Harmony detour is never
    // installed at all — not merely a no-op prefix — so the test is meaningful.
    // InitializationPatch is mandatory (it boots the UI + binds the player) and is
    // always applied. EscapeMenuPatch / VersionStringPatch / GameManagerPatch carry
    // no active [HarmonyPatch] targets, so they're intentionally absent here.
    private void ApplyPatches(Harmony h)
    {
        // 0.17.2 crash-bisect TEST variants compile a constant that force-disables a
        // group regardless of config (BloodCraftHub.Config.BuildVariant). Normal builds: all false.
        if (BloodCraftHub.Config.BuildVariant.IsTestVariant)
            Log.LogWarning($"*** BloodCraftHub CRASH-TEST VARIANT: {BloodCraftHub.Config.BuildVariant.Tag} — NOT a normal release; one patch group is compiled OFF. ***");

        bool chat   = Settings.EnableChatSystemHooks        && !BloodCraftHub.Config.BuildVariant.ForceChatHooksOff;
        bool layer  = Settings.EnableOverlayLayeringPatch    && !BloodCraftHub.Config.BuildVariant.ForceOverlayLayeringOff;
        // 0.17.2: the input-suppression group is split into "movement/ability" and
        // "menu" sub-groups so each can be dropped independently — the bisect pinned
        // this group as the crash culprit and we want to keep the safe half.
        bool inputBase = Settings.EnableInputSuppressionPatches && !BloodCraftHub.Config.BuildVariant.ForceInputSuppressionOff;
        bool moveInput = inputBase && !BloodCraftHub.Config.BuildVariant.ForceMoveInputOff;

        h.CreateClassProcessor(typeof(Patches.InitializationPatch)).Patch();

        if (chat)
        {
            h.CreateClassProcessor(typeof(Patches.ClientChatPatch)).Patch();
            Log.LogInfo("[compat] Chat-system patches APPLIED (inbound parsing + tabbed chat window).");
        }
        else
            Log.LogWarning("[compat] Chat-system patches SKIPPED — tabbed chat + command-reply parsing are DISABLED. Diagnostic; expected to be ON for normal use.");

        if (moveInput)
        {
            h.CreateClassProcessor(typeof(Patches.GameplayInputSuppressionPatch)).Patch();
            h.CreateClassProcessor(typeof(Patches.AbilityInputSuppressionPatch)).Patch();
            Log.LogInfo("[compat] Input-suppression (movement/ability) APPLIED.");
        }
        else
            Log.LogWarning("[compat] Input-suppression (movement/ability) SKIPPED — your character may move/cast while you type. Diagnostic.");

        // 0.17.2 CRASH FIX: the 3 menu-suppression patches (MenuInputSystem /
        // OpenHUDMenuSystem / ActionWheelSystem) are deliberately NOT attached —
        // the bisect proved they cause the V-Blood-tracking / map-open load crash.
        // Menu-open suppression while typing is now done safely by
        // InputSuppression.DrainMenuOpenRequests (registered on CoreUpdateBehavior),
        // which drains the menu-open request entities instead of detouring those hot
        // systems. The three patch classes remain in source but are intentionally
        // never patched.

        if (layer)
        {
            h.CreateClassProcessor(typeof(Patches.UICanvasSystemPatch)).Patch();
            Log.LogInfo("[compat] Overlay-layering patch APPLIED (overlays-behind-menus).");
        }
        else
            Log.LogWarning("[compat] Overlay-layering patch SKIPPED — overlays always render on top. Diagnostic; expected to be ON for normal use.");
    }

    /// <summary>Called from GameManagerPatch once the client World is available.</summary>
    public static void GameDataOnInitialize(World world)
    {
        if (IsGameDataInitialized || !IsClient) return;
        _client = world;
        IsGameDataInitialized = true;
        LogUtils.LogInfo("Client world bound; game data initialized.");
    }

    // 0.15.0: per-frame hotkey poll. BCHotkey.IsDown returns true only on
    // the frame the binding's main key transitions Up -> Down AND every
    // modifier is currently held — fires once per press regardless of how
    // long the user holds the key. Skipped until UIManager is initialized
    // since UIOnInitialize wires the click handlers we route to.
    private static void TickHotkeys()
    {
        if (UIManager == null || !UIManager.IsInitialized) return;

        var mainHotkey = Settings.HotkeyToggleMainPanel;
        if (!mainHotkey.IsEmpty && mainHotkey.IsDown())
        {
            try
            {
                LogUtils.LogDiagnostic($"Hotkey fired: HotkeyToggleMainPanel ({mainHotkey})");
                UIManager.ToggleMainPanel();
            }
            catch (System.Exception ex) { LogUtils.LogError($"HotkeyToggleMainPanel handler threw: {ex}"); }
        }

        var overlayHotkey = Settings.HotkeyToggleAllOverlays;
        if (!overlayHotkey.IsEmpty && overlayHotkey.IsDown())
        {
            try
            {
                LogUtils.LogDiagnostic($"Hotkey fired: HotkeyToggleAllOverlays ({overlayHotkey})");
                UIManager.ToggleAllOverlaysSuppressed();
            }
            catch (System.Exception ex) { LogUtils.LogError($"HotkeyToggleAllOverlays handler threw: {ex}"); }
        }
    }
}
