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

public class JiaShiZhenJieYuQuShenCard : SelectSpecialBaseCard
{
    public JiaShiZhenJieYuQuShenCard() : base(2, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromPower<JiaShiZhenJieYuQuShenPower>(),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<JiaShiZhenJieYuQuShenPower>(
            choiceContext,
            Owner.Creature,
            1,
            Owner.Creature,
            this);
    }
}
