using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Tool;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Power;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 荷鲁斯·祝福：造成12点伤害，手牌每有一张「荷鲁斯怪兽」（不含自身）伤害上升12点；
// 拥有能力「王之棺」的场合，这张卡免费打出。
// 缩放参考七星道魔术师：CalculationBaseVar + ExtraDamageVar + CalculatedDamageVar 计算型变量，
// 卡面 {CalculatedDamage:diff()} 实时显示总伤害；计数只计入除自身以外的「荷鲁斯怪兽」
// （预览时本卡在手牌中，显式排除；结算时本卡已移入场上堆，天然不在手牌），两处口径一致。
public class HeLuSiZhuFuCard : SelectSpecialBaseCard
{
    public HeLuSiZhuFuCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        // 计算式：基础12 + 额外12 × 手牌中除自身以外的「荷鲁斯怪兽」数
        new CalculationBaseVar(12m),
        new ExtraDamageVar(12m),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(static (card, _) =>
        {
            if (card?.Owner == null) return 0m;
            return PileType.Hand.GetPile(card.Owner).Cards.Count(c => c != card && c.Tags.Contains(SelectTags.HeLuSiGuaiShou));
        }),
    ];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.HeLuSiGuaiShou,
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.HeLuSiGuaiShou),
    ];

    public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (card != this || originalCost <= 0m) return false;
        if (!HorusFreePlay.IsAvailable(card)) return false;   // 卡名一回合一次
        modifiedCost = 0m;
        return true;
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        HorusFreePlay.Consume(this);
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");

        // 造成伤害：总伤害在结算时按手牌中除自身以外的「荷鲁斯怪兽」数计算
        await DamageCmd.Attack(DynamicVars.CalculatedDamage)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);
    }
}
