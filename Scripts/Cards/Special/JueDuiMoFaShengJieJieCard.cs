using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Power;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 绝对魔法圣结界：0 费能力牌（先古）。
// 卡面文本很长，全部回合开始/结束时机的效果由 JueDuiMoFaShengJieJiePower 承载。
public class JueDuiMoFaShengJieJieCard : SelectSpecialBaseCard
{
    public JueDuiMoFaShengJieJieCard() : base(0, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
    [
        HoverTipFactory.FromCard<ShengJieJieDeWuNvCard>(),
        HoverTipFactory.FromPower<JueDuiMoFaShengJieJiePower>(),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<JueDuiMoFaShengJieJiePower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }
}