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

namespace SelectSpecialCardMod.Scripts.Power;

// 死灵的诱惑：当自己的卡进入弃牌堆时，对随机敌人造成3点伤害（每张卡各触发一次）。
[RegisterPower]
public class SiLingDeYouHuoPower : SelectSpecialBasePower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        // 只处理"进入弃牌堆"（打出、丢弃等所有路径都会走到这里）
        if (card.Pile?.Type != PileType.Discard) return Task.CompletedTask;
        // 只响应自己的卡进弃牌堆（多人下队友的卡不触发）
        if (card.Owner?.Creature != Owner) return Task.CompletedTask;
        if (CombatState == null) return Task.CompletedTask;

        var owner = Owner;
        var combat = CombatState;
        return SelectSpecialCardMod.Scripts.Rules.CardEffectQueue.Schedule(async () =>
        {
            if (Owner != owner || CombatState != combat || !owner.IsAlive || !owner.Powers.Contains(this)
                || MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsOverOrEnding) return;
            Flash();
            Creature? enemy = owner.Player!.RunState.Rng.CombatTargets.NextItem(combat.HittableEnemies);
            if (enemy == null) return;
            await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), enemy, 3m, ValueProp.Move, owner, null, null);
        });
    }
}
