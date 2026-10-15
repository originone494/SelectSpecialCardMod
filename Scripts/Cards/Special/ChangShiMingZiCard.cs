using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 常世明子：将这个回合打出的卡加入手牌，耗能在0-3之间随机变化（仅本回合生效）。
public class ChangShiMingZiCard : SelectSpecialBaseCard
{
    public ChangShiMingZiCard() : base(1, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var combat = CombatState;
        if (Owner == null || combat == null) return;

        // 原生记录包含在本卡入手之前打出的牌，不依赖本卡当时是否参与钩子广播。
        // 只回收本人的已完成打出；当前自身还在结算，且同一实例重复打出只回收一次。
        var cards = CombatManager.Instance.History.CardPlaysFinished
            .Where(e => e.HappenedThisTurn(combat) && e.CardPlay.Player == Owner)
            .Select(e => e.CardPlay.Card)
            .Where(card => card != this)
            .Distinct().ToList();
        foreach (var card in cards)
        {
            if (CombatManager.Instance.IsOverOrEnding || CombatState != combat) break;
            if (card.Owner != Owner) continue;
            // 已打出的能力牌已被原版移出战斗，不能重新入堆；为它创建独立战斗副本。
            var recalled = card.HasBeenRemovedFromState ? combat.CloneCard(card) : card;
            if (recalled.Pile?.Type != PileType.Hand)
                await CardPileCmd.Add(recalled, PileType.Hand);
            if (recalled.Pile?.Type == PileType.Hand)
                recalled.EnergyCost.SetThisTurn(Owner.RunState.Rng.Niche.NextInt(0, 4));
        }
    }
}
