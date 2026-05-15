using System;
using System.Collections.Generic;
using BloodCraftHub.Config;
using BloodCraftHub.Services;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Models;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using BloodCraftHub.Utils;
using TMPro;
using UnityEngine;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;
using BloodCraftHub.UI.Framework.CustomLib.Util;

namespace BloodCraftHub.UI.ModContent;

// Secondary overlay: minimal Boxes-tab-equivalent for switching between boxes
// and binding/unbinding familiars without opening the main panel.
//
// Layout:
//   ┌──────────────────────────────────┐
//   │ ◄  ActiveBoxName  ►   [Refresh]  │  ← box header
//   │ (no boxes — click Refresh)       │  ← only when BoxList empty
//   │ Active: GoblinMaster             │  ← active-familiar indicator
//   │ ⚠  warning here when swap armed  │  ← reserved when idle
//   ├──────────────────────────────────┤
//   │ 01 — Skeleton  Lv 12             │  ← scrollable familiar list
//   │ 02 — Wolf      Lv 8  ★ Storm     │
//   │ 03 — Bat       Lv 5              │
//   │ ...                              │
//   ├──────────────────────────────────┤
//   │      [Unbind active]             │  ← footer
//   └──────────────────────────────────┘
//
// Bind / auto-swap mirror the main Boxes tab's two-click destruction-confirm
// (clicking another familiar while one is active arms the swap; second click
// within 5s sends `.fam ub` → `.fam b N`). State is local to the overlay so
// arming here doesn't carry into the main panel.
//
// On first show, if PlayerStateService.BoxList is empty, sends `.fam boxes`
// so the user doesn't have to open the main Boxes tab to populate the list.
public class FamiliarBrowserOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "FamiliarBrowserOverlay";
    public override PanelType PanelType => PanelType.FamiliarBrowserOverlay;

    public override int MinWidth  => 280;
    // Tall enough that the default size shows ~12 familiar rows comfortably
    // (a full 10-fam box plus a couple extra). At 22px per row that's 264px
    // for the list + ~110px for header/status/footer + chrome.
    public override int MinHeight => 440;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // Default to right side, slightly above center so it sits below the daily-quest
    // overlay if both are toggled on.
    public override Vector2 DefaultPosition  => new(
         Owner.Scaler.m_ReferenceResolution.x * 0.5f - 320,
        -40);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.FamiliarBrowserTransparency);

    // Header
    private TextMeshProUGUI _boxNameLabel;
    private TextMeshProUGUI _activeFamLabel;
    private TextMeshProUGUI _swapWarningLabel;
    // Dynamic familiar list
    private GameObject _famListContainer;
    // Footer
    private ButtonRef _unbindBtn;

    // Two-click destruction-confirm state — mirrors MainPanel's auto-swap
    // logic but kept local so arming in the overlay doesn't leak into the
    // main panel and vice versa.
    private int   _pendingSwapIndex = -1;
    private float _pendingSwapDeadline = -1f;
    private const float SWAP_CONFIRM_WINDOW_SECONDS = 5f;

    private bool _subscribed;

    public FamiliarBrowserOverlayPanel(UIBase owner) : base(owner) { }

    private bool _autoPullDone = false;

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();

        BuildHeader();
        BuildFamiliarList();
        BuildFooter();

        if (!_subscribed)
        {
            PlayerStateService.BoxListChanged     += OnAnyBoxStateChanged;
            PlayerStateService.BoxContentsChanged += OnAnyBoxStateChanged;
            PlayerStateService.ActiveBoxChanged   += OnAnyBoxStateChanged;
            PlayerStateService.FamiliarChanged    += OnAnyBoxStateChanged;
            _subscribed = true;
        }

        // Per-frame ticker that fires the first-load auto-pull as soon as
        // MessageService is ready. Pre-0.8.1 the auto-pull was an inline check
        // here in ConstructPanelContent — but with 0.6.0's overlay-restore-on-
        // init, this method runs DURING UIOnInitialize, which happens BEFORE
        // CommonClientDataSystem has a chance to set local user/character.
        // The inline check saw MessageService.IsInitialized=false and silently
        // skipped, so the user's restored overlay opened with an empty box
        // list and no auto-fetch ever happened. The ticker fires once the
        // service is bound (typically a frame or two later), then unregisters.
        Behaviors.CoreUpdateBehavior.Actions.Add(TickDeferredAutoPull);

        Render();
    }

    private void TickDeferredAutoPull()
    {
        if (_autoPullDone) return;
        if (!MessageService.IsInitialized) return; // wait for character/user binding
        _autoPullDone = true;
        // Skip the network round-trip if the box list is already populated
        // (e.g. from the main Boxes tab being opened first this session).
        if (PlayerStateService.BoxList != null && PlayerStateService.BoxList.Count > 0)
            return;
        EnqueueOrWarn(MessageService.BCCOM_FAM_BOXES);
    }

    private void BuildHeader()
    {
        // Box-cycling row: ◄ BoxName ► [Refresh]
        var headerRow = UIFactory.CreateHorizontalGroup(ContentRoot, "BoxHeader",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(headerRow,
            minWidth: 260, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);

        // Use ← / → (LEFTWARDS / RIGHTWARDS ARROW, U+2190 / U+2192) — these
        // render correctly in V Rising's TMPro fallback font (we use ← elsewhere
        // for the main Boxes tab Back button and it works). The earlier ◄ / ►
        // (BLACK POINTERS, U+25C4 / U+25BA) and ↻ (CLOCKWISE OPEN CIRCLE ARROW,
        // U+21BB) glyphs were missing from the font and rendered as squares.
        // Refresh now uses the word "Reload" since no compact safe glyph exists.
        var prev = UIFactory.CreateButton(headerRow, "BoxPrev", "←");
        UIFactory.SetLayoutElement(prev.GameObject,
            minWidth: 36, preferredWidth: 36, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var prevTxt = prev.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (prevTxt != null) { prevTxt.fontSize = Theme.ScaledOverlay(18); prevTxt.fontStyle = FontStyles.Bold; }
        prev.OnClick = () => CycleBox(-1);
        UI.TooltipHover.Attach(prev.GameObject, "Previous box (cycles left through your familiar boxes).");

        // Box-name label now shows "Name  (X / N)" so you can see where you
        // are in the cycle.
        var nameLbl = UIFactory.CreateLabel(headerRow, "BoxName", "(no box)",
            TextAlignmentOptions.Center, color: null, fontSize: Theme.ScaledOverlay(14));
        UIFactory.SetLayoutElement(nameLbl.GameObject,
            minWidth: 130, preferredWidth: 170, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        nameLbl.TextMesh.fontStyle = FontStyles.Bold;
        nameLbl.TextMesh.enableWordWrapping = false;
        nameLbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        _boxNameLabel = nameLbl.TextMesh;

        var next = UIFactory.CreateButton(headerRow, "BoxNext", "→");
        UIFactory.SetLayoutElement(next.GameObject,
            minWidth: 36, preferredWidth: 36, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var nextTxt = next.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (nextTxt != null) { nextTxt.fontSize = Theme.ScaledOverlay(18); nextTxt.fontStyle = FontStyles.Bold; }
        next.OnClick = () => CycleBox(+1);
        UI.TooltipHover.Attach(next.GameObject, "Next box (cycles right through your familiar boxes).");

        var refresh = UIFactory.CreateButton(headerRow, "BoxRefresh", "Reload");
        UIFactory.SetLayoutElement(refresh.GameObject,
            minWidth: 60, preferredWidth: 64, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var refreshTxt = refresh.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (refreshTxt != null) refreshTxt.fontSize = Theme.ScaledOverlay(12);
        refresh.OnClick = () =>
        {
            EnqueueOrWarn(MessageService.BCCOM_FAM_BOXES);
            EnqueueOrWarn(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);
        };
        UI.TooltipHover.Attach(refresh.GameObject, "Re-pull the box list AND the current box's familiar list from the server.");

        // Combined active-familiar / swap-warning label. When idle this shows
        // "Active: {name}" in italic; when a swap is armed this shows the
        // warning text in warm orange. Sharing one slot saves the ~36px the
        // dedicated warning row used to reserve, which the user reported as
        // wasted space at the top of the overlay (compressed the familiar
        // list and forced extra scrolling for full-10 boxes).
        var status = UIFactory.CreateLabel(ContentRoot, "StatusLine",
            "Active: (none)", TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledOverlay(12));
        UIFactory.SetLayoutElement(status.GameObject,
            minWidth: 260, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        status.TextMesh.enableWordWrapping = true;
        status.TextMesh.overflowMode = TextOverflowModes.Overflow;
        _activeFamLabel = status.TextMesh;
        _swapWarningLabel = status.TextMesh; // same widget, dual-purpose
    }

    private void BuildFamiliarList()
    {
        // Wrap the dynamic familiar buttons in a ScrollView so a full box of
        // 10+ familiars doesn't push the Unbind footer off-screen. The
        // ScrollView's content GameObject already has a VerticalLayoutGroup +
        // ContentSizeFitter applied by CreateScrollView, so the buttons just
        // get added to it normally.
        var scrollWrap = UIFactory.CreateScrollView(ContentRoot, "FamListScroll",
            out _famListContainer, out _);
        UIFactory.SetLayoutElement(scrollWrap,
            minWidth: 260, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 80, flexibleHeight: 1);
    }

    private void BuildFooter()
    {
        var footer = UIFactory.CreateHorizontalGroup(ContentRoot, "Footer",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(footer,
            minWidth: 260, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 30, preferredHeight: 32, flexibleHeight: 0);

        _unbindBtn = UIFactory.CreateButton(footer, "Unbind", "Unbind active");
        UIFactory.SetLayoutElement(_unbindBtn.GameObject,
            minWidth: 120, preferredWidth: 200, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        _unbindBtn.OnClick = () => EnqueueOrWarn(MessageService.BCCOM_FAM_UNBIND);
    }

    private static void EnqueueOrWarn(string command)
    {
        if (!MessageService.IsInitialized)
        {
            LogUtils.LogWarning($"FamiliarBrowserOverlay: cannot send '{command}' — MessageService not bound yet.");
            return;
        }
        MessageService.EnqueueMessage(command);
    }

    private void CycleBox(int direction)
    {
        var boxes = PlayerStateService.BoxList;
        if (boxes == null || boxes.Count == 0) return;

        // Find current active box index, default to 0 if not in list.
        int idx = boxes.IndexOf(PlayerStateService.ActiveBox ?? "");
        if (idx < 0) idx = 0;

        idx = (idx + direction + boxes.Count) % boxes.Count;
        var target = boxes[idx];
        ClearPendingSwap();
        PlayerStateService.SetActiveBox(target);
        EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_SWITCH_BOX_FORMAT, target));
        EnqueueOrWarn(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);
    }

    private void OnAnyBoxStateChanged()
    {
        Render();
    }

    private void Render()
    {
        if (_boxNameLabel == null) return;

        // Header label "Name  (X / N)"
        var boxes = PlayerStateService.BoxList;
        var active = PlayerStateService.ActiveBox;
        if (string.IsNullOrEmpty(active))
        {
            _boxNameLabel.text = "(no box selected)";
        }
        else if (boxes != null && boxes.Count > 0)
        {
            int idx = boxes.IndexOf(active);
            if (idx >= 0)
                _boxNameLabel.text = $"{active}  ({idx + 1} / {boxes.Count})";
            else
                _boxNameLabel.text = active; // active box not in list yet — race during refresh
        }
        else
        {
            _boxNameLabel.text = active;
        }

        // Active familiar
        var fam = PlayerStateService.Familiar;
        _activeFamLabel.text = string.IsNullOrEmpty(fam.Name)
            ? "Active: (none bound)"
            : $"Active: {fam.Name}   Lv {fam.Level}";
        if (_unbindBtn != null) _unbindBtn.Component.interactable = !string.IsNullOrEmpty(fam.Name);

        // Familiar list
        ClearChildren(_famListContainer);

        if (PlayerStateService.BoxList == null || PlayerStateService.BoxList.Count == 0)
        {
            AddListLine("(no boxes loaded — click Reload above)");
            return;
        }

        if (string.IsNullOrEmpty(active))
        {
            AddListLine("(use ← / → above to pick a box)");
            return;
        }

        if (!PlayerStateService.BoxContents.TryGetValue(active, out var entries) || entries.Count == 0)
        {
            AddListLine($"(loading familiars for {active}…)");
            return;
        }

        foreach (var entry in entries)
        {
            int idx = entry.Index;
            string label = $"{entry.Index:00}  —  {entry.Name}";
            if (entry.Level > 0)    label += $"  Lv {entry.Level}";
            if (entry.Prestige > 0) label += $"  P{entry.Prestige}";
            if (entry.IsShiny)
            {
                label += "  ★";
                var school = entry.ShinySchool;
                if (!string.IsNullOrEmpty(school)) label += $" {school}";
            }

            var b = UIFactory.CreateButton(_famListContainer, $"FamBtn_{entry.Index}", label);
            UIFactory.SetLayoutElement(b.GameObject,
                minWidth: 240, preferredWidth: 260, flexibleWidth: 1,
                minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
            var t = b.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null)
            {
                t.alignment = TextAlignmentOptions.MidlineLeft;
                t.fontSize = Theme.ScaledOverlay(12);
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Overflow;
            }
            b.OnClick = () => OnFamiliarClicked(idx);
        }
    }

    private void AddListLine(string text)
    {
        var lbl = UIFactory.CreateLabel(_famListContainer, "ListLine", text,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledOverlay(12));
        UIFactory.SetLayoutElement(lbl.GameObject,
            minWidth: 240, preferredWidth: 260, flexibleWidth: 1,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);
        lbl.TextMesh.fontStyle = FontStyles.Italic;
    }

    private void OnFamiliarClicked(int index)
    {
        bool hasActive = !string.IsNullOrEmpty(PlayerStateService.Familiar.Name);
        if (!hasActive)
        {
            ClearPendingSwap();
            EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_BIND_BY_INDEX_FORMAT, index));
            return;
        }

        float now = Time.realtimeSinceStartup;
        bool armed = _pendingSwapIndex == index && now <= _pendingSwapDeadline;
        if (armed)
        {
            ClearPendingSwap();
            EnqueueOrWarn(MessageService.BCCOM_FAM_UNBIND);
            EnqueueOrWarn(string.Format(MessageService.BCCOM_FAM_BIND_BY_INDEX_FORMAT, index));
            return;
        }

        string targetName = $"#{index}";
        var act = PlayerStateService.ActiveBox;
        if (!string.IsNullOrEmpty(act)
            && PlayerStateService.BoxContents.TryGetValue(act, out var entries))
        {
            foreach (var e in entries) if (e.Index == index) { targetName = e.Name; break; }
        }

        _pendingSwapIndex = index;
        _pendingSwapDeadline = now + SWAP_CONFIRM_WINDOW_SECONDS;
        if (_swapWarningLabel != null)
            _swapWarningLabel.text = $"Active: {PlayerStateService.Familiar.Name}. Click {targetName} again within {(int)SWAP_CONFIRM_WINDOW_SECONDS}s to unbind current and bind it.";
    }

    private void ClearPendingSwap()
    {
        _pendingSwapIndex = -1;
        _pendingSwapDeadline = -1f;
        if (_swapWarningLabel != null) _swapWarningLabel.text = "";
    }

    private static void ClearChildren(GameObject parent)
    {
        if (parent == null) return;
        var t = parent.transform;
        for (int i = t.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
    }

    internal override void Reset()
    {
        if (_subscribed)
        {
            PlayerStateService.BoxListChanged     -= OnAnyBoxStateChanged;
            PlayerStateService.BoxContentsChanged -= OnAnyBoxStateChanged;
            PlayerStateService.ActiveBoxChanged   -= OnAnyBoxStateChanged;
            PlayerStateService.FamiliarChanged    -= OnAnyBoxStateChanged;
            _subscribed = false;
        }
        // Always remove the auto-pull ticker so it doesn't keep firing after
        // panel destruction. Idempotent — Remove on a non-registered handler
        // is a no-op.
        Behaviors.CoreUpdateBehavior.Actions.Remove(TickDeferredAutoPull);
        _autoPullDone = false;
    }
}
