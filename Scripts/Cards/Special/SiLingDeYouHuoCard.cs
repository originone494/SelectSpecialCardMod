using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Power;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 死灵的诱惑：获得能力「死灵的诱惑」——自己的卡进入弃牌堆时，对随机敌人造成3点伤害。
public class SiLingDeYouHuoCard : SelectSpecialBaseCard
{
    public SiLingDeYouHuoCard() : base(0, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<SiLingDeYouHuoPower>(),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<SiLingDeYouHuoPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }
}