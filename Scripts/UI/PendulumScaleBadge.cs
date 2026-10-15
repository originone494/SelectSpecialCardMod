using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes.Cards;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Godot.NodeAttachments;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.UI;

// NCard 通用于手牌、奖励、图鉴和选牌预览；控制器只读取模型，不修改卡图或牌堆。
[RegisterNodeAttachment(typeof(NCard), "pendulum_scale_badge",
    NodeName = "SelectSpecialPendulumScaleBadgeController",
    DuplicatePolicy = NodeAttachmentDuplicatePolicy.ReuseExistingByName,
    SetupTiming = NodeAttachmentSetupTiming.AfterAdd)]
public sealed partial class PendulumScaleBadge : Node, INodeAttachmentSetup
{
    private static readonly Vector2 BadgeSize = new(52f, 34f);
    private const float Inset = 8f;
    private NCard? _card;
    private Control? _body;
    private TextureRect? _portrait;
    private Control? _typePlaque;
    private Control? _ancientTextBg;
    private Panel? _panel;
    private Label? _label;
    private int? _shownScale;

    public void Setup(Node parent, Node node)
    {
        _card = parent as NCard;
        Refresh();
    }

    // 同一 NCard 可换模型；逐帧轻量读取也支持未来效果改变刻度、隐藏/解锁卡面。
    public override void _Process(double delta) => Refresh();

    private void Refresh()
    {
        if (_card == null || !GodotObject.IsInstanceValid(_card)) return;
        if (_card.Visibility != ModelVisibility.Visible || _card.Model is not IPendulumCard pendulum
            || pendulum.PendulumScale < 0)
        {
            if (_panel != null) _panel.Visible = false;
            return;
        }

        if (_panel == null) CreateBadge();
        if (_panel == null || _body == null || _portrait == null) return;

        int scale = pendulum.PendulumScale;
        if (_shownScale != scale)
        {
            _label!.Text = "◇ " + scale.ToString(CultureInfo.InvariantCulture);
            _shownScale = scale;
        }

        // 先古插画铺满整卡；使用普通插画窗的下角并避开类型牌匾和先古正文背景。
        // 换算到 Body 局部坐标，使卡牌倾斜、缩放、飞行动画不改变徽标相对位置。
        Vector2 corner = ToBody(_portrait, _portrait.Size);
        float bottom = corner.Y;
        if (_typePlaque != null) bottom = Mathf.Min(bottom, ToBody(_typePlaque, Vector2.Zero).Y);
        if (_ancientTextBg?.Visible == true)
            bottom = Mathf.Min(bottom, ToBody(_ancientTextBg, Vector2.Zero).Y);
        _panel.Position = new Vector2(corner.X - BadgeSize.X - Inset, bottom - BadgeSize.Y - Inset);
        _panel.Visible = true;
    }

    private Vector2 ToBody(Control control, Vector2 point)
        => _body!.GetGlobalTransform().AffineInverse() * (control.GetGlobalTransform() * point);

    private void CreateBadge()
    {
        _body = _card!.Body;
        _portrait = _card.GetNodeOrNull<TextureRect>("%Portrait");
        _typePlaque = _card.GetNodeOrNull<Control>("%TypePlaque");
        _ancientTextBg = _card.GetNodeOrNull<Control>("%AncientTextBg");
        if (_body == null || _portrait == null) return;

        _panel = new Panel
        {
            Name = "SelectSpecialPendulumScaleBadge",
            Size = BadgeSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.11f, 0.16f, 0.94f),
            BorderColor = new Color(0.68f, 0.83f, 0.91f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
        });
        _label = new Label
        {
            Name = "ScaleLabel",
            Size = BadgeSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontSizeOverride("font_size", 23);
        _label.AddThemeColorOverride("font_color", new Color(0.93f, 0.97f, 1f));
        _label.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.04f, 0.07f));
        _label.AddThemeConstantOverride("outline_size", 2);
        _panel.AddChild(_label);
        // 挂在真实卡面下，随 Body 的可见性、透明度和动画自然变化；不拦截点击/右键。
        _body.AddChild(_panel);
    }
}
