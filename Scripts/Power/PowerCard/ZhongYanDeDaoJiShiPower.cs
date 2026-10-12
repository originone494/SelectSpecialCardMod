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

// 终焉的倒计时：回合开始获得1层计数器，达到20层时杀死所有敌人。
[RegisterPower]
public class ZhongYanDeDaoJiShiPower : SelectSpecialBasePower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    // 回合开始时获得1层计数器
    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side != Owner.Side) return;
        Flash();
        await PowerCmd.Apply<ZhongYanDeDaoJiShiPower>(new ThrowingPlayerChoiceContext(), Owner, 1, Owner, null);

        // 计数器达到20层时，杀死所有敌人
        if (Amount >= 20)
        {
            foreach (Creature enemy in Owner.CombatState!.HittableEnemies.ToList())
            {
                await CreatureCmd.Kill(enemy);
            }
        }
    }
}