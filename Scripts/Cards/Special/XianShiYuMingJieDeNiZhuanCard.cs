using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 现世与冥界的逆转：失去1点生命，交换消耗堆和弃牌堆里的卡。
public class XianShiYuMingJieDeNiZhuanCard : SelectSpecialBaseCard
{
    public XianShiYuMingJieDeNiZhuanCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 失去1点生命（不可格挡、不受力量加成）
        await CreatureCmd.Damage(choiceContext, Owner.Creature, 1, ValueProp.Unpowered | ValueProp.Unblockable, Owner.Creature, this, cardPlay);

        // 交换消耗堆和弃牌堆的卡（先快照，避免移动时相互干扰）
        var discardCards = PileType.Discard.GetPile(Owner).Cards.ToList();
        var exhaustCards = PileType.Exhaust.GetPile(Owner).Cards.ToList();

        await CardPileCmd.Add(discardCards, PileType.Exhaust);
        await CardPileCmd.Add(exhaustCards, PileType.Discard);
    }
}
