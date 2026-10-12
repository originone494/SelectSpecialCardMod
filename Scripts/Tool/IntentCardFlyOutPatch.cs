using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using SelectSpecialCardMod.Scripts.Intent;

namespace SelectSpecialCardMod.Scripts.Tool;

// 怪物开始执行意图时（NCreature.PerformIntent），标记连飞开始（首张卡由首个效果的执行触发）。
// 为什么不靠轮询：NextMove 在怪物行动期间不会被清空，意图消失检测不到正确的出手时机。
[HarmonyPatch(typeof(NCreature), "PerformIntent")]
public static class IntentCardFlyOutPatch
{
    [HarmonyPrefix]
    static void Prefix(NCreature __instance)
    {
        __instance.GetNodeOrNull<IntentCardOverlayNode>("IntentCardOverlay")?.BeginFlyOut();
    }
}

// 意图执行期门卫：MoveState.PerformMove 是怪物意图效果的真正执行入口（await move 自定义
// 的效果 lambda）。期间 PowerCmd/GainBlock/攻击拆分发出的飞卡通知有效；怪物回合之外的被动/
// 受击触发（如花园鳗反击加 buff）走同样的命令入口但不在此期间，会被 IntentCardFlySync 的
// 门卫拦下，避免把头顶的意图卡误飞出去。
//
// 开/关时机（日志实证过）：PerformMove 的同步段会同步执行 move lambda 的开头——首击攻击
// 命令在同步段内启动并发出首张飞卡通知，因此门卫必须在**原方法体之前**开启（Harmony Prefix），
// 再用 Postfix 包装返回的 Task，在任务结束时关闭。只用 Postfix 开门卫会晚一拍（首击被拦）；
// 只用 Prefix/Finalizer 做异步标记会因第一个 await 挂起提前关闭。
[HarmonyPatch(typeof(MoveState), nameof(MoveState.PerformMove))]
public static class MonsterIntentScopePatch
{
    [HarmonyPrefix]
    static bool EnterScope(MoveState __instance, ref Task __result)
    {
        IntentCardFlySync.BeginPerformScope();

        // 「强夺」：逃跑意图被抢 → 整个 move 直接跳过。逃跑动画（TriggerAnim("Flee")）
        // 在 move lambda 里独立于 CreatureCmd.Escape 提前播，命令层拦截来不及——
        // 在 move 入口跳过：无动画、无离场，怪物完全空过。
        // CurrentActor 由 MonsterModel.PerformMove 的 Prefix 在更外层设置（此处必然已就位）；
        // CreatureCmd.Escape 命令层的拦截保留，兜底复合招式里的逃跑意图。
        var actor = IntentCardTheft.CurrentActor;
        if (actor != null
            && __instance.Intents?.Any(i => i is EscapeIntent) == true
            && IntentCardTheft.ConsumeEscape(actor))
        {
            __result = Task.CompletedTask;
            return false;
        }
        return true;
    }

    [HarmonyPostfix]
    static void WrapScope(ref Task __result)
    {
        __result = Wrapped(__result);

        static async Task Wrapped(Task inner)
        {
            try
            {
                await inner;
            }
            finally
            {
                IntentCardFlySync.EndPerformScope();
            }
        }
    }
}
