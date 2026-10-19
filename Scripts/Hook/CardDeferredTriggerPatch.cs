using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Rules;

namespace SelectSpecialCardMod.Scripts.Hook;

// 本模组卡牌自己的消耗/抽到效果也不能插入正在结算的另一张卡。
// 重放令牌仅放行这一次原方法调用，不屏蔽它在执行中产生的新事件。
[HarmonyPatch]
public static class CardDeferredTriggerPatch
{
    private sealed record Replay(CardModel Card, MethodBase Method);
    private static readonly AsyncLocal<Replay?> Replaying = new();
    static IEnumerable<MethodBase> TargetMethods()
        => typeof(SelectSpecialBaseCard).Assembly.GetTypes()
            .Where(t => typeof(SelectSpecialBaseCard).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsAbstract && m.ReturnType == typeof(Task)
                && m.Name is "AfterCardExhausted" or "AfterCardDrawn");

    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    public static bool Before(CardModel __instance, CardModel card, MethodBase __originalMethod, object[] __args, ref Task __result)
    {
        if (Replaying.Value is { } replay && ReferenceEquals(replay.Card, __instance) && replay.Method == __originalMethod)
        {
            Replaying.Value = null;
            return true;
        }
        if (!ReferenceEquals(__instance, card)) return true;
        var args = __args.ToArray();
        var pile = card.Pile;
        __result = CardEffectQueue.Schedule(card, () => Invoke(__instance, __originalMethod, args), () => card.Pile == pile);
        return false;
    }
    private static async Task Invoke(CardModel card, MethodBase method, object[] args)
    {
        var context = args.OfType<PlayerChoiceContext>().FirstOrDefault();
        context?.PushModel(card);
        try
        {
            Task body;
            var previous = Replaying.Value;
            Replaying.Value = new Replay(card, method);
            try { body = (Task)((MethodInfo)method).Invoke(card, args)!; }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
            finally { Replaying.Value = previous; }
            await body;
        }
        finally { context?.PopModel(card); }
    }
}
