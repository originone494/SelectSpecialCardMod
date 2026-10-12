using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using STS2RitsuLib.Combat.HandSize;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「天降的宝札」（Card of Sanctity）：抽满手牌。
// 手牌上限经 MaxHandSizeCalculator 计算（基础值 + RitsuLib 修正器，与手牌上限类能力天然联动）。
public class TianCiBaoPaiCard : SelectSpecialBaseCard
{
    public TianCiBaoPaiCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 按当前手牌上限补抽（已满则不抽）
        int maxHand = MaxHandSizeCalculator.Calculate(Owner);
        int missing = maxHand - PileType.Hand.GetPile(Owner).Cards.Count;
        if (missing > 0)
        {
            await CardPileCmd.Draw(choiceContext, missing, Owner);
        }
    }
}