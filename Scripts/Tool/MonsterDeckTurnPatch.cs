using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SelectSpecialCardMod.Scripts.Intent;

namespace SelectSpecialCardMod.Scripts.Tool;

// 路线 A：把怪物的回合交给 MonsterDeck。
//   ① 玩家回合开始（Creature.PrepareForNextTurn）→ 建堆/换阶段/对账，头顶展示下一张；
//   ② 怪物行动（MoveState.PerformMove）→ 整段接管：把队首那张打出去（原版 move 效果不跑）。
// 原版 move 的效果 lambda 不再执行，但 MonsterModel.PerformMove 外层照常跑——
// 状态机推进（OnMovePerformed）、出手动画与死亡移除都不受影响。
[HarmonyPatch(typeof(Creature), nameof(Creature.PrepareForNextTurn))]
public static class MonsterDeckDrawPatch
{
    [HarmonyPostfix]
    static void After(Creature __instance)
    {
        if (FeatureStatus.Checked && !FeatureStatus.MonsterDeck) return;
        if (!__instance.IsMonster || __instance.Side != CombatSide.Enemy) return;
        MonsterDeck.PrepareTurn(__instance);
    }
}

[HarmonyPatch(typeof(MoveState), nameof(MoveState.PerformMove))]
public static class MonsterDeckTurnPatch
{
    // 先于 MonsterIntentScopePatch 执行：路线 A 全接管，原版 move 不跑
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    static bool TakeOver(MoveState __instance, ref Task __result)
    {
        if (FeatureStatus.Checked && !FeatureStatus.MonsterDeck) return true;
        // 重入守卫：怪物出牌时会直调原版 MoveState.PerformMove，那次必须放行跑原方法
        if (MonsterCardEffects.InPlay) return true;

        var actor = IntentCardTheft.CurrentActor;
        if (actor == null || !actor.IsMonster || actor.Side != CombatSide.Enemy) return true;
        // 只有「牌堆已建好」才接管：未建好（异常/玩家上下文缺失）交还原版，避免怪物整回合不出手
        if (!MonsterDeck.IsBuilt(actor)) return true;

        // 原方法开头会置 _performedAtLeastOnce（状态机换招判据），跳过时必须手动补上
        Traverse.Create(__instance).Field("_performedAtLeastOnce").SetValue(true);

        __result = RunTurn(actor);
        return false;
    }

    private static async Task RunTurn(Creature actor)
    {
        var scope = IntentCardFlySync.BeginPerformScope();
        try
        {
            await MonsterDeck.PlayTurn(actor);
        }
        finally
        {
            IntentCardFlySync.EndPerformScope(scope);
        }
    }
}

// 空过守卫：本回合打的是废牌（或付不起费用）时，脚本必须停在原地。
// 原版状态机会在下一个玩家回合开始时 RollMove 往前滚一格——被 HoldScript 标记的怪跳过这次 roll，
// 这样「塞废牌 = 空过一回合」而不是「永久顶掉那个招式」。
[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.RollMove))]
public static class MonsterRollHoldPatch
{
    [HarmonyPrefix]
    static bool Prefix(MonsterModel __instance)
    {
        if (FeatureStatus.Checked && !FeatureStatus.MonsterDeck) return true;
        try
        {
            return !MonsterDeck.ConsumeHold(__instance.Creature);
        }
        catch
        {
            return true;   // 拿不到 Creature（非战斗上下文）：照常
        }
    }
}

// 强制跳转对齐：玩家回合内状态机被 SetMoveImmediate 强制换招（击晕、boss 换阶段）时，
// 立刻按新的 NextMove 重排牌堆并刷新头顶展示，让玩家当场看到变化。
// 怪物行动期间的强制跳转不在这里处理——下一次 PrepareTurn / PlayTurn 的对账会自然对齐。
[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.SetMoveImmediate))]
public static class MonsterForcedMovePatch
{
    [HarmonyPostfix]
    static void After(MonsterModel __instance)
    {
        try
        {
            var creature = __instance.Creature;
            if (creature == null || creature.IsPlayer) return;
            if (creature.CombatState?.CurrentSide != CombatSide.Player) return;
            MonsterDeck.OnForcedMove(creature);
        }
        catch
        {
            // 拿不到 Creature：忽略
        }
    }
}
