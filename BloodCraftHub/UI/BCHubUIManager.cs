using System;
using System.Collections.Generic;
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

    // 0.9.0: session-only flag flipped by the master-overlay button on the
    // floating-button strip. When true, every overlay is hidden regardless
    // of its individual SetActive state; when flipped back to false, overlays
    // are re-shown ONLY if their per-overlay Settings flag is true (so the
    // master toggle never resurrects overlays the user has disabled in
    // config). Mirrors Settings.OverlaysSuppressedByUser; we keep the local
    // copy here for the per-frame visibility application.
    private bool _overlaysSuppressed;

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
        _shiftSpellOverlay = null;
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
        _floatingButton?.SetActive(active && true);
        _mainPanel?.SetActive(active && IsMainPanelOpen);
        _experienceOverlay?.SetActive(active && (_experienceOverlay?.Enabled ?? false));
        _familiarOverlay?.SetActive(active && (_familiarOverlay?.Enabled ?? false));
        _familiarBrowserOverlay?.SetActive(active && (_familiarBrowserOverlay?.Enabled ?? false));
        _dailyQuestOverlay?.SetActive(active && (_dailyQuestOverlay?.Enabled ?? false));
        _professionOverlay?.SetActive(active && (_professionOverlay?.Enabled ?? false));
        _shiftSpellOverlay?.SetActive(active && (_shiftSpellOverlay?.Enabled ?? false));
    }

    /// <summary>Show or hide the main tabbed panel.</summary>
    public void ToggleMainPanel()
    {
        EnsureMainPanel();
        _mainPanel.SetActive(!_mainPanel.Enabled);
    }

    /// <summary>Show or hide a specific tab inside the main panel (and bring the panel up if needed).</summary>
    public void ShowTab(PanelType tab)
    {
        EnsureMainPanel();
        _mainPanel.SetActive(true);
        _mainPanel.ShowTab(tab);
    }

    /// <summary>Toggle one of the secondary overlays.</summary>
    public void ToggleOverlay(PanelType overlay)
    {
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
            default:
                throw new ArgumentOutOfRangeException(nameof(overlay), overlay, "Not a secondary overlay.");
        }
    }

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
    }

    public bool IsOverlayOpen(PanelType overlay) => overlay switch
    {
        PanelType.ExperienceOverlay      => _experienceOverlay?.Enabled ?? false,
        PanelType.FamiliarOverlay        => _familiarOverlay?.Enabled ?? false,
        PanelType.FamiliarBrowserOverlay => _familiarBrowserOverlay?.Enabled ?? false,
        PanelType.DailyQuestOverlay      => _dailyQuestOverlay?.Enabled ?? false,
        PanelType.ProfessionOverlay      => _professionOverlay?.Enabled ?? false,
        PanelType.ShiftSpellOverlay      => _shiftSpellOverlay?.Enabled ?? false,
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
        _mainPanel?.RefreshOpacity();
        _floatingButton?.RefreshOpacity();
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
        RebuildOverlay(ref _experienceOverlay,      BloodCraftHub.Config.Settings.ShowExperienceOverlay, b => new ExperienceOverlayPanel(b));
        RebuildOverlay(ref _familiarOverlay,        BloodCraftHub.Config.Settings.ShowFamiliarOverlay,   b => new FamiliarOverlayPanel(b));
        RebuildOverlay(ref _familiarBrowserOverlay, BloodCraftHub.Config.Settings.ShowFamiliarBrowser,   b => new FamiliarBrowserOverlayPanel(b));
        RebuildOverlay(ref _dailyQuestOverlay,      BloodCraftHub.Config.Settings.ShowDailyQuestOverlay, b => new DailyQuestOverlayPanel(b));
        RebuildOverlay(ref _professionOverlay,      BloodCraftHub.Config.Settings.ShowProfessionOverlay, b => new ProfessionOverlayPanel(b));
        RebuildOverlay(ref _shiftSpellOverlay,      BloodCraftHub.Config.Settings.ShowShiftSpellOverlay, b => new ShiftSpellOverlayPanel(b));
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
