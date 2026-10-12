using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 狂战士之魂：失去5点生命，丢弃所有手牌，抽1张牌，若为攻击牌则打出并重复，
// 直到抽到非攻击牌为止。
public class KuangZhanShiZhiHunCard : SelectSpecialBaseCard
{
    public KuangZhanShiZhiHunCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 失去5点生命（不可格挡、不受力量加成）
        await CreatureCmd.Damage(choiceContext, Owner.Creature, 1, ValueProp.Unpowered | ValueProp.Unblockable, Owner.Creature, this, cardPlay);

        // 丢弃所有手牌
        var handCards = PileType.Hand.GetPile(Owner).Cards.ToList();
        await CardCmd.Discard(choiceContext, handCards);

        // 抽1张牌，若为攻击牌则打出，重复直到抽到非攻击牌
        while (true)
        {
            var drawn = (await CardPileCmd.Draw(choiceContext, 1, Owner)).ToList();
            if (drawn.Count == 0) break;
            var card = drawn[0];
            if (card.Type != CardType.Attack) break;

            Creature? target = Owner!.RunState.Rng.CombatTargets.NextItem(Owner.Creature.CombatState!.HittableEnemies);
            if (target == null) break;   // 没有可攻击的敌人，停止循环

            await CardCmd.AutoPlay(choiceContext, card, target);
        }
    }
}