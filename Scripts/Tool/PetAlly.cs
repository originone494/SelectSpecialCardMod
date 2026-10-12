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

    private static readonly Random Rng = new();

    public static void Init()
    {
        CombatManager.Instance.CombatBegan += _ => AttackPower.Clear();
        RitsuLibFramework.SubscribeLifecycle<PlayerTurnStartedEvent>(evt =>
        {
            // 本地玩家的回合才开始帮打（多人下避免每个客户端重复执行）
            if (evt.Player != IntentCardHook.LocalPlayer) return;
            _ = RunPetAttacksSafe();
        });
    }

    public static void Register(Creature pet, int damage) => AttackPower[pet] = damage;

    // 丢弃任务（fire-and-forget）：伤害命令走游戏命令队列串行执行；
    // 异常吞掉记日志——事件回调里不允许抛出。
    private static async Task RunPetAttacksSafe()
    {
        try
        {
            await RunPetAttacks();
        }
        catch (Exception e)
        {
            Entry.Logger.Error($"[PetAlly] 宠物帮打失败: {e}");
        }
    }

    private static async Task RunPetAttacks()
    {
        var combat = IntentCardHook.LocalPlayer?.Creature?.CombatState;
        if (combat == null) return;
        foreach (var pet in AttackPower.Keys.ToList())
        {
            int damage = AttackPower[pet];
            if (damage <= 0) continue; // 快照为 0（召唤时无攻击意图）：纯占位不帮打
            if (pet.IsDead || !combat.Creatures.Contains(pet)) continue;
            var targets = combat.HittableEnemies;
            if (targets.Count == 0) continue;
            var target = targets[Rng.Next(targets.Count)];

            await CreatureCmd.TriggerAnim(pet, "Attack", 0.3f);
            await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), target, damage, ValueProp.Move,
                pet, null, null);
        }
    }
}
