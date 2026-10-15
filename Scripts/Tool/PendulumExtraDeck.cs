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
        var card = player.RunState.CreateCard(canonical, player);
        card.AddCapability(new TemporaryPendulumExtraCapability { Temporary = true });
        bool added = false;
        try
        {
            added = (await CardPileCmd.Add(card, Entry.ExtraPile)).success;
        }
        finally { if (!added) RemoveTemporary(card); }
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
