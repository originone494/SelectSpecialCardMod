using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 查看抽牌堆顶的3张卡，选择1张加入手牌，其余返回抽牌堆顶。
// 加入手牌的卡获得保留，本回合费用增加1。
public class QiangYuBingQianXuZhiHuCard : SelectSpecialBaseCard
{
    private const int _foreseeAmount = 3;

    public QiangYuBingQianXuZhiHuCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
        HoverTipFactory.FromKeyword(CardKeyword.Retain),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var player = Owner;
        var combat = CombatState;
        if (combat == null || CombatManager.Instance.IsOverOrEnding) return;

        // 未选中的卡保持在抽牌堆顶，顺序不变。
        var selected = await SelectToolCmd.Foresee(choiceContext, player, _foreseeAmount, source: this, remainingToDiscard: false);
        foreach (var card in selected)
        {
            if (player.Creature.CombatState != combat || CombatManager.Instance.IsOverOrEnding
                || !player.Creature.IsAlive || card.Owner != player || card.HasBeenRemovedFromState
                || card.Pile != PileType.Hand.GetPile(player)) continue;

            // 保留属于本场战斗的卡牌实例，费用增量仅持续至本回合结束。
            card.EnergyCost.AddThisTurn(1);
            card.InvokeEnergyCostChanged();
            CardCmd.ApplyKeyword(card, CardKeyword.Retain);
        }
    }
}
