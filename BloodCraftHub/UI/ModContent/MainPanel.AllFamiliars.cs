using System;
using System.Collections.Generic;
using BloodCraftHub.Config;
using BloodCraftHub.Resources;
using BloodCraftHub.Services;
using BloodCraftHub.UI.Framework.CustomLib.Util;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Models;
using BloodCraftHub.Utils;
using TMPro;
using UnityEngine;

namespace BloodCraftHub.UI.ModContent;

// 0.11.0: cross-box "All Familiars" tab. Mirrors V-Bloods tab structure but
// lists EVERY familiar entry from PlayerStateService.BoxContents (not just
// V-Bloods). The V-Blood scanner already walks every box and populates
// BoxContents per box, so this tab is free to render — it reuses the same
// scan invocation rather than adding new server traffic.
//
// Row: "[box] idx — Name  Lv X  Pn  ★ school   [Bind] [Delete]"
// Click name area = switch box + bind that familiar (matches BoxesTab UX).
// Delete = two-click confirm; .fam cb <box> if needed, then .fam r <index>.
public partial class MainPanel
{
    // Filter / sort state — local to this tab. Sort cycle is independent
    // of FamiliarSortOrderSetting so the V-Bloods tab and All-Familiars tab
    // can each be on a different sort without fighting.
    private enum AllFamSort { BoxIndex, Alphabetical, LevelDesc, ShinyFirst }
    private AllFamSort _allFamSort = AllFamSort.BoxIndex;

    private string _allFamSearch = "";

    private GameObject       _allFamRowContainer;
    private TextMeshProUGUI  _allFamStatsLabel;
    private TextMeshProUGUI  _allFamScanStatusLabel;
    private ButtonRef        _allFamScanButton;
    private ButtonRef        _allFamSortButton;
    private InputFieldRef    _allFamSearchInput;
    private bool             _allFamSubscribed;

    // Two-click delete confirm — local state so deletions in the All-
    // Familiars tab don't share arming with the Boxes tab.
    private int   _allFamPendingDeleteRowKey = -1;
    private float _allFamPendingDeleteDeadline = -1f;
    private const float ALLFAM_DELETE_CONFIRM_WINDOW_SECONDS = 3f;

