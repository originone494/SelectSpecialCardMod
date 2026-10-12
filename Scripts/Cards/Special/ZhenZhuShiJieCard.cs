using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

public class ZhenZhuShiJieCard : SelectSpecialBaseCard
{
    public ZhenZhuShiJieCard() : base(0, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<ZhenZhuShiJiePower>(),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 「检索」1张「珠泪」卡（珠泪=带珠泪标签），检索到的卡加入手牌
        await SelectToolCmd.RetrieverCard(
            choiceContext,
            Owner,
            ZhuLeiFilter.IsCard,
            p => p is SelectSpecialCardPool,
            1, source: this);

        // 施加能力：打出「珠泪」卡时对随机敌人造成9点伤害
        await PowerCmd.Apply<ZhenZhuShiJiePower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }
}
