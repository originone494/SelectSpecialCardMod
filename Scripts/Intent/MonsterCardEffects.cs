using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using GameHook = MegaCrit.Sts2.Core.Hooks.Hook;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.ValueProps;

namespace SelectSpecialCardMod.Scripts.Intent;

// 路线 A：怪物侧卡牌效果。
// 怪物打出手牌时由 MonsterDeck 逐张调用——与卡自己的 OnPlay（玩家版，来源=玩家）不同，
// 这里来源是怪物本体，效果方向相反。
//
// 核心做法：**直接调用招式卡对应的原版 MoveState.PerformMove**。
// 因为召唤目标、塞牌种类、逃跑演出这些行为都硬编码在原版招式的 lambda 里、
// 意图对象上读不到，直调原生方法就能全部原样保留（无需按怪物手写映射表），
// 动画 / VFX / 音效 / 招式内部的条件判断也一并还原。
//
// 同时 InPlay 兼任两项职责：
//   ① 让 MonsterAttackSplitPatch 跳过拆分（路线 A 里一张卡 = 一整个招式）；
//   ② 作为 MonsterDeckTurnPatch 的重入守卫——原生 PerformMove 会再次经过同一个 Prefix。
internal static class MonsterCardEffects
{
    private static readonly System.Threading.AsyncLocal<int> PlayDepth = new();
    internal static bool InPlay => PlayDepth.Value > 0;

    private static readonly ThrowingPlayerChoiceContext Ctx = new();

    internal static async Task Play(Creature actor, CardModel card)
    {
        if (actor.Monster == null) return;
        // 生命为0不一定已经离场：利齿之眼等怪物会保留在战斗中，由当前原生招式完成复活。
        // 只放行仍在场、原版不允许移除且与当前状态一致的原生招式；死亡普通怪及旧招式仍不执行。
        if (actor.IsDead && (card is not MonsterMoveCard { SourceMove: not null } pending
            || !ReferenceEquals(pending.SourceMove, actor.Monster.NextMove)
            || actor.CombatState is not { } combat || !combat.Creatures.Contains(actor)
            || GameHook.ShouldCreatureBeRemovedFromCombatAfterDeath(combat, actor))) return;
        PlayDepth.Value++;
        try
        {
            if (card is not MonsterMoveCard move) return; // 废牌：无效果，仅消耗一个出牌位

            // 原生招式：跑原版 move（召唤 / 塞状态 / 塞诅咒 / 逃跑 / 处决 / 未知全部原样生效）
            if (move.SourceMove != null)
            {
                var targets = actor.CombatState?.PlayerCreatures ?? System.Array.Empty<Creature>();
                // 观测：拦截这一招期间真正发出的效果命令（读参数），而不是"前后差值" ——
                // 反击/自伤/触发类效果不会被误算进来（MonsterEffectRecord）
                MonsterEffectRecord.BeginRecord(actor, move.MonsterKind, move.SourceMove.Id);
                try
                {
                    await move.SourceMove.PerformMove(targets);
                    MonsterEffectRecord.CommitRecord();
                }
                finally
                {
                    MonsterEffectRecord.AbortRecord();
                }
                return;
            }

            // 兜底（没有原生招式引用）：按意图逐条通用结算
            foreach (var intent in move.Intents)
            {
                if (actor.IsDead) break;
                await PlayIntent(actor, intent, card);
            }
        }
        finally
        {
            PlayDepth.Value--;
        }
    }

    // 单个意图 → 怪物侧效果（仅在上面的兜底路径使用）
    private static async Task PlayIntent(Creature actor, AbstractIntent intent, CardModel src)
    {
        switch (intent.IntentType)
        {
            case IntentType.Attack:
            case IntentType.DeathBlow:
                if (intent is AttackIntent atk)
                {
                    var targets = Opponents(actor).ToArray();
                    int damage = SafeDamage(atk, targets, actor);
                    if (damage > 0)
                    {
                        await DamageCmd.Attack(damage).FromMonster(actor.Monster!)
                            .WithHitCount(System.Math.Max(1, atk.Repeats))
                            .WithHitFx("vfx/vfx_attack_slash")
                            .Execute(null);
                    }
                }
                break;
            case IntentType.Defend:
                await CreatureCmd.GainBlock(actor, 5m, ValueProp.Move, null);
                break;
            case IntentType.Buff:
                await PowerCmd.Apply<StrengthPower>(Ctx, actor, 1m, actor, src);
                break;
            case IntentType.Debuff:
            case IntentType.DebuffStrong:
                foreach (var target in Opponents(actor))
                    await PowerCmd.Apply<WeakPower>(Ctx, target, 1m, actor, src);
                break;
            case IntentType.Heal:
                await CreatureCmd.Heal(actor, 5m);
                break;
            // 召唤 / 塞状态 / 塞诅咒 / 逃跑 / 未知：无法通用还原，需走原生招式路径
        }
    }

    private static int SafeDamage(AttackIntent atk, Creature[] targets, Creature owner)
    {
        try
        {
            return atk.GetSingleDamage(targets, owner);
        }
        catch
        {
            return 0;
        }
    }

    private static IEnumerable<Creature> Opponents(Creature actor)
        => actor.CombatState?.PlayerCreatures.Where(c => c.IsAlive) ?? Enumerable.Empty<Creature>();
}
