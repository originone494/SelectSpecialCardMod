using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「替罪羊」：将4张费用分别为1、2、3、4的「羊衍生物」加入抽牌堆。
// 从 yuno mod 搬来，唯一差别：基类换成本 mod 的 SelectSpecialBaseCard，于是这张卡进入本 mod 的特殊卡池。
public class TiZuiYangCard : SelectSpecialBaseCard
{
    public TiZuiYangCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromCard<YangYanShengWuCard>(),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;

        var resultList = new List<CardPileAddResult>();
        for (int cost = 1; cost <= 4; cost++)
        {
            CardModel card = Owner.Creature.CombatState!.CreateCard<YangYanShengWuCard>(Owner);
            card.EnergyCost.SetCustomBaseCost(cost);              // 设置这张实例的费用
            card.DynamicVars["Repeat"].BaseValue = cost;          // 动态变量：格挡重复次数
            var addResult = await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Draw, cardPlay.Card.Owner);
            resultList.Add(addResult);
        }
        CardCmd.PreviewCardPileAdd(resultList, 2f);
    }
}
