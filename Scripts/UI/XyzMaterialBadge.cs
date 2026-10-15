using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes.Cards;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Godot.NodeAttachments;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.UI;

[RegisterNodeAttachment(typeof(NCard), "xyz_material_badge",
    NodeName = "SelectSpecialXyzMaterialBadgeController",
    DuplicatePolicy = NodeAttachmentDuplicatePolicy.ReuseExistingByName,
    SetupTiming = NodeAttachmentSetupTiming.AfterAdd)]
public sealed partial class XyzMaterialBadge : Node, INodeAttachmentSetup
{
    private static readonly Vector2 BadgeSize = new(82f, 30f);
    private NCard? _card;
    private Panel? _panel;
    private Label? _label;
    private Control? _body;
    private TextureRect? _portrait;
    private Control? _typePlaque;
    private Control? _ancientTextBg;
    private int _shownCount = -1;

    public void Setup(Node parent, Node node) { _card = parent as NCard; Refresh(); }
    public override void _Process(double delta) => Refresh();

    private void Refresh()
    {
        if (_card == null || !GodotObject.IsInstanceValid(_card)) return;
        if (_card.Visibility != ModelVisibility.Visible || _card.Model is not XyzMonsterCard xyz
            || xyz.CombatState == null)
        {
            if (_panel != null) _panel.Visible = false;
            return;
        }
        if (_panel == null) CreateBadge();
        if (_panel == null || _body == null || _portrait == null) return;
        int count = xyz.XyzMaterialCards.Count;
        if (_shownCount != count)
        {
            _label!.Text = "素材 " + count.ToString(CultureInfo.InvariantCulture);
            _shownCount = count;
        }
        var corner = ToBody(_portrait, _portrait.Size);
        float bottom = corner.Y;
        if (_typePlaque != null) bottom = Mathf.Min(bottom, ToBody(_typePlaque, Vector2.Zero).Y);
        if (_ancientTextBg?.Visible == true) bottom = Mathf.Min(bottom, ToBody(_ancientTextBg, Vector2.Zero).Y);
        var left = ToBody(_portrait, Vector2.Zero).X;
        _panel.Position = new Vector2(left + 8f, bottom - BadgeSize.Y - 8f);
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
        _panel = new Panel { Name = "SelectSpecialXyzMaterialBadge", Size = BadgeSize, MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.045f, 0.08f, 0.94f),
            BorderColor = new Color(0.88f, 0.72f, 0.35f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
        });
        _label = new Label
        {
            Name = "MaterialCount", Size = BadgeSize, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontSizeOverride("font_size", 20);
        _label.AddThemeColorOverride("font_color", new Color(1f, 0.89f, 0.63f));
        _label.AddThemeConstantOverride("outline_size", 2);
        _panel.AddChild(_label);
        _body.AddChild(_panel);
    }
}
