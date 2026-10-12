using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace SelectSpecialCardMod.Scripts.Tool;

// 露莎卡「应对」：让"那次攻击"变成真·0伤害——连格挡都不掉。
//
// 露莎卡的应对必须"受到攻击前当场问玩家"（异步选择），那时 Damage 已经在管线里了、
// 入口的 amount 改不到。所以改在"吃格挡"这一步把 amount 改成 0：
//   · DamageBlockInternal 收到 0 → Block 一点都不变；
//   · 于是 unblockedDamage = 完整伤害 → 由卡自己的 ModifyHpLostBeforeOsty 清零。
// 净效果：这次伤害既不掉格挡、也不掉血。
public static class LuShaKaDamageNegate
{
    private static readonly HashSet<Creature> Pending = new();

    // 举旗：接下来的这一次伤害按 0 处理
    public static void Mark(Creature creature) => Pending.Add(creature);

    // 读旗（不清）：吃格挡那一步用
    public static bool IsMarked(Creature creature) => Pending.Contains(creature);

    // 收旗（清）：扣血那一步用，保证只影响这一次伤害
    public static bool Consume(Creature creature) => Pending.Remove(creature);
}

// 「吃格挡」这一步：amount 改 0 → 格挡一点不掉
[HarmonyPatch(typeof(Creature), nameof(Creature.DamageBlockInternal))]
public static class LuShaKaDamageBlockPatch
{
    public static void Prefix(Creature __instance, ref decimal amount)
    {
        if (amount <= 0m) return;
        if (!LuShaKaDamageNegate.IsMarked(__instance)) return;

        amount = 0m;
    }
}