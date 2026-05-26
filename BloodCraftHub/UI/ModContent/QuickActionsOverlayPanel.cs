using BloodCraftHub.Config;
using BloodCraftHub.Services;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using UnityEngine;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;

namespace BloodCraftHub.UI.ModContent;

// 0.16: "Quick Actions" overlay — one-click Kindred command buttons.
//
// Currently a single "Stash All" button (.stash) for the back-at-base workflow.
// An audit of Kindred Logistics + Kindred Commands found Stash All is the only
// true one-click ACTION available to a player — every `.l xx` is a persistent
// mode TOGGLE, and the rest need arguments or admin rights. Named generically
// ("Quick Actions") rather than "Stash" so future action buttons can be added
// without renaming the persisted settings keys (Panels/QuickActionsOverlay,
// ShowQuickActionsOverlay, QuickActionsOverlayTransparency).
public class QuickActionsOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "QuickActionsOverlay";
    public override PanelType PanelType => PanelType.QuickActionsOverlay;

    // 0.16.1: smaller floor so the overlay can actually be shrunk (friend-test:
    // the Stash All button felt oversized and wouldn't shrink — the old 160x80
    // floor + the button's own minimums kept it big).
    public override int MinWidth  => 104;
    public override int MinHeight => 58;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // Lower-right by default, offset from the Shift overlay so they don't stack.
    public override Vector2 DefaultPosition  => new(
         Owner.Scaler.m_ReferenceResolution.x * 0.5f - 220f,
        -Owner.Scaler.m_ReferenceResolution.y * 0.5f + 340f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.QuickActionsOverlayTransparency);
    public override bool UsesCustomBackgroundColor => true;

    public QuickActionsOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();
        SetTitle("Quick Actions");

        // ContentRoot already carries a VerticalLayoutGroup (UIFactory.CreatePanel)
        // and the title bar above; buttons added here stack beneath it.
        var stashBtn = UIFactory.CreateButton(ContentRoot, "StashAllButton", "Stash All");
        // 0.16.1: smaller default + lower minimums so the button shrinks with the
        // overlay instead of staying oversized (friend-test feedback).
        UIFactory.SetLayoutElement(stashBtn.GameObject,
            minWidth: 64, preferredWidth: 100, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);
        stashBtn.OnClick = () =>
        {
            try { MessageService.EnqueueMessage(MessageService.BCCOM_KL_STASH_ALL); }
            catch (System.Exception ex) { Utils.LogUtils.LogError($"QuickActions Stash All failed: {ex}"); }
        };
        TooltipHover.Attach(stashBtn.GameObject,
            "Stash your inventory into nearby storage (Kindred Logistics '.stash'). One-click — issues the command immediately.");
    }

    internal override void Reset() { /* no tickers / subscriptions to clean up */ }
}
