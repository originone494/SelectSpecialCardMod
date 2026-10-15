using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「七星道法师」：先古攻击卡，造成 8 点伤害，并将抽牌堆顶 1 张卡送入弃牌堆；
// 之后目标在本回合失去力量：基础 4 点，若手牌存在费用 ≥2 的卡，弃牌堆每有 1 张不同卡名的卡额外 +1
// （卡面 {CalculatedDamage:diff()} 实时显示本次降低的力量总额，{ExtraDamage:diff()} 为每张不同卡名的加成）。
public class QiXingDaoFaShiCard : SelectSpecialBaseCard
{
    public QiXingDaoFaShiCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(8,ValueProp.Move),
        // 计算式：基础4 + 额外1 × 弃牌堆不同卡名数（仅当手牌有费用≥2的卡时），卡面上用 {CalculatedDamage:diff()} 实时显示降低的力量总额
        new CalculationBaseVar(4m),
        new ExtraDamageVar(1m),
        new CalculatedDamageVar(ValueProp.Unpowered).WithMultiplier(static (card, _) =>
        {
            if (card?.Owner == null) return 0m;
            if(PileType.Hand.GetPile(card.Owner).Cards.Where(c => c.EnergyCost.GetWithModifiers(CostModifiers.None) >= 2).ToList().Count()<=0) return 0m;
            return PileType.Discard.GetPile(card.Owner).Cards.Select(c => c.Id).Distinct().Count();
        }),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");

        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);

        await SelectToolCmd.DuiMu(choiceContext, Owner, 1);

        var handCards = PileType.Hand.GetPile(Owner).Cards.Where(c => c.EnergyCost.GetWithModifiers(CostModifiers.None) >= 2).ToList();

        int down = DynamicVars.CalculationBase.IntValue;

        if (handCards.Count() > 0)
            down = down + PileType.Discard.GetPile(Owner).Cards.Select(c => c.Id).Distinct().Count();


        await PowerCmd.Apply<QiXingDaoFaShiTempDownPower>(choiceContext, cardPlay.Target!, down, Owner.Creature, this);

    }
}