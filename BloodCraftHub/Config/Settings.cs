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

    // 0.9.0: two-axis text scaling. The UI scale affects the main tabbed
    // panel; the overlay scale affects the four secondary overlays (XP /
    // Familiar / Familiar Browser / Daily Quest / Professions). Stored as a
    // float multiplier so the user can pick Small/Standard/Large from a
    // segmented control and a future "fine-grained" slider can drop in
    // without a config schema change.
    public static float UITextScale =>
        (ConfigEntries[nameof(UITextScale)] as ConfigEntry<float>)?.Value ?? 1.0f;
    public static float OverlayTextScale =>
        (ConfigEntries[nameof(OverlayTextScale)] as ConfigEntry<float>)?.Value ?? 1.0f;
    public static void SetUITextScale(float v)       => SetFloat(nameof(UITextScale), v);
    public static void SetOverlayTextScale(float v)  => SetFloat(nameof(OverlayTextScale), v);

    // 0.9.0: per-overlay background transparency. User semantics (per
    // friend-testing direction): 0.0 = solid (opaque), 1.0 = invisible. We
    // floor at 0.95 internally so the panel's chrome / drag handle remain
    // visible — at 1.0 the user could no longer find the panel to
    // close/move it. Text and other foreground elements stay fully opaque
    // regardless; only the Image-backed background tracks this.
    public const float OVERLAY_TRANSPARENCY_FLOOR = 0.95f;

    public static float XPOverlayTransparency        => GetFloat(nameof(XPOverlayTransparency),        UITransparency);
    public static float FamiliarOverlayTransparency  => GetFloat(nameof(FamiliarOverlayTransparency),  UITransparency);
    public static float FamiliarBrowserTransparency  => GetFloat(nameof(FamiliarBrowserTransparency),  UITransparency);
    public static float DailyQuestTransparency       => GetFloat(nameof(DailyQuestTransparency),       UITransparency);
    public static float ProfessionOverlayTransparency => GetFloat(nameof(ProfessionOverlayTransparency), UITransparency);
    public static void SetXPOverlayTransparency(float v)        => SetFloat(nameof(XPOverlayTransparency), v);
    public static void SetFamiliarOverlayTransparency(float v)  => SetFloat(nameof(FamiliarOverlayTransparency), v);
    public static void SetFamiliarBrowserTransparency(float v)  => SetFloat(nameof(FamiliarBrowserTransparency), v);
    public static void SetDailyQuestTransparency(float v)       => SetFloat(nameof(DailyQuestTransparency), v);
    public static void SetProfessionOverlayTransparency(float v) => SetFloat(nameof(ProfessionOverlayTransparency), v);

    /// <summary>Convert a user-facing "transparency" value (0=opaque, 1=invisible)
    /// to an alpha multiplier suitable for Image.color, applying the legibility
    /// floor so panel chrome remains visible.</summary>
    public static float TransparencyToAlpha(float userTransparency)
    {
        var clamped = UnityEngine.Mathf.Clamp(userTransparency, 0f, OVERLAY_TRANSPARENCY_FLOOR);
        return 1f - clamped;
    }

    /// <summary>Visibility-suppress flag for the master overlay toggle on the
    /// floating-button strip. Session-only — not persisted — so the user can't
    /// suppress everything and forget how to bring it back. Reset on game
    /// restart. Per friend-testing direction: this must NEVER make
    /// hidden-by-config overlays visible; it only re-hides / re-shows the
    /// overlays that were already visible per their per-overlay config flags.</summary>
    public static bool OverlaysSuppressedByUser { get; set; }

    private static float GetFloat(string key, float fallback)
    {
        return (ConfigEntries.TryGetValue(key, out var entry) && entry is ConfigEntry<float> f)
            ? f.Value : fallback;
    }
    private static void SetFloat(string key, float value)
    {
        if (ConfigEntries.TryGetValue(key, out var entry) && entry is ConfigEntry<float> f)
            f.Value = value;
    }
    public static bool UseHorizontalContentLayout =>
        (ConfigEntries[nameof(UseHorizontalContentLayout)] as ConfigEntry<bool>)?.Value ?? true;
    public static bool ClearServerMessages =>
        (ConfigEntries[nameof(ClearServerMessages)] as ConfigEntry<bool>)?.Value ?? false;

    // 0.9.1: when on, the chat copy of action-confirmation messages
    // (.fam b / .fam ub / .fam t / .fam cb / .fam mb / .fam sb / .fam r)
    // is suppressed. Friend-testing feedback: switching boxes and bouncing
    // between familiars produces a wall of confirmation chat that's noisy
    // for users who already see the live state in the UI. The UI continues
    // to work because the data feeds (.fam boxes / .fam l intercepts + the
    // Eclipse stream) aren't affected — only the human-readable confirmation
    // lines are eaten.
    public static bool SuppressFamiliarActionChatter =>
        (ConfigEntries[nameof(SuppressFamiliarActionChatter)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetSuppressFamiliarActionChatter(bool v) => SetBool(nameof(SuppressFamiliarActionChatter), v);
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
    public static bool ShowProfessionOverlay   => (ConfigEntries[nameof(ShowProfessionOverlay)]   as ConfigEntry<bool>)?.Value ?? false;

    public static void SetShowExperienceOverlay(bool v) => SetBool(nameof(ShowExperienceOverlay), v);
    public static void SetShowFamiliarOverlay(bool v)   => SetBool(nameof(ShowFamiliarOverlay),   v);
    public static void SetShowFamiliarBrowser(bool v)   => SetBool(nameof(ShowFamiliarBrowser),   v);
    public static void SetShowDailyQuestOverlay(bool v) => SetBool(nameof(ShowDailyQuestOverlay), v);
    public static void SetShowProfessionOverlay(bool v) => SetBool(nameof(ShowProfessionOverlay), v);

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

    // (SuspendGameInputWhileTyping was removed in 0.8.2 — its Harmony prefix on
    // InputActionSystem.OnUpdate also wedged Unity's UI input pipeline,
    // locking the entire game when users typed into a form. A proper fix needs
    // a different patch target; until then the feature is gone. SuspendGameInputWhileUIOpen
    // was removed in 0.1.2 for the same reason. Stale .cfg entries are inert.)

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

    // (Settings.IsAdmin was removed in 0.8.2 — the toggle gate it backed was
    // unreliable: ShowTab(ActiveTab) on the same tab didn't always rebuild the
    // page, so the user had to fully relaunch the game for admin tabs to
    // surface. Admin tabs are now always visible with an info note at the top;
    // the server enforces permissions, so non-admins clicking commands just
    // get rejection messages.)

    public Settings InitConfig()
    {
        if (!Directory.Exists(CONFIG_PATH)) Directory.CreateDirectory(CONFIG_PATH);

        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(ClearServerMessages),         true,  "Clear server and command messages from chat.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(SuppressFamiliarActionChatter), false, "Suppress the chat confirmation lines that Bloodcraft prints when you switch boxes / bind / unbind / move / smartbind familiars. The UI still updates normally (box list, contents, and overlays read from separate pipes). Off by default; toggle in Display settings.");
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
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ShowProfessionOverlay),       false, "Whether the Professions overlay (Bloodcraft profession levels) was visible at last logout.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(IsPanelAutoResizeEnabled),    true,  "Auto-resize the main panel vertically to fit the active tab's content (capped at 90% of screen height).");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(UITextScale),                 1.0f,  "Font scale multiplier for the main panel (Small=0.85, Standard=1.0, Large=1.2). Changes apply when the panel is closed and reopened.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(OverlayTextScale),            1.0f,  "Font scale multiplier for the secondary overlays (Small=0.85, Standard=1.0, Large=1.2). Changes apply when each overlay is toggled off and back on.");

        // Per-overlay background transparency. User semantics:
        //   0.0 = solid (opaque), 1.0 = invisible. Floored at 0.95 internally
        //   so the panel chrome / drag handle stays visible at "100%".
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(XPOverlayTransparency),       0.4f,  "XP overlay background transparency (0.0=solid, 1.0=invisible). Floor at 0.95 keeps drag handle visible.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(FamiliarOverlayTransparency), 0.4f,  "Familiar overlay background transparency.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(FamiliarBrowserTransparency), 0.4f,  "Familiar Browser overlay background transparency.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(DailyQuestTransparency),      0.4f,  "Daily quest overlay background transparency.");
        InitConfigEntry(OVERLAY_SETTINGS_GROUP, nameof(ProfessionOverlayTransparency), 0.4f, "Profession overlay background transparency.");
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
