using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Tool;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Godot.NodeAttachments;

namespace SelectSpecialCardMod.Scripts.UI;

[RegisterNodeAttachment(typeof(NCombatUi), "pendulum_summon_button",
    NodeName = "SelectSpecialPendulumSummonButton",
    DuplicatePolicy = NodeAttachmentDuplicatePolicy.ReuseExistingByName,
    SetupTiming = NodeAttachmentSetupTiming.AfterAdd)]
public sealed partial class PendulumSummonButton : Button, INodeAttachmentSetup
{
    private NCombatUi? _ui;
    private bool _connected;
    private ulong _nextRefresh;

    private static string TextFor(string suffix)
        => new LocString("card_selection", "SELECT_SPECIAL_CARD_MOD_PENDULUM_SUMMON_" + suffix).GetFormattedText();

    public void Setup(Node parent, Node node)
    {
        _ui = parent as NCombatUi;
        CustomMinimumSize = new Vector2(148f, 44f);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        Visible = false;
        AddThemeFontSizeOverride("font_size", 22);
        AddThemeColorOverride("font_color", new Color(0.96f, 0.93f, 0.80f));
        AddThemeStyleboxOverride("normal", Style(new Color(0.08f, 0.16f, 0.20f), new Color(0.66f, 0.78f, 0.79f)));
        AddThemeStyleboxOverride("hover", Style(new Color(0.12f, 0.23f, 0.26f), new Color(0.95f, 0.82f, 0.43f)));
        AddThemeStyleboxOverride("pressed", Style(new Color(0.04f, 0.10f, 0.13f), new Color(0.95f, 0.82f, 0.43f)));
        AddThemeStyleboxOverride("disabled", Style(new Color(0.08f, 0.10f, 0.12f), new Color(0.35f, 0.40f, 0.43f)));
        if (!_connected)
        {
            Pressed += OnPressed;
            _connected = true;
        }
        Refresh();
    }

    public override void _Process(double delta)
    {
        PositionAtEnergy();
        if (Time.GetTicksMsec() < _nextRefresh) return;
        _nextRefresh = Time.GetTicksMsec() + 100;
        Refresh();
    }

    private Player? LocalPlayer()
    {
        var run = RunManager.Instance?.DebugOnlyGetState();
        return run == null ? null : LocalContext.GetMe(run.Players);
    }

    private void Refresh()
    {
        var player = LocalPlayer();
        Visible = player != null && PendulumSummon.TryGetPair(player, out _, out _);
        if (!Visible || player == null) return;
        bool used = PendulumSummon.IsUsed(player);
        Text = TextFor(used ? "USED" : "BUTTON");
        Disabled = used || PendulumSummon.IsBusy(player) || !PendulumSummon.CanOperate(player)
            || _ui?.Hand.CurrentMode != NPlayerHand.Mode.Play || !PendulumSummon.HasCandidates(player);
        TooltipText = TextFor(used ? "USED_HINT" : "HINT");
    }

    private void OnPressed()
    {
        if (!Disabled && LocalPlayer() is { } player) PendulumSummon.Request(player);
        Refresh();
    }

    private void PositionAtEnergy()
    {
        var energy = _ui?.EnergyCounterContainer;
        if (_ui == null || energy == null) return;
        var orb = energy.GetChildren().OfType<NEnergyCounter>().FirstOrDefault();
        // 以能量图标底边的中点为锚，按钮水平居中放在正下方。
        Vector2 bottomCenter = orb != null
            ? _ui.GetGlobalTransform().AffineInverse()
                * (orb.GetGlobalTransform() * new Vector2(orb.Size.X * 0.5f, orb.Size.Y))
            : _ui.GetGlobalTransform().AffineInverse()
                * (energy.GetGlobalTransform() * new Vector2(64f, 128f));
        Position = bottomCenter + new Vector2(-Size.X * 0.5f, 12f);
    }

    private static StyleBoxFlat Style(Color background, Color border) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
        CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        ContentMarginLeft = 10, ContentMarginRight = 10,
        ContentMarginTop = 6, ContentMarginBottom = 6,
    };
}
