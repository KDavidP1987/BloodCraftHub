using System;
using BloodCraftHub.Config;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.Utils;
using UnityEngine.UI;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;

namespace BloodCraftHub.UI.Framework.CustomLib.Panel;

public abstract class ResizeablePanelBase : PanelBase
{
    protected ResizeablePanelBase(UIBase owner) : base(owner) { }

    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public virtual bool ResizeWholePanel => true;
    private string PanelConfigKey => $"{PanelType}{PanelId}".Replace("'", "").Replace("\"", "");
    private bool ApplyingSaveData { get; set; } = true;

    /// <summary>0.10.8: when true (default), ConstructPanelContent applies
    /// Settings.OverlayEdgePadding as left/right padding on this panel's
    /// ContentRoot VerticalLayoutGroup. Overlays inherit this — opt out by
    /// overriding to false for panels that draw their own inset chrome.</summary>
    protected virtual bool ApplyOverlayEdgePadding => true;

    protected override void ConstructPanelContent()
    {
        // Disable the title bar, but still enable the draggable box area (this now being set to the whole panel)
        TitleBar.SetActive(false);
        if (ResizeWholePanel)
        {
            Dragger.DraggableArea = Rect;
            // Update resizer elements
            Dragger.OnEndResize();
        }
        if (ApplyOverlayEdgePadding) ApplyContentEdgePadding();
    }

