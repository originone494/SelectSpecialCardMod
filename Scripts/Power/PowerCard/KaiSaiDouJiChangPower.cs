using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Power;

// 凯撒斗技场：敌人数量大于 1 时，所有敌人造成的伤害为 0。
[RegisterPower]
public class KaiSaiDouJiChangPower : SelectSpecialBasePower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    // 敌人数量大于 1 时，所有敌人造成的伤害为 0（数量随战斗实时变化，仅剩 1 个敌人时恢复）
    public override decimal ModifyDamageMultiplicative(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay)
    {
        if (dealer == null || !dealer.IsMonster) return 1m;
        if (Owner == null || Owner.CombatState == null) return 1m;
        if (Owner.CombatState.HittableEnemies.Count <= 1) return 1m;

        return 0m;
    }
}