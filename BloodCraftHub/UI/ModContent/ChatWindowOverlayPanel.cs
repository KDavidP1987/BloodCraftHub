using System;
using System.Collections.Generic;
using System.Text;
using BloodCraftHub.Config;
using BloodCraftHub.Services;
using BloodCraftHub.Utils;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.CustomLib.Util;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Models;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using ProjectM.Network;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;

namespace BloodCraftHub.UI.ModContent;

// 0.17 (feature/v0.17-standalone-ui): standalone tabbed chat window.
//
// INCREMENT 1 = read-only display. Mirrors every inbound chat message into a
// persistent, channel-tabbed, scrollable view; native chat left untouched.
// Drag/resize follow the standard overlay rules — subject to the "Lock overlays"
// toggle like every other overlay (turn it off to move/resize). Formatting +
// per-window customization (timestamps, channel labels, transparency) are wired
// through Settings so the primary UI can drive them.
//
// Later increments add input/send, sender names, per-person whisper tabs, and
// the option to REPLACE the native chat (hide it) for a single powerful window.
public class ChatWindowOverlayPanel : ResizeablePanelBase
{
    public override string PanelId => "ChatWindowOverlay";
    public override PanelType PanelType => PanelType.ChatWindowOverlay;

    public override int MinWidth  => 320;
    public override int MinHeight => 180;

    public override Vector2 DefaultAnchorMin => new(0.5f, 0.5f);
    public override Vector2 DefaultAnchorMax => new(0.5f, 0.5f);
    public override Vector2 DefaultPivot     => new(0.5f, 0.5f);
    // Lower-left by default — where the native chat lives.
    public override Vector2 DefaultPosition  => new(
        -Owner.Scaler.m_ReferenceResolution.x * 0.5f + 30f,
        -Owner.Scaler.m_ReferenceResolution.y * 0.5f + 220f);

    public override bool CanDrag => true;
    public override PanelDragger.ResizeTypes CanResize => PanelDragger.ResizeTypes.All;
    public override float Opacity => Settings.TransparencyToAlpha(Settings.ChatWindowOverlayTransparency);
    public override bool UsesCustomBackgroundColor => true;

    // 0.17.0: the chat window themes off its OWN color (Settings.ChatWindowBackgroundColor),
    // independent of the main panel's color. Overriding means BOTH construct-time and the
    // shared RefreshAllPanelBackgrounds walk paint the chat window with its own color, so
    // changing the main panel color never bleeds into the chat window.
    public override void RefreshBackgroundColor()
    {
        var rgb = Settings.ChatWindowBackgroundColor;
        UIFactory.ApplyBackgroundColorRgbToPanel(uiRoot, rgb);
        // ALSO recolor the main "Content" background image directly. The shared
        // walker skips "card-ish" neutral greys (its heuristic), and the chat
        // window's only opaque surface IS this base image (its rows are transparent)
        // — so without this a neutral color pick wouldn't visibly apply. Alpha is
        // left to the transparency control (ApplyOpacityToPanel owns it). NOTE: at
        // 100% transparency the alpha floors to ~0.05, so ANY color is ~invisible.
        try
        {
            var content = uiRoot != null ? uiRoot.transform.Find("Content") : null;
            var img = content != null ? content.GetComponent<UnityEngine.UI.Image>() : null;
            if (img != null) { var c = img.color; img.color = new Color(rgb.r, rgb.g, rgb.b, c.a); }
        }
        catch { }
    }

    private static readonly (ChatRelayService.Channel? Filter, string Label)[] TabDefs =
    {
        (null,                             "All"),
        (ChatRelayService.Channel.Global,  "Global"),
        (ChatRelayService.Channel.Local,   "Local"),
        (ChatRelayService.Channel.Clan,    "Clan"),
        (ChatRelayService.Channel.System,  "System"),
        (ChatRelayService.Channel.Whisper, "Whispers"),
    };

    private int _activeTab;
    private TextMeshProUGUI _log;
    private bool _subscribed;
    private readonly List<ButtonRef> _tabButtons = new();
    private InputFieldRef _input;
    private GameObject _inputRow;       // 0.17.0: kept so the row can grow with wrapped input
    // 0.17.0: the scroll view + its content rect, kept so we can auto-scroll the
    // log to the newest message (bottom or top, per ChatNewestAtBottom).
    private UnityEngine.UI.ScrollRect _scrollRect;
    private RectTransform _scrollContentRect;
    // 0.17.0: input grows vertically (word-wrap) from one line up to this many px.
    private const float InputBaseHeight = 24f;
    private const float InputMaxHeight  = 96f;

    // 0.17.0 whisper sub-tabs. When the Whispers top-tab is active, a second row
    // appears: "All" (every whisper) + one sub-tab per conversation partner.
    private GameObject _whisperSubRow;
    // 0.17.3: persistent "whisper a typed name" row (for players who haven't spoken,
    // so aren't in the picker). Created once so typing survives sub-tab rebuilds.
    private GameObject _whisperEntryRow;
    private InputFieldRef _whisperNameInput;
    private readonly List<ButtonRef> _whisperSubButtons = new();
    private readonly List<string> _whisperSubPartners = new(); // parallel to _whisperSubButtons; null = All
    private string _activeWhisperPartner; // null = All Whispers
    // Players chosen from the picker but not yet messaged — kept so their sub-tab
    // shows immediately (before any whisper line exists for them).
    private readonly HashSet<string> _initiatedPartners = new();
    // Partners the user explicitly CLOSED (× on the sub-tab). Suppresses their tab
    // even if old whisper lines remain in the buffer; cleared (re-opened) when a
    // new whisper from them arrives.
    private readonly HashSet<string> _closedPartners = new();
    private TMPro.TMP_Dropdown _whisperPicker;
    private List<PlayerRosterService.PlayerRef> _pickerRoster;
    private static readonly int WhispersTabIndex = System.Array.FindIndex(TabDefs, t => t.Filter == ChatRelayService.Channel.Whisper);
    // 0.17.0: per-tab unread counts. A message for a channel you're NOT currently
    // viewing bumps that tab's count; selecting the tab resets it. The All tab
    // (index 0) shows everything, so it never carries a badge.
    private readonly int[] _unread = new int[TabDefs.Length];
    // Max chars per chat message — under the FixedString512Bytes wire cap.
    private const int MaxChatChars = 500;

    // 0.17.0: All-tab compose target — mirrors the native chat's Enter+Tab flow.
    // On the All tab a "Send to:" dropdown picks the outgoing channel/whisper, and
    // Tab cycles through the available targets (Global, Local, Clan if in a clan,
    // then each active whisper partner). Other tabs send on their own channel.
    private readonly struct SendTarget
    {
        public readonly ChatMessageType? Channel; // null => whisper to Whisper
        public readonly string Whisper;            // partner name when Channel is null
        public readonly string Label;              // dropdown text
        private SendTarget(ChatMessageType? channel, string whisper, string label)
        { Channel = channel; Whisper = whisper; Label = label; }
        public static SendTarget Chan(ChatMessageType c, string label) => new(c, null, label);
        public static SendTarget Whis(string partner) => new(null, partner, $"@{partner}");
        public bool IsWhisper => Channel == null;
    }
    private GameObject _composeDropdownObj; // compact target dropdown INSIDE the input row (All tab)
    private TMP_Dropdown _composeDropdown;
    private readonly List<SendTarget> _composeTargets = new();
    private int _composeIndex;
    private string _composeSignature = ""; // rebuild guard (clan state + whisper partners)
    private Action _composeKeyTicker;
    private Action _tabHotkeyTicker; // 0.17.3: <Modifier>+1..6 tab switch (chat open, not typing)
    // Frames of "still counts as typing" grace after the input reports unfocused.
    // The field's focus flag blips for a frame around a Tab press, which made every
    // OTHER Tab a no-op (friend-test: Tab needed pressing twice to switch). The
    // grace bridges that blip so each Tab press cycles exactly once.
    private int _inputFocusGrace;

