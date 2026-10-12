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
    private static int _performDepth;

    public static void BeginPerformScope() => _performDepth++;

    public static void EndPerformScope() => _performDepth = System.Math.Max(0, _performDepth - 1);

    // 是否处于怪物意图执行期：占位/强夺计数只在执行期消费——
    // 怪物回合之外的被动触发（受击反噬加 buff 等）走同样的命令入口，
    // 不能误消费玩家的塞牌/抢卡计数。
    internal static bool InPerformScope => _performDepth > 0;

    public static void NotifyHit(Creature creature)
    {
        if (_performDepth <= 0) return;
        CardHit?.Invoke(creature);
    }
}
