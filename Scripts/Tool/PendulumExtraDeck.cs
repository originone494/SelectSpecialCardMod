using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Models.Capabilities;

namespace SelectSpecialCardMod.Scripts.Tool;

public sealed class TemporaryPendulumExtraState { public bool Temporary { get; set; } }

// 额外堆是持久牌堆，必须使用 RunState 实体。显式标记仅本场有效的检索卡，
// 战斗结束清除；打出时生成战斗副本，不能把战斗卡直接塞进持久牌堆。
[RegisterModelCapability]
public sealed class TemporaryPendulumExtraCapability : StatefulModelCapability<CardModel, TemporaryPendulumExtraState>
{
    public bool Temporary { get => State.Temporary; set => SetState(new() { Temporary = value }); }
    public override IModelCapability CloneFor(AbstractModel clonedOwner)
    {
        var clone = (TemporaryPendulumExtraCapability)base.CloneFor(clonedOwner);
        clone.Temporary = false;
        return clone;
    }
}

public static class PendulumExtraDeck
{
    public static bool IsTemporary(CardModel card)
        => card.Capabilities().All.OfType<TemporaryPendulumExtraCapability>().Any(c => c.Temporary);

    public static async Task AddTemporary(Player player, CardModel canonical)
    {
        if (player.Creature.CombatState == null || CombatManager.Instance.IsOverOrEnding) return;
        // 已注册的模型能力必须通过工厂克隆；直接 new 会重复注册同一 ModelId，
        // 导致邀请②在两次选卡后抛异常，打出中的卡无法完成落堆。
        var marker = ModelCapabilityRegistry.Create<TemporaryPendulumExtraCapability>();
        marker.Temporary = true;
        var card = player.RunState.CreateCard(canonical, player);
        bool added = false;
        try
        {
            card.AddCapability(marker);
            added = (await CardPileCmd.Add(card, Entry.ExtraPile)).success;
        }
        finally
        {
            // 包括标记挂载失败在内，均回收刚创建的临时实体，不留下局内资产。
            if (!added)
            {
                card.RemoveFromCurrentPile();
                if (player.RunState.ContainsCard(card)) player.RunState.RemoveCard(card);
            }
        }
    }

    public static void RemoveTemporary(CardModel card)
    {
        if (!IsTemporary(card) || card.Owner == null) return;
        var run = card.Owner.RunState;
        card.RemoveFromCurrentPile();
        if (run.ContainsCard(card)) run.RemoveCard(card);
    }

    public static void ClearTemporary(Player player)
    {
        foreach (var card in player.Piles.SelectMany(p => p.Cards).Where(IsTemporary).Distinct().ToList())
            RemoveTemporary(card);
    }
}
