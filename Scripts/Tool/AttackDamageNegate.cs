using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace SelectSpecialCardMod.Scripts.Tool;

// 每个攻击命令独立维护保护；只有命令自身发出的伤害关联该攻击，回调伤害不继承。
public static class AttackDamageNegate
{
    public sealed class AttackFrame(AttackCommand command, AttackFrame? previous)
    {
        internal AttackCommand Command { get; } = command;
        internal AttackFrame? Previous { get; } = previous;
        internal HashSet<Creature> Targets { get; } = [];
    }

    private static readonly AsyncLocal<AttackFrame?> Current = new();
    private static readonly AsyncLocal<AttackFrame?> PendingDamage = new();

    internal static AttackFrame Begin(AttackCommand command)
    {
        var frame = new AttackFrame(command, Current.Value);
        Current.Value = frame;
        return frame;
    }

    internal static void Restore(AttackFrame frame)
    {
        if (ReferenceEquals(Current.Value, frame)) Current.Value = frame.Previous;
    }

    internal static async Task<AttackCommand> Complete(Task<AttackCommand> task, AttackFrame frame)
    {
        try { return await task; }
        finally { frame.Targets.Clear(); }
    }

    public static bool Mark(AttackCommand command, Creature target)
    {
        var frame = Current.Value;
        return frame != null && ReferenceEquals(frame.Command, command) && frame.Targets.Add(target);
    }

    internal static AttackFrame? TakePendingDamage()
    {
        var frame = PendingDamage.Value;
        PendingDamage.Value = null;
        return frame;
    }

    // 由 Execute 状态机的直接伤害调用进入；Damage Prefix 取走标记后，回调中不再保留它。
    public static async Task<IEnumerable<DamageResult>> DealDamage(PlayerChoiceContext context,
        IEnumerable<Creature> targets, decimal amount, ValueProp props, Creature? dealer,
        CardModel? cardSource, CardPlay? cardPlay)
    {
        var previous = PendingDamage.Value;
        PendingDamage.Value = Current.Value;
        try { return await CreatureCmd.Damage(context, targets, amount, props, dealer, cardSource, cardPlay); }
        finally { PendingDamage.Value = previous; }
    }
}

[HarmonyPatch(typeof(AttackCommand), nameof(AttackCommand.Execute), new[] { typeof(PlayerChoiceContext) })]
public static class AttackDamageScopePatch
{
    static void Prefix(AttackCommand __instance, out AttackDamageNegate.AttackFrame __state)
        => __state = AttackDamageNegate.Begin(__instance);

    static void Postfix(ref Task<AttackCommand> __result, AttackDamageNegate.AttackFrame __state)
    {
        AttackDamageNegate.Restore(__state);
        __result = AttackDamageNegate.Complete(__result, __state);
    }

    static Exception? Finalizer(Exception? __exception, AttackDamageNegate.AttackFrame? __state)
    {
        if (__exception != null && __state != null)
        {
            AttackDamageNegate.Restore(__state);
            __state.Targets.Clear();
        }
        return __exception;
    }
}

[HarmonyPatch]
public static class AttackDamageOriginPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        // RitsuLib owns the vanilla Execute redirection. Wrap only its final damage calls,
        // after BeforeAttackHit callbacks, so nested callback damage has its own origin.
        var hook = typeof(STS2RitsuLib.Combat.AttackHits.AttackHitHook);
        var dispatch = AccessTools.Method(hook, "DamageWithAttackHitHooks");
        yield return dispatch; // fallback when a hit context cannot be constructed
        var body = AccessTools.Method(hook, "DamageAndAfterAttackHit");
        var machine = body.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? hook.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Single(t => t.Name.StartsWith("<DamageAndAfterAttackHit>d__", StringComparison.Ordinal));
        yield return AccessTools.Method(machine, nameof(IAsyncStateMachine.MoveNext));
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var damage = AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Damage),
            [typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp),
                typeof(Creature), typeof(CardModel), typeof(CardPlay)]);
        var wrapper = AccessTools.Method(typeof(AttackDamageNegate), nameof(AttackDamageNegate.DealDamage));
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(damage))
            {
                instruction.operand = wrapper;
                replaced++;
            }
            yield return instruction;
        }
        if (replaced != 1)
            throw new InvalidOperationException($"Attack damage origin patch expected one Damage call, found {replaced}.");
    }
}
