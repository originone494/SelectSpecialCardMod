using System.Reflection;
using HarmonyLib;

namespace SelectSpecialCardMod.Scripts.Tool;

public static class FeatureStatus
{
    private static readonly HashSet<Type> Applied = [];
    public static bool Checked { get; private set; }
    public static bool AttackProtection { get; private set; }
    public static bool Arrival { get; private set; }
    public static bool RunSetup { get; private set; }
    public static bool MonsterDeck { get; private set; }
    public static bool PendulumPlacement { get; private set; }
    public static bool Xyz { get; private set; }
    public static void AppliedPatch(Type type) => Applied.Add(type);
    public static void Complete()
    {
        Arrival = Applied.Contains(typeof(DrawWindowPatch))
            && Applied.Contains(typeof(SelectSpecialCardMod.Scripts.Hook.CardPlayEffectQueuePatch))
            && Applied.Contains(typeof(SelectSpecialCardMod.Scripts.Hook.CardEffectBoundaryPatch))
            && Applied.Contains(typeof(SelectSpecialCardMod.Scripts.Hook.CardDeferredTriggerPatch));
        AttackProtection = new[] { typeof(AttackDamageScopePatch), typeof(AttackDamageOriginPatch),
            typeof(ResponseDamageScopePatch), typeof(ResponseDamageBlockPatch), typeof(ResponseHpLossPatch),
            typeof(ResponseAttackDamageAmountPatch) }.All(Applied.Contains);
        var redirect = typeof(STS2RitsuLib.Combat.AttackHits.AttackHitHook).Assembly.GetType(
            "STS2RitsuLib.Combat.AttackHits.Patches.AttackCommandExecuteHitHookPatch");
        bool frameworkRedirect = Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m)?.Transpilers
            .Any(p => p.PatchMethod.DeclaringType == redirect) == true);
        AttackProtection &= frameworkRedirect;
        RunSetup = new[] { typeof(ExtraDeckNewRunPatch), typeof(RunSetupRoomPatch), typeof(ExtraDeckCombatStartPatch), typeof(ExtraDeckCombatEndPatch), typeof(RunLifetimeCleanupPatch) }.All(Applied.Contains);
        MonsterDeck = new[] { typeof(MonsterActorScopePatch), typeof(MonsterDeckDrawPatch), typeof(MonsterDeckTurnPatch), typeof(MonsterRollHoldPatch), typeof(MonsterForcedMovePatch), typeof(MonsterIntentScopePatch) }.All(Applied.Contains);
        PendulumPlacement = new[] { typeof(YiXiangMingHandInsertPatch), typeof(YiXiangMingHandRemovePatch), typeof(YiXiangMingHandOrderPatch), typeof(YiXiangMingHandContentsPatch), typeof(YiXiangMingHandLayoutPatch), typeof(YiXiangMingAutoPlayLockPatch), typeof(YiXiangMingPlayPileLockPatch) }.All(Applied.Contains);
        Xyz = Applied.Contains(typeof(XyzMaterialHooksPatch)) && Applied.Contains(typeof(XyzMaterialBindingCleanupPatch));
        Checked = true;
        Entry.Logger.Info($"[Features] Xyz={Xyz}");
        Entry.Logger.Info($"[Features] Arrival={Arrival}; AttackProtection={AttackProtection}; FrameworkHitRedirect={frameworkRedirect}; RunSetup={RunSetup}; MonsterDeck={MonsterDeck}; PendulumPlacement={PendulumPlacement}");
        if (!Arrival || !AttackProtection || !RunSetup || !MonsterDeck || !PendulumPlacement) Godot.GD.PushError("[SelectSpecialCardMod] 必要补丁缺失，相关功能已停用；详见 Features 日志。");
    }
}
