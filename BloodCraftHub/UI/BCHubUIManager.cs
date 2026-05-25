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
    private ChatWindowOverlayPanel _chatWindowOverlay; // 0.17: standalone tabbed chat window
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

    // 0.16.x: tracks whether the whole BCH UIBase is active (false while the
    // escape menu is up). Drives RefreshFloatingButtonVisibility.
    private bool _uiActive = true;

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
        ApplyPinnedTo(_chatWindowOverlay, pinned);
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
        // 0.16.x: floating launcher follows a single visibility rule (below).
        RefreshFloatingButtonVisibility();
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
        _floatingButton?.SetActive(_uiActive && !hideForFullscreen);
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
            case PanelType.ChatWindowOverlay:
                EnsureChatWindowOverlay();
                _chatWindowOverlay.SetActive(!_chatWindowOverlay.Enabled);
                BloodCraftHub.Config.Settings.SetShowChatWindowOverlay(_chatWindowOverlay.Enabled);
                ApplyNativeChatVisibility();
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
        ApplyOverlaySuppression();
    }

    public bool AreOverlaysSuppressed => _overlaysSuppressed;

    private void ApplyOverlaySuppression()
    {
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
            return;
        }
        // Un-suppress: re-show only overlays whose per-overlay Settings flag
        // is true. Anything the user disabled stays disabled.
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
        if (BloodCraftHub.Config.Settings.ShowFamiliarBrowser)
        {
            EnsureFamiliarBrowserOverlay();
            _familiarBrowserOverlay.SetActive(true);
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
        if (BloodCraftHub.Config.Settings.ShowChatWindowOverlay)
        {
            EnsureChatWindowOverlay();
            _chatWindowOverlay.SetActive(true);
        }
    }

    /// <summary>
    /// Construct + show any overlay that was visible at last logout. Called from
    /// SetupAndShowUI once the UI bootstraps in-world. Pre-0.6.0, overlay
    /// visibility never persisted across sessions because no caller wrote the
    /// Settings.Show* values back when the user toggled, AND nobody read them
    /// on init either. Both halves of the loop are wired now.
    /// </summary>
    public void RestoreOverlaysFromSettings()
    {
        // 0.14.0: combined-mode short-circuits the standalone-info restore.
        // ApplyCombinedOverlayMutualExclusion ensures the right set is up;
        // we still restore FamiliarBrowser + ShiftSpell because they're
        // independent of combined-mode.
        // 0.15.0: feature-flag gating on each restore was reverted — see
        // ApplyServerFeatureFlagsToOverlays.
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
        if (BloodCraftHub.Config.Settings.ShowFamiliarBrowser)
        {
            EnsureFamiliarBrowserOverlay();
            _familiarBrowserOverlay.SetActive(true);
        }
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
        if (BloodCraftHub.Config.Settings.ShowChatWindowOverlay)
        {
            EnsureChatWindowOverlay();
            _chatWindowOverlay.SetActive(true);
        }
        ApplyNativeChatVisibility();
        // 0.14.0: re-show combined overlay last, after the un-suppress walk
        // through individual overlays — ApplyCombinedOverlayMutualExclusion
        // will hide whichever individuals it conflicts with.
        if (BloodCraftHub.Config.Settings.ShowCombinedOverlay)
            ApplyCombinedOverlayMutualExclusion();
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
        PanelType.ChatWindowOverlay      => _chatWindowOverlay?.Enabled ?? false,
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

    private void EnsureChatWindowOverlay()
    {
        if (_chatWindowOverlay != null) return;
        _chatWindowOverlay = new ChatWindowOverlayPanel(UiBase);
        _panels.Add(_chatWindowOverlay);
        _chatWindowOverlay.SetActive(false);
    }

    // 0.17: let the Game UI customization toggles re-render the live chat window.
    public void RefreshChatWindowOverlay() => _chatWindowOverlay?.Refresh();

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
        => (_chatWindowOverlay?.Enabled ?? false) && BloodCraftHub.Config.Settings.HideNativeChat;

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
        _combinedOverlay?.RefreshOpacity();
        _mainPanel?.RefreshOpacity();
        _floatingButton?.RefreshOpacity();
    }

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
        _chatWindowOverlay?.RefreshBackgroundColor();
        _combinedOverlay?.RefreshBackgroundColor();
        // Floating button intentionally excluded — it's a single-button
        // strip without a chrome backdrop the user would want themed.
    }

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
        RebuildOverlay(ref _chatWindowOverlay,      BloodCraftHub.Config.Settings.ShowChatWindowOverlay,              b => new ChatWindowOverlayPanel(b));
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
