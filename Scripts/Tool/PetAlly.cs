using System;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib;
using SelectSpecialCardMod.Scripts.Intent;

namespace SelectSpecialCardMod.Scripts.Tool;

// 玩家方宠物（「强夺」召唤卡副本召唤的同款怪物）：
// 宠物由游戏宠物系统承载（PlayerCmd.AddPet）——站玩家身边、无意图、不被敌人攻击、
// 回合循环不驱动它。帮打由本类驱动：本地玩家回合开始时，每只宠物对一名随机敌人
// 造成伤害（伤害=召唤时所选敌人意图的攻击伤害快照，注册时定死）。
//
// 攻击演出：TriggerAnim("Attack") + CreatureCmd.Damage——不走 AttackCommand.FromMonster
// （它默认 AOE 全体对手，且宠物会被 MonsterAttackSplitPatch 误认为怪物攻击而拆分）。
// choiceContext 用 BlockingPlayerChoiceContext（游戏内自主伤害如黑洞能力的同款用法，
// 无玩家选择环节）。
public static class PetAlly
{
    // 宠物 → 每回合帮打伤害。战斗开始（CombatBegan）清空——宠物随战斗创建/销毁。
    private static readonly System.Collections.Generic.Dictionary<Creature, int> AttackPower = new();

    public static void Init()
    {
        CombatManager.Instance.CombatBegan += _ => AttackPower.Clear();
        CombatManager.Instance.CombatEnded += _ => AttackPower.Clear();
    }
    public static void Register(Creature pet, int damage) => AttackPower[pet] = damage;
    public static async Task RunPetAttacks(PlayerChoiceContext context, MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        var combat = player.Creature.CombatState;
        if (combat == null) return;
        // Combat creature order is synchronized; never depend on Dictionary enumeration order.
        foreach (var pet in combat.Creatures.Where(p => p.PetOwner == player && AttackPower.ContainsKey(p)).ToList())
        {
            if (CombatManager.Instance.IsOverOrEnding || !ReferenceEquals(player.Creature.CombatState, combat)) break;
            int damage = AttackPower[pet];
            if (damage <= 0 || pet.IsDead || !combat.Creatures.Contains(pet)) continue;
            var targets = combat.HittableEnemies.ToList();
            if (targets.Count == 0) break;
            var target = RitsuLibFramework.GetModRunRng(player, Entry.ModId, "pet_targets").NextItem(targets)!;
            await CreatureCmd.TriggerAnim(pet, "Attack", 0.3f);
            if (CombatManager.Instance.IsOverOrEnding || !ReferenceEquals(player.Creature.CombatState, combat)) break;
            if (!target.IsAlive || pet.IsDead) continue;
            await CreatureCmd.Damage(context, target, damage, ValueProp.Move, pet, null, null);
        }
    }
}
