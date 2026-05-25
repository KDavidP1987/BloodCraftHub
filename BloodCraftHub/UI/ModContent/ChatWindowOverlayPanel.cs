using System.Text;
using BloodCraftHub.Config;
using BloodCraftHub.Services;
using BloodCraftHub.UI.Framework.CustomLib.Panel;
using BloodCraftHub.UI.Framework.CustomLib.Util;
using BloodCraftHub.UI.Framework.UniverseLib.UI;
using BloodCraftHub.UI.Framework.UniverseLib.UI.Panels;
using BloodCraftHub.UI.ModContent.Data;
using TMPro;
using UnityEngine;
using UIBase = BloodCraftHub.UI.Framework.UniverseLib.UI.UIBase;

namespace BloodCraftHub.UI.ModContent;

// 0.17 (feature/v0.17-standalone-ui): standalone tabbed chat window.
//
// INCREMENT 1 = read-only display. Mirrors every inbound chat message into a
// persistent, channel-tabbed, scrollable view; the native chat window is left
// untouched (so you never lose chat input while we validate capture/render).
// Later increments add input/send, per-person whisper tabs, sender names, and
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

    public ChatWindowOverlayPanel(UIBase owner) : base(owner) { }

    protected override void ConstructPanelContent()
    {
        base.ConstructPanelContent();
        SetTitle("Chat");

        // Tab button row.
        var tabRow = UIFactory.CreateHorizontalGroup(ContentRoot, "ChatTabs",
            forceExpandWidth: true, forceExpandHeight: false,
            childControlWidth: true, childControlHeight: true,
            spacing: 2, padding: new Vector4(2, 2, 2, 2));
        UIFactory.SetLayoutElement(tabRow, minHeight: 24, preferredHeight: 24, flexibleWidth: 1);

        for (int i = 0; i < TabDefs.Length; i++)
        {
            int idx = i;
            var btn = UIFactory.CreateButton(tabRow, $"ChatTab_{TabDefs[i].Label}", TabDefs[i].Label);
            UIFactory.SetLayoutElement(btn.GameObject,
                minWidth: 36, preferredWidth: 60, flexibleWidth: 1,
                minHeight: 22, preferredHeight: 22, flexibleHeight: 0);
            btn.OnClick = () => { _activeTab = idx; Render(); };
        }

        // Scrollable message log: one multi-line label rebuilt on update.
        var scroll = UIFactory.CreateScrollView(ContentRoot, "ChatLogScroll",
            out var scrollContent, out _, color: new Color(0f, 0f, 0f, 0f));
        UIFactory.SetLayoutElement(scroll,
            minWidth: 300, preferredWidth: 360, flexibleWidth: 1,
            minHeight: 120, preferredHeight: 220, flexibleHeight: 1);

        var lbl = UIFactory.CreateLabel(scrollContent, "ChatLog", string.Empty, TextAlignmentOptions.TopLeft);
        _log = lbl.TextMesh;
        _log.enableWordWrapping = true;
        _log.fontSize = Theme.ScaledOverlay(12);
        UIFactory.SetLayoutElement(_log.gameObject, flexibleWidth: 1, flexibleHeight: 1);

        if (!_subscribed)
        {
            ChatRelayService.LineCaptured += OnLineCaptured;
            _subscribed = true;
        }
        Render();
    }

    private void OnLineCaptured(ChatRelayService.ChatLine line)
    {
        try { Render(); } catch { /* never let chat rendering throw into the inbound pump */ }
    }

    private void Render()
    {
        if (_log == null) return;
        var filter = TabDefs[_activeTab].Filter;
        var sb = new StringBuilder(2048);
        var buf = ChatRelayService.Buffer;
        for (int i = 0; i < buf.Count; i++)
        {
            var ln = buf[i];
            if (filter.HasValue && ln.Channel != filter.Value) continue;
            sb.Append("<color=#888888>").Append(ln.Received.ToString("HH:mm")).Append("</color> ")
              .Append(ChannelTag(ln.Channel)).Append(' ')
              .Append(ln.Text).Append('\n');
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
        _log = null;
    }
}
