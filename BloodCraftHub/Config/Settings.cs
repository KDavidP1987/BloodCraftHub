using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace BloodCraftHub.Config;

// Static settings registry. Modeled on BloodCraftUI's Config/Settings.cs.
// The copied UI framework references `Settings.UITransparency` (etc.) as
// static, so this class is static-by-design.
//
// To add a setting:
//   1. Pick a section constant below (or add one).
//   2. Add a public-static getter that reads from ConfigEntries via nameof().
//   3. Add a matching InitConfigEntry(...) call in InitConfig().
public class Settings
{
    private static string CONFIG_PATH = Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME);
    private static readonly Dictionary<string, ConfigEntryBase> ConfigEntries = new();

    public const string UI_SETTINGS_GROUP       = "UISettings";
    public const string FAM_SETTINGS_GROUP      = "FamiliarSettings";
    public const string GENERAL_SETTINGS_GROUP  = "GeneralOptions";
    public const string OVERLAY_SETTINGS_GROUP  = "Overlays";

    // ---- UI / general ----
    public static float UITransparency =>
        (ConfigEntries[nameof(UITransparency)] as ConfigEntry<float>)?.Value ?? 0.6f;
    public static bool UseHorizontalContentLayout =>
        (ConfigEntries[nameof(UseHorizontalContentLayout)] as ConfigEntry<bool>)?.Value ?? true;
    public static bool ClearServerMessages =>
        (ConfigEntries[nameof(ClearServerMessages)] as ConfigEntry<bool>)?.Value ?? false;
    public static int GlobalQueryIntervalInSeconds { get; } = 2;
    public static int FamStatsQueryIntervalInSeconds
    {
        get
        {
            var value = (ConfigEntries[nameof(FamStatsQueryIntervalInSeconds)] as ConfigEntry<int>)?.Value ?? 10;
            if (value < 5) value = 5;
            return value;
        }
    }

    // ---- Familiar UI flags ----
    public static bool IsFamStatsPanelEnabled  => (ConfigEntries[nameof(IsFamStatsPanelEnabled)]  as ConfigEntry<bool>)?.Value ?? true;
    public static bool IsBoxPanelEnabled       => (ConfigEntries[nameof(IsBoxPanelEnabled)]       as ConfigEntry<bool>)?.Value ?? true;
    public static bool IsBindButtonEnabled     => (ConfigEntries[nameof(IsBindButtonEnabled)]     as ConfigEntry<bool>)?.Value ?? true;
    public static bool IsCombatButtonEnabled   => (ConfigEntries[nameof(IsCombatButtonEnabled)]   as ConfigEntry<bool>)?.Value ?? true;
    public static bool IsPrestigeButtonEnabled => (ConfigEntries[nameof(IsPrestigeButtonEnabled)] as ConfigEntry<bool>)?.Value ?? true;
    public static bool IsToggleButtonEnabled   => (ConfigEntries[nameof(IsToggleButtonEnabled)]   as ConfigEntry<bool>)?.Value ?? true;
    public static bool AutoEnableFamiliarEquipment =>
        (ConfigEntries[nameof(AutoEnableFamiliarEquipment)] as ConfigEntry<bool>)?.Value ?? true;

    public static string LastBindCommand
    {
        get => (ConfigEntries[nameof(LastBindCommand)] as ConfigEntry<string>)?.Value ?? "";
        set => ConfigEntries[nameof(LastBindCommand)].BoxedValue = value;
    }

    // ---- Secondary overlays (BloodCraftHub addition) ----
    // These are read on UIOnInitialize to restore each overlay's last visibility
    // state across sessions. BCHubUIManager.ToggleOverlay writes the new value
    // here so a flip persists. (Pre-0.6.0 these settings existed but were never
    // wired into the toggle path, so the overlays always defaulted to off.)
    public static bool ShowExperienceOverlay   => (ConfigEntries[nameof(ShowExperienceOverlay)]   as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowFamiliarOverlay     => (ConfigEntries[nameof(ShowFamiliarOverlay)]     as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowFamiliarBrowser     => (ConfigEntries[nameof(ShowFamiliarBrowser)]     as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowDailyQuestOverlay   => (ConfigEntries[nameof(ShowDailyQuestOverlay)]   as ConfigEntry<bool>)?.Value ?? false;

    public static void SetShowExperienceOverlay(bool v) => SetBool(nameof(ShowExperienceOverlay), v);
    public static void SetShowFamiliarOverlay(bool v)   => SetBool(nameof(ShowFamiliarOverlay),   v);
    public static void SetShowFamiliarBrowser(bool v)   => SetBool(nameof(ShowFamiliarBrowser),   v);
    public static void SetShowDailyQuestOverlay(bool v) => SetBool(nameof(ShowDailyQuestOverlay), v);

    private static void SetBool(string key, bool value)
    {
        if (ConfigEntries.TryGetValue(key, out var entry) && entry is ConfigEntry<bool> b)
            b.Value = value;
    }

    // Auto-resize: main panel grows vertically to fit content (capped at 90% of
    // screen height). User-toggleable via the footer checkbox - some players
    // prefer a fixed-size panel they can manually resize.
    public static bool IsPanelAutoResizeEnabled =>
        (ConfigEntries[nameof(IsPanelAutoResizeEnabled)] as ConfigEntry<bool>)?.Value ?? true;

    // EXPERIMENTAL — default OFF as of 0.1.3 because the underlying mechanism
    // (skipping InputActionSystem.OnUpdate via Harmony) also wedges Unity's UI
    // input pipeline. When on, clicking into a form field can lock you out of
    // the panel itself with no way to recover. We default off until a non-
    // locking suspension mechanism is available; opting in is fine if you're
    // willing to take the risk (mash Escape / quit if it freezes).
    public static bool SuspendGameInputWhileTyping =>
        (ConfigEntries[nameof(SuspendGameInputWhileTyping)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetSuspendGameInputWhileTyping(bool value)
    {
        if (ConfigEntries.TryGetValue(nameof(SuspendGameInputWhileTyping), out var entry)
            && entry is ConfigEntry<bool> b)
            b.Value = value;
    }

    // (SuspendGameInputWhileUIOpen was removed in 0.1.2 — the implementation
    // also wedged the UI-input pipeline, so any user who toggled it on got
    // their game frozen across sessions. The .cfg entry is no longer registered;
    // any stale value in the user's config is inert.)

    // Tristate per-server-mod availability (Auto / On / Off). Auto uses a
    // probe to decide:
    //   - Bloodcraft: present iff EclipseProtocolService.UserRegistered ever
    //     becomes true within the session (the server ACK'd our handshake).
    //   - Kindred: no protocol indicator, so Auto defaults to "assume present"
    //     for now; flip to Off manually if your server doesn't have it.
    // Set to Off and the corresponding tab group renders collapsed + grayed.
    public enum ModAvailability { Auto, On, Off }

    private static ModAvailability ReadAvailability(string key)
    {
        var raw = (ConfigEntries.TryGetValue(key, out var entry) && entry is ConfigEntry<string> s) ? s.Value : "Auto";
        return raw switch { "On" => ModAvailability.On, "Off" => ModAvailability.Off, _ => ModAvailability.Auto };
    }

    public static ModAvailability BloodcraftAvailability => ReadAvailability(nameof(BloodcraftAvailability));
    public static ModAvailability KindredAvailability    => ReadAvailability(nameof(KindredAvailability));
    public static void SetBloodcraftAvailability(ModAvailability v) => SetAvailability(nameof(BloodcraftAvailability), v);
    public static void SetKindredAvailability(ModAvailability v)    => SetAvailability(nameof(KindredAvailability), v);
    private static void SetAvailability(string key, ModAvailability v)
    {
        if (ConfigEntries.TryGetValue(key, out var entry) && entry is ConfigEntry<string> s)
            s.Value = v.ToString();
    }

    // User-asserted "I am a server admin" flag. Off by default. When off, all
    // admin panels (Bloodcraft Admin tab + 3 Kindred admin sub-tabs) display a
    // placeholder explaining the gate and a toggle to flip the flag, so a
    // non-admin player isn't presented with commands the server will reject
    // anyway. We don't probe the server for actual admin status because the
    // chat-pipe protocol gives us no reliable signal.
    public static bool IsAdmin =>
        (ConfigEntries[nameof(IsAdmin)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetIsAdmin(bool value)
    {
        if (ConfigEntries.TryGetValue(nameof(IsAdmin), out var entry)
            && entry is ConfigEntry<bool> b)
            b.Value = value;
    }

    public Settings InitConfig()
    {
        if (!Directory.Exists(CONFIG_PATH)) Directory.CreateDirectory(CONFIG_PATH);

        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(ClearServerMessages),         true,  "Clear server and command messages from chat.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(FamStatsQueryIntervalInSeconds), 10,  "Query interval for familiar stats update (min 5s).");

        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(UseHorizontalContentLayout),  true,  "Horizontal vs vertical layout for the main content panel.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(UITransparency),              0.6f,  "Background opacity for all panels (0=transparent .. 1=opaque).");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(IsFamStatsPanelEnabled),      true,  "Show the familiar stats panel.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(IsBoxPanelEnabled),           true,  "Show the box panel.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(IsBindButtonEnabled),         true,  "Show the bind button.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(IsCombatButtonEnabled),       true,  "Show the combat button.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(IsPrestigeButtonEnabled),     true,  "Show the prestige button.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(IsToggleButtonEnabled),       true,  "Show the toggle button.");

        InitConfigEntry(FAM_SETTINGS_GROUP,     nameof(LastBindCommand),             "",    "Last bind command sent (used to restore selection).");
        InitConfigEntry(FAM_SETTINGS_GROUP,     nameof(AutoEnableFamiliarEquipment), true,  "Automatically enable familiar equipment management on UI bring-up.");

        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowExperienceOverlay),       false, "Whether the XP overlay was visible at last logout. Restored automatically on UI bring-up.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowFamiliarOverlay),         false, "Whether the Familiar overlay (active stats) was visible at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowFamiliarBrowser),         false, "Whether the Familiar Browser overlay was visible at last logout.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowDailyQuestOverlay),       false, "Whether the Daily Quest overlay was visible at last logout.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(IsPanelAutoResizeEnabled),    true,  "Auto-resize the main panel vertically to fit the active tab's content (capped at 90% of screen height).");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(SuspendGameInputWhileTyping), false, "EXPERIMENTAL — off by default in 0.1.3+. When on, suspends gameplay input while you're typing into a BCH form field (so WASD doesn't move the character). The implementation can also lock the UI on some configs — if your panel becomes unresponsive after clicking a field, this is why. Turn back on at your own risk; mash Escape / quit V Rising if it freezes.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(IsAdmin),                     false, "Self-asserted: 'I have admin privileges on this server'. Off by default; when off, the admin tabs are hidden behind a 'You are not an admin' placeholder. Toggle on if you actually have admin so the admin commands surface.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(BloodcraftAvailability),      "Auto", "Whether the server has the Bloodcraft mod. Auto = present iff the server ACK'd our Eclipse handshake. On = always assume present. Off = always disable the BLOODCRAFT tab group.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(KindredAvailability),         "Auto", "Whether the server has the Kindred suite (KindredCommands + KindredLogistics). No protocol probe is wired yet, so Auto currently means 'assume present'. Set to Off explicitly if your server doesn't have these mods to grey out the KINDRED tab group.");

        return this;
    }

    private static ConfigEntry<T> InitConfigEntry<T>(string section, string key, T defaultValue, string description)
    {
        var entry = Plugin.Instance.Config.Bind(section, key, defaultValue, description);

        // Honor any value the user already set in the .cfg on disk.
        var cfgFile = Path.Combine(Paths.ConfigPath, $"{MyPluginInfo.PLUGIN_GUID}.cfg");
        if (File.Exists(cfgFile))
        {
            var config = new ConfigFile(cfgFile, true);
            if (config.TryGetEntry(section, key, out ConfigEntry<T> existing))
                entry.Value = existing.Value;
        }

        ConfigEntries[key] = entry;
        return entry;
    }
}
