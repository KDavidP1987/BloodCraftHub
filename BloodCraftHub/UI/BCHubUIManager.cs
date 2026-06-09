using System;
using System.Collections.Generic;
using BloodCraftHub.Services;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent;
using BloodCraftHub.UI.ModContent.Data;
using UIManagerBase = BloodCraftHub.UI.Framework.ModernLib.UIManagerBase;

namespace BloodCraftHub.UI;

// Root UI controller. Owns:
//   - the always-visible floating toggle button
//   - the main tabbed panel (hidden by default)
//   - the two secondary overlays (hidden by default)
//
// Flow:
//   1. Plugin.Load -> new BCHubUIManager()
//   2. InitializationPatch fires once the player is in-world -> SetupAndShowUI()
//   3. SetupAndShowUI() creates the floating button (and only the button).
//   4. Clicking the button toggles the main panel.
//   5. The main panel exposes checkboxes that toggle the overlays.
//
// PanelType values from BloodCraftHub.UI.ModContent.Data.PanelType drive routing.
public class BCHubUIManager : UIManagerBase
{
    private readonly List<IPanelBase> _panels = new();

    private FloatingButtonPanel _floatingButton;
    private MainPanel _mainPanel;
    private ExperienceOverlayPanel _experienceOverlay;
    private FamiliarOverlayPanel _familiarOverlay;
    private FamiliarBrowserOverlayPanel _familiarBrowserOverlay;
    private DailyQuestOverlayPanel _dailyQuestOverlay;
    private ProfessionOverlayPanel _professionOverlay;
    private ShiftSpellOverlayPanel _shiftSpellOverlay;
    private QuickActionsOverlayPanel _quickActionsOverlay; // 0.16: one-click Kindred action buttons (Stash All)
    private BeelzActionBarOverlayPanel _beelzActionBarOverlay; // 0.18: Beelzebub extra-ability buttons + cooldown rings
    private BeelzSummonsOverlayPanel _beelzSummonsOverlay; // 0.19: one-click stash/restore for Beelzebub summons
    private BeelzTransformOverlayPanel _beelzTransformOverlay; // 0.20: browser-style transform/phase/revert overlay
    private UrielSharedOverlayPanel _urielSharedOverlay; // 0.26: nearby public-storage badges (client-side detection)
    private UrielObjectSpawnerOverlayPanel _urielObjectSpawnerOverlay; // 0.29: quick-build object-spawn palette
    private ChatWindowOverlayPanel _chatWindowOverlay; // 0.17: standalone tabbed chat window
    private SecondaryChatOverlayPanel _secondaryChatOverlay; // 0.24: view-only second chat window (channel subset)
    private ProjectM.UI.HUDChatWindow _nativeChat; // 0.17: cached native chat window (for the takeover)
    // 0.14.0: single combined info overlay. Mutually exclusive with the 4
    // standalone info overlays (XP / Familiar / Daily Quest / Profession);
    // when ShowCombinedOverlay is true, those are hidden regardless of
    // their individual ShowXxxOverlay flags. FamiliarBrowser + ShiftSpell
    // stay independent — they're not info-readout overlays.
    private CombinedOverlayPanel _combinedOverlay;

    // 0.9.0: session-only flag flipped by the master-overlay button on the
    // floating-button strip. When true, every overlay is hidden regardless
    // of its individual SetActive state; when flipped back to false, overlays
    // are re-shown ONLY if their per-overlay Settings flag is true (so the
    // master toggle never resurrects overlays the user has disabled in
    // config). Mirrors Settings.OverlaysSuppressedByUser; we keep the local
    // copy here for the per-frame visibility application.
    private bool _overlaysSuppressed;

    // 0.28: seconds left on a TIMED master-hide before overlays auto-reappear. 0 = no timed hide
    // pending (sticky-toggle mode, or overlays aren't currently hidden). Ticked down each frame by
    // TickOverlayHideTimer; armed in ToggleAllOverlaysSuppressed when Timed mode is on.
    private float _hideTimerRemaining;

    // 0.16.x: tracks whether the whole BCH UIBase is active (false while the
    // escape menu is up). Drives RefreshFloatingButtonVisibility.
    private bool _uiActive = true;

    // 0.18.1 logout/relog visibility. The UIManager + its canvas are DontDestroyOnLoad, so
    // they SURVIVE leaving the game — which is exactly why every overlay used to linger over
    // the main menu after "Leave Game". We can't hide them inside the ClientBootstrapSystem.OnDestroy
    // teardown hook (any GameObject work there native-crashes the disposing world). Instead:
    //   - logout sets _hideForLogoutPending (pure flag) + _loggedOut,
    //   - the next CoreUpdateBehavior tick (TickPendingHide, runs in the main menu AFTER teardown)
    //     does the actual SetActive(false),
    //   - relog (CharacterHUDEntry.Awake -> UIOnInitialize) re-shows via RestoreAfterRelogIfNeeded.
    // _loggedOut gates the relog restore so a HUD Awake that ISN'T a relog can't thrash the overlays.
    private bool _hideForLogoutPending;
    private bool _loggedOut;

    public bool IsMainPanelOpen => _mainPanel != null && _mainPanel.Enabled;

    // 0.9.7: per-panel accessors so the Size & Positioning settings section
    // can adjust each panel's width/height and reset-to-default. The Ensure*
    // construction is on-demand by design (overlays don't exist until the
    // user toggles them on), so these can return null and callers must
    // null-check before invoking AdjustSize / SetDefaultSizeAndPosition.
    public MainPanel                    MainPanel             => _mainPanel;
    public ExperienceOverlayPanel       ExperienceOverlay     => _experienceOverlay;
    public FamiliarOverlayPanel         FamiliarOverlay       => _familiarOverlay;
    public FamiliarBrowserOverlayPanel  FamiliarBrowserOverlay => _familiarBrowserOverlay;
    public DailyQuestOverlayPanel       DailyQuestOverlay     => _dailyQuestOverlay;
    public ProfessionOverlayPanel       ProfessionOverlay     => _professionOverlay;
    public ShiftSpellOverlayPanel       ShiftSpellOverlay     => _shiftSpellOverlay;
    public CombinedOverlayPanel         CombinedOverlay       => _combinedOverlay;

    /// <summary>0.10.14: read the global Settings.LockOverlays toggle and
    /// apply IsPinned to every currently-constructed overlay. Called when
    /// the user flips the lock switch on the main panel — overlays not
    /// yet constructed will pick up the same state via
    /// ResizeablePanelBase.LateConstructUI when they're built.</summary>
    public void ApplyOverlayLockState()
    {
        bool pinned = BloodCraftHub.Config.Settings.LockOverlays;
        ApplyPinnedTo(_experienceOverlay, pinned);
        ApplyPinnedTo(_familiarOverlay, pinned);
        ApplyPinnedTo(_familiarBrowserOverlay, pinned);
        ApplyPinnedTo(_dailyQuestOverlay, pinned);
        ApplyPinnedTo(_professionOverlay, pinned);
        ApplyPinnedTo(_shiftSpellOverlay, pinned);
        ApplyPinnedTo(_quickActionsOverlay, pinned);
        ApplyPinnedTo(_beelzActionBarOverlay, pinned);
        ApplyPinnedTo(_beelzSummonsOverlay, pinned);
        ApplyPinnedTo(_beelzTransformOverlay, pinned);
        ApplyPinnedTo(_urielSharedOverlay, pinned);
        ApplyPinnedTo(_urielObjectSpawnerOverlay, pinned);
        ApplyPinnedTo(_chatWindowOverlay, pinned);
        ApplyPinnedTo(_secondaryChatOverlay, pinned);
        ApplyPinnedTo(_combinedOverlay, pinned);
    }

    private static void ApplyPinnedTo(ResizeablePanelBase panel, bool pinned)
    {
        if (panel == null) return;
        // PanelDragger checks UIPanel.IsPinned on every Update — flipping
        // it here takes effect immediately on the next frame.
        panel.IsPinned = pinned;
    }

    public override void Reset()
    {
        base.Reset();
        foreach (var p in _panels)
        {
            if (p is ResizeablePanelBase r) r.Reset();
            p.Destroy();
        }
        _panels.Clear();
        _floatingButton = null;
        _mainPanel = null;
        _experienceOverlay = null;
        _familiarOverlay = null;
        _familiarBrowserOverlay = null;
        _dailyQuestOverlay = null;
        _professionOverlay = null;
        _combinedOverlay = null;
        _shiftSpellOverlay = null;
        _quickActionsOverlay = null;
        _beelzActionBarOverlay = null;
        _beelzSummonsOverlay = null;
        _beelzTransformOverlay = null;
        _urielSharedOverlay = null;
        _urielObjectSpawnerOverlay = null;
    }

    protected override void AddMainContentPanel()
    {
        // The "main content" in our world is just the floating toggle.
        // Everything else (main panel, overlays) is created lazily on demand.
        _floatingButton = new FloatingButtonPanel(UiBase);
        _panels.Add(_floatingButton);
    }