    private void BuildAllFamiliarsTab(GameObject page)
    {
        var headerCard = AddCard(page, "AllFamHeaderCard");
        AddSectionHeading(headerCard, "All Familiars");
        AddBodyText(headerCard,
            "Every familiar in every box, in one list. Click a row to switch to that box and bind the familiar. " +
            $"Delete permanently removes the entry ({Mono(".fam r")} — two-click confirm). " +
            $"The list is populated by the same V-Blood scanner — click {Mono("Scan all")} to walk every box; if you already scanned for V-Bloods the rows are already here.");

        AddSpacer(page, 6);

        var statusCard = AddCard(page, "AllFamStatusCard");
        var statusRow = UIFactory.CreateHorizontalGroup(statusCard, "AllFamStatusRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(statusRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 30, preferredHeight: 32, flexibleHeight: 0);

        _allFamStatsLabel = AddInfoLabel(statusRow, "AllFamStats", "0 familiars across 0 boxes",
            FontStyles.Bold, fontSize: Theme.ScaledUI(13));
        UIFactory.SetLayoutElement(_allFamStatsLabel.gameObject,
            minWidth: 180, preferredWidth: 260, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 28, flexibleHeight: 0);

        _allFamScanButton = UIFactory.CreateButton(statusRow, "AllFamScanBtn", "Scan all");
        UIFactory.SetLayoutElement(_allFamScanButton.GameObject,
            minWidth: 90, preferredWidth: 110, flexibleWidth: 0,
            minHeight: 28, preferredHeight: 30, flexibleHeight: 0);
        var scanBtnText = _allFamScanButton.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (scanBtnText != null) { scanBtnText.fontSize = Theme.ScaledUI(12); scanBtnText.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(_allFamScanButton.GameObject,
            "Sweep every familiar box (same backend as the V-Bloods tab Scan). Populates this list AND the V-Blood collection in one pass. ~30–60s.");
        _allFamScanButton.OnClick = () =>
        {
            if (VBloodScannerService.Scanning) VBloodScannerService.CancelScan();
            else                                VBloodScannerService.StartScan();
            RefreshAllFamScanButton();
        };

        _allFamScanStatusLabel = AddInfoLabel(statusCard, "AllFamScanStatus", "",
            FontStyles.Italic, fontSize: Theme.ScaledUI(11));
        _allFamScanStatusLabel.gameObject.SetActive(false);

        AddSpacer(page, 6);

        // Filter card — search input + sort cycle button.
        var filterCard = AddCard(page, "AllFamFilterCard");
        var filterRow = UIFactory.CreateHorizontalGroup(filterCard, "AllFamFilterRow",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 6, padding: new Vector4(0, 0, 0, 0));
        UIFactory.SetLayoutElement(filterRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 30, preferredHeight: 32, flexibleHeight: 0);

        _allFamSearchInput = UIFactory.CreateInputField(filterRow, "AllFamSearch", "Filter by name…");
        UIFactory.SetLayoutElement(_allFamSearchInput.GameObject,
            minWidth: 180, preferredWidth: 240, flexibleWidth: 1,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        _allFamSearchInput.OnValueChanged += (string val) =>
        {
            _allFamSearch = val ?? "";
            RebuildAllFamRows();
        };

        _allFamSortButton = UIFactory.CreateButton(filterRow, "AllFamSortBtn", FormatAllFamSortText());
        UIFactory.SetLayoutElement(_allFamSortButton.GameObject,
            minWidth: 110, preferredWidth: 140, flexibleWidth: 0,
            minHeight: 26, preferredHeight: 28, flexibleHeight: 0);
        var sortTxt = _allFamSortButton.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (sortTxt != null) { sortTxt.fontSize = Theme.ScaledUI(12); sortTxt.alignment = TextAlignmentOptions.Center; }
        TooltipHover.Attach(_allFamSortButton.GameObject,
            "Cycle sort order: Box order (server) → A→Z → Level desc → Shinies first.");
        _allFamSortButton.OnClick = () =>
        {
            _allFamSort = _allFamSort switch
            {
                AllFamSort.BoxIndex     => AllFamSort.Alphabetical,
                AllFamSort.Alphabetical => AllFamSort.LevelDesc,
                AllFamSort.LevelDesc    => AllFamSort.ShinyFirst,
                AllFamSort.ShinyFirst   => AllFamSort.BoxIndex,
                _                       => AllFamSort.BoxIndex,
            };
            RefreshAllFamSortButton();
            RebuildAllFamRows();
        };

        AddSpacer(page, 6);

        var rowsCard = AddCard(page, "AllFamRowsCard", padding: 4, innerSpacing: 2);
        BuildAllFamColumnHeader(rowsCard);

        _allFamRowContainer = UIFactory.CreateVerticalGroup(rowsCard, "AllFamRows",
            forceWidth: true, forceHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(0, 0, 2, 2));
        UIFactory.SetLayoutElement(_allFamRowContainer,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 60, flexibleHeight: 0);

        if (!_allFamSubscribed)
        {
            PlayerStateService.BoxContentsChanged += OnAllFamBoxContentsChanged;
            VBloodScannerService.ScanStateChanged += OnAllFamScanStateChanged;
            _allFamSubscribed = true;
        }

        RebuildAllFamRows();
        RefreshAllFamHeader();
        RefreshAllFamScanButton();
    }

    private void BuildAllFamColumnHeader(GameObject parent)
    {
        var headerRow = UIFactory.CreateHorizontalGroup(parent, "AllFamColHeader",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(headerRow,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 20, preferredHeight: 22, flexibleHeight: 0);

        string ColLabel(string s) => $"<color={Theme.MutedBodyHex}><b>{s}</b></color>";

        void AddCol(string text, int min, int pref, int flex)
        {
            var lbl = UIFactory.CreateLabel(headerRow, $"Col_{text}", ColLabel(text),
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
            UIFactory.SetLayoutElement(lbl.GameObject,
                minWidth: min, preferredWidth: pref, flexibleWidth: flex,
                minHeight: 18, preferredHeight: 20, flexibleHeight: 0);
            lbl.TextMesh.enableWordWrapping = false;
            lbl.TextMesh.overflowMode = TextOverflowModes.Overflow;
        }

        AddCol("Box · #",          90,  110, 0);
        AddCol("Familiar",        160, 200, 1);
        AddCol("Level",            46,  56, 0);
        AddCol("",                 60,  80, 0); // [Bind] column
        AddCol("",                 56,  72, 0); // [Delete] column
    }

    private void OnAllFamBoxContentsChanged()
    {
        RebuildAllFamRows();
        RefreshAllFamHeader();
    }

    private void OnAllFamScanStateChanged()
    {
        RefreshAllFamScanButton();
        RefreshAllFamHeader();
    }

    private void RefreshAllFamScanButton()
    {
        if (_allFamScanButton == null) return;
        var t = _allFamScanButton.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = VBloodScannerService.Scanning ? "Cancel" : "Scan all";
    }

    private void RefreshAllFamHeader()
    {
        if (_allFamStatsLabel == null) return;
        int boxes = 0, entries = 0, shinies = 0;
        foreach (var kv in PlayerStateService.BoxContents)
        {
            if (kv.Value == null || kv.Value.Count == 0) continue;
            boxes++;
            foreach (var e in kv.Value)
            {
                entries++;
                if (e.IsShiny) shinies++;
            }
        }
        string text = $"{entries} familiar{(entries == 1 ? "" : "s")} across {boxes} box{(boxes == 1 ? "" : "es")}";
        if (shinies > 0) text += $"  ·  {shinies} shiny";
        _allFamStatsLabel.text = text;

        if (_allFamScanStatusLabel != null)
        {
            if (VBloodScannerService.Scanning)
            {
                string box = VBloodScannerService.CurrentBoxBeingScanned;
                string suffix = string.IsNullOrEmpty(box) ? "" : $" — {box}";
                _allFamScanStatusLabel.text = $"Scanning… box {VBloodScannerService.CompletedForCurrentScan + 1} / {VBloodScannerService.TotalForCurrentScan}{suffix}";
                if (!_allFamScanStatusLabel.gameObject.activeSelf) _allFamScanStatusLabel.gameObject.SetActive(true);
            }
            else if (_allFamScanStatusLabel.text != null && _allFamScanStatusLabel.text.StartsWith("Scanning"))
            {
                _allFamScanStatusLabel.gameObject.SetActive(false);
            }
        }
    }

    private string FormatAllFamSortText() => _allFamSort switch
    {
        AllFamSort.BoxIndex     => "Sort: Box · #",
        AllFamSort.Alphabetical => "Sort: A→Z",
        AllFamSort.LevelDesc    => "Sort: Level ↓",
        AllFamSort.ShinyFirst   => "Sort: Shinies ↑",
        _                       => "Sort: Box · #",
    };

    private void RefreshAllFamSortButton()
    {
        if (_allFamSortButton == null) return;
        var t = _allFamSortButton.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = FormatAllFamSortText();
    }

    private struct AllFamRow
    {
        public string Box;
        public int    Index;
        public string Name;
        public int    Level;
        public int    Prestige;
        public bool   IsShiny;
        public string ShinySchool;
        // Stable key for delete-arming state across rebuilds.
        public int    RowKey => HashCode.Combine(Box ?? "", Index);
    }

    private void RebuildAllFamRows()
    {
        if (_allFamRowContainer == null) return;

        // Tear down existing rows.
        for (int i = _allFamRowContainer.transform.childCount - 1; i >= 0; --i)
        {
            var child = _allFamRowContainer.transform.GetChild(i);
            if (child == null) continue;
            UnityEngine.Object.Destroy(child.gameObject);
        }

        // Flatten BoxContents into a row list, applying the search filter.
        string search = (_allFamSearch ?? "").Trim();
        bool hasSearch = !string.IsNullOrEmpty(search);

        var rows = new List<AllFamRow>();
        foreach (var kv in PlayerStateService.BoxContents)
        {
            var box = kv.Key;
            var entries = kv.Value;
            if (entries == null) continue;
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.Name)) continue;
                if (hasSearch && e.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                rows.Add(new AllFamRow
                {
                    Box         = box,
                    Index       = e.Index,
                    Name        = e.Name,
                    Level       = e.Level,
                    Prestige    = e.Prestige,
                    IsShiny     = e.IsShiny,
                    ShinySchool = e.ShinySchool,
                });
            }
        }

        // Sort according to the local sort mode.
        switch (_allFamSort)
        {
            case AllFamSort.Alphabetical:
                rows.Sort((a, b) =>
                {
                    int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    c = string.Compare(a.Box ?? "", b.Box ?? "", StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return a.Index.CompareTo(b.Index);
                });
                break;
            case AllFamSort.LevelDesc:
                rows.Sort((a, b) =>
                {
                    if (b.Level != a.Level) return b.Level.CompareTo(a.Level);
                    int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return a.Index.CompareTo(b.Index);
                });
                break;
            case AllFamSort.ShinyFirst:
                rows.Sort((a, b) =>
                {
                    if (a.IsShiny != b.IsShiny) return b.IsShiny.CompareTo(a.IsShiny);
                    int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return a.Index.CompareTo(b.Index);
                });
                break;
            default:
                rows.Sort((a, b) =>
                {
                    int c = string.Compare(a.Box ?? "", b.Box ?? "", StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    return a.Index.CompareTo(b.Index);
                });
                break;
        }

        if (rows.Count == 0)
        {
            var empty = UIFactory.CreateLabel(_allFamRowContainer, "AllFamEmpty",
                PlayerStateService.BoxContents.Count == 0
                    ? "(no box data yet — click Scan all)"
                    : hasSearch
                        ? "(no familiars match this search)"
                        : "(no familiars captured)",
                TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
            UIFactory.SetLayoutElement(empty.GameObject,
                minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
                minHeight: 28, preferredHeight: 32, flexibleHeight: 0);
            empty.TextMesh.fontStyle = FontStyles.Italic;
            return;
        }

        foreach (var r in rows)
            BuildAllFamRow(_allFamRowContainer, r);
    }

    private void BuildAllFamRow(GameObject parent, AllFamRow r)
    {
        var row = UIFactory.CreateHorizontalGroup(parent, $"AllFamRow_{r.Box}_{r.Index}",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 4, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(row,
            minWidth: 360, preferredWidth: 400, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 26, flexibleHeight: 0);

        // Box · # column
        var boxCol = UIFactory.CreateLabel(row, "Box", $"<color={Theme.MutedBodyHex}>{r.Box}</color> · {r.Index:00}",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(boxCol.GameObject,
            minWidth: 90, preferredWidth: 110, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        boxCol.TextMesh.enableWordWrapping = false;
        boxCol.TextMesh.overflowMode = TextOverflowModes.Ellipsis;

        // Familiar name column (with optional shiny/prestige decoration)
        string nameText = r.Name;
        if (r.Prestige > 0) nameText += $"  <color={Theme.MutedBodyHex}>P{r.Prestige}</color>";
        if (r.IsShiny)
        {
            nameText += "  <color=#FFD75A>★</color>";
            if (!string.IsNullOrEmpty(r.ShinySchool)) nameText += $" {r.ShinySchool}";
        }
        var nameCol = UIFactory.CreateLabel(row, "Name", nameText,
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(12));
        UIFactory.SetLayoutElement(nameCol.GameObject,
            minWidth: 160, preferredWidth: 200, flexibleWidth: 1,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        nameCol.TextMesh.enableWordWrapping = false;
        nameCol.TextMesh.overflowMode = TextOverflowModes.Ellipsis;

        // Level column
        var lvlCol = UIFactory.CreateLabel(row, "Level", r.Level > 0 ? $"Lv {r.Level}" : "—",
            TextAlignmentOptions.MidlineLeft, color: null, fontSize: Theme.ScaledUI(11));
        UIFactory.SetLayoutElement(lvlCol.GameObject,
            minWidth: 46, preferredWidth: 56, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);

        // Bind button — switches to that box and binds the entry.
        var bindBtn = UIFactory.CreateButton(row, $"AllFamBind_{r.Box}_{r.Index}", "Bind");
        UIFactory.SetLayoutElement(bindBtn.GameObject,
            minWidth: 60, preferredWidth: 80, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        var bindTxt = bindBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (bindTxt != null) bindTxt.fontSize = Theme.ScaledUI(11);
        TooltipHover.Attach(bindBtn.GameObject,
            "Switch to this familiar's box and bind it. If you already have an active familiar, this issues an unbind first.");
        string captureBox = r.Box;
        int    captureIdx = r.Index;
        bindBtn.OnClick = () => OnAllFamBindClicked(captureBox, captureIdx);

        // Delete button — two-click confirm, then .fam cb <box> + .fam r <idx>.
        var delBtn = UIFactory.CreateButton(row, $"AllFamDel_{r.Box}_{r.Index}", "Delete",
            new Color(0.55f, 0.18f, 0.18f));
        UIFactory.SetLayoutElement(delBtn.GameObject,
            minWidth: 56, preferredWidth: 72, flexibleWidth: 0,
            minHeight: 22, preferredHeight: 24, flexibleHeight: 0);
        var delTxt = delBtn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (delTxt != null) delTxt.fontSize = Theme.ScaledUI(11);
        TooltipHover.Attach(delBtn.GameObject,
            $"PERMANENTLY delete this familiar ({Mono(".fam r")}). Two-click confirm — first click changes the label to 'Confirm?' and waits {ALLFAM_DELETE_CONFIRM_WINDOW_SECONDS:0}s. Box record is gone forever.");
        int rowKey = r.RowKey;
        delBtn.OnClick = () => OnAllFamDeleteClicked(captureBox, captureIdx, rowKey, delBtn);
    }

    private void OnAllFamBindClicked(string box, int index)
    {
        if (string.IsNullOrEmpty(box) || index <= 0) return;
        if (!MessageService.IsInitialized)
        {
            LogUtils.LogWarning("AllFamiliars: cannot bind — MessageService not bound yet.");
            return;
        }

        var fam = PlayerStateService.Familiar;
        if (fam.HasActive)
            MessageService.EnqueueMessage(MessageService.BCCOM_FAM_UNBIND);

        bool sameBox = !string.IsNullOrEmpty(PlayerStateService.ActiveBox)
                    && string.Equals(box, PlayerStateService.ActiveBox, StringComparison.OrdinalIgnoreCase);
        if (!sameBox)
        {
            PlayerStateService.SetActiveBox(box);
            MessageService.EnqueueMessage(string.Format(MessageService.BCCOM_FAM_SWITCH_BOX_FORMAT, box));
        }
        MessageService.EnqueueMessage(string.Format(MessageService.BCCOM_FAM_BIND_BY_INDEX_FORMAT, index));
    }

    private void OnAllFamDeleteClicked(string box, int index, int rowKey, ButtonRef btn)
    {
        float now = Time.realtimeSinceStartup;
        bool armed = _allFamPendingDeleteRowKey == rowKey && now <= _allFamPendingDeleteDeadline;
        if (armed)
        {
            ClearAllFamPendingDelete();
            var label = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = "Delete";
            if (!MessageService.IsInitialized)
            {
                LogUtils.LogWarning("AllFamiliars: cannot delete — MessageService not bound yet.");
                return;
            }
            bool sameBox = !string.IsNullOrEmpty(PlayerStateService.ActiveBox)
                        && string.Equals(box, PlayerStateService.ActiveBox, StringComparison.OrdinalIgnoreCase);
            if (!sameBox)
            {
                PlayerStateService.SetActiveBox(box);
                MessageService.EnqueueMessage(string.Format(MessageService.BCCOM_FAM_SWITCH_BOX_FORMAT, box));
            }
            MessageService.EnqueueMessage(string.Format(MessageService.BCCOM_FAM_REMOVE_FORMAT, index));
            // Re-list so the row vanishes without a manual reload.
            MessageService.EnqueueMessage(MessageService.BCCOM_FAM_LIST_CURRENT_BOX);
            return;
        }

        _allFamPendingDeleteRowKey  = rowKey;
        _allFamPendingDeleteDeadline = now + ALLFAM_DELETE_CONFIRM_WINDOW_SECONDS;
        var lbl = btn.Component.GetComponentInChildren<TextMeshProUGUI>();
        if (lbl != null) lbl.text = "Confirm?";
    }

    private void ClearAllFamPendingDelete()
    {
        _allFamPendingDeleteRowKey  = -1;
        _allFamPendingDeleteDeadline = -1f;
    }
}
