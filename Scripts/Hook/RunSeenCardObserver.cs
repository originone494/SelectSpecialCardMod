using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Runs;

namespace SelectSpecialCardMod.Scripts.Hook;

// setter 时卡可能尚未进场或隐藏；在可见帧登记，支持先隐藏后显示与卡面替换。
public partial class RunSeenCardObserver : Node
{
    private CardModel? _recorded;
    private RunState? _run;

    public override void _Process(double delta)
    {

        var card = GetParent<NCard>();
        var model = card.Model;
        var run = RunManager.Instance?.DebugOnlyGetState();
        if (model == null || (ReferenceEquals(model, _recorded) && ReferenceEquals(run, _run))) return;
        if (card.Visibility != ModelVisibility.Visible || !card.IsVisibleInTree()) return;
        // NCard 根节点与 CardContainer 只是定位容器，原版场景中的尺寸为0。
        // 用真实卡框的范围判断是否出现在屏幕上，否则任何卡都无法登记。
        var rect = VisibleBounds(card);
        rect = rect.Intersection(card.GetViewportRect());
        if (!rect.HasArea() || card.SelfModulate.A <= 0) return;
        for (Node? node = card; node != null; node = node.GetParent())
        {
            if (node is CanvasItem canvas && canvas.Modulate.A <= 0) return;
            if (node is Control { ClipContents: true } clip)
            {
                rect = rect.Intersection(clip.GetGlobalRect());
                if (!rect.HasArea()) return;
            }
        }
        if (!RunSeenCards.RecordVisible(model)) return;
        _recorded = model;
        _run = run;
    }

    internal static Rect2 VisibleBounds(NCard card)
        => (card.GetNodeOrNull<Control>("%Frame") ?? card).GetGlobalRect();
}
