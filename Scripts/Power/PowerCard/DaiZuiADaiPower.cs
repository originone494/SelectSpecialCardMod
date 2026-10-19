using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Power;

// 「代罪阿呆」：复制品的链接能力。
//
// 这个能力挂在**复制品自己身上**（不是玩家身上）：
//   · Owner 就是那只复制品；
//   · 复制品死亡时能力随它一起被移除；
//   · 玩家身上不再多出一个看不懂的 buff 图标。
//
// 职责：① 每回合把它重新压成"眩晕"；② 玩家回合开始、重掷行动后再压一次意图；
//       ③ 复制品受到伤害时，把相同数值的伤害转给本体；④ 本体死亡时复制品死亡。
[RegisterPower]
public class DaiZuiADaiPower : LinkedEnemyCopyPower
{
    // 纯后台标记：不参与 buff/debuff 语义，也不显示在 UI 上
    public override PowerType Type => PowerType.None;
    public override PowerStackType StackType => PowerStackType.Single;

    // 每次施加都是独立实例（每只复制品一个），不会按 Id 合并
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    protected override bool IsVisibleInternal => false;

    public override async Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side != CombatSide.Enemy || !Owner.IsAlive) return;

        await CreatureCmd.Stun(Owner);
    }

    // 玩家回合开始时，游戏会为所有敌人重掷行动并刷新意图（PrepareForNextTurn），
    // 复制体会在玩家回合内短暂显示普通攻击意图；此处在该流程之后（Hook.AfterSideTurnStart
    // 晚于 PrepareForNextTurn）重新眩晕，将意图压回眩晕状态。
    public override async Task AfterSideTurnStart(
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side == CombatSide.Player && Owner.IsAlive)
        {
            await CreatureCmd.Stun(Owner);
        }
    }

    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (target != Owner || Original == null || !Original.IsAlive || result.TotalDamage <= 0) return;

        await CreatureCmd.Damage(
            choiceContext,
            Original,
            result.TotalDamage,
            ValueProp.Unpowered,
            null,
            null);
    }
}
