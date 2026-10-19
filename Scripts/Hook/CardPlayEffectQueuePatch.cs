using System;
using System.Threading.Tasks;
using HarmonyLib;
using STS2RitsuLib.Cards;
using SelectSpecialCardMod.Scripts.Rules;

namespace SelectSpecialCardMod.Scripts.Hook;

// RitsuLib已将原版OnPlay调用替换为这个入口，不能再次寻找已被替换的调用指令。
// 包住框架实际返回的Task，保留其前后钩子及原版联机分支任务边界。
[HarmonyPatch(typeof(CardOnPlayHook), nameof(CardOnPlayHook.RunCardOnPlayHooks))]
public static class CardPlayEffectQueuePatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    public static void Before(out CardEffectQueue.Frame __state) => __state = CardEffectQueue.Begin();

    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    public static void After(ref Task __result, CardEffectQueue.Frame? __state)
    {
        if (__state == null) return;
        CardEffectQueue.Restore(__state);
        __result = CardEffectQueue.Complete(__result, __state);
    }

    [HarmonyFinalizer]
    public static Exception? Finally(Exception? __exception, CardEffectQueue.Frame? __state)
    {
        if (__exception != null && __state != null) CardEffectQueue.Abort(__state, failed: true);
        return __exception;
    }
}
