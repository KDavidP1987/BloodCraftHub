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
    public static bool ShowExperienceOverlay => (ConfigEntries[nameof(ShowExperienceOverlay)] as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowFamiliarOverlay   => (ConfigEntries[nameof(ShowFamiliarOverlay)]   as ConfigEntry<bool>)?.Value ?? false;

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

        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowExperienceOverlay),       false, "Show the experience tracker overlay by default.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowFamiliarOverlay),         false, "Show the quick-familiar overlay by default.");

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
