using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Power;

// 一寸家：造成的伤害翻倍，受到的伤害减半。
// 回合结束时失去1点最大能量；若能量为0，则退出一寸家并恢复最大能量。
[RegisterPower]
public class YiCunJiaPower : SelectSpecialBasePower
{
    private int _lostMaxEnergy;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override decimal ModifyDamageMultiplicative(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay)
    {
        // 与原版双倍伤害使用同一倍率入口，未选目标的卡面预览也必须生效。
        // 文案按伤害来源/承受者判定，不限制对方类型或伤害是否来自攻击卡。
        decimal multiplier = 1m;
        if (dealer == Owner) multiplier *= 2m;
        if (target == Owner) multiplier *= 0.5m;
        return multiplier;
    }

    public override decimal ModifyMaxEnergy(Player player, decimal amount)
    {
        return player == Owner.Player ? Math.Max(0m, amount - _lostMaxEnergy) : amount;
    }

    public override async Task AfterSideTurnEndLate(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        var player = Owner.Player;
        if (side != Owner.Side || !participants.Contains(Owner) || player?.PlayerCombatState == null) return;

        // 等其他回合结束效果结算完，再减少最大能量并检查同一次结束的最终结果。
        _lostMaxEnergy++;
        // 最大能量降到0时也立即退出，不能等下一回合补能为0后再等一次回合结束。
        if (player.PlayerCombatState.Energy > 0 && player.PlayerCombatState.MaxEnergy > 0) return;

        await PowerCmd.Remove(this);
    }
}
