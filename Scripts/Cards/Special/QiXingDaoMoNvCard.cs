using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「七星道魔女」：先古攻击卡，打出后可从手牌免费跟进1张费用≥2的牌
public class QiXingDaoMoNvCard : SelectSpecialBaseCard
{
    public QiXingDaoMoNvCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(16m, ValueProp.Move),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {

        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");


        // 丢弃1张手牌（玩家选择，手牌为空则跳过）
        var handCards = PileType.Hand.GetPile(Owner).Cards.ToList();
        if (handCards.Count == 0) return;

        var selectedCard = (await CardSelectCmd.FromHand(
            prefs: CardPrefs(this, DiscardNamedPrompt, 0, 1),
            context: choiceContext,
            player: Owner,
            filter: null,
            source: this)).ToList();
        if (selectedCard.Count == 0) return;

        await CardCmd.Discard(choiceContext, selectedCard[0]);

        // 造成16点伤害
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);

        // 从手牌选择1张费用在2及2以上的牌免费打出（可跳过），目标为随机敌人
        var selected = (await CardSelectCmd.FromHand(
            prefs: CardPrefs(this, SelectionScreenPrompt, 0, 1),
            context: choiceContext,
            player: Owner,
            filter: c => c.EnergyCost.GetWithModifiers(CostModifiers.None) >= 2,
            source: this)).FirstOrDefault();
        if (selected == null) return;

        Creature? target = Owner!.RunState.Rng.CombatTargets.NextItem(Owner.Creature.CombatState!.HittableEnemies);
        if (target == null) return;

        await CardCmd.AutoPlay(choiceContext, selected, target);
    }
}