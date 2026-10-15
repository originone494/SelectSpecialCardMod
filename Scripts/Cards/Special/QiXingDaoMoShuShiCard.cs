using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「七星道魔术师」：先古攻击卡，将抽牌堆顶1张送入弃牌堆，
// 伤害 = 21 + 3 × 弃牌堆中不同卡名的数量（用 CalculatedDamageVar 动态显示变化伤害）
public class QiXingDaoMoShuShiCard : SelectSpecialBaseCard
{
    public QiXingDaoMoShuShiCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        // 计算式：基础21 + 额外3 × 弃牌堆不同卡名数，卡面上用 {CalculatedDamage:diff()} 实时显示总伤害
        new CalculationBaseVar(21m),
        new ExtraDamageVar(3m),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(static (card, _) =>
        {
            if (card?.Owner == null) return 0m;
            return PileType.Discard.GetPile(card.Owner).Cards.Select(c => c.Id).Distinct().Count();
        }),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");

        // 将抽牌堆顶的1张卡送去弃牌堆（抽牌堆为空时自动把弃牌堆洗回抽牌堆）
        await SelectToolCmd.DuiMu(choiceContext, Owner, 1);

        // 造成伤害：总伤害在弃牌后计算，因此刚送入弃牌堆的卡若是新的卡名，也会计入+3
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await DamageCmd.Attack(DynamicVars.CalculatedDamage)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);
    }
}