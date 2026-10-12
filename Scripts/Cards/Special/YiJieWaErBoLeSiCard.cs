using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Power;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 异解·瓦尔波勒斯：获得被动（回合开始时向除外堆加入5张随机卡，之后可将1张「异解」卡加入手牌）
public class YiJieWaErBoLeSiCard : SelectSpecialBaseCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.YiJie),
        HoverTipFactory.FromKeyword(SelectKeywords.YiJieMoXian),
    ];

    public YiJieWaErBoLeSiCard() : base(0, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override HashSet<CardTag> CanonicalTags => [SelectTags.YiJie, SelectTags.YiJieMoXian];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<YiJieWaErBoLeSiPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }
}