    public override void SetActive(bool active)
    {
        // When the whole UIBase is disabled (e.g. escape menu open), hide everything;
        // restore visibility when re-enabled. Each panel keeps its own previous-state.
        _uiActive = active;
        _mainPanel?.SetActive(active && IsMainPanelOpen);
        _experienceOverlay?.SetActive(active && (_experienceOverlay?.Enabled ?? false));
        _familiarOverlay?.SetActive(active && (_familiarOverlay?.Enabled ?? false));
        _familiarBrowserOverlay?.SetActive(active && (_familiarBrowserOverlay?.Enabled ?? false));
        _dailyQuestOverlay?.SetActive(active && (_dailyQuestOverlay?.Enabled ?? false));
        _professionOverlay?.SetActive(active && (_professionOverlay?.Enabled ?? false));
        _shiftSpellOverlay?.SetActive(active && (_shiftSpellOverlay?.Enabled ?? false));
        _quickActionsOverlay?.SetActive(active && (_quickActionsOverlay?.Enabled ?? false));
        // 0.18.1: these three were added after SetActive was first written and were never
        // included here — so hiding the UI (escape menu, and the new logout teardown) left them
        // visible. The Combined overlay / Chat window / Beelz action bar are the overlays that
        // "lingered over the main menu" after logout; include them so a hide covers everything.
        _combinedOverlay?.SetActive(active && (_combinedOverlay?.Enabled ?? false));
        _chatWindowOverlay?.SetActive(active && (_chatWindowOverlay?.Enabled ?? false));
        _secondaryChatOverlay?.SetActive(active && (_secondaryChatOverlay?.Enabled ?? false));
        _beelzActionBarOverlay?.SetActive(active && (_beelzActionBarOverlay?.Enabled ?? false));
        _beelzSummonsOverlay?.SetActive(active && (_beelzSummonsOverlay?.Enabled ?? false));
        _beelzTransformOverlay?.SetActive(active && (_beelzTransformOverlay?.Enabled ?? false));
        _urielSharedOverlay?.SetActive(active && (_urielSharedOverlay?.Enabled ?? false));
        _urielObjectSpawnerOverlay?.SetActive(active && (_urielObjectSpawnerOverlay?.Enabled ?? false));
        // 0.16.x: floating launcher follows a single visibility rule (below).
        RefreshFloatingButtonVisibility();
    }

    /// <summary>0.18.1: queue a full BCH-UI hide because the player left the game. SAFE to call
    /// from the ClientBootstrapSystem.OnDestroy teardown hook — pure flag assignment, no UI/ECS
    /// work (the actual hide happens on the next CoreUpdateBehavior tick, see TickPendingHide).
    /// Without this, every overlay + the floating launcher lingered over the main menu after
    /// "Leave Game" because the canvas is DontDestroyOnLoad.</summary>
    public void RequestHideForLogout()
    {
        _hideForLogoutPending = true;
        _loggedOut = true;
    }

    /// <summary>0.18.1: per-frame (CoreUpdateBehavior, registered in Plugin.Load). When a logout
    /// hide is queued, hide everything. Runs in the MAIN MENU after world teardown has completed,
    /// so the GameObject toggles in SetActive(false) are safe (unlike doing them in OnDestroy).
    /// No-op (one bool check) the rest of the time.</summary>
    public void TickPendingHide()
    {
        if (!_hideForLogoutPending) return;
        _hideForLogoutPending = false;
        try { SetActive(false); }   // hides main panel + every overlay + floating launcher
        catch (System.Exception ex) { BloodCraftHub.Utils.LogUtils.LogDebug($"Logout UI hide failed: {ex.Message}"); }
    }

    /// <summary>0.18.1: re-show BCH UI after the player re-enters a world (relog). The UIManager +
    /// canvas persist across logout and IsInitialized stays true, so UIOnInitialize routes here
    /// instead of rebuilding. Gated on _loggedOut so a HUD Awake that isn't a relog (IsInitialized
    /// already true but we never left the game) is a no-op and can't thrash the overlays. Restores
    /// the floating launcher and re-runs the saved-overlay restore (config-driven via
    /// Settings.Show*), so the user gets back exactly the overlays they had.</summary>
    public void RestoreAfterRelogIfNeeded()
    {
        if (!_loggedOut) return;
        _loggedOut = false;
        _hideForLogoutPending = false;   // a relog cancels any still-queued hide
        _uiActive = true;

        // 0.28: SAFETY — a relog always returns to configured visibility. Clear the session-only
        // master-hide (and any pending timed-hide countdown) BEFORE refreshing the launcher, so the
        // user can never re-enter a world still suppressed. Without this, a hide with "Hide buttons
        // too" + a hotkey-only escape would leave the BCH/OV launcher hidden after a relog — a clean
        // screen with no visible way back. Mirrors the "reset on game restart" intent of the flag.
        _overlaysSuppressed = false;
        _hideTimerRemaining = 0f;
        BloodCraftHub.Config.Settings.OverlaysSuppressedByUser = false;

        RefreshFloatingButtonVisibility();

        // 0.18.3: reset to the "unavailable until confirmed" baseline for the NEW server. The
        // protocol services were Reset on logout (UserRegistered=false / IsPresent=false), so:
        //   - grey the Bloodcraft + Beelzebub tab groups back out (they re-light on this server's
        //     handshake ACK, via AvailabilityChanged), and
        //   - hide any BC/Beelz overlay that lingered, so the new server re-detects from scratch
        //     instead of inheriting the previous server's tab/overlay state.
        try { _mainPanel?.RefreshTabGroupAvailabilityNow(); } catch { }
        try { ApplyAvailabilityToOverlays(); } catch { }
        // 0.18.3: wipe the chat window (scrollback was cleared in the teardown hook via
        // ChatRelayService.Clear(); this clears any half-typed message + repaints empty) so a
        // server-switch doesn't carry the previous server's chat into the new one.
        try { _chatWindowOverlay?.ResetForServerSwitch(); } catch { }

        ScheduleOverlayRestore();        // same deferred, config-driven path as first login
    }

    /// <summary>Show or hide the main tabbed panel.</summary>
    public void ToggleMainPanel()
    {
        EnsureMainPanel();
        bool nextState = !_mainPanel.Enabled;
        BloodCraftHub.Utils.LogUtils.LogDiagnostic($"ToggleMainPanel: {_mainPanel.Enabled} -> {nextState}");
        _mainPanel.SetActive(nextState);
        RefreshFloatingButtonVisibility();
    }

    /// <summary>0.16: hide the always-on-top floating launcher while the main
    /// panel is fullscreen. On small/laptop monitors the panel's own title-bar
    /// close/restore controls land underneath the floating cluster, which —
    /// being always on top — intercepted the click so the user couldn't close
    /// the panel. The launcher is redundant while the panel is maximized (close
    /// or restore via the title bar), so we simply hide it for the duration.</summary>
    internal void OnMainPanelFullscreenChanged(bool fullscreen)
    {
        RefreshFloatingButtonVisibility();
    }

    // 0.16.x: single source of truth for the floating launcher's visibility.
    // Hidden ONLY while the main panel is open AND fullscreen (so the panel's own
    // close/restore controls aren't intercepted); otherwise it follows the overall
    // UI active state. Centralizing this prevents the launcher being orphaned
    // (hidden with no way to reopen the panel) by the close / escape-menu /
    // fullscreen-exit paths.
    internal void RefreshFloatingButtonVisibility()
    {
        bool hideForFullscreen = (_mainPanel?.Enabled ?? false) && (_mainPanel?.IsFullscreen ?? false);
        // 0.28: optionally hide the launcher cluster along with the master overlay hide.
        // CanHideLauncherButtons gates this on a guaranteed way back (timed auto-restore, or a bound
        // hide-all hotkey), so a misconfiguration can never strand the user with the panel unreachable.
        bool hideForOverlaySuppress = _overlaysSuppressed && BloodCraftHub.Config.Settings.CanHideLauncherButtons;
        _floatingButton?.SetActive(_uiActive && !hideForFullscreen && !hideForOverlaySuppress);
    }

    /// <summary>Show or hide a specific tab inside the main panel (and bring the panel up if needed).</summary>
    public void ShowTab(PanelType tab)
    {
        EnsureMainPanel();
        _mainPanel.SetActive(true);
        _mainPanel.ShowTab(tab);
        RefreshFloatingButtonVisibility();
    }

