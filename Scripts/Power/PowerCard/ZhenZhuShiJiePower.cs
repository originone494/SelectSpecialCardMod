using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Power;

// 珍珠世界：打出「珠泪」卡时，对随机敌人造成9点伤害
[RegisterPower]
public class ZhenZhuShiJiePower : SelectSpecialBasePower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 只有打出的卡带「珠泪」标签时触发
        if (!ZhuLeiFilter.IsCard(cardPlay.Card)) return Task.CompletedTask;
        // 只响应自己打出的珠泪卡（与珠泪·哀唱的守卫口径一致）
        if (cardPlay.Card.Owner != Owner.Player) return Task.CompletedTask;
        if (CombatState == null) return Task.CompletedTask;

        Flash();

        Creature? enemy = Owner.Player!.RunState.Rng.CombatTargets.NextItem(CombatState.HittableEnemies);
        if (enemy == null) return Task.CompletedTask;

        return CreatureCmd.Damage(choiceContext, enemy, 9m, ValueProp.Unpowered, Owner, null, null);
    }
}
