using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Intent;

// 「召唤」的玩家版：把指定敌人复制成玩家方宠物（站玩家身边、帮打）。
// 供 MonsterSummonCard（路线 B 单意图卡）与 MonsterMoveCard（路线 A 招式卡）共用，避免两处重复。
internal static class MonsterPet
{
    internal static async Task Summon(PlayerChoiceContext choiceContext, Player owner, Creature enemy)
    {
        if (enemy.Monster == null) return;
        var combat = owner.Creature.CombatState;
        if (combat == null) return;
        // 同款怪物已在己方（战斗中途它换过边等异常情况）：不重复召唤
        if (combat.Allies.Any(c => ReferenceEquals(c.Monster, enemy.Monster))) return;

        // 从模型库取同款怪物的规范实例克隆，创建到玩家侧并注册为宠物（Osty 同路径）
        var canonical = ModelDb.GetById<MonsterModel>(enemy.Monster.Id);
        var pet = combat.CreateCreature(canonical.ToMutable(), owner.Creature.Side, null);
        await PlayerCmd.AddPet(pet, owner);

        // 生命对齐所选敌人；登场淡入演出（Osty 召唤同款）
        await CreatureCmd.SetMaxAndCurrentHp(pet, enemy.MaxHp);
        var node = NCombatRoom.Instance?.GetCreatureNode(pet);
        if (node != null)
        {
            node.Modulate = Colors.Transparent;
            node.CreateTween().TweenProperty(node, "modulate", Colors.White, 0.35f).SetDelay(0.1f);
        }

        PetAlly.Register(pet, SnapshotIntentDamage(enemy));
    }

    // 意图攻击伤害快照：取该敌人当前意图里的单次攻击数值（GetSingleDamage 含力量修正）。
    // 无攻击意图（强化/防御回合被召唤）返回 0——宠物纯占位不帮打。
    private static int SnapshotIntentDamage(Creature enemy)
    {
        try
        {
            var targets = new[] { enemy.CombatState!.Players[0].Creature };
            return enemy.Monster!.NextMove?.Intents
                .OfType<AttackIntent>()
                .Select(a => a.GetSingleDamage(targets, enemy))
                .DefaultIfEmpty(0)
                .Max() ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    // 「这一招原本会召唤的怪」的玩家版：按观测到的怪物种类召出一只玩家方宠物（生命照搬）。
    internal static async Task SummonKind(PlayerChoiceContext choiceContext, Player owner, string monsterKind, int maxHp)
    {
        if (string.IsNullOrEmpty(monsterKind)) return;
        var canonical = FindMonster(monsterKind);
        if (canonical == null)
        {
            GD.PushWarning($"[SelectSpecialCardMod] 找不到要召唤的怪物 {monsterKind}，跳过");
            return;
        }
        var combat = owner.Creature.CombatState;
        if (combat == null) return;

        var pet = combat.CreateCreature(canonical.ToMutable(), owner.Creature.Side, null);
        await PlayerCmd.AddPet(pet, owner);
        if (maxHp > 0) await CreatureCmd.SetMaxAndCurrentHp(pet, maxHp);

        var node = NCombatRoom.Instance?.GetCreatureNode(pet);
        if (node != null)
        {
            node.Modulate = Colors.Transparent;
            node.CreateTween().TweenProperty(node, "modulate", Colors.White, 0.35f).SetDelay(0.1f);
        }

        PetAlly.Register(pet, SnapshotOwnAttackDamage(pet));
    }

    // 宠物帮打伤害：取它自己招式表里的第一个攻击意图数值（取不到则 0 = 不帮打）
    private static int SnapshotOwnAttackDamage(Creature pet)
    {
        try
        {
            var machine = pet.Monster?.MoveStateMachine;
            if (machine == null) return 0;
            var me = pet.CombatState?.Players.Count > 0 ? pet.CombatState.Players[0].Creature : null;
            var targets = me != null ? new[] { me } : System.Array.Empty<Creature>();
            foreach (var state in machine.States.Values)
            {
                if (state is not MoveState move) continue;
                foreach (var intent in move.Intents)
                    if (intent is AttackIntent atk)
                        return Math.Max(0, atk.GetSingleDamage(targets, pet));
            }
        }
        catch
        {
            // 取不到当 0
        }
        return 0;
    }

    // 按种类 id 找怪物规范实例（避免拼 ModelId 的类别串）
    private static MonsterModel? FindMonster(string monsterKind)
    {
        foreach (var monster in ModelDb.Monsters)
            if (monster.Id.Entry == monsterKind) return monster;
        return null;
    }
}
