using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Power;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「皇帝斗技场」（Kaiser Colosseum）：敌人数量大于1时，所有敌人造成的伤害为0。
// 能力牌 + 消耗关键字：打出场即消耗，效果由凯撒斗技场能力持续整场战斗。
public class KaiSaiDouJiChangCard : SelectSpecialBaseCard
{
    public KaiSaiDouJiChangCard() : base(0, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
        HoverTipFactory.FromPower<KaiSaiDouJiChangPower>(),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<KaiSaiDouJiChangPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }
}