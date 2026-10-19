using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Rules;

namespace SelectSpecialCardMod.Scripts.Hook;

// 打出后回手/同调、登场、送墓后注和回合/应对效果各自作为完整效果处理。
// OnPlay统一由CardPlayEffectQueuePatch处理，避免给每张卡重复打补丁。
[HarmonyPatch]
public static class CardEffectBoundaryPatch
{
    static IEnumerable<MethodBase> TargetMethods()
        => typeof(SelectSpecialBaseCard).Assembly.GetTypes()
            .Where(t => typeof(SelectSpecialBaseCard).IsAssignableFrom(t) || typeof(SelectSpecialBasePower).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsAbstract && m.ReturnType == typeof(Task)
                && m.Name is "AfterCardPlayed" or "DengChangSpecial" or "SongMuSpecial"
                    or "BeforeSideTurnStart" or "AfterSideTurnStart" or "BeforeSideTurnEnd" or "AfterSideTurnEnd"
                    or "AfterSideTurnEndLate" or "BeforeDamageReceived" or "BeforeAttack");

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
