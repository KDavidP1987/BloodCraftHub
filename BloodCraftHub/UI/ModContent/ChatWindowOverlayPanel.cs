using System.Collections.Generic;
using System.Text;
using BloodCraftHub.Config;
using BloodCraftHub.Services;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.CustomLib.Util;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Models;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using ProjectM.Network;
using TMPro;
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
    // 0.17.0: the scroll view + its content rect, kept so we can auto-scroll the
    // log to the newest message (bottom or top, per ChatNewestAtBottom).
    private UnityEngine.UI.ScrollRect _scrollRect;
    private RectTransform _scrollContentRect;

    // 0.17.0 whisper sub-tabs. When the Whispers top-tab is active, a second row
    // appears: "All" (every whisper) + one sub-tab per conversation partner.
    private GameObject _whisperSubRow;
    private readonly List<ButtonRef> _whisperSubButtons = new();
    private readonly List<string> _whisperSubPartners = new(); // parallel to _whisperSubButtons; null = All
    private string _activeWhisperPartner; // null = All Whispers
    // Players chosen from the picker but not yet messaged — kept so their sub-tab
    // shows immediately (before any whisper line exists for them).
    private readonly HashSet<string> _initiatedPartners = new();
    private TMPro.TMP_Dropdown _whisperPicker;
    private List<PlayerRosterService.PlayerRef> _pickerRoster;
    private static readonly int WhispersTabIndex = System.Array.FindIndex(TabDefs, t => t.Filter == ChatRelayService.Channel.Whisper);
    // 0.17.0: per-tab unread counts. A message for a channel you're NOT currently
    // viewing bumps that tab's count; selecting the tab resets it. The All tab
    // (index 0) shows everything, so it never carries a badge.
    private readonly int[] _unread = new int[TabDefs.Length];
    // Max chars per chat message — under the FixedString512Bytes wire cap.
    private const int MaxChatChars = 500;

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
            btn.OnClick = () =>
            {
                _activeTab = idx;
                _unread[idx] = 0;                       // viewing this tab clears its badge
                if (idx == 0) for (int k = 0; k < _unread.Length; k++) _unread[k] = 0; // All sees everything
                UpdateWhisperSubRow();                  // show/hide + rebuild the whisper sub-tabs
                UpdateTabHighlight();
                Render();
            };
            _tabButtons.Add(btn);
        }

        // 0.17.0: whisper sub-tab row — hidden unless the Whispers tab is active.
        _whisperSubRow = UIFactory.CreateHorizontalGroup(ContentRoot, "ChatWhisperSubTabs",
            false, false, true, true, 2, new Vector4(2, 0, 2, 0), bgColor: new Color(0f, 0f, 0f, 0f));
        UIFactory.SetLayoutElement(_whisperSubRow, minHeight: 22, preferredHeight: 22, flexibleWidth: 1);
        _whisperSubRow.SetActive(false);

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
        UIFactory.SetLayoutElement(inputRow, minHeight: 26, preferredHeight: 26, flexibleWidth: 1);
        _input = UIFactory.CreateInputField(inputRow, "ChatInput", "Type a message…");
        UIFactory.SetLayoutElement(_input.GameObject,
            minWidth: 160, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        // Respect the game's chat length: ChatMessageEvent.MessageText is a
        // FixedString512Bytes, so cap input well under 512 bytes (500 chars leaves
        // headroom for multi-byte characters). SubmitText also truncates as a backstop.
        _input.Component.characterLimit = MaxChatChars;
        _input.Component.onSubmit.AddListener(OnChatSubmit);
        _input.Component.onSelect.AddListener(OnChatSelect);
        _input.Component.onDeselect.AddListener(OnChatDeselect);
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
        ApplyChatTextScale(); // size the input field to match the chat scale
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
            // A whisper from a new partner adds a sub-tab while the Whispers tab is open.
            if (line.Channel == ChatRelayService.Channel.Whisper
                && WhispersTabIndex >= 0 && _activeTab == WhispersTabIndex)
                RebuildWhisperSubTabs();
            Render();
        }
        catch { /* never let chat rendering throw into the inbound pump */ }
    }

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
                btn.ButtonText.text = _unread[i] > 0 ? $"{TabDefs[i].Label} ({_unread[i]})" : TabDefs[i].Label;
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
        if (show) RebuildWhisperSubTabs();
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
        foreach (var p in partners) AddWhisperSubButton(p, p);
        AddWhisperPicker(); // "+ Whisper…" dropdown of online players
        HighlightWhisperSubTabs();
    }

    // Dropdown listing online players (queried from the client's User entities) to
    // start a new whisper. Selecting a name opens that conversation immediately.
    private void AddWhisperPicker()
    {
        _pickerRoster = PlayerRosterService.GetOnlinePlayers();
        var options = new List<string> { "+ Whisper…" };
        foreach (var p in _pickerRoster) options.Add(p.Name);
        var ddObj = UIFactory.CreateDropdown(_whisperSubRow, "WhisperPicker", out _whisperPicker,
            "+ Whisper…", 12, OnWhisperPickerChanged, options.ToArray());
        UIFactory.SetLayoutElement(ddObj, minWidth: 104, preferredWidth: 140, flexibleWidth: 0,
            minHeight: 20, preferredHeight: 20, flexibleHeight: 0);
    }

    private void OnWhisperPickerChanged(int index)
    {
        try
        {
            if (index <= 0 || _pickerRoster == null || index - 1 >= _pickerRoster.Count) return;
            var pr = _pickerRoster[index - 1];
            ChatRelayService.RememberWhisperTarget(pr.Name, pr.Id); // reply works before they message us
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
            minWidth: 40, preferredWidth: 70, flexibleWidth: 1, minHeight: 20, preferredHeight: 20, flexibleHeight: 0);
        btn.OnClick = () => { _activeWhisperPartner = partner; HighlightWhisperSubTabs(); Render(); };
        _whisperSubButtons.Add(btn);
        _whisperSubPartners.Add(partner);
    }

    // Distinct whisper conversation partners: everyone with a whisper line (the
    // Partner field), plus players picked from the dropdown but not yet messaged.
    private List<string> WhisperPartners()
    {
        var list = new List<string>();
        var buf = ChatRelayService.Buffer;
        for (int i = 0; i < buf.Count; i++)
        {
            var ln = buf[i];
            if (ln.Channel != ChatRelayService.Channel.Whisper || string.IsNullOrEmpty(ln.Partner)) continue;
            if (!list.Contains(ln.Partner)) list.Add(ln.Partner);
        }
        foreach (var p in _initiatedPartners) if (!list.Contains(p)) list.Add(p);
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
        }
    }

    private void Render()
    {
        if (_log == null) return;
        bool showTime = Settings.ChatShowTimestamps;
        bool showTag  = Settings.ChatShowChannelTags;
        bool newestAtBottom = Settings.ChatNewestAtBottom;
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
            if (whisperPartnerFilter && ln.Partner != _activeWhisperPartner) continue;
            line.Clear();
            if (showTime) line.Append("<color=#808080>").Append(ln.Received.ToString("HH:mm")).Append("</color> ");
            if (showTag)  line.Append(ChannelTag(ln.Channel)).Append(' ');
            // Game-resolved sender name (empty for system messages). The native
            // userName may already carry color tags — render as-is.
            if (!string.IsNullOrEmpty(ln.Sender))
                line.Append(ln.Sender).Append(": ");
            line.Append(ln.Text);
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

    private static string ChannelTag(ChatRelayService.Channel ch) => ch switch
    {
        ChatRelayService.Channel.Global  => "<color=#FFFFFF>[G]</color>",
        ChatRelayService.Channel.Local   => "<color=#B0E0FF>[L]</color>",
        ChatRelayService.Channel.Clan    => "<color=#90EE90>[Clan]</color>",
        ChatRelayService.Channel.System  => "<color=#FFD700>[Sys]</color>",
        ChatRelayService.Channel.Whisper => "<color=#FF9CEF>[W]</color>",
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
        _whisperPicker = null;
        _whisperSubRow = null;
        _input = null;
        _log = null;
        _scrollRect = null;
        _scrollContentRect = null;
    }
}
