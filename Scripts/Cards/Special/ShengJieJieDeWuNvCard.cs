using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Power;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 圣结界的巫女：3 费攻击牌（先古）。
// 造成40点伤害；自身拥有能力「绝对魔法圣结界」的场合，造成80点伤害。
// 缩放参考荷鲁斯·祝福：CalculationBaseVar + ExtraDamageVar + CalculatedDamageVar 计算型变量。
public class ShengJieJieDeWuNvCard : SelectSpecialBaseCard
{
    public ShengJieJieDeWuNvCard() : base(3, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        // 计算式：基础40 + 额外40 × 拥有能力「绝对魔法圣结界」?1:0
        new CalculationBaseVar(40m),
        new ExtraDamageVar(40m),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(static (card, _) =>
        {
            if (card?.Owner == null) return 0m;
            return card.Owner.Creature.HasPower<JueDuiMoFaShengJieJiePower>() ? 1m : 0m;
        }),
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
    [
        HoverTipFactory.FromPower<JueDuiMoFaShengJieJiePower>(),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");

        await DamageCmd.Attack(DynamicVars.CalculatedDamage)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);
    }
}