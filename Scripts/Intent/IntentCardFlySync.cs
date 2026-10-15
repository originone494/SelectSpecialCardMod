using MegaCrit.Sts2.Core.Entities.Creatures;

namespace SelectSpecialCardMod.Scripts.Intent;

// 飞卡与伤害的同步桥：战斗逻辑侧（MonsterAttackSplitPatch）在每次攻击结算前调用
// NotifyHit(攻击者)，演出侧（IntentCardOverlayNode）订阅事件，让对应怪物头顶的
// 下一张卡立即飞出——"怪物每出手一次，飞一张卡"，两个序列由同一循环驱动，严格对齐。
public static class IntentCardFlySync
{
    public static event System.Action<Creature>? CardHit;

    // “怪物正在执行意图”的门卫计数：由 MonsterIntentScopePatch 在 MoveState.PerformMove
    // 的整个执行期（含 await）持有。怪物的被动/受击触发（如花园鳗反击）也走同样的
    // PowerCmd/GainBlock 入口，但不在意图执行期内——门卫拦下这些通知，避免误飞卡。
    private static readonly SelectSpecialCardMod.Scripts.Rules.AsyncScope<object> Scope = new();
    public static SelectSpecialCardMod.Scripts.Rules.AsyncScope<object>.Frame BeginPerformScope() => Scope.Enter(new object());
    public static void RestorePerformScope(SelectSpecialCardMod.Scripts.Rules.AsyncScope<object>.Frame frame) => Scope.Restore(frame);
    public static void EndPerformScope(SelectSpecialCardMod.Scripts.Rules.AsyncScope<object>.Frame frame) => Scope.Complete(frame);
    internal static bool InPerformScope => Scope.Current != null;
    public static void NotifyHit(Creature creature)
    { if (InPerformScope) CardHit?.Invoke(creature); }
}