    public ChatWindowOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();
        SetTitle("Chat");

        // 0.17.0: only the message log should grow/shrink when the window is
        // resized — not the tab row or the input row. The ContentRoot's vertical
        // group force-expands children, which distributed extra height to ALL rows
        // and left blank space in the header/footer (friend-test report). Turn it
        // off so extra space goes only to the flexibleHeight log below.
        var contentVlg = ContentRoot.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (contentVlg != null) { contentVlg.childForceExpandHeight = false; contentVlg.childControlHeight = true; }

        // Tab button row.
        var tabRow = UIFactory.CreateHorizontalGroup(ContentRoot, "ChatTabs",
            true, false, true, true, 2, new Vector4(2, 2, 2, 2), bgColor: new Color(0f, 0f, 0f, 0f));
        UIFactory.SetLayoutElement(tabRow, minHeight: 24, preferredHeight: 24, flexibleWidth: 1);

        _tabButtons.Clear();
        for (int i = 0; i < TabDefs.Length; i++)
        {
            int idx = i;
            var btn = UIFactory.CreateButton(tabRow, $"ChatTab_{TabDefs[i].Label}", TabDefs[i].Label);
            UIFactory.SetLayoutElement(btn.GameObject,
                minWidth: 36, preferredWidth: 60, flexibleWidth: 1,
                minHeight: 22, preferredHeight: 22, flexibleHeight: 0);
            // 0.17.3: keep the label (incl. the unread badge "Clan (3)") on ONE line.
            // At larger chat text it used to word-wrap to two lines and overlap the
            // rows below; ellipsis truncates horizontally instead of spilling.
            if (btn.ButtonText != null)
            {
                btn.ButtonText.enableWordWrapping = false;
                btn.ButtonText.overflowMode = TextOverflowModes.Ellipsis;
            }
            btn.OnClick = () => SwitchToTab(idx);
            _tabButtons.Add(btn);
        }

        // 0.17.0: whisper sub-tab row — hidden unless the Whispers tab is active.
        _whisperSubRow = UIFactory.CreateHorizontalGroup(ContentRoot, "ChatWhisperSubTabs",
            false, false, true, true, 2, new Vector4(2, 0, 2, 0), bgColor: new Color(0f, 0f, 0f, 0f));
        UIFactory.SetLayoutElement(_whisperSubRow, minHeight: 22, preferredHeight: 22, flexibleWidth: 1);
        _whisperSubRow.SetActive(false);

        // 0.17.3: persistent "whisper by typing a name" row — lets you start a whisper
        // with a player who hasn't spoken (so isn't in the +Whisper… picker). Shown
        // only on the Whispers tab; created once so typing survives sub-tab rebuilds.
        _whisperEntryRow = UIFactory.CreateHorizontalGroup(ContentRoot, "ChatWhisperEntry",
            false, false, true, true, 2, new Vector4(2, 0, 2, 0), bgColor: new Color(0f, 0f, 0f, 0f));
        UIFactory.SetLayoutElement(_whisperEntryRow, minHeight: 24, preferredHeight: 24, flexibleWidth: 1);
        _whisperNameInput = UIFactory.CreateInputField(_whisperEntryRow, "WhisperNameInput", "Whisper player by name…");
        UIFactory.SetLayoutElement(_whisperNameInput.GameObject,
            minWidth: 140, preferredWidth: 220, flexibleWidth: 1, minHeight: 22, preferredHeight: 22, flexibleHeight: 0);
        _whisperNameInput.Component.onSubmit.AddListener(OnWhisperNameSubmit);
        var startWhisperBtn = UIFactory.CreateButton(_whisperEntryRow, "WhisperStartBtn", "Whisper");
        UIFactory.SetLayoutElement(startWhisperBtn.GameObject,
            minWidth: 72, preferredWidth: 72, flexibleWidth: 0, minHeight: 22, preferredHeight: 22, flexibleHeight: 0);
        startWhisperBtn.OnClick = () => StartTypedWhisper();
        TooltipHover.Attach(startWhisperBtn.GameObject, "Start a whisper with the typed player. They must be nearby or have spoken in chat (so the client knows their whisper target). Tip: you can also type \\whisper Name in the message box.");
        _whisperEntryRow.SetActive(false);

        // (0.17.0: the All-tab compose-target control is a compact dropdown placed
        // INSIDE the input row — built by RebuildComposeRow below, beside the input.)

        // Scrollable message log: one multi-line label rebuilt on update.
        var scroll = UIFactory.CreateScrollView(ContentRoot, "ChatLogScroll",
            out var scrollContent, out _, color: new Color(0f, 0f, 0f, 0f));
        UIFactory.SetLayoutElement(scroll,
            minWidth: 300, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 120, preferredHeight: 220, flexibleHeight: 1);
        // CreateScrollView paints its background Theme.Level1 (red) when the passed
        // color == default; force it transparent so the panel's themed background
        // shows through, matching the other overlays.
        var scrollBg = scroll.GetComponent<UnityEngine.UI.Image>();
        if (scrollBg != null) scrollBg.color = new Color(0f, 0f, 0f, 0f);
        // Cache the ScrollRect + content rect for auto-scroll-to-newest.
        _scrollRect = scroll.GetComponent<UnityEngine.UI.ScrollRect>();
        _scrollContentRect = scrollContent.GetComponent<RectTransform>();

        var lbl = UIFactory.CreateLabel(scrollContent, "ChatLog", string.Empty, TextAlignmentOptions.TopLeft);
        _log = lbl.TextMesh;
        _log.richText = true;
        _log.enableWordWrapping = true;
        // 0.17.0: chat-only size — NOT Theme.ScaledOverlay, which is the shared
        // overlay multiplier (enlarging it grew every other overlay too).
        _log.fontSize = ChatFontSize();
        _log.lineSpacing = 4f;
        _log.margin = new Vector4(6, 4, 6, 4);
        _log.color = Theme.DefaultText;
        UIFactory.SetLayoutElement(_log.gameObject, flexibleWidth: 1, flexibleHeight: 1);

