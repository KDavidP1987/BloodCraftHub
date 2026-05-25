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

    public ChatWindowOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();
        SetTitle("Chat");

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
            btn.OnClick = () => { _activeTab = idx; UpdateTabHighlight(); Render(); };
            _tabButtons.Add(btn);
        }

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

        var lbl = UIFactory.CreateLabel(scrollContent, "ChatLog", string.Empty, TextAlignmentOptions.TopLeft);
        _log = lbl.TextMesh;
        _log.richText = true;
        _log.enableWordWrapping = true;
        _log.fontSize = Theme.ScaledOverlay(12);
        _log.lineSpacing = 4f;
        _log.margin = new Vector4(6, 4, 6, 4);
        _log.color = Theme.DefaultText;
        UIFactory.SetLayoutElement(_log.gameObject, flexibleWidth: 1, flexibleHeight: 1);

        // Input row — type + Enter or the Send button to send on the active tab's
        // channel. While the field is focused, gameplay input is suppressed (you
        // don't move/attack while typing) via InputSuppression.ChatInputActive.
        var inputRow = UIFactory.CreateHorizontalGroup(ContentRoot, "ChatInputRow",
            true, false, true, true, 2, new Vector4(2, 2, 2, 2), bgColor: new Color(0f, 0f, 0f, 0f));
        UIFactory.SetLayoutElement(inputRow, minHeight: 26, preferredHeight: 26, flexibleWidth: 1);
        _input = UIFactory.CreateInputField(inputRow, "ChatInput", "Type a message…");
        UIFactory.SetLayoutElement(_input.GameObject,
            minWidth: 160, preferredWidth: 280, flexibleWidth: 1,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        _input.Component.onSubmit.AddListener(OnChatSubmit);
        _input.Component.onSelect.AddListener(OnChatSelect);
        _input.Component.onDeselect.AddListener(OnChatDeselect);
        // Explicit Send button — reliable even if Enter is intercepted by the
        // still-visible native chat (until the native-takeover increment).
        var sendBtn = UIFactory.CreateButton(inputRow, "ChatSendButton", "Send");
        UIFactory.SetLayoutElement(sendBtn.GameObject,
            minWidth: 52, preferredWidth: 60, flexibleWidth: 0,
            minHeight: 24, preferredHeight: 24, flexibleHeight: 0);
        sendBtn.OnClick = () => SubmitText(keepFocus: true);

        if (!_subscribed)
        {
            ChatRelayService.LineCaptured += OnLineCaptured;
            _subscribed = true;
        }
        UpdateTabHighlight();
        Render();
    }

    private void OnLineCaptured(ChatRelayService.ChatLine line)
    {
        try { Render(); } catch { /* never let chat rendering throw into the inbound pump */ }
    }

    // Re-render with current Settings (called by the Game UI customization toggles).
    internal void Refresh()
    {
        try { UpdateTabHighlight(); Render(); } catch { }
    }

    // ---- input / send (increment 2) ----

    // 0.17 (2c): focus the input — the divert target when the takeover intercepts
    // the game's chat-open key (Enter). ActivateInputField fires onSelect, which
    // sets ChatInputActive so gameplay input is suppressed while you type.
    internal void FocusInput()
    {
        try
        {
            bool was = _input?.Component?.isFocused ?? false;
            Utils.LogUtils.LogInfo($"[Chat] FocusInput wasFocused={was}");
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
        Utils.LogUtils.LogInfo($"[Chat] SubmitText keepFocus={keepFocus} text='{_input.Text}'");
        try
        {
            var msg = _input.Text?.Trim();
            if (!string.IsNullOrEmpty(msg))
            {
                MessageService.SendChat(msg, ActiveSendChannel());
                // The server broadcasts our message to others but doesn't echo it
                // back to us, so add a local echo so we see our own message here.
                ChatRelayService.CaptureLocalEcho(TabDefs[_activeTab].Filter ?? ChatRelayService.Channel.Local, msg);
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
    private ChatMessageType ActiveSendChannel() => TabDefs[_activeTab].Filter switch
    {
        ChatRelayService.Channel.Global => ChatMessageType.Global,
        ChatRelayService.Channel.Clan   => ChatMessageType.Team,
        ChatRelayService.Channel.Local  => ChatMessageType.Local,
        _                               => ChatMessageType.Local,
    };

    private void UpdateTabHighlight()
    {
        var activeColor = Theme.SliderFill;
        var inactiveColor = activeColor * 0.5f; inactiveColor.a = activeColor.a;
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            var btn = _tabButtons[i];
            if (btn?.Component == null) continue;
            var baseC = (i == _activeTab) ? activeColor : inactiveColor;
            var cb = btn.Component.colors;
            cb.normalColor      = baseC;
            cb.highlightedColor = baseC * 1.2f;
            cb.selectedColor    = baseC * 1.1f;
            cb.pressedColor     = baseC * 0.7f;
            btn.Component.colors = cb;
        }
    }

    private void Render()
    {
        if (_log == null) return;
        bool showTime = Settings.ChatShowTimestamps;
        bool showTag  = Settings.ChatShowChannelTags;
        var filter = TabDefs[_activeTab].Filter;

        var sb = new StringBuilder(2048);
        var buf = ChatRelayService.Buffer;
        for (int i = 0; i < buf.Count; i++)
        {
            var ln = buf[i];
            if (filter.HasValue && ln.Channel != filter.Value) continue;
            if (showTime) sb.Append("<color=#808080>").Append(ln.Received.ToString("HH:mm")).Append("</color> ");
            if (showTag)  sb.Append(ChannelTag(ln.Channel)).Append(' ');
            // Game-resolved sender name (empty for system messages). The native
            // userName may already carry color tags — render as-is.
            if (!string.IsNullOrEmpty(ln.Sender))
                sb.Append(ln.Sender).Append(": ");
            sb.Append(ln.Text).Append('\n');
        }
        _log.text = sb.ToString();
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
        _input = null;
        _log = null;
    }
}
