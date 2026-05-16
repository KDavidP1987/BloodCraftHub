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

    // 0.9.2: when on, the XP overlay + Prestige info display render a
    // horizontal progress bar alongside the % text. Friend-testing feedback:
    // "some people have requested if it could be a visual progress bar".
    // Off by default so existing users see no change.
    public static bool ShowProgressBars =>
        (ConfigEntries[nameof(ShowProgressBars)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetShowProgressBars(bool v) => SetBool(nameof(ShowProgressBars), v);

    // 0.9.6: when on, the XP overlay shows the chosen bonus-stat names AND
    // their current numeric values for the equipped weapon expertise and the
    // current blood legacy beneath each row (e.g. "+12% PhysicalPower"). The
    // values come from .wep get / .bl get replies which are auto-fetched on
    // overlay show + every OverlayBonusStatsRefreshSeconds while visible.
    // Default off so users who want a minimal overlay keep the compact view.
    public static bool ShowOverlayBonusStats =>
        (ConfigEntries[nameof(ShowOverlayBonusStats)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetShowOverlayBonusStats(bool v) => SetBool(nameof(ShowOverlayBonusStats), v);

    // 0.10.7: optional numerical XP-progress row under Weapon and Legacy on
    // the XP overlay. Renders "Exp: 123 / 4500 (2.7%)" so the user can see
    // exactly how much expertise / essence is needed for the next level.
    // Values come from parsing the .wep get / .bl get reply preamble (chat
    // reply contains the raw numbers; the Eclipse stream only has the
    // percentage). Off by default — the existing "Lv X (P%)" title row is
    // sufficient for most users.
    public static bool ShowOverlayXpCounter =>
        (ConfigEntries[nameof(ShowOverlayXpCounter)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetShowOverlayXpCounter(bool v) => SetBool(nameof(ShowOverlayXpCounter), v);

    // 0.10.7: progress-bar height settings.
    // Absolute = explicit pixel height (clamped 4..24, default 8).
    // Relative = bar height scales with the overlay's content area (current pre-0.10.7
    // behavior). User feedback was that bars grew too aggressively when the overlay was
    // enlarged for additional info rows; absolute is the new default.
    public const int PROGRESS_BAR_HEIGHT_MIN = 4;
    public const int PROGRESS_BAR_HEIGHT_MAX = 24;
    public static int ProgressBarHeight =>
        UnityEngine.Mathf.Clamp(
            (ConfigEntries[nameof(ProgressBarHeight)] as ConfigEntry<int>)?.Value ?? 8,
            PROGRESS_BAR_HEIGHT_MIN, PROGRESS_BAR_HEIGHT_MAX);
    public static void SetProgressBarHeight(int v) => SetInt(nameof(ProgressBarHeight),
        UnityEngine.Mathf.Clamp(v, PROGRESS_BAR_HEIGHT_MIN, PROGRESS_BAR_HEIGHT_MAX));
    public static bool ProgressBarHeightRelative =>
        (ConfigEntries[nameof(ProgressBarHeightRelative)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetProgressBarHeightRelative(bool v) => SetBool(nameof(ProgressBarHeightRelative), v);

    // 0.10.10: auto-scan-on-open toggle. Pre-0.10.10 the V-Bloods tab
    // unconditionally fired VBloodScannerService.StartScan() the first
    // time the user opened it after a session start. The new box-sweep
    // scanner (0.10.9) is much faster than the old .fam s scan, but it
    // STILL switches the active box ~10-15 times and (until the silent
    // suppression flag added in 0.10.10) leaked the `.fam cb`/`.fam l`
    // confirmations into chat. Friend-testing surfaced this as the
    // dominant new-version annoyance — auto-scan now defaults to OFF,
    // and the V-Bloods tab waits for the user to click "Scan all".
    public static bool AutoScanVBloodsOnTabOpen =>
        (ConfigEntries[nameof(AutoScanVBloodsOnTabOpen)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetAutoScanVBloodsOnTabOpen(bool v) => SetBool(nameof(AutoScanVBloodsOnTabOpen), v);

    // 0.10.14: overlay-lock toggle. When on, every overlay panel's
    // IsPinned flag is set true, which short-circuits PanelDragger's
    // per-frame Update — no user drag, no user resize, no resize-hover
    // cursor. Programmatic resize via Rect.sizeDelta (used by the
    // auto-resize-on-data path and by the Display Settings nudge
    // buttons) is unaffected: IsPinned only blocks the dragger, not
    // direct rect mutations. Friend-test: "lock overlays in place so
    // I don't accidentally drag them while playing."
    public static bool LockOverlays =>
        (ConfigEntries[nameof(LockOverlays)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetLockOverlays(bool v) => SetBool(nameof(LockOverlays), v);

    // 0.10.8: per-overlay edge padding. Pre-0.10.8 text in the overlays sat
    // flush with the panel border, which read as cramped — especially
    // visible on the Familiar Browser overlay where long V-Blood names
    // butted up against the scrollbar gutter on the right. This setting
    // controls the LEFT / RIGHT inner padding applied to every overlay's
    // content area via ResizeablePanelBase.ApplyOverlayEdgePadding. The
    // value is applied at construct time; rebuilding the overlay (toggle
    // off + back on, or change overlay text scale) picks up the new
    // padding live.
    public const int OVERLAY_EDGE_PADDING_MIN = 0;
    public const int OVERLAY_EDGE_PADDING_MAX = 32;
    public static int OverlayEdgePadding =>
        UnityEngine.Mathf.Clamp(
            (ConfigEntries[nameof(OverlayEdgePadding)] as ConfigEntry<int>)?.Value ?? 6,
            OVERLAY_EDGE_PADDING_MIN, OVERLAY_EDGE_PADDING_MAX);
    public static void SetOverlayEdgePadding(int v) => SetInt(nameof(OverlayEdgePadding),
        UnityEngine.Mathf.Clamp(v, OVERLAY_EDGE_PADDING_MIN, OVERLAY_EDGE_PADDING_MAX));

    // 0.10.7: prestige-progress sub-line inside the main XP/expertise/legacy
    // bars. Eclipse renders a thin secondary fill ABOVE/BELOW the main bar
    // showing how far you are toward the next prestige tier; users asked for
    // the same. Implementation draws a slim 25%-height inset bar inside the
    // existing MiniBar. Off by default.
    public static bool ShowPrestigeSubLine =>
        (ConfigEntries[nameof(ShowPrestigeSubLine)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetShowPrestigeSubLine(bool v) => SetBool(nameof(ShowPrestigeSubLine), v);

    // 0.10.7: V-Bloods tab view mode. "Chips" (default) — one row per
    // V-Blood NAME with the B/S/P/Ps capture chips (matches 0.10.0..0.10.6
    // behavior). "Instances" — one row per CAPTURED FAMILIAR (level / box
    // / shiny / primal / summon button), enumerated from PlayerStateService
    // .BoxContents. The instance view requires the user to have navigated
    // each box that contains V-Bloods at least once (so its contents are
    // cached); future "Deep Scan" feature will automate the sweep.
    public static bool VBloodPerInstanceView =>
        (ConfigEntries[nameof(VBloodPerInstanceView)] as ConfigEntry<bool>)?.Value ?? false;
    public static void SetVBloodPerInstanceView(bool v) => SetBool(nameof(VBloodPerInstanceView), v);

    // 0.10.6: Chat Logging — three per-category visibility toggles + master
    // helpers for the diagnostic Settings section. Suppression only applies
    // to commands whose replies BCH already shows in its own UI surfaces
    // (BoxList, BloodInfo, V-Blood scanner, etc.). Commands whose replies
    // would otherwise be invisible (admin actions, KindredCommands without
    // structured parsing) stay visible regardless of these toggles — see
    // CommandClassification.HasBchUIDisplay.
    //
    // Defaults:
    //   ShowChatBchAuto    = false (silent; BCH-fired auto traffic stays hidden)
    //   ShowChatBloodcraft = true  (user sees their Bloodcraft command replies)
    //   ShowChatKindred    = true  (user sees their Kindred command replies)
    //
    // These DO NOT touch ClearServerMessages (the global admin clear) — that
    // remains a separate setting with its own meaning.
    public static bool ShowChatBchAuto =>
        (ConfigEntries[nameof(ShowChatBchAuto)] as ConfigEntry<bool>)?.Value ?? false;
    public static bool ShowChatBloodcraft =>
        (ConfigEntries[nameof(ShowChatBloodcraft)] as ConfigEntry<bool>)?.Value ?? true;
    public static bool ShowChatKindred =>
        (ConfigEntries[nameof(ShowChatKindred)] as ConfigEntry<bool>)?.Value ?? true;
    public static void SetShowChatBchAuto(bool v)    => SetBool(nameof(ShowChatBchAuto), v);
    public static void SetShowChatBloodcraft(bool v) => SetBool(nameof(ShowChatBloodcraft), v);
    public static void SetShowChatKindred(bool v)    => SetBool(nameof(ShowChatKindred), v);

    /// <summary>Master "show all" — flip every Chat Logging category on.</summary>
    public static void ShowAllChat()
    {
        SetShowChatBchAuto(true);
        SetShowChatBloodcraft(true);
        SetShowChatKindred(true);
    }

    /// <summary>Master "hide all" — flip every Chat Logging category off. Does
    /// NOT touch ClearServerMessages (that's a separate admin-level setting).</summary>
    public static void HideAllChat()
    {
        SetShowChatBchAuto(false);
        SetShowChatBloodcraft(false);
        SetShowChatKindred(false);
    }

    // 0.10.1: sort order applied to the Familiar Browser overlay's familiar
    // list AND the V-Bloods tab grid. Persisted as an int (the enum value)
    // so adding new modes later doesn't break the saved config.
    //   Default      — server-provided order (familiars: as listed by .fam l;
    //                  V-Bloods: alphabetical from VBloodRegistry)
    //   Alphabetical — by name, ascending
    //   Level        — by familiar level, DESCENDING (high to low)
    //   Location     — by map region. RESERVED; not selectable in 0.10.1
    //                  because we lack a canonical location table; planned
    //                  for 0.10.2 with user-supplied data.
    public enum FamiliarSortOrder { Default = 0, Alphabetical = 1, Level = 2, Location = 3 }

    // 0.10.2: text alignment for overlay rows. Default Left preserves the
    // pre-0.10.2 visual; Right is for users who pin the overlay to the
    // right edge of the screen and want the values closer to the panel
    // border so the eye doesn't have to track left.
    public enum OverlayAlignment { Left = 0, Right = 1 }

    public static OverlayAlignment OverlayTextAlignmentSetting
    {
        get
        {
            var raw = (ConfigEntries[nameof(OverlayTextAlignmentSetting)] as ConfigEntry<int>)?.Value ?? 0;
            if (raw < 0 || raw > 1) raw = 0;
            return (OverlayAlignment)raw;
        }
    }
    public static void SetOverlayTextAlignment(OverlayAlignment v)
    {
        if (ConfigEntries.TryGetValue(nameof(OverlayTextAlignmentSetting), out var entry) && entry is ConfigEntry<int> i)
            i.Value = (int)v;
    }

    public static FamiliarSortOrder FamiliarSortOrderSetting
    {
        get
        {
            var raw = (ConfigEntries[nameof(FamiliarSortOrderSetting)] as ConfigEntry<int>)?.Value ?? 0;
            // Defensive clamp so a corrupted cfg can't crash the dropdown logic.
            if (raw < 0 || raw > 3) raw = 0;
            return (FamiliarSortOrder)raw;
        }
    }
    public static void SetFamiliarSortOrder(FamiliarSortOrder v)
    {
        if (ConfigEntries.TryGetValue(nameof(FamiliarSortOrderSetting), out var entry) && entry is ConfigEntry<int> i)
            i.Value = (int)v;
    }

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

    private static void SetInt(string key, int value)
    {
        if (ConfigEntries.TryGetValue(key, out var entry) && entry is ConfigEntry<int> i)
            i.Value = value;
    }

    // Auto-resize: main panel grows vertically to fit content (capped at 90% of
    // screen height). User-toggleable via the footer checkbox - some players
    // prefer a fixed-size panel they can manually resize.
    public static bool IsPanelAutoResizeEnabled =>
        (ConfigEntries[nameof(IsPanelAutoResizeEnabled)] as ConfigEntry<bool>)?.Value ?? true;
    // 0.9.7: setter for the new Size & Positioning section's Auto-size button.
    // The existing footer toggle writes via direct Config.Bind — exposing a
    // proper setter keeps the new callsite aligned with how every other
    // Settings flag handles writes (Set* methods + SetBool helper).
    public static void SetIsPanelAutoResizeEnabled(bool v) => SetBool(nameof(IsPanelAutoResizeEnabled), v);

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
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowProgressBars),            false, "Show experience progress and prestige progress as horizontal bars alongside the % numbers. Affects the XP overlay (XP%) and the Prestige info box (level/max). Off by default; toggle in Display settings.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowOverlayBonusStats),       false, "Show the chosen bonus-stat names AND their current numeric values for your weapon expertise and blood legacy on the XP overlay. Auto-fetches .wep get + .bl get every 10s while the overlay is visible. Off by default for a minimal overlay; toggle in Display settings.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowOverlayXpCounter),        false, "Show a numerical XP-progress row under Weapon and Legacy on the XP overlay (e.g. \"Exp: 123 / 4500 (2.7%)\"). Values come from parsing .wep get / .bl get chat replies. Off by default — the existing 'Lv X (P%)' title row is sufficient for most users.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ProgressBarHeight),           8,     "Progress bar height in pixels when 'Scale bar with overlay' is OFF. Clamped 4..24. Default 8.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ProgressBarHeightRelative),   false, "Scale progress bar height with the overlay (pre-0.10.7 behavior). Off by default — bars stay at the fixed pixel height regardless of overlay size.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(OverlayEdgePadding),          6,     "Left/right inner padding (pixels) applied to every overlay's content. Prevents text from sitting flush with the panel border. Clamped 0..32. Default 6. Applied at overlay construction; toggle an overlay off and back on (or change the overlay text scale) to pick up a new value live.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(AutoScanVBloodsOnTabOpen),    false, "Automatically run a V-Blood scan the first time you open the V-Bloods tab in a session. Off by default — the scanner switches your active box ~10-15 times to walk all boxes; the user-controlled 'Scan all' button is the default trigger. Turn on if you want the scan to fire without a click.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(LockOverlays),                false, "Lock the position and size of every overlay so they can't be moved or resized by accident during play. Programmatic resize when settings change (e.g. enabling progress bars on the XP overlay) still works. Toggle via the 'Lock overlays' switch beside Auto-resize on the main panel.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(ShowPrestigeSubLine),         false, "Show a thin secondary fill inside the main XP/expertise/legacy bars reflecting prestige progress, like Eclipse's overlay. Off by default.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(VBloodPerInstanceView),       false, "V-Bloods tab: render one row per CAPTURED FAMILIAR (level / box / shiny / primal / summon) instead of one row per V-Blood name with capture chips. Requires box contents to have been cached (navigate each box at least once). Off by default — chip view shows the full registry at a glance.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(FamiliarSortOrderSetting),    0,     "Sort order for the Familiar Browser overlay and the V-Bloods tab. 0=Default (server/registry order), 1=Alphabetical by name, 2=By level (descending), 3=By region. Cycle the Sort button in either UI to change.");
        InitConfigEntry(UI_SETTINGS_GROUP,      nameof(OverlayTextAlignmentSetting), 0,     "Overlay text alignment. 0=Left (default), 1=Right. Toggle in Display Settings → HUD extras. Rebuilds open overlays so the change takes effect immediately.");
        // 0.10.6: Chat Logging diagnostic toggles. Default hides BCH's own
        // auto-fired chat (V-Blood scanner, overlay bonus-stats refresh, tab
        // auto-refresh — all silent by default). Bloodcraft + Kindred default
        // visible so user-initiated commands surface their replies as today.
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(ShowChatBchAuto),             false, "Diagnostic: show BCH's own auto-fired command replies in chat (V-Blood scanner, overlay bonus-stats refresh, etc.). Off by default — turn on if a BCH feature isn't working and you want to see the raw server replies.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(ShowChatBloodcraft),          true,  "Show chat copies of replies to Bloodcraft commands whose data BCH already mirrors to its UI (BoxList, BloodInfo, etc.). On = chat + UI both show the info. Off = UI-only (less chat noise). Action confirmations and admin replies that BCH doesn't parse stay visible regardless.");
        InitConfigEntry(GENERAL_SETTINGS_GROUP, nameof(ShowChatKindred),             true,  "Same as the Bloodcraft toggle but for KindredCommands / KindredLogistics replies BCH structurally parses. BCH doesn't structurally parse any Kindred replies yet, so this toggle has no effect today; reserved for when Kindred structured parsing lands in a future version.");
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
