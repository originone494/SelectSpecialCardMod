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
    private int _originalMaxEnergy;
    private bool _entered;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        if (!_entered && Owner.Player != null)
        {
            _originalMaxEnergy = Owner.Player.MaxEnergy;
            _entered = true;
        }
        return Task.CompletedTask;
    }

    public override decimal ModifyDamageMultiplicative(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        CardPlay? cardPlay)
    {
        if (dealer == Owner && target != null && target.IsMonster && props.IsPoweredAttack())
            return 2m;
        if (target == Owner && dealer != null && dealer.IsMonster)
            return 0.5m;
        return 1m;
    }

    public override decimal ModifyMaxEnergy(Player player, decimal amount)
    {
        return amount;
    }

    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        var player = Owner.Player;
        if (side != Owner.Side || !participants.Contains(Owner) || player == null) return;

        player.MaxEnergy = Math.Max(0, player.MaxEnergy - 1);
        if (player.PlayerCombatState?.Energy != 0) return;

        await PowerCmd.Remove(this);
        player.MaxEnergy = _originalMaxEnergy;
    }
}