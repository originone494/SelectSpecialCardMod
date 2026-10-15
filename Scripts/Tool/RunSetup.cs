using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using GameHook = MegaCrit.Sts2.Core.Hooks.Hook;
using MegaCrit.Sts2.Core.Runs;

namespace SelectSpecialCardMod.Scripts.Tool;
public static class RunSetup
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RunState, SemaphoreSlim> Gates = new();
    public static async Task Prepare(IRunState run)
    {
        if (FeatureStatus.Checked && !FeatureStatus.RunSetup) return;
        var gate = Gates.GetValue((RunState)run, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try { await PrepareCore(run); }
        finally { gate.Release(); }
    }
    private static async Task PrepareCore(IRunState run)
    {
        foreach (var player in run.Players.OrderBy(p => p.NetId))
        {
            if (!ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), run)) return;
            PendulumExtraDeck.ClearTemporary(player);
            await ExtraDeckRunState.Ensure(player);
            await DuelDskGrant.Grant(player);
            await ExtraDeckSeries.EnsureFull(player);
        }
    }
}
[HarmonyPatch(typeof(GameHook), nameof(GameHook.AfterRoomEntered))]
public static class RunSetupRoomPatch
{
    static void Postfix(IRunState runState, ref Task __result) => __result = Complete(__result, runState);
    static async Task Complete(Task task, IRunState run) { await task; await RunSetup.Prepare(run); }
}
[HarmonyPatch(typeof(GameHook), nameof(GameHook.BeforeCombatStart))]
public static class ExtraDeckCombatStartPatch
{
    static void Postfix(IRunState runState, ref Task __result) => __result = Complete(__result, runState);
    static async Task Complete(Task task, IRunState run) { await task; await RunSetup.Prepare(run); }
}
[HarmonyPatch(typeof(GameHook), nameof(GameHook.AfterCombatEnd))]
public static class ExtraDeckCombatEndPatch
{
    static void Postfix(IRunState runState, ref Task __result) => __result = Complete(__result, runState);
    static async Task Complete(Task task, IRunState run)
    {
        await task;
        foreach (var p in run.Players.OrderBy(p => p.NetId))
        {
            PendulumExtraDeck.ClearTemporary(p);
            await ExtraDeckSeries.EnsureFull(p);
        }
    }
}
[HarmonyPatch(typeof(GameHook), nameof(GameHook.AfterPlayerTurnStart))]
public static class PetAllyTurnPatch
{
    static void Postfix(PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Entities.Players.Player player, ref Task __result)
        => __result = Complete(__result, choiceContext, player);
    static async Task Complete(Task task, PlayerChoiceContext ctx, MegaCrit.Sts2.Core.Entities.Players.Player player)
    { await task; await PetAlly.RunPetAttacks(ctx, player); }
}
