using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace SelectSpecialCardMod.Scripts.Hook;

// 「时机」可发动的提示：作为牌堆按钮的子节点，条件满足时让按钮金色呼吸泛光，条件消失自动复原。
// 条件会随打牌实时变化（本回合攻击数、牌堆内容），故用 _Process 逐帧判定；
// 不满足时 CanActivateAnyShiJi 会在攻击数不足处提前返回，开销可忽略。
// 每个节点绑定一个牌堆（抽牌堆/弃牌堆/消耗堆各自独立判定与泛光）。
public partial class ShiJiHighlightNode : Node
{
    // 呼吸泛光的峰值颜色（暖金色）
    private static readonly Color GlowColor = new(1f, 0.85f, 0.35f);

    private readonly PileType _pile;
    private Control? _button;
    private Tween? _tween;
    private bool _highlighting;

    public ShiJiHighlightNode(PileType pile)
    {
        _pile = pile;
    }

    public override void _Ready()
    {
        // 挂载时取父节点（即牌堆按钮）作为着色目标
        _button = GetParent() as Control;
    }

    public override void _Process(double delta)
    {
        if (_button == null || !GodotObject.IsInstanceValid(_button)) return;

        bool shouldGlow = ShiJiHook.CurrentTurnPlayer is { } player && ShiJiHook.CanActivateAnyShiJi(player, _pile);
        if (shouldGlow == _highlighting) return;

        _highlighting = shouldGlow;
        if (shouldGlow) StartGlow();
        else StopGlow();
    }

    // 在按钮自身的 modulate 上做循环呼吸；游戏的悬停/按下动画只改子节点 _icon 的 modulate，不会互相覆盖
    private void StartGlow()
    {
        _tween?.Kill();
        _tween = _button!.CreateTween().SetLoops();
        _tween.TweenProperty(_button, "modulate", GlowColor, 0.7)
              .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        _tween.TweenProperty(_button, "modulate", Colors.White, 0.7)
              .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
    }

    private void StopGlow()
    {
        _tween?.Kill();
        _tween = null;
        if (_button != null && GodotObject.IsInstanceValid(_button))
            _button.Modulate = Colors.White;
    }

    public override void _ExitTree()
    {
        _tween?.Kill();
        _tween = null;
    }
}
