using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SelectSpecialCardMod.Scripts.Tool;

// 仅管理显示的等待和同一触发上下文的重入；规则账本仍由各卡维护。
internal static class ZhuChangEffectVfx
{
    private static readonly HashSet<(CardModel Card, object Trigger)> Playing = [];

    public static async Task<bool> Play(CardModel card, object trigger, bool fromPlay = false)
    {
        var key = (card, trigger);
        if (!Playing.Add(key)) return false;
        try
        {
            if (!fromPlay && card.Pile?.Type == PileType.Hand && NCombatRoom.Instance?.Ui is { } ui)
            {
                // 入手通知可能先于手牌节点布局；最多等两帧，远端无 UI 时直接跳过显示。
                for (int i = 0; i < 2 && NCard.FindOnTable(card, PileType.Hand) == null; i++)
                {
                    if (!GodotObject.IsInstanceValid(ui) || !ui.IsInsideTree()) return false;
                    await ui.ToSignal(ui.GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                if (NCard.FindOnTable(card, PileType.Hand) == null && LocalContext.IsMe(card.Owner))
                    return await SpecialEffectVfx.PlayFromHandAnchor(card);
            }
            return fromPlay
                ? await SpecialEffectVfx.PlayFromPlay(card)
                : await SpecialEffectVfx.Play(card);
        }
        finally { Playing.Remove(key); }
    }
}
