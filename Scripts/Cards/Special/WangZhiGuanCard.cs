using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Power;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 王之棺：获得能力「王之棺」（回合开始检索/回收荷鲁斯怪兽，荷鲁斯怪兽进入弃牌堆时造成伤害）。
public class WangZhiGuanCard : SelectSpecialBaseCard
{
    public WangZhiGuanCard() : base(0, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<WangZhiGuanPower>(),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<WangZhiGuanPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }
}