    /// <summary>0.10.8: stamp Settings.OverlayEdgePadding into the ContentRoot's
    /// VerticalLayoutGroup so labels never sit flush with the panel border.
    /// Reads the setting once at construct time — toggling an overlay off and
    /// back on re-applies the latest value, same lifecycle as text-scale
    /// changes.</summary>
    private void ApplyContentEdgePadding()
    {
        if (ContentRoot == null) return;
        var vlg = ContentRoot.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) return;
        int pad = Settings.OverlayEdgePadding;
        var p = vlg.padding;
        p.left  = pad;
        p.right = pad;
        // Keep the existing top/bottom — only the horizontal axis is the
        // user-controllable axis. Top/bottom padding is normally 0 since
        // the title bar / first child supply their own vertical breathing
        // room.
        vlg.padding = p;
    }

    /// <summary>
    /// Intended to be called when leaving a server to ensure joining the next can build up the UI correctly again
    /// </summary>
    internal abstract void Reset();

    protected override void OnClosePanelClicked()
    {
        // Do nothing for now
    }

    public override void OnFinishDrag()
    {
        base.OnFinishDrag();
        SaveInternalData();
    }

    public override void OnFinishResize()
    {
        base.OnFinishResize();
        SaveInternalData();
    }

    public void SaveInternalData()
    {
        if (ApplyingSaveData) return;

        SetSaveDataToConfigValue();
    }

    private void SetSaveDataToConfigValue()
    {
        Plugin.Instance.Config.Bind("Panels", PanelConfigKey, "", "Serialized panel data").Value = ToSaveData();
    }

    private string ToSaveData()
    {
        try
        {
            return string.Join("|", new string[]
            {
                Rect.RectAnchorsToString(),
                Rect.RectPositionToString(),
                IsPinned.ToString()
            });
        }
        catch (Exception ex)
        {
            LogUtils.LogWarning($"Exception generating Panel save data: {ex}");
            return "";
        }
    }

    private void ApplySaveData()
    {
        var data = Plugin.Instance.Config.Bind("Panels", PanelConfigKey, "", "Serialized panel data").Value;
        // Load from the old key if the new key is empty. This ensures a good transition to the new format, while not losing existing config.
        // This is deprecated and should be removed in a later version.
        if (string.IsNullOrEmpty(data))
        {
            data = Plugin.Instance.Config.Bind("Panels", $"{PanelType}", "", "Serialized panel data").Value;
        }
        ApplySaveData(data);
    }

    private void ApplySaveData(string data)
    {
        if (string.IsNullOrEmpty(data))
            return;
        string[] split = data.Split('|');

        try
        {
            Rect.SetAnchorsFromString(split[0]);
            Rect.SetPositionFromString(split[1]);
            if (split.Length > 2 && bool.TryParse(split[2], out var pinned))
            {
                // 0.15.0 fix: only restore IsPinned for panels that opt in
                // to the lock-overlays system. The main panel
                // (RespectsLockOverlays=false) must NEVER be pinned from
                // save data — pre-0.15.0 a stale IsPinned=True bit (e.g.
                // persisted from a pre-0.11.2 fullscreen session that
                // triggered a save before the "don't save fullscreen state"
                // comment landed) would permanently lock the main panel
                // against drag/resize, with no UI affordance to unpin
                // because RespectsLockOverlays=false also skips the
                // Settings.LockOverlays clear path in LateConstructUI.
                // Friend-test 0.15.0: "panel feels locked in place — I
                // click on the borders and it doesn't move." On next save
                // (any drag/resize/etc.) the IsPinned=false current value
                // overwrites the stale True, so the .cfg self-heals
                // permanently after one session.
                if (RespectsLockOverlays)
                    IsPinned = pinned;
                else if (pinned)
                    LogUtils.LogInfo($"Panel {PanelConfigKey}: ignoring stale IsPinned=True from save data (this panel does not respect lock-overlays; .cfg will be corrected on next save).");
            }
            EnsureValidSize();
            EnsureValidPosition();
        }
        catch
        {
            LogUtils.LogWarning("Invalid or corrupt panel save data! Restoring to default.");
            SetDefaultSizeAndPosition();
            SetSaveDataToConfigValue();
        }
    }

    /// <summary>0.10.14: when true (the default for overlays), this panel
    /// honors the global <see cref="Settings.LockOverlays"/> toggle by
    /// setting <see cref="IsPinned"/> after restore-from-save. The main
    /// panel overrides this to false so the lock setting doesn't pin
    /// the main UI.</summary>
    protected virtual bool RespectsLockOverlays => true;

    protected override void LateConstructUI()
    {
        ApplyingSaveData = true;

        base.LateConstructUI();

        // apply panel save data or revert to default
        try
        {
            ApplySaveData();
        }
        catch (Exception ex)
        {
            LogUtils.LogError($"Exception loading panel save data: {ex}");
            SetDefaultSizeAndPosition();
        }

        ApplyingSaveData = false;

        // 0.10.14: apply the global "Lock overlays" toggle AFTER
        // save-data restore. ApplySaveData may have read a per-panel
        // IsPinned bit from the legacy save format, but Settings.
        // LockOverlays is the authoritative session-wide override and
        // wins. Pin only the overlays; the main panel overrides
        // RespectsLockOverlays to false so its drag/resize stays
        // available regardless of the lock setting.
        if (RespectsLockOverlays && Settings.LockOverlays)
        {
            IsPinned = true;
        }

        if (PinPanelToggleControl != null)
            PinPanelToggleControl.isOn = IsPinned;

        Dragger.OnEndResize();
    }

    /// <summary>
    /// 0.9.8: reset just the panel size (sizeDelta) to its factory minimums
    /// — MinWidth x MinHeight — WITHOUT touching anchor / pivot / position.
    /// The full SetDefaultSizeAndPosition also re-anchors to the panel's
    /// DefaultPosition, which for several overlays (e.g. XP overlay) is the
    /// top-left of the screen. Friend-testing in 0.9.7: users clicked the
    /// Size &amp; Positioning [Default] button expecting "reset size" and
    /// instead lost their carefully-placed overlay. This method is the
    /// "reset size only" alternative the Settings section now calls.
    /// </summary>
    public void SetDefaultSize()
    {
        if (Rect == null) return;
        Rect.sizeDelta = new UnityEngine.Vector2(MinWidth, MinHeight);
        Dragger?.OnEndResize();
        EnsureValidSize();
        SaveInternalData();
    }

    /// <summary>
    /// 0.9.7: programmatic size adjustment for the Size &amp; Positioning settings
    /// section. Applies the deltas to the current Rect, then runs the existing
    /// MinWidth / MinHeight / MaxWidth clamps via EnsureValidSize and persists
    /// the new size to config so it survives a logout/login.
    /// </summary>
    public void AdjustSize(int deltaWidth, int deltaHeight)
    {
        if (Rect == null) return;
        var size = Rect.sizeDelta;
        // Anchor-stretched panels (e.g. main panel in fullscreen) carry their
        // size in offsetMin/offsetMax rather than sizeDelta. For now this
        // helper is scoped to "normal" sized panels — fullscreen mode is
        // handled by ToggleFullscreen which restores its prior sizeDelta on
        // exit, so AdjustSize while fullscreen would fight the layout. The
        // Size & Positioning UI gates +/- buttons accordingly.
        size.x = UnityEngine.Mathf.Max(MinWidth,  size.x + deltaWidth);
        if (MaxWidth > 0) size.x = UnityEngine.Mathf.Min(MaxWidth, size.x);
        size.y = UnityEngine.Mathf.Max(MinHeight, size.y + deltaHeight);
        Rect.sizeDelta = size;
        Dragger.OnEndResize();
        EnsureValidSize();
        SaveInternalData();
    }
}