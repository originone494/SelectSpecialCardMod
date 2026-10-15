using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Power;

// 假世真界预取身：自身能力；敌人生命值不高于30%时，若手牌攻击牌伤害总和超过其当前生命值则死亡。
[RegisterPower]
public class JiaShiZhenJieYuQuShenPower : SelectSpecialBasePower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (!creature.IsMonster || !creature.IsAlive || CombatState == null) return;

        // 能力在玩家身上，因此敌人的生命值变化时，由玩家能力检查全部敌人。
        foreach (var enemy in CombatState.HittableEnemies.ToList())
        {
            if (enemy.IsAlive)
            {
                await CheckExecute(enemy, new ThrowingPlayerChoiceContext());
            }
        }
    }

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (CombatState == null) return;

        foreach (var enemy in CombatState.HittableEnemies.ToList())
        {
            if (enemy.IsAlive)
            {
                await CheckExecute(enemy, choiceContext);
            }
        }
    }

    private async Task CheckExecute(Creature enemy, PlayerChoiceContext choiceContext)
    {
        if (!enemy.IsAlive || enemy.MaxHp <= 0 || enemy.CurrentHp > enemy.MaxHp * 0.3m) return;

        decimal handAttackDamage = Owner.Player == null
            ? 0m
            : PileType.Hand.GetPile(Owner.Player).Cards
                .Where(card => card.Type == CardType.Attack)
                .Sum(card => BaseAttackDamage(card, enemy));

        if (handAttackDamage > enemy.CurrentHp)
        {
            Flash();
            await CreatureCmd.Kill(enemy);
        }
    }

    private static decimal BaseAttackDamage(CardModel card, Creature target)
    {
        if (card.DynamicVars.ContainsKey("Damage"))
            return Math.Max(0m, card.DynamicVars.Damage.BaseValue);
        if (card.DynamicVars.ContainsKey("CalculatedDamage")
            && card.DynamicVars["CalculatedDamage"] is CalculatedDamageVar calculated)
            return Math.Max(0m, calculated.Calculate(target));
        return 0m;
    }
}