    /// <summary>Toggle one of the secondary overlays.</summary>
    public void ToggleOverlay(PanelType overlay)
    {
        BloodCraftHub.Utils.LogUtils.LogDiagnostic($"ToggleOverlay({overlay}).");
        switch (overlay)
        {
            case PanelType.ExperienceOverlay:
                EnsureExperienceOverlay();
                _experienceOverlay.SetActive(!_experienceOverlay.Enabled);
                BloodCraftHub.Config.Settings.SetShowExperienceOverlay(_experienceOverlay.Enabled);
                break;
            case PanelType.FamiliarOverlay:
                EnsureFamiliarOverlay();
                _familiarOverlay.SetActive(!_familiarOverlay.Enabled);
                BloodCraftHub.Config.Settings.SetShowFamiliarOverlay(_familiarOverlay.Enabled);
                break;
            case PanelType.FamiliarBrowserOverlay:
                EnsureFamiliarBrowserOverlay();
                _familiarBrowserOverlay.SetActive(!_familiarBrowserOverlay.Enabled);
                BloodCraftHub.Config.Settings.SetShowFamiliarBrowser(_familiarBrowserOverlay.Enabled);
                break;
            case PanelType.DailyQuestOverlay:
                EnsureDailyQuestOverlay();
                _dailyQuestOverlay.SetActive(!_dailyQuestOverlay.Enabled);
                BloodCraftHub.Config.Settings.SetShowDailyQuestOverlay(_dailyQuestOverlay.Enabled);
                break;
            case PanelType.ProfessionOverlay:
                EnsureProfessionOverlay();
                _professionOverlay.SetActive(!_professionOverlay.Enabled);
                BloodCraftHub.Config.Settings.SetShowProfessionOverlay(_professionOverlay.Enabled);
                break;
            case PanelType.ShiftSpellOverlay:
                EnsureShiftSpellOverlay();
                _shiftSpellOverlay.SetActive(!_shiftSpellOverlay.Enabled);
                BloodCraftHub.Config.Settings.SetShowShiftSpellOverlay(_shiftSpellOverlay.Enabled);
                break;
            case PanelType.QuickActionsOverlay:
                EnsureQuickActionsOverlay();
                _quickActionsOverlay.SetActive(!_quickActionsOverlay.Enabled);
                BloodCraftHub.Config.Settings.SetShowQuickActionsOverlay(_quickActionsOverlay.Enabled);
                break;
            case PanelType.BeelzActionBarOverlay:
                EnsureBeelzActionBarOverlay();
                _beelzActionBarOverlay.SetActive(!_beelzActionBarOverlay.Enabled);
                BloodCraftHub.Config.Settings.SetShowBeelzActionBarOverlay(_beelzActionBarOverlay.Enabled);
                break;
            case PanelType.BeelzSummonsOverlay:
                EnsureBeelzSummonsOverlay();
                _beelzSummonsOverlay.SetActive(!_beelzSummonsOverlay.Enabled);
                BloodCraftHub.Config.Settings.SetShowBeelzSummonsOverlay(_beelzSummonsOverlay.Enabled);
                break;
            case PanelType.BeelzTransformOverlay:
                EnsureBeelzTransformOverlay();
                _beelzTransformOverlay.SetActive(!_beelzTransformOverlay.Enabled);
                BloodCraftHub.Config.Settings.SetShowBeelzTransformOverlay(_beelzTransformOverlay.Enabled);
                break;
            case PanelType.UrielSharedOverlay:
                EnsureUrielSharedOverlay();
                _urielSharedOverlay.SetActive(!_urielSharedOverlay.Enabled);
                BloodCraftHub.Config.Settings.SetShowUrielSharedOverlay(_urielSharedOverlay.Enabled);
                break;
            case PanelType.UrielObjectSpawnerOverlay:
                EnsureUrielObjectSpawnerOverlay();
                _urielObjectSpawnerOverlay.SetActive(!_urielObjectSpawnerOverlay.Enabled);
                BloodCraftHub.Config.Settings.SetShowUrielObjectSpawnerOverlay(_urielObjectSpawnerOverlay.Enabled);
                break;
            case PanelType.ChatWindowOverlay:
                EnsureChatWindowOverlay();
                _chatWindowOverlay.SetActive(!_chatWindowOverlay.Enabled);
                BloodCraftHub.Config.Settings.SetShowChatWindowOverlay(_chatWindowOverlay.Enabled);
                ApplyNativeChatVisibility();
                break;
            case PanelType.SecondaryChatOverlay:
                EnsureSecondaryChatOverlay();
                _secondaryChatOverlay.SetActive(!_secondaryChatOverlay.Enabled);
                BloodCraftHub.Config.Settings.SetShowSecondaryChatOverlay(_secondaryChatOverlay.Enabled);
                break;
            case PanelType.CombinedOverlay:
                // 0.14.0: toggling combined-mode swaps which set of overlays
                // is visible. ApplyCombinedOverlayMutualExclusion does the
                // heavy lifting so the same logic drives footer toggles,
                // Settings checkbox flips, and startup restore.
                bool newOn = !(_combinedOverlay?.Enabled ?? false);
                BloodCraftHub.Config.Settings.SetShowCombinedOverlay(newOn);
                ApplyCombinedOverlayMutualExclusion();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(overlay), overlay, "Not a secondary overlay.");
        }
    }

    /// <summary>0.14.0: enforce the mutual exclusion between the combined
    /// overlay and the 4 standalone info overlays it replaces. Called any
    /// time Settings.ShowCombinedOverlay flips (footer toggle, Settings
    /// checkbox, startup restore). The individual ShowXxxOverlay flags are
    /// NEVER mutated here — they persist independently so toggling combined
    /// off restores whatever the user had before.</summary>
    public void ApplyCombinedOverlayMutualExclusion()
    {
        bool combined = BloodCraftHub.Config.Settings.ShowCombinedOverlay;
        if (combined)
        {
            EnsureCombinedOverlay();
            _combinedOverlay.SetActive(true);
            // Hide the four info overlays the combined panel replaces.
            // FamiliarBrowser + ShiftSpell are NOT info overlays and stay
            // independent.
            // 0.14.0 friend-test v6: always EnsureExperienceOverlay even when
            // combined is on. ExperienceOverlay owns the auto-fetch ticker
            // for .wep get + .bl get; combined reads its cached data to
            // render Bonus Stats + XP Counter sub-rows. Without ensuring
            // construction, the ticker doesn't exist and combined-mode users
            // get no live values.
            EnsureExperienceOverlay();
            _experienceOverlay.SetActive(false);
            _familiarOverlay?.SetActive(false);
            _dailyQuestOverlay?.SetActive(false);
            _professionOverlay?.SetActive(false);
        }
        else
        {
            _combinedOverlay?.SetActive(false);
            // Restore the four info overlays per their individual config flags.
            if (BloodCraftHub.Config.Settings.ShowExperienceOverlay)
            { EnsureExperienceOverlay(); _experienceOverlay.SetActive(true); }
            if (BloodCraftHub.Config.Settings.ShowFamiliarOverlay)
            { EnsureFamiliarOverlay();   _familiarOverlay.SetActive(true); }
            if (BloodCraftHub.Config.Settings.ShowDailyQuestOverlay)
            { EnsureDailyQuestOverlay(); _dailyQuestOverlay.SetActive(true); }
            if (BloodCraftHub.Config.Settings.ShowProfessionOverlay)
            { EnsureProfessionOverlay(); _professionOverlay.SetActive(true); }
        }
    }

    /// <summary>0.14.0: push per-section visibility changes from the Settings
    /// tab (CombinedOverlayShowXxx checkboxes) into the live combined
    /// overlay without rebuilding. No-op when the panel isn't constructed.</summary>
    public void RefreshCombinedOverlaySections() => _combinedOverlay?.RefreshSections();

    /// <summary>0.9.0: master overlay show/hide. Flips a session-only flag and
    /// applies it to every overlay. Crucially this never *enables* an overlay
    /// the user has disabled via the per-overlay footer toggle — when
    /// un-suppressing, each overlay is only re-shown if its config flag is
    /// true AND we previously suppressed it. Visibility persistence stays on
    /// the per-overlay Settings flags; this toggle is purely transient.</summary>
    public void ToggleAllOverlaysSuppressed()
    {
        _overlaysSuppressed = !_overlaysSuppressed;
        BloodCraftHub.Config.Settings.OverlaysSuppressedByUser = _overlaysSuppressed;

        // 0.28: timed-hide bookkeeping. Starting a hide while Timed mode is on arms the auto-restore
        // countdown; any un-hide (manual toggle, hotkey, or the timer firing) clears it so a stale timer
        // can't re-hide later. Read the duration once, here, so a mid-hide settings change can't strand it.
        if (_overlaysSuppressed && BloodCraftHub.Config.Settings.OverlayTimedHide)
            _hideTimerRemaining = BloodCraftHub.Config.Settings.OverlayHideDurationSeconds;
        else
            _hideTimerRemaining = 0f;

        ApplyOverlaySuppression();
        RefreshFloatingButtonVisibility(); // 0.28: the launcher cluster may hide/show with the overlays
    }

    public bool AreOverlaysSuppressed => _overlaysSuppressed;

    /// <summary>0.28: seconds remaining on a pending timed master-hide (0 when none). Exposed for the
    /// Settings UI / potential on-screen countdown.</summary>
    public float OverlayHideSecondsRemaining => _hideTimerRemaining;

    /// <summary>0.28: per-frame countdown for a TIMED master-hide. When the user hides overlays while
    /// Timed mode is on, this ticks the remaining duration down and auto-restores (un-suppresses) on
    /// expiry. No-op (one float compare) when no timed hide is pending. Registered on
    /// CoreUpdateBehavior.Actions; never throws into the per-frame pump.</summary>
    public void TickOverlayHideTimer()
    {
        if (_hideTimerRemaining <= 0f) return;
        // Defensive: if something else un-suppressed us, drop the timer.
        if (!_overlaysSuppressed) { _hideTimerRemaining = 0f; return; }

        _hideTimerRemaining -= UnityEngine.Time.deltaTime;
        if (_hideTimerRemaining > 0f) return;

        _hideTimerRemaining = 0f;
        try { ToggleAllOverlaysSuppressed(); } // flip back to visible via the same path a manual un-hide takes
        catch (System.Exception ex) { BloodCraftHub.Utils.LogUtils.LogDebug($"TickOverlayHideTimer restore failed: {ex.Message}"); }
    }

    private void ApplyOverlaySuppression()
    {
        // 0.18.3: availability gates mirror ApplyAvailabilityToOverlays (confirmed-present only)
        // so un-suppressing on a server that lacks Bloodcraft/Beelzebub doesn't resurrect empty
        // overlays, and the un-suppress can't out-race detection on a fresh relog.
        bool bcAvailable = !Services.EclipseProtocolService.StandDownForEclipse()
                           && Services.EclipseProtocolService.UserRegistered;
        bool beelzAvailable = Services.Beelzebub.BeelzProtocolService.IsPresent;
        bool urielAvailable = Services.Uriel.UrielProtocolService.IsPresent;

        if (_overlaysSuppressed)
        {
            // Hide whatever is open. We do NOT touch each overlay's
            // per-config Settings.Show* flag so the original visibility
            // preference survives.
            _experienceOverlay?.SetActive(false);
            _familiarOverlay?.SetActive(false);
            _familiarBrowserOverlay?.SetActive(false);
            _dailyQuestOverlay?.SetActive(false);
            _professionOverlay?.SetActive(false);
            _shiftSpellOverlay?.SetActive(false);
            _quickActionsOverlay?.SetActive(false);
            _combinedOverlay?.SetActive(false);
            _beelzActionBarOverlay?.SetActive(false); // 0.18.3: the Beelz bar hides with the master toggle too
            _beelzSummonsOverlay?.SetActive(false);   // 0.19: the Beelz summons overlay hides with it too
            _beelzTransformOverlay?.SetActive(false); // 0.20: the Beelz transforms overlay hides with it too
            _urielSharedOverlay?.SetActive(false);    // 0.26: the Uriel public-storage overlay hides with it too
            _urielObjectSpawnerOverlay?.SetActive(false); // 0.29: the Uriel object-spawn palette hides with it too
            // 0.18.3: optionally include the chat window in the master "hide overlays" toggle. Default
            // OFF — chat normally stays visible (the requested default). When ON, the upper-right toggle
            // also hides chat; ApplyNativeChatVisibility keeps V Rising's native chat in sync.
            if (BloodCraftHub.Config.Settings.HideChatWithOverlaysToggle)
            {
                _chatWindowOverlay?.SetActive(false);
                ApplyNativeChatVisibility();
            }
            return;
        }
        // Un-suppress: re-show only overlays whose per-overlay Settings flag is true AND whose backing
        // mod is available. Anything the user disabled (or whose mod is absent) stays hidden.
        if (bcAvailable && BloodCraftHub.Config.Settings.ShowExperienceOverlay)
        {
            EnsureExperienceOverlay();
            _experienceOverlay.SetActive(true);
        }
        if (bcAvailable && BloodCraftHub.Config.Settings.ShowFamiliarOverlay)
        {
            EnsureFamiliarOverlay();
            _familiarOverlay.SetActive(true);
        }
        if (BloodCraftHub.Config.Settings.ShowFamiliarBrowser)
        {
            EnsureFamiliarBrowserOverlay();
            _familiarBrowserOverlay.SetActive(true);
        }
        if (bcAvailable && BloodCraftHub.Config.Settings.ShowDailyQuestOverlay)
        {
            EnsureDailyQuestOverlay();
            _dailyQuestOverlay.SetActive(true);
        }
        if (bcAvailable && BloodCraftHub.Config.Settings.ShowProfessionOverlay)
        {
            EnsureProfessionOverlay();
            _professionOverlay.SetActive(true);
        }
        // B7 (0.19): the Shift-spell overlay reads ShiftCooldownService (resolved straight from the
        // game), NOT the Bloodcraft stream — and Shift is used by BOTH Bloodcraft and Beelzebub. So it
        // is independent of bcAvailable: show it whenever the user enabled it (master-suppression still
        // applies via the early return above).
        if (BloodCraftHub.Config.Settings.ShowShiftSpellOverlay)
        {
            EnsureShiftSpellOverlay();
            _shiftSpellOverlay.SetActive(true);
        }
        if (BloodCraftHub.Config.Settings.ShowQuickActionsOverlay)
        {
            EnsureQuickActionsOverlay();
            _quickActionsOverlay.SetActive(true);
        }
        if (beelzAvailable && BloodCraftHub.Config.Settings.ShowBeelzActionBarOverlay)
        {
            EnsureBeelzActionBarOverlay();
            _beelzActionBarOverlay.SetActive(true);
        }
        if (beelzAvailable && BloodCraftHub.Config.Settings.ShowBeelzSummonsOverlay)
        {
            EnsureBeelzSummonsOverlay();
            _beelzSummonsOverlay.SetActive(true);
        }
        if (beelzAvailable && BloodCraftHub.Config.Settings.ShowBeelzTransformOverlay)
        {
            EnsureBeelzTransformOverlay();
            _beelzTransformOverlay.SetActive(true);
        }
        if (urielAvailable && BloodCraftHub.Config.Settings.ShowUrielSharedOverlay)
        {
            EnsureUrielSharedOverlay();
            _urielSharedOverlay.SetActive(true);
        }
        if (urielAvailable && BloodCraftHub.Config.Settings.ShowUrielObjectSpawnerOverlay)
        {
            EnsureUrielObjectSpawnerOverlay();
            _urielObjectSpawnerOverlay.SetActive(true);
        }
        if (BloodCraftHub.Config.Settings.ShowChatWindowOverlay)
        {
            EnsureChatWindowOverlay();
            _chatWindowOverlay.SetActive(true);
        }
        if (BloodCraftHub.Config.Settings.ShowSecondaryChatOverlay)
        {
            EnsureSecondaryChatOverlay();
            _secondaryChatOverlay.SetActive(true);
        }
        ApplyNativeChatVisibility(); // 0.18.3: keep native chat in sync after (re)showing the chat window
    }

    /// <summary>
    /// Construct + show any overlay that was visible at last logout. Called from
    /// SetupAndShowUI once the UI bootstraps in-world. Pre-0.6.0, overlay
    /// visibility never persisted across sessions because no caller wrote the
    /// Settings.Show* values back when the user toggled, AND nobody read them
    /// on init either. Both halves of the loop are wired now.
    /// </summary>
    // -----------------------------------------------------------------------
    // 0.17.2: deferred overlay restore — push overlay construction off the login
    // frame onto a quiet one. Armed by Plugin.UIOnInitialize; ticked every frame by
    // CoreUpdateBehavior (registered in Plugin.Load). No-op until armed and the
    // UiBuildDelaySeconds window elapses. A delay of 0 restores immediately.
    // -----------------------------------------------------------------------
    private bool _restoreArmed;
    private double _restoreFireAt;

    public void ScheduleOverlayRestore()
    {
        int delay = BloodCraftHub.Config.Settings.UiBuildDelaySeconds;
        if (delay <= 0)
        {
            DoDeferredBringUp();   // legacy: build immediately on the spawn frame
            return;
        }
        _restoreFireAt = UnityEngine.Time.realtimeSinceStartupAsDouble + delay;
        _restoreArmed = true;
        BloodCraftHub.Utils.LogUtils.LogDiagnostic($"Overlay restore deferred {delay}s past login.");
    }

    public void TickDeferredRestore()
    {
        if (!_restoreArmed) return;
        if (UnityEngine.Time.realtimeSinceStartupAsDouble < _restoreFireAt) return;
        _restoreArmed = false;   // disarm before running so a build exception can't re-fire
        DoDeferredBringUp();
    }

    private void DoDeferredBringUp()
    {
        // Bring back any overlays the user had visible at last logout (0.6.0+).
        try { RestoreOverlaysFromSettings(); }
        catch (System.Exception ex) { BloodCraftHub.Utils.LogUtils.LogError($"Deferred overlay restore failed: {ex}"); }
        // 0.10.3: V-Blood scanner init (subscribes to MessageService.FamSearchCompleted).
        // By now LocalCharacter is bound and the ECS World is fully available.
        try { Services.VBloodScannerService.Initialize(); }
        catch (System.Exception ex) { BloodCraftHub.Utils.LogUtils.LogError($"Deferred V-Blood scanner init failed: {ex}"); }
    }

    public void RestoreOverlaysFromSettings()
    {
        // 0.14.0: combined-mode short-circuits the standalone-info restore.
        // ApplyCombinedOverlayMutualExclusion ensures the right set is up;
        // we still restore FamiliarBrowser + ShiftSpell because they're
        // independent of combined-mode.
        // 0.15.0: feature-flag gating on each restore was reverted — see
        // ApplyServerFeatureFlagsToOverlays.
        // 0.17.1: when Eclipse is present, BCH stands down from its STREAM-DRIVEN
        // stat overlays (XP / Familiar-active / Daily Quest / Professions / Shift /
        // Combined) — Eclipse shows that live data, and BCH gets no stream in
        // stand-down so these would be empty anyway. We only SKIP showing them; the
        // user's saved Show* preference is untouched, so a later session WITHOUT
        // Eclipse restores them. The Familiar Browser, Quick Actions (Kindred), and
        // Chat Window overlays don't use the stream and stay available.
        bool standDown = Services.EclipseProtocolService.StandDownForEclipse();
        // 0.18.3: "hidden until confirmed" — only restore the Bloodcraft stream overlays / Beelz
        // bar if THIS server has confirmed the backing mod. On a fresh relog these are false until
        // the handshake ACKs; AvailabilityChanged → ApplyAvailabilityToOverlays then brings them up.
        // This stops the previous server's overlays from flashing back on a server-switch.
        bool bcAvailable = !standDown && Services.EclipseProtocolService.UserRegistered;
        bool beelzAvailable = Services.Beelzebub.BeelzProtocolService.IsPresent;
        bool urielAvailable = Services.Uriel.UrielProtocolService.IsPresent;
        if (bcAvailable)
        {
            if (BloodCraftHub.Config.Settings.ShowCombinedOverlay)
            {
                ApplyCombinedOverlayMutualExclusion();
            }
            else
            {
                if (BloodCraftHub.Config.Settings.ShowExperienceOverlay)
                {
                    EnsureExperienceOverlay();
                    _experienceOverlay.SetActive(true);
                }
                if (BloodCraftHub.Config.Settings.ShowFamiliarOverlay)
                {
                    EnsureFamiliarOverlay();
                    _familiarOverlay.SetActive(true);
                }
                if (BloodCraftHub.Config.Settings.ShowDailyQuestOverlay)
                {
                    EnsureDailyQuestOverlay();
                    _dailyQuestOverlay.SetActive(true);
                }
                if (BloodCraftHub.Config.Settings.ShowProfessionOverlay)
                {
                    EnsureProfessionOverlay();
                    _professionOverlay.SetActive(true);
                }
            }
        }
        // Always-available overlays (no Bloodcraft stream → safe alongside Eclipse).
        // B7 (0.19): Shift-spell overlay moved here — it reads ShiftCooldownService (resolved from the
        // game), used by BOTH Bloodcraft and Beelzebub, so it restores regardless of bcAvailable.
        if (BloodCraftHub.Config.Settings.ShowShiftSpellOverlay)
        {
            EnsureShiftSpellOverlay();
            _shiftSpellOverlay.SetActive(true);
        }
        if (BloodCraftHub.Config.Settings.ShowFamiliarBrowser)
        {
            EnsureFamiliarBrowserOverlay();
            _familiarBrowserOverlay.SetActive(true);
        }
        if (BloodCraftHub.Config.Settings.ShowQuickActionsOverlay)
        {
            EnsureQuickActionsOverlay();
            _quickActionsOverlay.SetActive(true);
        }
        // 0.18.3: Beelz bar only restores once Beelzebub is confirmed on THIS server.
        if (beelzAvailable && BloodCraftHub.Config.Settings.ShowBeelzActionBarOverlay)
        {
            EnsureBeelzActionBarOverlay();
            _beelzActionBarOverlay.SetActive(true);
        }
        // 0.19: Beelz summons overlay, same Beelz-confirmed gating as the action bar.
        if (beelzAvailable && BloodCraftHub.Config.Settings.ShowBeelzSummonsOverlay)
        {
            EnsureBeelzSummonsOverlay();
            _beelzSummonsOverlay.SetActive(true);
        }
        if (beelzAvailable && BloodCraftHub.Config.Settings.ShowBeelzTransformOverlay)
        {
            EnsureBeelzTransformOverlay();
            _beelzTransformOverlay.SetActive(true);
        }
        if (urielAvailable && BloodCraftHub.Config.Settings.ShowUrielSharedOverlay)
        {
            EnsureUrielSharedOverlay();
            _urielSharedOverlay.SetActive(true);
        }
        if (urielAvailable && BloodCraftHub.Config.Settings.ShowUrielObjectSpawnerOverlay)
        {
            EnsureUrielObjectSpawnerOverlay();
            _urielObjectSpawnerOverlay.SetActive(true);
        }
        if (BloodCraftHub.Config.Settings.ShowChatWindowOverlay)
        {
            EnsureChatWindowOverlay();
            _chatWindowOverlay.SetActive(true);
        }
        if (BloodCraftHub.Config.Settings.ShowSecondaryChatOverlay)
        {
            EnsureSecondaryChatOverlay();
            _secondaryChatOverlay.SetActive(true);
        }
        ApplyNativeChatVisibility();
        // 0.14.0: re-show combined overlay last, after the un-suppress walk
        // through individual overlays — ApplyCombinedOverlayMutualExclusion
        // will hide whichever individuals it conflicts with.
        // 0.17.1: but not under Eclipse stand-down (the combined overlay is a
        // stream-driven stat overlay — Eclipse shows that data).
        // 0.18.3: and only once Bloodcraft is confirmed on this server.
        if (BloodCraftHub.Config.Settings.ShowCombinedOverlay && bcAvailable)
            ApplyCombinedOverlayMutualExclusion();
    }

    /// <summary>0.18.3: hide overlays whose backing server mod isn't present, and re-show them per the
    /// user's saved Show* prefs when it is. Driven by the AvailabilityChanged events (Eclipse/Beelz)
    /// via MainPanel.OnBloodcraftAvailabilityChanged. Fixes: Bloodcraft stream overlays sitting empty
    /// on a non-BC server, and the Beelz action-bar overlay being stuck visible on a non-Beelz server
    /// (Moonie couldn't hide it on TSR since the Beelzebub tab is greyed out there). Never mutates the
    /// Show* prefs — a later session with the mod present restores them. Respects the master overlay
    /// suppression + combined-mode mutual exclusion.</summary>
    // 0.18.4: MainPanel-INDEPENDENT driver for overlay availability. Background: under the new
    // "hidden until confirmed" model, ApplyAvailabilityToOverlays must run when the server's handshake
    // resolves — but it was only ever called from MainPanel.OnBloodcraftAvailabilityChanged, and the
    // MainPanel is built LAZILY (first time you open it). So a player who has the XP/Familiar/etc.
    // overlays enabled but never opens the main panel would never see them re-appear after the ACK
    // (the deferred login restore can fire BEFORE the ACK — UiBuildDelaySeconds defaults to 3s, a race).
    // This per-frame ticker (registered on CoreUpdateBehavior in Plugin.Load) watches the two
    // availability bits and re-applies on any transition, regardless of whether the MainPanel exists.
    // Cheap: two bool reads + a compare; only does UI work on the (rare) transition frame. Skipped while
    // the whole UI is hidden (escape menu) so it can't resurrect an overlay over the pause menu — the
    // transition is re-detected and applied once the menu closes.
    private bool _lastBcAvailable;
    private bool _lastBeelzAvailable;
    private bool _availabilityTrackInit;

    internal void TickOverlayAvailability()
    {
        if (!_uiActive) return;
        try
        {
            bool bc = !Services.EclipseProtocolService.StandDownForEclipse()
                      && Services.EclipseProtocolService.UserRegistered;
            bool bz = Services.Beelzebub.BeelzProtocolService.IsPresent;
            if (_availabilityTrackInit && bc == _lastBcAvailable && bz == _lastBeelzAvailable) return;
            _availabilityTrackInit = true;
            _lastBcAvailable = bc;
            _lastBeelzAvailable = bz;
            ApplyAvailabilityToOverlays();
        }
        catch (System.Exception ex) { BloodCraftHub.Utils.LogUtils.LogDebug($"TickOverlayAvailability: {ex.Message}"); }
    }

    public void ApplyAvailabilityToOverlays()
    {
        try
        {
            // 0.18.3: "available" = CONFIRMED present (mirrors the tab-group gate). Was
            // "present OR still probing" — but that left the BC stream overlays / Beelz bar
            // visible (showing the previous server's stale data) through the whole probe window
            // on a server-switch. Now overlays hide the instant we relog and only return when
            // THIS server confirms the mod. Under Eclipse stand-down the BC stream overlays are
            // empty (Eclipse owns that HUD) → treat as off.
            bool bcAvailable = !Services.EclipseProtocolService.StandDownForEclipse()
                               && Services.EclipseProtocolService.UserRegistered;
            bool beelzAvailable = Services.Beelzebub.BeelzProtocolService.IsPresent;
            bool urielAvailable = Services.Uriel.UrielProtocolService.IsPresent;

            // ---- Bloodcraft stream-driven overlays ----
            if (!bcAvailable)
            {
                _combinedOverlay?.SetActive(false);
                _experienceOverlay?.SetActive(false);
                _familiarOverlay?.SetActive(false);
                _dailyQuestOverlay?.SetActive(false);
                _professionOverlay?.SetActive(false);
            }
            else if (!_overlaysSuppressed) // available AND not master-suppressed → restore per pref
            {
                if (BloodCraftHub.Config.Settings.ShowCombinedOverlay)
                {
                    EnsureCombinedOverlay();
                    ApplyCombinedOverlayMutualExclusion();
                }
                else
                {
                    if (BloodCraftHub.Config.Settings.ShowExperienceOverlay) { EnsureExperienceOverlay(); _experienceOverlay.SetActive(true); }
                    if (BloodCraftHub.Config.Settings.ShowFamiliarOverlay)   { EnsureFamiliarOverlay();   _familiarOverlay.SetActive(true); }
                    if (BloodCraftHub.Config.Settings.ShowDailyQuestOverlay) { EnsureDailyQuestOverlay(); _dailyQuestOverlay.SetActive(true); }
                    if (BloodCraftHub.Config.Settings.ShowProfessionOverlay) { EnsureProfessionOverlay(); _professionOverlay.SetActive(true); }
                }
            }

            // ---- Shift-spell overlay (B7, 0.19): mod-INDEPENDENT ----
            // Reads ShiftCooldownService (resolved straight from the game), used by BOTH Bloodcraft and
            // Beelzebub, so it is NOT gated by bcAvailable. Only the master suppression + user pref apply.
            // CRITICAL (0.19 crash fix): do NOT CONSTRUCT the overlay here. ApplyAvailabilityToOverlays
            // runs from TickOverlayAvailability, whose first tick fires at the MAIN MENU (before
            // SetupAndShowUI builds UiBase) because _uiActive defaults true — and EnsureShiftSpellOverlay
            // would then `new` a panel with a null Owner, NRE-ing in PanelBase.ConstructUI. The mod-gated
            // branches above are safe there (overlays null → null-safe SetActive); this one wasn't.
            // Construction happens only in UI-ready paths: RestoreOverlaysFromSettings (deferred login
            // restore, which builds it on EVERY server now), ToggleOverlay, and ApplyOverlaySuppression.
            // Here we only RE-ASSERT visibility on an already-built overlay.
            if (_shiftSpellOverlay != null && !_overlaysSuppressed && BloodCraftHub.Config.Settings.ShowShiftSpellOverlay)
                _shiftSpellOverlay.SetActive(true);

            // ---- Beelzebub action-bar overlay ----
            if (!beelzAvailable)
                _beelzActionBarOverlay?.SetActive(false);
            else if (!_overlaysSuppressed && BloodCraftHub.Config.Settings.ShowBeelzActionBarOverlay)
            {
                EnsureBeelzActionBarOverlay();
                _beelzActionBarOverlay.SetActive(true);
            }

            // ---- Beelzebub summons overlay ---- (0.19; same Beelz-gating as the action bar)
            if (!beelzAvailable)
                _beelzSummonsOverlay?.SetActive(false);
            else if (!_overlaysSuppressed && BloodCraftHub.Config.Settings.ShowBeelzSummonsOverlay)
            {
                EnsureBeelzSummonsOverlay();
                _beelzSummonsOverlay.SetActive(true);
            }

            // ---- Beelzebub transforms overlay ---- (0.20; same Beelz-gating)
            if (!beelzAvailable)
                _beelzTransformOverlay?.SetActive(false);
            else if (!_overlaysSuppressed && BloodCraftHub.Config.Settings.ShowBeelzTransformOverlay)
            {
                EnsureBeelzTransformOverlay();
                _beelzTransformOverlay.SetActive(true);
            }

            // ---- Uriel public-storage overlay ---- (0.26; gated on Uriel confirmed-present)
            if (!urielAvailable)
                _urielSharedOverlay?.SetActive(false);
            else if (!_overlaysSuppressed && BloodCraftHub.Config.Settings.ShowUrielSharedOverlay)
            {
                EnsureUrielSharedOverlay();
                _urielSharedOverlay.SetActive(true);
            }

            // ---- Uriel object-spawn palette overlay ---- (0.29; same Uriel-gating)
            if (!urielAvailable)
                _urielObjectSpawnerOverlay?.SetActive(false);
            else if (!_overlaysSuppressed && BloodCraftHub.Config.Settings.ShowUrielObjectSpawnerOverlay)
            {
                EnsureUrielObjectSpawnerOverlay();
                _urielObjectSpawnerOverlay.SetActive(true);
            }

            _mainPanel?.RefreshAllOverlayToggleStates();
        }
        catch (System.Exception ex)
        {
            BloodCraftHub.Utils.LogUtils.LogError($"ApplyAvailabilityToOverlays failed: {ex}");
        }
    }

    public bool IsOverlayOpen(PanelType overlay) => overlay switch
    {
        PanelType.ExperienceOverlay      => _experienceOverlay?.Enabled ?? false,
        PanelType.FamiliarOverlay        => _familiarOverlay?.Enabled ?? false,
        PanelType.FamiliarBrowserOverlay => _familiarBrowserOverlay?.Enabled ?? false,
        PanelType.DailyQuestOverlay      => _dailyQuestOverlay?.Enabled ?? false,
        PanelType.ProfessionOverlay      => _professionOverlay?.Enabled ?? false,
        PanelType.ShiftSpellOverlay      => _shiftSpellOverlay?.Enabled ?? false,
        PanelType.QuickActionsOverlay    => _quickActionsOverlay?.Enabled ?? false,
        PanelType.BeelzActionBarOverlay  => _beelzActionBarOverlay?.Enabled ?? false,
        PanelType.BeelzSummonsOverlay    => _beelzSummonsOverlay?.Enabled ?? false,
        PanelType.BeelzTransformOverlay  => _beelzTransformOverlay?.Enabled ?? false,
        PanelType.UrielSharedOverlay     => _urielSharedOverlay?.Enabled ?? false,
        PanelType.UrielObjectSpawnerOverlay => _urielObjectSpawnerOverlay?.Enabled ?? false,
        PanelType.ChatWindowOverlay      => _chatWindowOverlay?.Enabled ?? false,
        PanelType.SecondaryChatOverlay   => _secondaryChatOverlay?.Enabled ?? false,
        PanelType.CombinedOverlay        => _combinedOverlay?.Enabled ?? false,
        _ => false,
    };

    private void EnsureMainPanel()
    {
        if (_mainPanel != null) return;
        _mainPanel = new MainPanel(UiBase);
        _panels.Add(_mainPanel);
        _mainPanel.SetActive(false);
    }

    private void EnsureExperienceOverlay()
    {
        if (_experienceOverlay != null) return;
        _experienceOverlay = new ExperienceOverlayPanel(UiBase);
        _panels.Add(_experienceOverlay);
        _experienceOverlay.SetActive(false);
    }

    private void EnsureFamiliarBrowserOverlay()
    {
        if (_familiarBrowserOverlay != null) return;
        _familiarBrowserOverlay = new FamiliarBrowserOverlayPanel(UiBase);
        _panels.Add(_familiarBrowserOverlay);
        _familiarBrowserOverlay.SetActive(false);
    }

    private void EnsureDailyQuestOverlay()
    {
        if (_dailyQuestOverlay != null) return;
        _dailyQuestOverlay = new DailyQuestOverlayPanel(UiBase);
        _panels.Add(_dailyQuestOverlay);
        _dailyQuestOverlay.SetActive(false);
    }

    private void EnsureFamiliarOverlay()
    {
        if (_familiarOverlay != null) return;
        _familiarOverlay = new FamiliarOverlayPanel(UiBase);
        _panels.Add(_familiarOverlay);
        _familiarOverlay.SetActive(false);
    }

    private void EnsureProfessionOverlay()
    {
        if (_professionOverlay != null) return;
        _professionOverlay = new ProfessionOverlayPanel(UiBase);
        _panels.Add(_professionOverlay);
        _professionOverlay.SetActive(false);
    }

    private void EnsureShiftSpellOverlay()
    {
        if (_shiftSpellOverlay != null) return;
        _shiftSpellOverlay = new ShiftSpellOverlayPanel(UiBase);
        _panels.Add(_shiftSpellOverlay);
        _shiftSpellOverlay.SetActive(false);
    }

    private void EnsureQuickActionsOverlay()
    {
        if (_quickActionsOverlay != null) return;
        _quickActionsOverlay = new QuickActionsOverlayPanel(UiBase);
        _panels.Add(_quickActionsOverlay);
        _quickActionsOverlay.SetActive(false);
    }

    private void EnsureBeelzActionBarOverlay()
    {
        if (_beelzActionBarOverlay != null) return;
        _beelzActionBarOverlay = new BeelzActionBarOverlayPanel(UiBase);
        _panels.Add(_beelzActionBarOverlay);
        _beelzActionBarOverlay.SetActive(false);
    }

    private void EnsureBeelzSummonsOverlay()
    {
        if (_beelzSummonsOverlay != null) return;
        _beelzSummonsOverlay = new BeelzSummonsOverlayPanel(UiBase);
        _panels.Add(_beelzSummonsOverlay);
        _beelzSummonsOverlay.SetActive(false);
    }

    private void EnsureUrielSharedOverlay()
    {
        if (_urielSharedOverlay != null) return;
        _urielSharedOverlay = new UrielSharedOverlayPanel(UiBase);
        _panels.Add(_urielSharedOverlay);
        _urielSharedOverlay.SetActive(false);
    }

    private void EnsureUrielObjectSpawnerOverlay()
    {
        if (_urielObjectSpawnerOverlay != null) return;
        _urielObjectSpawnerOverlay = new UrielObjectSpawnerOverlayPanel(UiBase);
        _panels.Add(_urielObjectSpawnerOverlay);
        _urielObjectSpawnerOverlay.SetActive(false);
    }

    private void EnsureBeelzTransformOverlay()
    {
        if (_beelzTransformOverlay != null) return;
        _beelzTransformOverlay = new BeelzTransformOverlayPanel(UiBase);
        _panels.Add(_beelzTransformOverlay);
        _beelzTransformOverlay.SetActive(false);
    }

    private void EnsureChatWindowOverlay()
    {
        if (_chatWindowOverlay != null) return;
        _chatWindowOverlay = new ChatWindowOverlayPanel(UiBase);
        _panels.Add(_chatWindowOverlay);
        _chatWindowOverlay.SetActive(false);
    }

    // 0.17: let the Game UI customization toggles re-render the live chat window.
    public void RefreshChatWindowOverlay() => _chatWindowOverlay?.Refresh();

    private void EnsureSecondaryChatOverlay()
    {
        if (_secondaryChatOverlay != null) return;
        _secondaryChatOverlay = new SecondaryChatOverlayPanel(UiBase);
        _panels.Add(_secondaryChatOverlay);
        _secondaryChatOverlay.SetActive(false);
    }

    // 0.24: re-render the secondary chat window when its channel selection / text scale changes.
    public void RefreshSecondaryChatOverlay() => _secondaryChatOverlay?.Refresh();

    // 0.17 (2c): replace the game's chat with the tabbed window. When the tabbed
    // chat window is open AND Settings.HideNativeChat is on, hide the native chat
    // by zeroing its ContentCanvasGroup (alpha + raycasts + interactable). This
    // keeps the native ClientChatSystem RUNNING — so our FormatFullChatMessage
    // capture of other players' messages keeps working — while the native UI is
    // invisible and non-interactive. Restored when the tabbed window closes or
    // the setting is off, so there's always a chat available.
    private bool _nativeHidden;

    // True while the tabbed chat window is taking over (open + HideNativeChat on).
    public bool IsNativeChatHideActive()
    {
        // Normal "replacement" model: the BCH chat window is open AND the user chose to hide the
        // native one behind it.
        if ((_chatWindowOverlay?.Enabled ?? false) && BloodCraftHub.Config.Settings.HideNativeChat) return true;
        // 0.28: during a master overlay-hide that also drops BCH chat (HideChatWithOverlaysToggle on),
        // keep the GAME's native chat hidden too for a clean screen instead of letting it pop back —
        // unless the user opted out. Decoupled from the BCH chat overlay's Enabled state, which the
        // master toggle has already flipped off by this point.
        if (_overlaysSuppressed
            && BloodCraftHub.Config.Settings.HideChatWithOverlaysToggle
            && BloodCraftHub.Config.Settings.KeepNativeChatHiddenWhileOverlaysHidden)
            return true;
        return false;
    }

    // Focus the tabbed chat window's input — the divert target for the chat-open key.
    public void FocusChatInput() => _chatWindowOverlay?.FocusInput();

    // 0.17.0 escape hatch: force-release our chat input (Escape). Clears focus +
    // ChatInputActive so suppressed gameplay/menu input is restored — the user can
    // never be trapped focused (e.g. in the coffin).
    public void ReleaseChatInput() => _chatWindowOverlay?.ReleaseInput();

    // Diagnostic: is the native chat window currently focused?
    public bool IsNativeChatFocused()
    {
        try { return _nativeChat != null && _nativeChat.IsChatFocused; }
        catch { return false; }
    }

    // 0.17.0: is OUR tabbed-chat input currently focused? Polled each frame to
    // drive InputSuppression.ChatInputActive, so suppression reflects reality.
    public bool IsChatInputFocused()
        => (_chatWindowOverlay?.Enabled ?? false) && (_chatWindowOverlay?.IsInputFocused() ?? false);

    // 0.17.3: is the cursor over the open chat window? Drives attack-suppression so a
    // click on the chat (tabs / input) can't leak into the world as a stuck attack.
    public bool IsPointerOverChatWindow()
    {
        try { return _chatWindowOverlay?.IsPointerOverWindow() ?? false; }
        catch { return false; }
    }

    // 0.19: is the cursor over the OPEN main panel? Drives always-on attack/cast suppression so a
    // left-click on the main UI (buttons, forms, tabs) can't leak into the world as a primary attack
    // or spell. The main panel is the strongest case for this (you're definitely interacting with UI),
    // so — like the chat window — it's ALWAYS suppressed, independent of the default-off
    // BlockInputWhenPointerOverUI setting (which covers the smaller scattered overlays).
    public bool IsPointerOverMainPanel()
    {
        try
        {
            var p = _mainPanel;
            if (p == null || !p.Enabled || p.Rect == null) return false;
            return UnityEngine.RectTransformUtility.RectangleContainsScreenPoint(p.Rect, UnityEngine.Input.mousePosition, null);
        }
        catch { return false; }
    }

    // B3 (0.19): is the cursor over ANY visible BCH panel/overlay? Generalizes
    // IsPointerOverChatWindow across every registered panel so the (proven-safe) primary-attack /
    // ability suppression can optionally cover all BCH surfaces — gated by
    // Settings.BlockInputWhenPointerOverUI (default OFF). Pure rect-contains math on the
    // ScreenSpaceOverlay canvases (null camera). Only feeds ability suppression — NOT movement and
    // NOT the menu patches — so it can't cause the movement action-loop or the menu-patch crash class.
    public bool IsPointerOverAnyUI()
    {
        try
        {
            var mp = UnityEngine.Input.mousePosition;
            foreach (var p in _panels)
            {
                if (p == null || !p.Enabled || p.Rect == null) continue;
                if (UnityEngine.RectTransformUtility.RectangleContainsScreenPoint(p.Rect, mp, null))
                    return true;
            }
            return false;
        }
        catch { return false; }
    }

    // 0.17.3 (#38): entry point for the social-menu whisper redirect
    // (ClanWhisperRedirectPatch). Brings the chat window up if needed, opens a whisper
    // composed at the given player (resolved target — name + NetworkId), and focuses the
    // input so the user can type immediately. Returns true if BCH took over the whisper
    // (the caller then suppresses the native one); false leaves the native whisper to run.
    public bool BeginExternalWhisper(ProjectM.Network.NetworkId id, string name)
    {
        try
        {
            EnsureChatWindowOverlay();
            if (_chatWindowOverlay == null) return false;
            if (!_chatWindowOverlay.Enabled)
            {
                _chatWindowOverlay.SetActive(true);
                BloodCraftHub.Config.Settings.SetShowChatWindowOverlay(true);
            }
            _chatWindowOverlay.BeginWhisperTo(name, id);
            _chatWindowOverlay.FocusInput();
            return true;
        }
        catch (Exception ex)
        {
            BloodCraftHub.Utils.LogUtils.LogWarning($"BeginExternalWhisper: {ex.Message}");
            return false;
        }
    }

    public void ApplyNativeChatVisibility()
    {
        try
        {
            bool hide = IsNativeChatHideActive();
            if (!hide && !_nativeHidden) return; // not hiding and wasn't — nothing to do

            if (_nativeChat == null)
                _nativeChat = UnityEngine.Object.FindObjectOfType<ProjectM.UI.HUDChatWindow>();
            if (_nativeChat == null) return;

            var cg = _nativeChat.ContentCanvasGroup;
            if (cg != null)
            {
                cg.alpha          = hide ? 0f : 1f;
                cg.blocksRaycasts = !hide;
                // NEVER set interactable=false here: doing so trapped the native
                // chat in a focused-but-uncloseable state and froze ALL game input.
                // Focus is prevented via the SetFocused prefix + the force-unfocus
                // safety net below instead.
            }

            // Freeze-safety net: if the native chat is somehow focused while we're
            // taking over, force it unfocused so V Rising's ChatInputFocused flag
            // can't stay stuck (the cause of the movement/actions/menus freeze).
            if (hide && _nativeChat.IsChatFocused)
                _nativeChat.SetFocused(false);

            _nativeHidden = hide;
        }
        catch (System.Exception ex)
        {
            BloodCraftHub.Utils.LogUtils.LogDebug($"ApplyNativeChatVisibility: {ex.Message}");
        }
    }

    // 0.17.3: per-frame guard so the HIDDEN native chat can never trap input while
    // we've taken over. The SetFocused/FocusInputField prefixes block the usual focus
    // paths, but the P-key social menu's right-click "Whisper" opens native whisper
    // mode through SocialMenuMapper — a path those prefixes don't cover — which (with
    // the native chat hidden) would otherwise leave the player focused in an invisible
    // chat and unable to move. Running the same unfocus the toggle-time safety net does
    // EVERY frame closes that window. Registered on CoreUpdateBehavior in Plugin.Load.
    internal void TickNativeChatGuard()
    {
        try
        {
            if (!IsNativeChatHideActive()) return;   // only while we've taken over
            if (_nativeChat == null)
                _nativeChat = UnityEngine.Object.FindObjectOfType<ProjectM.UI.HUDChatWindow>();
            if (_nativeChat != null && _nativeChat.IsChatFocused)
                _nativeChat.SetFocused(false);
        }
        catch { /* best-effort guard; never throw into the per-frame pump */ }
    }

    private void EnsureCombinedOverlay()
    {
        if (_combinedOverlay != null) return;
        _combinedOverlay = new CombinedOverlayPanel(UiBase);
        _panels.Add(_combinedOverlay);
        _combinedOverlay.SetActive(false);
    }

    // -----------------------------------------------------------------------
    // 0.9.2: live refresh helpers for the Settings tab.
    //
    // RefreshAllOpacities — re-applies each overlay's current Opacity value
    // to its background Image. Pre-0.9.2 UIFactory.CreatePanel accepted the
    // opacity parameter but never wrote it to the Image, so per-overlay
    // transparency settings had no visible effect. Now that UIFactory
    // applies it on construct, this method exists to push runtime changes.
    //
    // RebuildMainPanel / RebuildOverlay — destroy and recreate the panel
    // so labels pick up the new Theme.UIFontMultiplier (or
    // OverlayFontMultiplier). Without this, scale changes only take effect
    // on next game launch because fontSize is baked into TMP_Text at
    // construct time. Deferred via CoreUpdateBehavior.Actions so the
    // triggering click handler completes before we tear down the panel
    // hosting that very click.
    // -----------------------------------------------------------------------

    public void RefreshAllOpacities()
    {
        _experienceOverlay?.RefreshOpacity();
        _familiarOverlay?.RefreshOpacity();
        _familiarBrowserOverlay?.RefreshOpacity();
        _dailyQuestOverlay?.RefreshOpacity();
        _professionOverlay?.RefreshOpacity();
        _shiftSpellOverlay?.RefreshOpacity();
        _quickActionsOverlay?.RefreshOpacity();
        _beelzActionBarOverlay?.RefreshOpacity();
        _beelzSummonsOverlay?.RefreshOpacity();
        _beelzTransformOverlay?.RefreshOpacity();
        _urielSharedOverlay?.RefreshOpacity();
        _chatWindowOverlay?.RefreshOpacity();   // 0.17.0: chat window honors its transparency live
        _secondaryChatOverlay?.RefreshOpacity();
        _combinedOverlay?.RefreshOpacity();
        _mainPanel?.RefreshOpacity();
        _floatingButton?.RefreshOpacity();
    }

    // 0.17.0: re-apply just the chat window's own background theme color (used by
    // the Game UI chat color picker so the change shows immediately).
    public void RefreshChatWindowBackground() => _chatWindowOverlay?.RefreshBackgroundColor();

    /// <summary>0.12.0: push the user's Settings.PanelBackgroundColor
    /// (RGB only — alpha is owned by the transparency settings) onto every
    /// panel that opted in via PanelBase.UsesCustomBackgroundColor. After
    /// the friend-test redirect on the v0.12.0 pre-release, that's every
    /// panel BCH builds — main panel + all six overlays.</summary>
    public void RefreshAllPanelBackgrounds()
    {
        _mainPanel?.RefreshBackgroundColor();
        _experienceOverlay?.RefreshBackgroundColor();
        _familiarOverlay?.RefreshBackgroundColor();
        _familiarBrowserOverlay?.RefreshBackgroundColor();
        _dailyQuestOverlay?.RefreshBackgroundColor();
        _professionOverlay?.RefreshBackgroundColor();
        _shiftSpellOverlay?.RefreshBackgroundColor();
        _quickActionsOverlay?.RefreshBackgroundColor();
        _beelzActionBarOverlay?.RefreshBackgroundColor();
        _beelzSummonsOverlay?.RefreshBackgroundColor();
        _beelzTransformOverlay?.RefreshBackgroundColor();
        _urielSharedOverlay?.RefreshBackgroundColor();
        _chatWindowOverlay?.RefreshBackgroundColor();
        _secondaryChatOverlay?.RefreshBackgroundColor();
        _combinedOverlay?.RefreshBackgroundColor();
        // Floating button intentionally excluded — it's a single-button
        // strip without a chrome backdrop the user would want themed.
    }

    /// <summary>0.18.4: recolor every themed button to the user's Settings.ButtonBackgroundColor.
    /// Live (no rebuild) — UIFactory keeps a registry of themed buttons and recolors them in place.
    /// Buttons with a deliberate color (Danger red etc.) are untouched. Pushed by the Settings →
    /// Display button-color picker.</summary>
    public void RefreshAllButtonColors()
        => BloodCraftHub.UI.Framework.UniverseLib.UI.UIFactory.ApplyThemedButtonColor();

    /// <summary>0.18.4: re-apply the launcher (BCH/OV) button size after the user changes
    /// Settings.FloatingButtonScale in Settings → Display.</summary>
    public void RefreshFloatingButtonScale() => _floatingButton?.RefreshScale();

    /// <summary>0.12.0: push Settings.InnerPanelBackgroundColor onto the
    /// panels that own scroll-view interiors — the main panel (each tab's
    /// content scroll view) and the Familiar Browser (the familiar list
    /// scroll view). The five small info overlays don't host scroll views
    /// worth recoloring so they stay out of this pass.</summary>
    public void RefreshScopedInnerBackgrounds()
    {
        _mainPanel?.RefreshInnerBackgroundColor();
        _familiarBrowserOverlay?.RefreshInnerBackgroundColor();
    }

    /// <summary>0.13.0: live re-render of the Professions overlay after the
    /// user flips any of the per-profession Settings.ShowProfession* flags
    /// in Settings → Display. Cheaper than rebuilding the overlay — just
    /// walks the label rows + bars and re-reads PlayerStateService.</summary>
    public void RefreshProfessionOverlay() => _professionOverlay?.Refresh();

    /// <summary>0.15.0 (reverted): per-system overlay auto-hide based on
    /// detected feature flags. False positives on the friend-test (Familiar
    /// / Shift signals only fire when the user is actively engaging with
    /// the system at broadcast time) made this user-hostile — hiding the
    /// overlay people just enabled. Reverted to a no-op until a reliable
    /// probe lands. PlayerStateService.FeatureFlags still tracks
    /// detection internally + emits diagnostic-mode log lines, but
    /// nothing visually acts on it.</summary>
    public void ApplyServerFeatureFlagsToOverlays()
    {
        // Intentionally a no-op for 0.15.0 — see comment above.
    }

    /// <summary>0.15.0: thin pass-through; always returns true while
    /// auto-detect visual gating is reverted. Future code that adds
    /// reliable per-system probes can route through this.</summary>
    public static bool IsSystemAvailable(PlayerStateService.SystemKind kind)
    {
        _ = kind;
        return true;
    }

    public void RequestRebuildMainPanel()
    {
        if (_mainPanel == null) return;
        // Defer to next frame so the click that requested this rebuild
        // finishes processing on the about-to-be-destroyed page.
        Behaviors.CoreUpdateBehavior.Actions.Add(_deferredMainPanelRebuild ??= () =>
        {
            Behaviors.CoreUpdateBehavior.Actions.Remove(_deferredMainPanelRebuild);
            _deferredMainPanelRebuild = null;
            RebuildMainPanelNow();
        });
    }
    private System.Action _deferredMainPanelRebuild;

    private void RebuildMainPanelNow()
    {
        if (_mainPanel == null) return;
        var wasOpen = _mainPanel.Enabled;
        var activeTab = _mainPanel.ActiveTab;
        if (_mainPanel is BloodCraftHub.UI.Framework.CustomLib.Panel.ResizeablePanelBase mp)
            mp.Reset();
        _mainPanel.Destroy();
        _panels.Remove(_mainPanel);
        _mainPanel = null;
        if (wasOpen)
        {
            EnsureMainPanel();
            _mainPanel.SetActive(true);
            _mainPanel.ShowTab(activeTab);
        }
    }

    public void RequestRebuildAllOverlays()
    {
        Behaviors.CoreUpdateBehavior.Actions.Add(_deferredOverlayRebuild ??= () =>
        {
            Behaviors.CoreUpdateBehavior.Actions.Remove(_deferredOverlayRebuild);
            _deferredOverlayRebuild = null;
            RebuildAllOverlaysNow();
        });
    }
    private System.Action _deferredOverlayRebuild;

    private void RebuildAllOverlaysNow()
    {
        // 0.14.0 friend-test v3: gate the 4 info overlays on combined-mode
        // mutual exclusion. Pre-fix, changing overlay text scale triggered
        // a full rebuild — RebuildOverlay's wasVisibleByConfig was each
        // Show*Overlay flag, so individuals reappeared even when combined
        // mode was on. Effective visibility = !combined && Show*Overlay.
        // FamiliarBrowser + ShiftSpell are independent overlays so they
        // skip the gate.
        bool combined = BloodCraftHub.Config.Settings.ShowCombinedOverlay;
        RebuildOverlay(ref _experienceOverlay,      !combined && BloodCraftHub.Config.Settings.ShowExperienceOverlay, b => new ExperienceOverlayPanel(b));
        RebuildOverlay(ref _familiarOverlay,        !combined && BloodCraftHub.Config.Settings.ShowFamiliarOverlay,   b => new FamiliarOverlayPanel(b));
        RebuildOverlay(ref _familiarBrowserOverlay, BloodCraftHub.Config.Settings.ShowFamiliarBrowser,                b => new FamiliarBrowserOverlayPanel(b));
        RebuildOverlay(ref _dailyQuestOverlay,      !combined && BloodCraftHub.Config.Settings.ShowDailyQuestOverlay, b => new DailyQuestOverlayPanel(b));
        RebuildOverlay(ref _professionOverlay,      !combined && BloodCraftHub.Config.Settings.ShowProfessionOverlay, b => new ProfessionOverlayPanel(b));
        RebuildOverlay(ref _shiftSpellOverlay,      BloodCraftHub.Config.Settings.ShowShiftSpellOverlay,              b => new ShiftSpellOverlayPanel(b));
        RebuildOverlay(ref _quickActionsOverlay,    BloodCraftHub.Config.Settings.ShowQuickActionsOverlay,            b => new QuickActionsOverlayPanel(b));
        RebuildOverlay(ref _beelzActionBarOverlay,  BloodCraftHub.Config.Settings.ShowBeelzActionBarOverlay,          b => new BeelzActionBarOverlayPanel(b));
        RebuildOverlay(ref _beelzSummonsOverlay,    BloodCraftHub.Config.Settings.ShowBeelzSummonsOverlay,            b => new BeelzSummonsOverlayPanel(b));
        RebuildOverlay(ref _beelzTransformOverlay,  BloodCraftHub.Config.Settings.ShowBeelzTransformOverlay,          b => new BeelzTransformOverlayPanel(b));
        RebuildOverlay(ref _urielSharedOverlay,     BloodCraftHub.Config.Settings.ShowUrielSharedOverlay,             b => new UrielSharedOverlayPanel(b));
        RebuildOverlay(ref _chatWindowOverlay,      BloodCraftHub.Config.Settings.ShowChatWindowOverlay,              b => new ChatWindowOverlayPanel(b));
        RebuildOverlay(ref _secondaryChatOverlay,   BloodCraftHub.Config.Settings.ShowSecondaryChatOverlay,           b => new SecondaryChatOverlayPanel(b));
        // 0.14.0: combined overlay is now part of the rebuild so its text
        // scale changes when the user toggles overlay text size. Pre-fix
        // the panel's labels stayed at construct-time font size because
        // it wasn't in the rebuild list.
        RebuildOverlay(ref _combinedOverlay,        combined,                                                          b => new CombinedOverlayPanel(b));
        // 0.14.0 friend-test v8: snap-to-MinHeight on rebuild moved into
        // CombinedOverlayPanel.LateConstructUI itself. v6 ran synchronously
        // (was overridden by deferred ApplySaveData); v7 deferred to next
        // frame via CoreUpdateBehavior.Actions (raced with ApplySaveData,
        // sometimes lost the race). The override in LateConstructUI runs
        // SAME FRAME AS ApplySaveData and AFTER it (base.LateConstructUI
        // does ApplySaveData first, then our override executes), so there's
        // no race and no Coroutine/Update ordering dependency.
        // After all rebuilds settle, push the post-rebuild reality back
        // into the footer/Settings toggles so they don't show stale
        // construct-time isOn values.
        _mainPanel?.RefreshAllOverlayToggleStates();
    }

    private void RebuildOverlay<T>(ref T slot, bool wasVisibleByConfig, System.Func<BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase, T> factory)
        where T : BloodCraftHub.UI.Framework.CustomLib.Panel.ResizeablePanelBase
    {
        if (slot == null) return; // never constructed; nothing to rebuild
        slot.Reset();
        slot.Destroy();
        _panels.Remove(slot);
        slot = null;
        if (wasVisibleByConfig)
        {
            var fresh = factory(UiBase);
            _panels.Add(fresh);
            fresh.SetActive(true);
            slot = fresh;
        }
    }
}
