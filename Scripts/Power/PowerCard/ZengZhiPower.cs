using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Power;

// 游戏王「增殖」：复制品的链接能力。
//
// 这个能力挂在**复制品自己身上**（不是玩家身上）：
//   · Owner 就是那只复制品，所以不需要再持有 Copy 引用；
//   · 复制品死亡时能力随它一起被移除，不会留下悬空引用；
//   · 玩家身上不再多出一个看不懂的 buff 图标。
//
// 职责：① 每回合把它重新压成"眩晕"（无攻击意图、不行动）；
//       ② 玩家回合开始、游戏重掷敌人行动后，再压一次意图；
//       ③ 同侧其他敌人死亡时，自己也死亡。
[RegisterPower]
public class ZengZhiPower : SelectSpecialBasePower
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

    public override async Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature creature,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (wasRemovalPrevented || creature == Owner || !Owner.IsAlive) return;
        if (!creature.IsMonster || creature.Side != Owner.Side) return;

        await CreatureCmd.Kill(Owner);
    }
}
