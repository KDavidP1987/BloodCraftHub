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

    // 0.9.0: session-only flag flipped by the master-overlay button on the
    // floating-button strip. When true, every overlay is hidden regardless
    // of its individual SetActive state; when flipped back to false, overlays
    // are re-shown ONLY if their per-overlay Settings flag is true (so the
    // master toggle never resurrects overlays the user has disabled in
    // config). Mirrors Settings.OverlaysSuppressedByUser; we keep the local
    // copy here for the per-frame visibility application.
    private bool _overlaysSuppressed;

    public bool IsMainPanelOpen => _mainPanel != null && _mainPanel.Enabled;

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
    }

    public bool IsOverlayOpen(PanelType overlay) => overlay switch
    {
        PanelType.ExperienceOverlay      => _experienceOverlay?.Enabled ?? false,
        PanelType.FamiliarOverlay        => _familiarOverlay?.Enabled ?? false,
        PanelType.FamiliarBrowserOverlay => _familiarBrowserOverlay?.Enabled ?? false,
        PanelType.DailyQuestOverlay      => _dailyQuestOverlay?.Enabled ?? false,
        PanelType.ProfessionOverlay      => _professionOverlay?.Enabled ?? false,
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
}