        // Input row — type + Enter or the Send button to send on the active tab's
        // channel. While the field is focused, gameplay input is suppressed (you
        // don't move/attack while typing) via InputSuppression.ChatInputActive.
        // forceExpandWidth=false: the input field (flexibleWidth=1) takes the slack
        // and the Send button keeps its fixed width, instead of both flexing as you
        // type (friend-test: the Send button grew/shrank while typing).
        var inputRow = UIFactory.CreateHorizontalGroup(ContentRoot, "ChatInputRow",
            false, false, true, true, 2, new Vector4(2, 2, 2, 2), bgColor: new Color(0f, 0f, 0f, 0f));
        // childAlignment Upper so the (fixed-height) Send button stays top-aligned
        // beside the input as the input grows downward with wrapped text.
        var inputRowHlg = inputRow.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        if (inputRowHlg != null) { inputRowHlg.childAlignment = TextAnchor.UpperCenter; inputRowHlg.childForceExpandHeight = false; }
        _inputRow = inputRow;
        UIFactory.SetLayoutElement(inputRow, minHeight: 26, preferredHeight: 26, flexibleWidth: 1);
        _input = UIFactory.CreateInputField(inputRow, "ChatInput", "Type a message…");
        UIFactory.SetLayoutElement(_input.GameObject,
            minWidth: 160, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        // 0.17.0: word-wrap. SingleLine never wraps (it scrolls horizontally);
        // MultiLineSubmit wraps long messages onto multiple lines while Enter
        // STILL submits (send). The text component already has word-wrapping on.
        _input.Component.lineType = TMPro.TMP_InputField.LineType.MultiLineSubmit;
        if (_input.Component.textComponent != null)
            _input.Component.textComponent.alignment = TextAlignmentOptions.TopLeft;
        // 0.17.0: readable typing area. The framework default is grey-on-grey
        // (light grey box + grey placeholder), which the user found hard to read.
        // Give it a dark, near-opaque box (independent of the window's transparency —
        // ApplyOpacityToPanel only touches the main Content image, not this) with
        // bright typed text and a lighter placeholder. ApplyBackgroundColorRgbToPanel
        // skips this Image too (no LayoutGroup), so the theme color won't override it.
        try
        {
            // Transition None so the image shows our exact color (ColorTint would
            // multiply it by the theme's normal/hover tint and shift the result).
            _input.Component.transition = UnityEngine.UI.Selectable.Transition.None;
            var inBg = _input.GameObject.GetComponent<UnityEngine.UI.Image>();
            if (inBg != null) inBg.color = new Color(0.10f, 0.10f, 0.13f, 0.95f);
            if (_input.Component.textComponent != null)
                _input.Component.textComponent.color = new Color(0.95f, 0.95f, 0.97f, 1f);
            if (_input.PlaceholderText != null)
                _input.PlaceholderText.color = new Color(0.62f, 0.62f, 0.68f, 1f);
        }
        catch { }
        // Respect the game's chat length: ChatMessageEvent.MessageText is a
        // FixedString512Bytes, so cap input well under 512 bytes (500 chars leaves
        // headroom for multi-byte characters). SubmitText also truncates as a backstop.
        _input.Component.characterLimit = MaxChatChars;
        _input.Component.onSubmit.AddListener(OnChatSubmit);
        _input.Component.onSelect.AddListener(OnChatSelect);
        _input.Component.onDeselect.AddListener(OnChatDeselect);
        // Grow the input row vertically as wrapped text adds lines (once-per-frame
        // via InputFieldRef.OnValueChanged), shrinking back when it's cleared. Also
        // strip any Tab char that slipped in — Tab is the compose-cycle key, not text.
        _input.OnValueChanged += OnInputValueChanged;
        // Explicit Send button — reliable even if Enter is intercepted by the
        // still-visible native chat (until the native-takeover increment).
        var sendBtn = UIFactory.CreateButton(inputRow, "ChatSendButton", "Send");
        UIFactory.SetLayoutElement(sendBtn.GameObject,
            minWidth: 64, preferredWidth: 64, flexibleWidth: 0,  // fixed width — never flex while typing
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        sendBtn.OnClick = () => SubmitText(keepFocus: true);

        if (!_subscribed)
        {
            ChatRelayService.LineCaptured += OnLineCaptured;
            _subscribed = true;
        }
        // 0.17.0: Tab-to-cycle the All-tab compose target. Registered once; polls
        // each frame but no-ops unless our input is focused on the All tab.
        _composeKeyTicker = TickComposeKeys;
        BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Add(_composeKeyTicker);
        // 0.17.3: chat-tab switch hotkeys (Modifier + 1..6). No-ops unless the chat
        // window is open, the feature is on, and the input isn't focused.
        _tabHotkeyTicker = TickTabHotkeys;
        BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Add(_tabHotkeyTicker);

        ApplyChatTextScale(); // size the input field to match the chat scale
        UpdateComposeRow();   // build + show the compose dropdown if All tab is active
        UpdateTabHighlight();
        Render();
    }

    private void OnLineCaptured(ChatRelayService.ChatLine line)
    {
        try
        {
            // Bump the unread badge for the message's channel tab — unless you're
            // already on that tab, or on All (which shows everything).
            int t = TabIndexForChannel(line.Channel);
            if (t >= 1 && _activeTab != 0 && t != _activeTab)
            {
                _unread[t]++;
                UpdateTabHighlight(); // refresh the (N) labels
            }
            // A new whisper (received or sent) re-opens a previously-closed tab.
            if (line.Channel == ChatRelayService.Channel.Whisper && !string.IsNullOrEmpty(line.Partner))
                _closedPartners.Remove(line.Partner);
            // A whisper from a new partner adds a sub-tab while the Whispers tab is open.
            if (line.Channel == ChatRelayService.Channel.Whisper
                && WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex)
                RebuildWhisperSubTabs();
            // …and adds a target to the All-tab compose dropdown/cycle (rebuilds only
            // when the target set actually changed).
            if (line.Channel == ChatRelayService.Channel.Whisper && _activeTab == 0)
                UpdateComposeRow();
            Render();
        }
        catch { /* never let chat rendering throw into the inbound pump */ }
    }

    // 0.17.3: whether a channel is shown in the consolidated All tab (per settings).
    private static bool AllTabIncludes(ChatRelayService.Channel c) => c switch
    {
        ChatRelayService.Channel.Global  => Settings.AllTabShowGlobal,
        ChatRelayService.Channel.Local   => Settings.AllTabShowLocal,
        ChatRelayService.Channel.Clan    => Settings.AllTabShowClan,
        ChatRelayService.Channel.System  => Settings.AllTabShowSystem,
        ChatRelayService.Channel.Whisper => Settings.AllTabShowWhisper,
        _ => true,
    };

    // The channel tab index for a captured line's channel, or -1 (All / unmapped).
    private static int TabIndexForChannel(ChatRelayService.Channel c)
    {
        for (int i = 1; i < TabDefs.Length; i++)
            if (TabDefs[i].Filter == c) return i;
        return -1;
    }

    // Re-render with current Settings (called by the Game UI customization toggles).
    // Also re-applies the chat-only text scale so a size change takes effect
    // immediately without closing + reopening the window.
    internal void Refresh()
    {
        try { ApplyChatTextScale(); UpdateTabHighlight(); Render(); } catch { }
    }

    // 0.17.0: chat-window font size, independent of the shared overlay text
    // scale. 12px baseline × the chat-only multiplier.
    private static float ChatFontSize() =>
        UnityEngine.Mathf.Max(8f, UnityEngine.Mathf.RoundToInt(12 * Settings.ChatTextScale));

    // Push the current chat text scale onto the log + input (+ placeholder) so a
    // live size change is reflected without a rebuild.
    private void ApplyChatTextScale()
    {
        float size = ChatFontSize();
        try { if (_log != null) _log.fontSize = size; } catch { }
        try
        {
            if (_input?.Component?.textComponent != null) _input.Component.textComponent.fontSize = size;
            if (_input?.PlaceholderText != null) _input.PlaceholderText.fontSize = size;
        }
        catch { }
        UpdateInputHeight();
    }

    private void OnInputValueChanged(string v)
    {
        // Strip control chars that the field shouldn't hold: Tab (our compose-cycle
        // key) and newlines/carriage returns. The Enter that opens/sends chat can
        // leave a stray newline in the field (the "extra blank line" friend-test
        // report) — chat is single-line + auto-wraps, so no real newline is wanted.
        // Re-setting Text re-fires this handler (now clean), which then sizes the row.
        if (!string.IsNullOrEmpty(v) && (v.IndexOf('\t') >= 0 || v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0))
        {
            _input.Text = v.Replace("\t", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
            return;
        }
        // 0.17.3: native-style channel/whisper commands — "\g " / "/global " switch the
        // send channel; "\w Name " / "\whisper Name " start a whisper. The command is
        // consumed (removed) and the channel/whisper changes live, so an All-tab user
        // can switch targets without leaving All. Re-sets Text (re-fires this clean).
        if (TryApplyChatCommand(v, out var remainder))
        {
            _input.Text = remainder ?? string.Empty;
            return;
        }
        UpdateInputHeight();
    }

    // ---- 0.17.3: in-chat channel/whisper commands (\g \global /l \whisper Name …) ----

    // Parses a leading "\cmd " / "/cmd " (or "\whisper Name "). On a recognized,
    // COMPLETED command, applies it and returns true with `remainder` = the text to
    // keep in the input (the command itself is stripped). Accepts both \ and / so
    // it's seamless for players used to the native chat.
    private bool TryApplyChatCommand(string text, out string remainder)
    {
        remainder = text;
        if (string.IsNullOrEmpty(text) || (text[0] != '\\' && text[0] != '/')) return false;
        int sp = text.IndexOf(' ');
        if (sp < 1) return false; // need a completed "<cmd> " token (cmd + trailing space)
        string cmd = text.Substring(1, sp - 1).ToLowerInvariant();
        string after = text.Substring(sp + 1);

        ChatMessageType? chan = cmd switch
        {
            "g" or "global"         => ChatMessageType.Global,
            "l" or "local"          => ChatMessageType.Local,
            "c" or "clan" or "team" => ChatMessageType.Team,
            _                       => (ChatMessageType?)null,
        };
        if (chan.HasValue)
        {
            SetComposeChannel(chan.Value);
            remainder = after;            // keep whatever message they'd started typing
            return true;
        }

        if (cmd is "w" or "whisper" or "wisp" or "wispr")
        {
            int sp2 = after.IndexOf(' ');
            if (sp2 < 1) return false;    // name still being typed (no trailing space yet)
            string name = after.Substring(0, sp2);
            string rest = after.Substring(sp2 + 1);
            if (TryStartWhisperByName(name)) { remainder = rest; return true; }
            // Unresolved: tell the user (with who IS whisperable) and consume the
            // command so it doesn't re-fire each keystroke.
            ReportWhisperResolveFailure(name);
            remainder = string.Empty;
            return true;
        }
        return false;
    }

    // Point the All-tab compose target at a channel (switching to the All tab first,
    // since that's where free channel-switching lives).
    private void SetComposeChannel(ChatMessageType type)
    {
        if (_activeTab != 0)
        {
            _activeTab = 0;
            UpdateWhisperSubRow();
            UpdateTabHighlight();
        }
        UpdateComposeRow();               // ensures targets + the dropdown exist
        int idx = _composeTargets.FindIndex(t => !t.IsWhisper && t.Channel == type);
        if (idx < 0) idx = DefaultComposeIndex(); // e.g. Clan requested while not in a clan
        _composeIndex = Mathf.Clamp(idx, 0, Mathf.Max(0, _composeTargets.Count - 1));
        if (_composeDropdown != null) _composeDropdown.SetValueWithoutNotify(_composeIndex);
        Render();
    }

    // Resolve a typed name to a known whisper target and make it the active All-tab
    // compose target (stays on All). Returns false if the name can't be resolved.
    private bool TryStartWhisperByName(string typed)
    {
        if (!ResolvePlayer(typed, out var name, out var id)) return false;
        ChatRelayService.RememberWhisperTarget(name, id);
        _closedPartners.Remove(name);
        _initiatedPartners.Add(name);
        if (_activeTab != 0)
        {
            _activeTab = 0;
            UpdateWhisperSubRow();
            UpdateTabHighlight();
        }
        UpdateComposeRow();               // rebuilds the dropdown to include the new partner
        int idx = _composeTargets.FindIndex(t => t.IsWhisper && string.Equals(t.Whisper, name, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            _composeIndex = idx;
            if (_composeDropdown != null) _composeDropdown.SetValueWithoutNotify(_composeIndex);
        }
        Render();
        return true;
    }

    // 0.17.3: resolve a typed (possibly partial) player name to a whisper target.
    // Pool = players seen in chat (have a NetworkId) + the nearby/online roster.
    // Exact case-insensitive match wins; otherwise the first unique startswith match.
    // Truly-remote silent players can't be resolved client-side (no NetworkId yet).
    private static bool ResolvePlayer(string typed, out string name, out NetworkId id)
    {
        name = null; id = default;
        if (string.IsNullOrWhiteSpace(typed)) return false;
        typed = typed.Trim();
        var pool = ChatRelayService.GetKnownPlayers();
        try { pool.AddRange(PlayerRosterService.GetOnlinePlayers()); } catch { }
        foreach (var p in pool)
            if (string.Equals(p.Name, typed, StringComparison.OrdinalIgnoreCase)) { name = p.Name; id = p.Id; return true; }
        foreach (var p in pool)
            if (!string.IsNullOrEmpty(p.Name) && p.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) { name = p.Name; id = p.Id; return true; }
        return false;
    }

    // 0.17.3: the distinct set of players the client can currently whisper (have a
    // NetworkId): seen in chat + the nearby/online roster. Used for the helpful
    // "who CAN I whisper" feedback when a typed name doesn't resolve.
    private static List<string> ResolvablePlayerNames()
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(IEnumerable<PlayerRosterService.PlayerRef> src)
        {
            foreach (var p in src) if (!string.IsNullOrEmpty(p.Name) && seen.Add(p.Name)) names.Add(p.Name);
        }
        try { Add(ChatRelayService.GetKnownPlayers()); } catch { }
        try { Add(PlayerRosterService.GetOnlinePlayers()); } catch { }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    // 0.17.3: explain a failed whisper resolution AND list who's actually whisperable,
    // so the user (and we) can see whether the target is simply outside the client's
    // known set. A whisper needs the target's NetworkId, which the client only has for
    // nearby players + anyone who's spoken — a truly-remote silent player can't be
    // resolved client-side (V Rising has no server-side whisper-by-name).
    private static void ReportWhisperResolveFailure(string typed)
    {
        var names = ResolvablePlayerNames();
        string list = names.Count == 0
            ? "(none known yet — no one nearby has spoken)"
            : (names.Count <= 15 ? string.Join(", ", names)
                                 : string.Join(", ", names.GetRange(0, 15)) + $", +{names.Count - 15} more");
        ChatRelayService.CaptureLocalEcho(ChatRelayService.Channel.System,
            $"No whisper match for \"{typed}\". Players I can whisper now: {list}. (The client can only whisper players who are nearby or have spoken in chat.)");
        Utils.LogUtils.LogInfo($"[Whisper] resolve failed for '{typed}'. Whisperable ({names.Count}): {string.Join(", ", names)}");
    }

    // 0.17.0: size the input row to fit the wrapped text — one line at rest,
    // growing up to InputMaxHeight, then shrinking back when the message is sent.
    private void UpdateInputHeight()
    {
        try
        {
            var txt = _input?.Component?.textComponent;
            if (txt == null) return;
            // Empty field → snap straight back to one line. Reading preferredHeight
            // alone didn't shrink reliably after a send (TMP keeps the prior multi-
            // line geometry until the mesh is rebuilt), so force the rebuild first.
            string cur = _input.Text;
            float pref;
            if (string.IsNullOrEmpty(cur)) { pref = 0f; }
            else { txt.ForceMeshUpdate(); pref = txt.preferredHeight; }
            int h = (int)UnityEngine.Mathf.Clamp(pref + 6f, InputBaseHeight, InputMaxHeight);
            UIFactory.SetLayoutElement(_input.GameObject, minHeight: h, preferredHeight: h);
            if (_inputRow != null)
                UIFactory.SetLayoutElement(_inputRow, minHeight: h + 2, preferredHeight: h + 2);
        }
        catch { }
    }

    // ---- input / send (increment 2) ----

    // 0.17 (2c): focus the input — the divert target when the takeover intercepts
    // the game's chat-open key (Enter). ActivateInputField fires onSelect, which
    // sets ChatInputActive so gameplay input is suppressed while you type.
    internal void FocusInput()
    {
        try
        {
            // 0.17.0: don't re-activate an already-focused field — re-activating
            // every frame is what pinned ChatInputActive on and trapped the player.
            if (_input?.Component != null && !_input.Component.isFocused)
                _input.Component.ActivateInputField();
        }
        catch { }
    }

    // 0.17.0 escape hatch / canonical defocus. Deactivates the field, clears the
    // EventSystem selection, and clears ChatInputActive. The EventSystem clear is
    // load-bearing: V Rising suppresses gameplay input while a UI text field is the
    // EventSystem's selected object, and DeactivateInputField alone does NOT clear
    // that selection — so without this the game keeps gameplay frozen even after we
    // "defocus" (the post-chat freeze).
    internal void ReleaseInput()
    {
        try { _input?.Component?.DeactivateInputField(); } catch { }
        try
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && _input?.Component != null
                && es.currentSelectedGameObject == _input.Component.gameObject)
                es.SetSelectedGameObject(null);
        }
        catch { }
        Patches.InputSuppression.ChatInputActive = false;
    }

    // 0.17.0: authoritative focus state, polled each frame to drive ChatInputActive.
    // onSelect/onDeselect aren't reliable — DeactivateInputField doesn't always raise
    // onDeselect — so the event-set flag could stick true and leave gameplay input
    // suppressed after you finished chatting (the post-chat freeze). Reading the
    // field's real focus each frame means suppression can never stick on.
    internal bool IsInputFocused()
    {
        try { return _input?.Component != null && _input.Component.isFocused; }
        catch { return false; }
    }

    // 0.17.3: is the mouse cursor currently over the chat window? Used to suppress the
    // game's primary ATTACK while you click the window (tabs / input), so the click
    // doesn't leak into the world as an attack and get the character stuck repeating
    // it. Targeted rect test (not the whole EventSystem), so it's only ever true when
    // the cursor is literally over this window — never during combat.
    internal bool IsPointerOverWindow()
    {
        try
        {
            if (!Enabled) return false;
            var rt = uiRoot != null ? uiRoot.GetComponent<RectTransform>() : null;
            if (rt == null) return false;
            // ScreenSpaceOverlay canvas → null camera.
            return UnityEngine.RectTransformUtility.RectangleContainsScreenPoint(rt, UnityEngine.Input.mousePosition, null);
        }
        catch { return false; }
    }

    private void OnChatSelect(string _)   => Patches.InputSuppression.ChatInputActive = true;
    private void OnChatDeselect(string _) => Patches.InputSuppression.ChatInputActive = false;
    private void OnChatSubmit(string _)   => SubmitText(keepFocus: false); // Enter

    // Sends the current input on the active tab's channel, then clears. keepFocus
    // (Send button) re-activates the field so you can keep chatting; Enter
    // releases focus so movement resumes (game-like).
    private void SubmitText(bool keepFocus)
    {
        if (_input == null) return;
        try
        {
            var msg = _input.Text?.Trim();
            if (msg != null && msg.Length > MaxChatChars) msg = msg.Substring(0, MaxChatChars); // backstop
            if (!string.IsNullOrEmpty(msg))
            {
                bool onWhisperPartner = WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex && _activeWhisperPartner != null;
                if (onWhisperPartner)
                {
                    // Whisper reply: send to the partner's captured NetworkId.
                    if (ChatRelayService.TryGetWhisperTarget(_activeWhisperPartner, out var target))
                    {
                        MessageService.SendWhisper(msg, target);
                        ChatRelayService.CaptureLocalEcho(ChatRelayService.Channel.Whisper, msg, _activeWhisperPartner);
                    }
                    // else: no known target for this partner yet — nothing to send to.
                }
                else if (_activeTab == 0)
                {
                    // All tab: send to the chosen compose target (Global/Local/Clan
                    // or a whisper partner) — set via the "Send to:" dropdown or Tab.
                    EnsureComposeTargets();
                    if (_composeTargets.Count > 0 && _composeIndex >= 0 && _composeIndex < _composeTargets.Count
                        && _composeTargets[_composeIndex].IsWhisper)
                    {
                        var wp = _composeTargets[_composeIndex].Whisper;
                        if (ChatRelayService.TryGetWhisperTarget(wp, out var nid))
                        {
                            MessageService.SendWhisper(msg, nid);
                            ChatRelayService.CaptureLocalEcho(ChatRelayService.Channel.Whisper, msg, wp);
                        }
                    }
                    else
                    {
                        var type = (_composeTargets.Count > 0 && _composeIndex >= 0 && _composeIndex < _composeTargets.Count
                                    && _composeTargets[_composeIndex].Channel.HasValue)
                            ? _composeTargets[_composeIndex].Channel.Value
                            : ActiveSendChannel();
                        MessageService.SendChat(msg, type);
                        ChatRelayService.CaptureLocalEcho(EchoChannelFor(type), msg);
                    }
                }
                else
                {
                    var type = ActiveSendChannel();
                    MessageService.SendChat(msg, type);
                    // The server broadcasts our message to others but doesn't echo it
                    // back to us, so add a local echo so we see our own message here.
                    ChatRelayService.CaptureLocalEcho(EchoChannelFor(type), msg);
                }
            }
        }
        catch (System.Exception ex)
        {
            Utils.LogUtils.LogError($"Chat send failed: {ex}");
        }
        finally
        {
            _input.Text = string.Empty;
            UpdateInputHeight(); // contract the (possibly grown) input back to one line
            if (keepFocus)
            {
                _input.Component.ActivateInputField();
            }
            else
            {
                ReleaseInput(); // deactivate + clear EventSystem selection + flag
            }
        }
    }

    // The active tab decides the outgoing channel. All / System / Whispers fall
    // back to Local (System isn't player-sendable; whisper send needs a target,
    // which comes with the per-person whisper tabs in a later increment).
    private ChatMessageType ActiveSendChannel()
    {
        var f = TabDefs[_activeTab].Filter;
        // All tab: send on the user's chosen default (Global or Local).
        if (f == null)
            return Settings.ChatAllTabDefaultGlobal ? ChatMessageType.Global : ChatMessageType.Local;
        return f switch
        {
            ChatRelayService.Channel.Global => ChatMessageType.Global,
            ChatRelayService.Channel.Clan   => ChatMessageType.Team,
            ChatRelayService.Channel.Local  => ChatMessageType.Local,
            _                               => ChatMessageType.Local, // System not player-sendable
        };
    }

    // Map an outgoing ChatMessageType to the buffer channel for the local echo.
    private static ChatRelayService.Channel EchoChannelFor(ChatMessageType t) => t switch
    {
        ChatMessageType.Global => ChatRelayService.Channel.Global,
        ChatMessageType.Team   => ChatRelayService.Channel.Clan,
        _                      => ChatRelayService.Channel.Local,
    };

    // 0.17.3: select a tab (shared by tab-button clicks and the tab hotkeys).
    private void SwitchToTab(int idx)
    {
        if (idx < 0 || idx >= TabDefs.Length) return;
        _activeTab = idx;
        _unread[idx] = 0;                                              // viewing clears its badge
        if (idx == 0) for (int k = 0; k < _unread.Length; k++) _unread[k] = 0; // All sees everything
        UpdateWhisperSubRow();
        UpdateComposeRow();
        UpdateTabHighlight();
        Render();
    }

    // 0.17.3: <Modifier>+1..6 switches tabs while the chat window is open and the
    // input is NOT focused (so the keys still type normally while composing). The
    // key isn't consumed — the game still sees it — so pick a non-conflicting
    // modifier (default Shift). Registered on CoreUpdateBehavior.
    private void TickTabHotkeys()
    {
        try
        {
            if (!Enabled || !Settings.ChatTabHotkeysEnabled) return;
            if (IsInputFocused()) return;                 // don't hijack keys while typing
            if (!ModifierHeld(Settings.ChatTabHotkeyModifier)) return;
            int count = System.Math.Min(TabDefs.Length, 9);
            for (int i = 0; i < count; i++)
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1 + i)) { SwitchToTab(i); break; }
            }
        }
        catch { }
    }

    private static bool ModifierHeld(string mod)
    {
        switch ((mod ?? "Alt").Trim().ToLowerInvariant())
        {
            case "none":                 return true;
            case "ctrl": case "control": return UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
            case "shift":                return UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            default:                     return UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt); // Alt (default)
        }
    }

    private void UpdateTabHighlight()
    {
        var activeColor = Theme.SliderFill;
        var inactiveColor = activeColor * 0.5f; inactiveColor.a = activeColor.a;
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            var btn = _tabButtons[i];
            if (btn?.Component == null) continue;
            // Unread badge in the label, e.g. "Clan (3)". All tab never badges.
            if (btn.ButtonText != null)
            {
                btn.ButtonText.text = _unread[i] > 0 ? $"{TabDefs[i].Label} ({_unread[i]})" : TabDefs[i].Label;
                // 0.17.0: tint the tab label in its channel's color (All stays neutral).
                btn.ButtonText.color = (Settings.ChatColorTabs && TabDefs[i].Filter.HasValue)
                    ? ChannelColor(TabDefs[i].Filter) : Theme.DefaultText;
            }
            var baseC = (i == _activeTab) ? activeColor : inactiveColor;
            var cb = btn.Component.colors;
            cb.normalColor      = baseC;
            cb.highlightedColor = baseC * 1.2f;
            cb.selectedColor    = baseC * 1.1f;
            cb.pressedColor     = baseC * 0.7f;
            btn.Component.colors = cb;
        }
    }

    // Show/hide the whisper sub-tab row and rebuild it when the Whispers tab is active.
    private void UpdateWhisperSubRow()
    {
        if (_whisperSubRow == null) return;
        bool show = WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex;
        _whisperSubRow.SetActive(show);
        if (_whisperEntryRow != null) _whisperEntryRow.SetActive(show); // 0.17.3 type-a-name row
        if (show) RebuildWhisperSubTabs();
    }

    private void OnWhisperNameSubmit(string _) => StartTypedWhisper();

    // 0.17.3: start a whisper with the name typed into the Whispers-tab entry box.
    // Resolves against players seen in chat + the nearby roster; opens their sub-tab.
    private void StartTypedWhisper()
    {
        try
        {
            var typed = _whisperNameInput?.Text?.Trim();
            if (string.IsNullOrEmpty(typed)) return;
            if (!ResolvePlayer(typed, out var name, out var id))
            {
                ReportWhisperResolveFailure(typed);
                return;
            }
            ChatRelayService.RememberWhisperTarget(name, id);
            _closedPartners.Remove(name);
            _initiatedPartners.Add(name);
            _activeWhisperPartner = name;                 // open their conversation
            if (_whisperNameInput != null) _whisperNameInput.Text = string.Empty;
            RebuildWhisperSubTabs();
            Render();
        }
        catch (System.Exception ex) { Utils.LogUtils.LogError($"StartTypedWhisper: {ex}"); }
    }

    // Rebuild the whisper sub-tab buttons: "All" + one per distinct partner.
    private void RebuildWhisperSubTabs()
    {
        if (_whisperSubRow == null) return;
        // Destroy ALL existing children (sub-tab buttons + the picker dropdown).
        var t = _whisperSubRow.transform;
        for (int i = t.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        _whisperSubButtons.Clear();
        _whisperSubPartners.Clear();
        _whisperPicker = null;

        var partners = WhisperPartners();
        if (_activeWhisperPartner != null && !partners.Contains(_activeWhisperPartner))
            _activeWhisperPartner = null; // partner gone — fall back to All

        AddWhisperSubButton(null, "All");
        foreach (var p in partners) { AddWhisperSubButton(p, p); AddWhisperCloseButton(p); }
        AddWhisperPicker(); // "+ Whisper…" dropdown of players seen in chat
        HighlightWhisperSubTabs();
    }

    // Small "x" beside each partner sub-tab to close that conversation. Lets a long
    // session's whisper tabs be cleared. Closing suppresses the partner (even if old
    // whisper lines remain) until they whisper again.
    private void AddWhisperCloseButton(string partner)
    {
        var btn = UIFactory.CreateButton(_whisperSubRow, $"WhisperClose_{partner}", "x");
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 18, preferredWidth: 18, flexibleWidth: 0, minHeight: 20, preferredHeight: 20, flexibleHeight: 0);
        btn.OnClick = () => CloseWhisperPartner(partner);
        TooltipHover.Attach(btn.GameObject, $"Close the whisper tab with {partner}. It reappears if they whisper you again.");
    }

    private void CloseWhisperPartner(string partner)
    {
        if (string.IsNullOrEmpty(partner)) return;
        _initiatedPartners.Remove(partner);
        _closedPartners.Add(partner);
        if (_activeWhisperPartner == partner) _activeWhisperPartner = null;
        RebuildWhisperSubTabs();
        Render();
    }

    // Dropdown listing players we've SEEN in chat (any channel) to start a new
    // whisper. (The client holds a User entity only for the local player, so a
    // roster query can't enumerate others — names+targets come from chat traffic
    // via ChatRelayService.) Selecting a name opens that conversation immediately.
    private void AddWhisperPicker()
    {
        _pickerRoster = ChatRelayService.GetKnownPlayers();
        var options = new List<string> { "+ Whisper…" };
        foreach (var p in _pickerRoster) options.Add(p.Name);
        var ddObj = UIFactory.CreateDropdown(_whisperSubRow, "WhisperPicker", out _whisperPicker,
            "+ Whisper…", 12, OnWhisperPickerChanged, options.ToArray());
        UIFactory.SetLayoutElement(ddObj, minWidth: 104, preferredWidth: 140, flexibleWidth: 0,
            minHeight: 20, preferredHeight: 20, flexibleHeight: 0);
        ApplyDropdownNoWrap(_whisperPicker); // 0.17.3: long names stay one line
        // Without this the dropdown never closes — TMP's own blocker doesn't fire in
        // our canvas, so the registry's per-frame outside-click check dismisses it.
        BloodCraftHub.UI.Forms.FormDropdownRegistry.Register(_whisperPicker);
    }

    private void OnWhisperPickerChanged(int index)
    {
        try
        {
            if (index <= 0 || _pickerRoster == null || index - 1 >= _pickerRoster.Count) return;
            var pr = _pickerRoster[index - 1];
            ChatRelayService.RememberWhisperTarget(pr.Name, pr.Id); // reply works before they message us
            _closedPartners.Remove(pr.Name); // explicit re-initiation un-closes
            _initiatedPartners.Add(pr.Name);
            _activeWhisperPartner = pr.Name;
            RebuildWhisperSubTabs(); // adds their sub-tab + a fresh picker
            Render();
        }
        catch (System.Exception ex) { Utils.LogUtils.LogError($"WhisperPicker: {ex}"); }
    }

    private void AddWhisperSubButton(string partner, string label)
    {
        var btn = UIFactory.CreateButton(_whisperSubRow, $"WhisperSub_{label}", label);
        UIFactory.SetLayoutElement(btn.GameObject,
            minWidth: 40, preferredWidth: 90, flexibleWidth: 1, minHeight: 20, preferredHeight: 20, flexibleHeight: 0);
        // 0.17.3: long partner names used to word-wrap inside the sub-tab cell and
        // look odd — keep them on one line, truncating with an ellipsis instead.
        if (btn.ButtonText != null)
        {
            btn.ButtonText.enableWordWrapping = false;
            btn.ButtonText.overflowMode = TextOverflowModes.Ellipsis;
        }
        btn.OnClick = () => { _activeWhisperPartner = partner; HighlightWhisperSubTabs(); Render(); };
        _whisperSubButtons.Add(btn);
        _whisperSubPartners.Add(partner);
    }

    // Distinct whisper conversation partners: everyone with a whisper line (the
    // Partner field), plus players picked from the dropdown to initiate — MINUS any
    // the user has explicitly closed (so merely-chatted players never appear here;
    // only actual whispers, received or initiated, create a tab).
    private List<string> WhisperPartners()
    {
        var list = new List<string>();
        var buf = ChatRelayService.Buffer;
        for (int i = 0; i < buf.Count; i++)
        {
            var ln = buf[i];
            if (ln.Channel != ChatRelayService.Channel.Whisper || string.IsNullOrEmpty(ln.Partner)) continue;
            if (_closedPartners.Contains(ln.Partner)) continue;
            if (!list.Contains(ln.Partner)) list.Add(ln.Partner);
        }
        foreach (var p in _initiatedPartners)
            if (!_closedPartners.Contains(p) && !list.Contains(p)) list.Add(p);
        return list;
    }

    private void HighlightWhisperSubTabs()
    {
        var activeColor = Theme.SliderFill;
        var inactiveColor = activeColor * 0.5f; inactiveColor.a = activeColor.a;
        for (int i = 0; i < _whisperSubButtons.Count; i++)
        {
            var btn = _whisperSubButtons[i];
            if (btn?.Component == null) continue;
            bool isActive = i < _whisperSubPartners.Count && _whisperSubPartners[i] == _activeWhisperPartner;
            var baseC = isActive ? activeColor : inactiveColor;
            var cb = btn.Component.colors;
            cb.normalColor = baseC; cb.highlightedColor = baseC * 1.2f;
            cb.selectedColor = baseC * 1.1f; cb.pressedColor = baseC * 0.7f;
            btn.Component.colors = cb;
            // 0.17.0: tint whisper sub-tab labels in the Whisper color when colored
            // tabs are on (these are all whisper conversations).
            if (btn.ButtonText != null)
                btn.ButtonText.color = Settings.ChatColorTabs
                    ? ChannelColor(ChatRelayService.Channel.Whisper) : Theme.DefaultText;
        }
    }

    // ---- All-tab compose target (Send-to dropdown + Tab cycle) ----

    // Show/hide the compact compose dropdown (All tab only, beside the input) and
    // rebuild it when the available targets change (clan join/leave, new partner).
    private void UpdateComposeRow()
    {
        bool show = _activeTab == 0;
        if (!show)
        {
            if (_composeDropdownObj != null) _composeDropdownObj.SetActive(false);
            return;
        }
        bool changed = EnsureComposeTargets();
        if (changed || _composeDropdown == null || _composeDropdownObj == null) RebuildComposeRow();
        else
        {
            _composeDropdownObj.SetActive(true);
            _composeDropdown.SetValueWithoutNotify(Mathf.Clamp(_composeIndex, 0, _composeTargets.Count - 1));
        }
    }

    // Rebuild _composeTargets = Global, Local, [Clan if in a clan], then each active
    // whisper partner. Returns true if the set changed (so the dropdown is rebuilt).
    // Preserves the current selection by label where possible; else falls to default.
    private bool EnsureComposeTargets()
    {
        var partners = WhisperPartners();
        string sig = (IsInClan() ? "C|" : "-|") + string.Join("|", partners);
        if (sig == _composeSignature && _composeTargets.Count > 0) return false;
        _composeSignature = sig;

        string prevLabel = (_composeIndex >= 0 && _composeIndex < _composeTargets.Count) ? _composeTargets[_composeIndex].Label : null;
        _composeTargets.Clear();
        _composeTargets.Add(SendTarget.Chan(ChatMessageType.Global, "Global"));
        _composeTargets.Add(SendTarget.Chan(ChatMessageType.Local, "Local"));
        if (IsInClan()) _composeTargets.Add(SendTarget.Chan(ChatMessageType.Team, "Clan"));
        foreach (var p in partners) _composeTargets.Add(SendTarget.Whis(p));

        int idx = (prevLabel != null) ? _composeTargets.FindIndex(t => t.Label == prevLabel) : -1;
        if (idx < 0) idx = DefaultComposeIndex();
        _composeIndex = Mathf.Clamp(idx, 0, _composeTargets.Count - 1);
        return true;
    }

    // Default All-tab send channel: Global or Local per the user's setting.
    private static int DefaultComposeIndex() => Settings.ChatAllTabDefaultGlobal ? 0 : 1;

    // Build the compact compose dropdown as the FIRST child of the input row, so it
    // sits as a small selectable button left of the text input on the All tab.
    private void RebuildComposeRow()
    {
        if (_inputRow == null) return;
        if (_composeDropdownObj != null) UnityEngine.Object.Destroy(_composeDropdownObj);
        _composeDropdownObj = null;
        _composeDropdown = null;
        EnsureComposeTargets();
        if (_composeTargets.Count == 0) return;

        var options = new string[_composeTargets.Count];
        for (int i = 0; i < _composeTargets.Count; i++) options[i] = _composeTargets[i].Label;
        int sel = Mathf.Clamp(_composeIndex, 0, options.Length - 1);
        var ddObj = UIFactory.CreateDropdown(_inputRow, "ComposeDropdown", out _composeDropdown,
            options[sel], 11, OnComposeChanged, options);
        // 0.17.3: wider so a whisper target "@LongName" fits, and never word-wrap the
        // caption or list items (long names used to wrap inside the cell and look odd).
        UIFactory.SetLayoutElement(ddObj,
            minWidth: 80, preferredWidth: 116, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        ApplyDropdownNoWrap(_composeDropdown);
        ddObj.transform.SetSiblingIndex(0); // leftmost — before the input field + Send
        _composeDropdownObj = ddObj;
        _composeDropdown.SetValueWithoutNotify(sel);
        // Outside-click close (TMP's own blocker doesn't fire in our canvas).
        BloodCraftHub.UI.Forms.FormDropdownRegistry.Register(_composeDropdown);
        TooltipHover.Attach(ddObj, "Channel this message sends to. Click to pick, or press Tab while typing to cycle (Global / Local / Clan / active whispers).");
    }

    private void OnComposeChanged(int i) { if (i >= 0 && i < _composeTargets.Count) _composeIndex = i; }

    // 0.17.3: keep a dropdown's caption + list items on ONE line (ellipsis), so a long
    // whisper target / player name doesn't word-wrap inside the cell and look odd.
    private static void ApplyDropdownNoWrap(TMP_Dropdown dd)
    {
        try
        {
            if (dd == null) return;
            if (dd.captionText != null) { dd.captionText.enableWordWrapping = false; dd.captionText.overflowMode = TextOverflowModes.Ellipsis; }
            if (dd.itemText != null)    { dd.itemText.enableWordWrapping    = false; dd.itemText.overflowMode    = TextOverflowModes.Ellipsis; }
        }
        catch { }
    }

    // Tab cycles the compose target (Global → Local → [Clan] → whisper partners → …).
    // Does NOT re-resolve the target list here — that's done when the row is built —
    // so a mid-cycle rebuild can't reset the index and eat a press.
    private void CycleCompose(int dir)
    {
        int n = _composeTargets.Count;
        if (n == 0) { EnsureComposeTargets(); n = _composeTargets.Count; if (n == 0) return; }
        _composeIndex = ((_composeIndex + dir) % n + n) % n;
        if (_composeDropdown != null) _composeDropdown.SetValueWithoutNotify(_composeIndex);
    }

    // Per-frame: on the All tab while typing, Tab cycles the send target. Uses a
    // short focus grace so the one-frame focus blip around a Tab press doesn't make
    // every other press a no-op.
    private void TickComposeKeys()
    {
        try
        {
            if (!Enabled || _activeTab != 0) { _inputFocusGrace = 0; return; }
            if (IsInputFocused()) _inputFocusGrace = 6;
            else if (_inputFocusGrace > 0) _inputFocusGrace--;
            if (_inputFocusGrace > 0 && UnityEngine.Input.GetKeyDown(KeyCode.Tab)) CycleCompose(+1);
        }
        catch { }
    }

    private static bool IsInClan()
    {
        try
        {
            var u = MessageService.LocalUser;
            if (u == Entity.Null) return false;
            // User.ClanEntity is a ProjectM.NetworkedEntity; its ._Entity resolves to
            // Entity.Null when the player isn't in a clan. (If a clan member's clan
            // isn't resolved client-side they just won't see Clan in the All-tab
            // cycle — the dedicated Clan tab still sends clan messages either way.)
            return u.Read<ProjectM.Network.User>().ClanEntity._Entity != Entity.Null;
        }
        catch { return false; }
    }

    private void Render()
    {
        if (_log == null) return;
        bool showTime = Settings.ChatShowTimestamps;
        bool showTag  = Settings.ChatShowChannelTags;
        bool newestAtBottom = Settings.ChatNewestAtBottom;
        bool tabular  = Settings.ChatTabularLayout;
        var filter = TabDefs[_activeTab].Filter;
        // On the Whispers tab, an active partner sub-tab narrows to that conversation.
        bool whisperPartnerFilter = WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex && _activeWhisperPartner != null;

        // Build the matching lines in chronological (oldest→newest) order first,
        // then emit in the user's chosen direction. Cheap — chat is low-volume.
        var lines = new List<string>();
        var line = new StringBuilder(160);
        var buf = ChatRelayService.Buffer;
        for (int i = 0; i < buf.Count; i++)
        {
            var ln = buf[i];
            if (filter.HasValue && ln.Channel != filter.Value) continue;
            // 0.17.3: All-tab per-channel filter (settings). Excluded channels are
            // hidden from the All aggregate; each channel's own tab is unaffected.
            if (!filter.HasValue && !AllTabIncludes(ln.Channel)) continue;
            if (whisperPartnerFilter && ln.Partner != _activeWhisperPartner) continue;
            line.Clear();
            if (tabular)
            {
                AppendTabularLine(line, ln, showTime, showTag);
            }
            else
            {
                if (showTime) line.Append("<color=#808080>").Append(ln.Received.ToString("HH:mm")).Append("</color> ");
                if (showTag)  line.Append(ChannelTag(ln.Channel)).Append(' ');
                // Game-resolved sender name (empty for system messages). The native
                // userName may already carry color tags — render as-is.
                if (!string.IsNullOrEmpty(ln.Sender))
                    line.Append(ln.Sender).Append(": ");
                line.Append(ln.Text);
            }
            lines.Add(line.ToString());
        }

        var sb = new StringBuilder(2048);
        if (newestAtBottom)
            for (int i = 0; i < lines.Count; i++) sb.Append(lines[i]).Append('\n');
        else
            for (int i = lines.Count - 1; i >= 0; i--) sb.Append(lines[i]).Append('\n');
        _log.text = sb.ToString();

        // 0.17.0: keep the newest message in view as lines arrive (bottom or top
        // per the setting). Off lets the user scroll back through history freely.
        if (Settings.ChatAutoScroll) ScrollToNewest(newestAtBottom);
    }

    // 0.17.3: build one chat line in aligned columns using TMP <pos>/<indent>:
    //   [time]      [tag] Sender:      message (wrapped lines hang-indent under the
    //   message column). Columns are % of the log width so they scale with the window.
    // If a sender name is long enough to pass the message column, <pos> can't move
    // backwards so the text just flows inline — a graceful fallback, not a break.
    // <indent> is closed at the end so the next line (joined by \n in the same TMP
    // text block) doesn't inherit it.
    private static void AppendTabularLine(StringBuilder line, ChatRelayService.ChatLine ln, bool showTime, bool showTag)
    {
        int metaPct = showTime ? 12 : 0;
        int msgPct  = showTime ? 34 : 24;
        if (showTime)
            line.Append("<color=#808080>").Append(ln.Received.ToString("HH:mm")).Append("</color>");
        if (metaPct > 0) line.Append("<pos=").Append(metaPct).Append("%>");
        if (showTag)
        {
            var tag = ChannelTag(ln.Channel);
            if (!string.IsNullOrEmpty(tag)) line.Append(tag).Append(' ');
        }
        if (!string.IsNullOrEmpty(ln.Sender)) line.Append(ln.Sender).Append(':');
        line.Append("<indent=").Append(msgPct).Append("%><pos=").Append(msgPct).Append("%>")
            .Append(ln.Text).Append("</indent>");
    }

    // Snap the scroll view to the newest message's edge. ScrollRect convention:
    // verticalNormalizedPosition 1 = top, 0 = bottom (independent of pivot). A
    // forced layout rebuild first is load-bearing — the ContentSizeFitter hasn't
    // recomputed the new text's height yet when we set the position, so without it
    // the scroll lands on the PREVIOUS content size and the newest line is clipped.
    private void ScrollToNewest(bool newestAtBottom)
    {
        if (_scrollRect == null) return;
        try
        {
            UnityEngine.Canvas.ForceUpdateCanvases();
            if (_scrollContentRect != null)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContentRect);
            _scrollRect.verticalNormalizedPosition = newestAtBottom ? 0f : 1f;
        }
        catch { /* scroll is best-effort; never throw into the inbound pump */ }
    }

    // 0.17.0: single source of truth for per-channel color, shared by the inline
    // label tags AND the colored tab labels. Global is user-configurable; the rest
    // are fixed, distinct defaults (Local blue / Clan green / System gold / Whisper pink).
    internal static string ChannelColorHex(ChatRelayService.Channel? ch) => ch switch
    {
        ChatRelayService.Channel.Global  => Settings.ChatGlobalColorHex,
        ChatRelayService.Channel.Local   => "#B0E0FF",
        ChatRelayService.Channel.Clan    => "#90EE90",
        ChatRelayService.Channel.System  => "#FFD700",
        ChatRelayService.Channel.Whisper => "#FF9CEF",
        _                                => "#FFFFFF",
    };

    private static Color ChannelColor(ChatRelayService.Channel? ch) =>
        UnityEngine.ColorUtility.TryParseHtmlString(ChannelColorHex(ch), out var c) ? c : Theme.DefaultText;

    private static string ChannelTag(ChatRelayService.Channel ch)
    {
        string label = Settings.ChatChannelLabelsSpelledOut ? SpelledLabel(ch) : ShortLabel(ch);
        return string.IsNullOrEmpty(label) ? string.Empty : $"<color={ChannelColorHex(ch)}>{label}</color>";
    }

    private static string ShortLabel(ChatRelayService.Channel ch) => ch switch
    {
        ChatRelayService.Channel.Global  => "[G]",
        ChatRelayService.Channel.Local   => "[L]",
        ChatRelayService.Channel.Clan    => "[Clan]",
        ChatRelayService.Channel.System  => "[Sys]",
        ChatRelayService.Channel.Whisper => "[W]",
        _                                => string.Empty,
    };

    private static string SpelledLabel(ChatRelayService.Channel ch) => ch switch
    {
        ChatRelayService.Channel.Global  => "[Global]",
        ChatRelayService.Channel.Local   => "[Local]",
        ChatRelayService.Channel.Clan    => "[Clan]",
        ChatRelayService.Channel.System  => "[System]",
        ChatRelayService.Channel.Whisper => "[Whisper]",
        _                                => string.Empty,
    };

    internal override void Reset()
    {
        if (_subscribed) { ChatRelayService.LineCaptured -= OnLineCaptured; _subscribed = false; }
        Patches.InputSuppression.ChatInputActive = false;
        _tabButtons.Clear();
        _whisperSubButtons.Clear();
        _whisperSubPartners.Clear();
        _initiatedPartners.Clear();
        _closedPartners.Clear();
        _whisperPicker = null;
        _whisperSubRow = null;
        _input = null;
        _inputRow = null;
        _log = null;
        _scrollRect = null;
        _scrollContentRect = null;
        if (_composeKeyTicker != null)
        {
            BloodCraftHub.Behaviors.CoreUpdateBehavior.Actions.Remove(_composeKeyTicker);
            _composeKeyTicker = null;
        }
        _composeDropdownObj = null;
        _composeDropdown = null;
        _composeTargets.Clear();
        _composeIndex = 0;
        _composeSignature = " ";
    }
}
