using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace SelectSpecialCardMod.Scripts.Intent;

// 意图卡被「强夺」抢走后的状态：按怪物记录被抢走的份数。
// 怪物行动时，MonsterAttackSplitPatch / IntentEffectFlyPatch 在对应执行点消费这些计数——
// 每消费一份，怪物就少执行一次对应的动作（攻击/格挡/强化/削弱/治疗/召唤/塞牌/处决/逃跑），
// 与"真抢"语义一致。
//
// 生命周期：抢夺发生（玩家回合）→ 计数累加 → 怪物行动时逐份消费 →
// 意图变化（RollMove 后签名变、overlay 重建）或怪物死亡时作废——
// 被抢状态只对"当前这一手意图"有效，换一手后抢的是新卡，旧计数没有意义。
internal static class IntentCardTheft
{
    private sealed class State
    {
        public int Attacks;
        public int Blocks;
        public int Buffs;
        public int Debuffs;
        public int Heals;
        public int Summons;
        public int Statuses;
        public int DeathBlows;
        public int Escapes;
        // 塞状态/诅咒卡：通用占位计数——顶替怪物的下一个动作（无论类型）
        public int Blocked;
    }

    private static readonly Dictionary<Creature, State> Stolen = new();

    // 塞状态/诅咒卡的「废牌池」：跨回合持久，与强夺计数（Stolen）分开存——
    // 换手时强夺计数作废，废牌要留着继续参与后续抽牌，抽中才移除。
    private static readonly Dictionary<Creature, List<JunkKind>> JunkPools = new();

    // Status = 一次性；Curse = 保留；KnowledgeCurse = 保留 + 怪物抽到时掉血（「知识的诅咒」）
    internal enum JunkKind { Status, Curse, KnowledgeCurse }

    internal enum TheftKind { Attack, Block, Buff, Debuff, Heal, Summon, Status, DeathBlow, Escape, Blocked }

    internal static void Record(Creature creature, TheftKind kind)
    {
        if (!Stolen.TryGetValue(creature, out var s)) Stolen[creature] = s = new State();
        switch (kind)
        {
            case TheftKind.Attack: s.Attacks++; break;
            case TheftKind.Block: s.Blocks++; break;
            case TheftKind.Buff: s.Buffs++; break;
            case TheftKind.Debuff: s.Debuffs++; break;
            case TheftKind.Heal: s.Heals++; break;
            case TheftKind.Summon: s.Summons++; break;
            case TheftKind.Status: s.Statuses++; break;
            case TheftKind.DeathBlow: s.DeathBlows++; break;
            case TheftKind.Escape: s.Escapes++; break;
            case TheftKind.Blocked: s.Blocked++; break;
        }
    }

    // 消费一份；返回 false 表示没有可消费的（该动作照常执行）
    internal static bool ConsumeAttack(Creature creature) => Consume(creature, s => s.Attacks, s => s.Attacks--);
    internal static bool ConsumeBlock(Creature creature) => Consume(creature, s => s.Blocks, s => s.Blocks--);
    internal static bool ConsumeBuff(Creature creature) => Consume(creature, s => s.Buffs, s => s.Buffs--);
    internal static bool ConsumeDebuff(Creature creature) => Consume(creature, s => s.Debuffs, s => s.Debuffs--);
    internal static bool ConsumeHeal(Creature creature) => Consume(creature, s => s.Heals, s => s.Heals--);
    internal static bool ConsumeSummon(Creature creature) => Consume(creature, s => s.Summons, s => s.Summons--);
    internal static bool ConsumeStatus(Creature creature) => Consume(creature, s => s.Statuses, s => s.Statuses--);
    internal static bool ConsumeDeathBlow(Creature creature) => Consume(creature, s => s.DeathBlows, s => s.DeathBlows--);
    internal static bool ConsumeEscape(Creature creature) => Consume(creature, s => s.Escapes, s => s.Escapes--);

    // 消费一个通用占位（塞状态/诅咒卡）：任何动作都可用它顶替
    internal static bool ConsumeBlocked(Creature creature) => Consume(creature, s => s.Blocked, s => s.Blocked--);

    // 一次记多份占位：怪物换手抽牌时，有几张废牌被抽中就记几份
    internal static void AddBlocked(Creature creature, int count)
    {
        for (int i = 0; i < count; i++) Record(creature, TheftKind.Blocked);
    }

    // ── 废牌池（塞状态/诅咒卡）──
    internal static void AddJunk(Creature creature, JunkKind kind)
    {
        if (!JunkPools.TryGetValue(creature, out var list)) JunkPools[creature] = list = new List<JunkKind>();
        list.Add(kind);
    }

    // 只读快照：抽牌要在遍历的同时把抽中的废牌移出池子，不能直接遍历原集合
    internal static List<JunkKind> SnapshotJunk(Creature creature)
        => JunkPools.TryGetValue(creature, out var list) ? new List<JunkKind>(list) : new List<JunkKind>();

    // 抽中的废牌从池中移除（没抽中的留着，下回合继续参与）
    internal static void ConsumeJunk(Creature creature, JunkKind kind)
    {
        if (JunkPools.TryGetValue(creature, out var list)) list.Remove(kind);
    }

    // 怪物死亡/离场：计数与废牌池一并清空
    internal static void ClearAll(Creature? creature)
    {
        if (creature == null) return;
        Stolen.Remove(creature);
        JunkPools.Remove(creature);
    }

    internal static void Reset() { Stolen.Clear(); JunkPools.Clear(); }

    private static bool Consume(Creature creature, System.Func<State, int> get, System.Action<State> dec)
    {
        if (!Stolen.TryGetValue(creature, out var s) || get(s) <= 0) return false;
        dec(s);
        return true;
    }

    // 当前异步行动作用域的怪物；嵌套行动恢复父级，不污染调用方。
    // CreatureCmd.Add/Escape 只有目标参数、没有施加者——召唤/逃跑拦截靠它定位计数归属；
    // 怪物行动之外（战斗布置/玩家召唤/遗物效果）时为 null，一律放行。
    internal sealed class ActorState(Creature actor) { internal Creature Actor = actor; internal bool SkipSealAdds; }
    private static readonly SelectSpecialCardMod.Scripts.Rules.AsyncScope<ActorState> ActorScope = new();
    internal static Creature? CurrentActor => ActorScope.Current?.Actor;
    internal static bool SkipSealAdds { get => ActorScope.Current?.SkipSealAdds == true;
        set { if (ActorScope.Current is { } state) state.SkipSealAdds = value; } }
    internal static SelectSpecialCardMod.Scripts.Rules.AsyncScope<ActorState>.Frame BeginActorScope(Creature actor)
        => ActorScope.Enter(new ActorState(actor));
    internal static void RestoreActorScope(SelectSpecialCardMod.Scripts.Rules.AsyncScope<ActorState>.Frame frame) => ActorScope.Restore(frame);
    internal static void CompleteActorScope(SelectSpecialCardMod.Scripts.Rules.AsyncScope<ActorState>.Frame frame) => ActorScope.Complete(frame);

    // 意图重建（新的一手）或怪物离场：旧状态作废
    internal static void Invalidate(Creature? creature)
    {
        if (creature != null) Stolen.Remove(creature);
    }
}
