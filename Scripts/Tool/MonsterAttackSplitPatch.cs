using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Intent;

namespace SelectSpecialCardMod.Scripts.Tool;

// 怪物攻击的实际拆分：怪物的伤害不再一次结算，而是按卡面拆分多次攻击——
//   单段攻击拆伤害（11 → 4/4/3 三次攻击），多段攻击拆段数（3x8 → 3+3+2 段三次攻击），
//   与头顶卡面（IntentCardMapper.SplitDamage）同规则同份数。
//
// 实现：patch AttackCommand.Execute（怪物与玩家攻击的统一结算入口）。
//   检测到怪物来源（Attacker is MonsterModel）时跳过原执行，逐次修改私有字段
//   _damagePerHit（单段拆伤害）或 _hitCount（多段拆段数）后重入 Execute。
//   每次重入都会完整播攻击动画、受击特效和伤害结算——3 张卡对应 3 次攻击演出。
//
// 重入放行用实例标记（HashSet）：拆分期间同一实例的内部 Execute 直接放行，避免无限递归；
//   不同怪物的 AttackCommand 是不同实例，互不干扰，可并发拆分。
//
// 保守跳过：非怪物来源（玩家卡牌/随从 Osty）、伤害或段数 <=0
//   （CalculatedDamageVar 动态伤害构造的 _damagePerHit 恒为 0，读不到可拆数值，不拆）。
// ⚠ 路线 B 遗留，已停用（不参与 Harmony 打补丁）。
// 路线 A 下怪物一回合只打「一整张招式卡」（MonsterCardEffects.InPlay 期间不拆），
// 而这个补丁会把「被动/能力触发的怪物攻击」也拆成 3 段，属于错误行为。保留代码仅供查阅。
// [HarmonyPatch(typeof(AttackCommand), nameof(AttackCommand.Execute))]
public static class MonsterAttackSplitPatch
{
    private static readonly HashSet<AttackCommand> Splitting = new();

    [HarmonyPrefix]
    static bool Prefix(AttackCommand __instance, PlayerChoiceContext? choiceContext, ref Task<AttackCommand> __result)
    {
        if (Splitting.Contains(__instance)) return true; // 拆分期间的内部重入，放行
        // 路线 A：怪物手牌打出的攻击卡本身就是一次攻击，不再拆 3 份
        if (MonsterCardEffects.InPlay) return true;
        if (!__instance.Attacker.IsMonster) return true; // 只拆怪物攻击（Creature.IsMonster）
        if (__instance.Attacker.PetOwner != null) return true; // 玩家方宠物（召唤卡）帮打：不拆

        var t = Traverse.Create(__instance);
        decimal damage = t.Field("_damagePerHit").GetValue<decimal>();
        int hits = t.Field("_hitCount").GetValue<int>();
        if (damage <= 0 || hits <= 0) return true;

        // 单段拆伤害、多段拆段数——与卡面同一套拆分规则（唯一事实源：IntentCardMapper.SplitDamage）
        bool splitDamage = hits == 1;
        List<decimal> parts = IntentCardMapper
            .SplitDamage((int)(splitDamage ? damage : hits))
            .Select(x => (decimal)x)
            .ToList();
        if (parts.Count <= 1)
        {
            // 值太小（1 点伤害）拆不出多份，原样执行——但被「强夺」抢走/被塞废牌占位的那份要消费：
            // 占位（塞状态/诅咒卡）：该动作被废牌顶替，废牌飞出（飞卡通知发队头废牌）；
            // 强夺：怪物空过（不飞卡——对应卡已易主）。返回已完成 Task，防止下游 patch 拿到 null
            if (IntentCardTheft.ConsumeBlocked(__instance.Attacker!))
            {
                IntentCardFlySync.NotifyHit(__instance.Attacker!);
                __result = Task.FromResult(__instance);
                return false;
            }
            if (IntentCardTheft.ConsumeAttack(__instance.Attacker!))
            {
                __result = Task.FromResult(__instance);
                return false;
            }
            return true;
        }

        Splitting.Add(__instance);
        __result = RunSplit(__instance, t, choiceContext, parts, splitDamage);
        return false;
    }

    private static async Task<AttackCommand> RunSplit(AttackCommand cmd, Traverse t,
        PlayerChoiceContext? choiceContext, List<decimal> parts, bool splitDamage)
    {
        decimal origDamage = t.Field("_damagePerHit").GetValue<decimal>();
        int origHits = t.Field("_hitCount").GetValue<int>();
        try
        {
            int index = 0;
            foreach (decimal part in parts)
            {
                // 被塞废牌占位：这份攻击被废牌顶替——不执行，但废牌飞出（"抽到废牌，这个动作位废了"）
                if (IntentCardTheft.ConsumeBlocked(cmd.Attacker!))
                {
                    IntentCardFlySync.NotifyHit(cmd.Attacker!);
                    index++;
                    continue;
                }
                // 被「强夺」抢走一份：怪物少打一次攻击——不飞卡（对应卡已易主）、不执行
                if (IntentCardTheft.ConsumeAttack(cmd.Attacker!))
                {
                    index++;
                    continue;
                }
                if (index > 0)
                {
                    // 攻击间节奏：等 0.8s 再飞下一张卡（首张由首个效果的执行即时触发）
                    await Cmd.Wait(0.8f);
                }
                // 每次攻击前让怪物头顶飞出下一张卡——"怪物每出手一次，飞一张卡"
                IntentCardFlySync.NotifyHit(cmd.Attacker!);
                if (splitDamage) t.Field("_damagePerHit").SetValue(part);
                else t.Field("_hitCount").SetValue((int)part);
                if (cmd.Attacker.IsDead) break;
                await cmd.Execute(choiceContext);
                index++;
            }
        }
        finally
        {
            // 恢复原值（对象可能被复用），并解除重入标记
            t.Field("_damagePerHit").SetValue(origDamage);
            t.Field("_hitCount").SetValue(origHits);
            Splitting.Remove(cmd);
        }
        return cmd;
    }
}
