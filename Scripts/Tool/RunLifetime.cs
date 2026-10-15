using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
namespace SelectSpecialCardMod.Scripts.Tool;
public static class RunLifetime
{
    private static readonly ConditionalWeakTable<RunState, CancellationTokenSource> Sources = new();
    public static CancellationToken Token(RunState run) => Sources.GetValue(run, _ => new CancellationTokenSource()).Token;
    public static void Cancel(RunState run) => Sources.GetValue(run, _ => new CancellationTokenSource()).Cancel();
    public static Task<T> Wait<T>(Task<T> task, RunState run) => task.WaitAsync(Token(run));
}
[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
public static class RunLifetimeCleanupPatch
{
    static void Prefix(RunManager __instance)
    { if (__instance.DebugOnlyGetState() is { } run) RunLifetime.Cancel(run); }
}
