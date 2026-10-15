using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace SelectSpecialCardMod.Scripts.Tool;

// 每次 CreatureCmd.Damage 调用独立持有抵消记录；嵌套伤害不会消费外层应对。
public static class ResponseDamageNegate
{
    public sealed class DamageFrame(DamageFrame? previous, AttackDamageNegate.AttackFrame? attack)
    {
        internal DamageFrame? Previous { get; } = previous;
        internal AttackDamageNegate.AttackFrame? Attack { get; } = attack;
        internal HashSet<Creature> Targets { get; } = [];
    }

    private static readonly AsyncLocal<DamageFrame?> Current = new();

    internal static DamageFrame Begin()
    {
        var frame = new DamageFrame(Current.Value, AttackDamageNegate.TakePendingDamage());
        Current.Value = frame;
        return frame;
    }

    internal static void Restore(DamageFrame frame)
    {
        if (ReferenceEquals(Current.Value, frame)) Current.Value = frame.Previous;
    }

    internal static async Task<IEnumerable<DamageResult>> Complete(
        Task<IEnumerable<DamageResult>> task, DamageFrame frame)
    {
        try { return await task; }
        finally { frame.Targets.Clear(); }
    }

    public static bool Mark(Creature target) => Current.Value?.Targets.Add(target) == true;
    public static bool MarkWholeAttack(Creature target)
        => Current.Value?.Attack is { } attack ? attack.Targets.Add(target) : Mark(target);

    public static bool IsMarked(Creature target)
        => Current.Value?.Targets.Contains(target) == true || Current.Value?.Attack?.Targets.Contains(target) == true;

    public static bool IsWholeAttackMarked(Creature target)
        => Current.Value?.Attack?.Targets.Contains(target) == true;

    public static bool Consume(Creature target)
        => Current.Value?.Targets.Remove(target) == true || Current.Value?.Attack?.Targets.Contains(target) == true;
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage),
    new[] { typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp),
        typeof(Creature), typeof(CardModel), typeof(CardPlay) })]
public static class ResponseDamageScopePatch
{
    static void Prefix(out ResponseDamageNegate.DamageFrame __state) => __state = ResponseDamageNegate.Begin();

    static void Postfix(ref Task<IEnumerable<DamageResult>> __result, ResponseDamageNegate.DamageFrame __state)
    {
        // 原异步方法已经捕获当前结算帧；立即恢复调用方上下文，等待完成时清理帧。
        ResponseDamageNegate.Restore(__state);
        __result = ResponseDamageNegate.Complete(__result, __state);
    }

    static Exception? Finalizer(Exception? __exception, ResponseDamageNegate.DamageFrame? __state)
    {
        if (__exception != null && __state != null)
        {
            ResponseDamageNegate.Restore(__state);
            __state.Targets.Clear();
        }
        return __exception;
    }
}

// 保留格挡；生命损失在 BeforeOsty 阶段清零，避免转移给宠物。
[HarmonyPatch(typeof(Creature), nameof(Creature.DamageBlockInternal))]
public static class ResponseDamageBlockPatch
{
    static void Prefix(Creature __instance, ref decimal amount)
    {
        if (ResponseDamageNegate.IsMarked(__instance)) amount = 0m;
    }
}

[HarmonyPatch]
public static class ResponseHpLossPatch
{
    static MethodBase TargetMethod() => AccessTools.Method(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.ModifyHpLost),
        [typeof(IRunState), typeof(MegaCrit.Sts2.Core.Combat.ICombatState), typeof(Creature), typeof(decimal),
            typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(HpLossHookPhase), typeof(IEnumerable<AbstractModel>).MakeByRefType()]);

    static void Postfix(Creature target, HpLossHookPhase phases, ref decimal __result)
    {
        if (phases == HpLossHookPhase.BeforeOsty && ResponseDamageNegate.Consume(target)) __result = 0m;
    }
}

// 已抵消攻击的后续段在应对钩子前归零；预览和嵌套回调伤害不受影响。
[HarmonyPatch]
public static class ResponseAttackDamageAmountPatch
{
    static MethodBase TargetMethod() => AccessTools.Method(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage),
        [typeof(IRunState), typeof(MegaCrit.Sts2.Core.Combat.ICombatState), typeof(Creature), typeof(Creature),
            typeof(decimal), typeof(ValueProp), typeof(CardModel), typeof(CardPlay), typeof(ModifyDamageHookType),
            typeof(CardPreviewMode), typeof(IEnumerable<AbstractModel>).MakeByRefType()]);

    static void Postfix(Creature? target, CardPreviewMode previewMode, ref decimal __result)
    {
        if (previewMode == CardPreviewMode.None && target != null && ResponseDamageNegate.IsWholeAttackMarked(target))
            __result = 0m;
    }
}
