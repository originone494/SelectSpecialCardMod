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

// 游戏王「灵魂召集」：能力牌，打出后获得能力「灵魂召集」，
// 打出攻击牌时，从抽牌堆顶将3张卡送去弃牌堆。
public class LingHunZhaoJiCard : SelectSpecialBaseCard
{
    public LingHunZhaoJiCard() : base(0, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<LingHunZhaoJiPower>(),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<LingHunZhaoJiPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }
}