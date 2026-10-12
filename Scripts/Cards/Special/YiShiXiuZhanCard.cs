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

// 一时休战：抽1张牌，这个回合所有人造成的伤害为0。
public class YiShiXiuZhanCard : SelectSpecialBaseCard
{
    public YiShiXiuZhanCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
        HoverTipFactory.FromPower<YiShiXiuZhanPower>(),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 抽1张牌
        await CardPileCmd.Draw(choiceContext, 1, Owner);

        // 这个回合所有人造成的伤害为0：给玩家自己和所有敌人施加「一时休战」
        await PowerCmd.Apply<YiShiXiuZhanPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
        await PowerCmd.Apply<YiShiXiuZhanPower>(choiceContext, Owner.Creature.CombatState!.HittableEnemies, 1, Owner.Creature, this);
    }
}