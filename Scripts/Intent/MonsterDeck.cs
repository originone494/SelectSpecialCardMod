using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Cards.Special;

namespace SelectSpecialCardMod.Scripts.Intent;

// 怪物牌堆（队列模型）。
// 当前队列 = 已抽出的废牌／招式；新塞入的废牌另存，下一次抽牌时才排入队首。
//   · 每回合只打 1 张（队首）——回到原版「一回合一个动作」的节奏；
//   · 不洗牌：招式按状态机顺序循环，打完的招式回到队尾；
//   · 对账（Resync）：脚本队首必须等于状态机 NextMove，不等就按 NextMove 重建脚本
//     （boss 的强制招式、条件/随机跳转都靠这条对齐，等价于「换阶段换牌堆」）；
//   · 空过（HoldScript）：废牌打出的那一回合，状态机不推进、脚本原地不动，
//     下一回合继续打本来该打的招式（眩晕用 ReachableMoves 的 STUNNED 特判体现）；
//   · 换阶段只重建脚本层：玩家已塞入的废牌保留，直到打出或战斗结束。
internal static class MonsterDeck
{
    internal const int MaxEnergy = 3;

    private sealed class Deck
    {
        // 队首 = 下一张要出的牌。前段是废牌（干涉层），后段是招式循环（脚本层）。
        public readonly List<CardModel> Queue = new();
        // 不进入当前手牌，也不能在本次PlayTurn中顶替已经确定的意图。
        public readonly List<CardModel> PendingJunk = new();
        public int Energy;
        public bool Built;
        public string PhaseKey = "";
        public bool Resolving;
    }

    private static readonly Dictionary<Creature, Deck> Decks = new();

    // 空过标记：本回合没出招式（打废牌/付不起费用）→ 下一次 RollMove 不推进状态机，
    // 让脚本停在原地（否则原版状态机仍会往前走一格，等于「永久顶掉那个招式」）。
    private static readonly HashSet<Creature> HoldScript = new();

    // 强夺空过计数：被抢走几张，怪物就要空过几手（每手打不出任何牌）。
    // 与 HoldScript 相反——这里不加 hold：空过的那一回合游戏仍会照常调用
    // OnMovePerformed(NextMove)，于是下一回合 RollMove 自然越过被抢走的招式。
    private static readonly Dictionary<Creature, int> PendingSkip = new();

    private static readonly IReadOnlyList<CardModel> None = Array.Empty<CardModel>();

    internal static void ClearAll() { Decks.Clear(); HoldScript.Clear(); PendingSkip.Clear(); }

    internal static void Clear(Creature? creature)
    {
        if (creature == null) return;
        Decks.Remove(creature);
        HoldScript.Remove(creature);
        PendingSkip.Remove(creature);
    }

    internal static bool IsTracked(Creature? creature) => creature != null && Decks.ContainsKey(creature);

    internal static bool IsBuilt(Creature? creature)
        => creature != null && Decks.TryGetValue(creature, out var d) && d.Built;

    internal static bool IsResolving(Creature? creature)
        => creature != null && Decks.TryGetValue(creature, out var d) && d.Resolving;

    // 头顶展示 = 队首那 1 张（下一次要出的牌）
    internal static IReadOnlyList<CardModel> HandOf(Creature? creature)
    {
        if (creature == null) return None;
        // 被强夺：这一手没牌可打 → 不展示任何意图（下一手再显示）
        if (PendingSkip.TryGetValue(creature, out int skip) && skip > 0) return None;
        return Decks.TryGetValue(creature, out var d) && d.Queue.Count > 0 ? new[] { d.Queue[0] } : None;
    }

    internal static IReadOnlyList<CardModel> DrawOf(Creature? creature)
    {
        if (creature == null || !Decks.TryGetValue(creature, out var deck)) return None;
        if (deck.PendingJunk.Count == 0) return deck.Queue;
        int index = JunkPrefixLength(deck);
        return deck.Queue.Take(index).Concat(deck.PendingJunk).Concat(deck.Queue.Skip(index)).ToList();
    }

    // 队列模型下没有独立的弃牌堆（循环靠「打完回队尾」实现）
    internal static IReadOnlyList<CardModel> DiscardOf(Creature? creature) => None;

    // 只读预览队列中尚未展示的后续行动，不抽牌、不推进状态机，也不启用PendingJunk。
    // 当前招式不再额外回填到灰色队尾：实际打出后的循环由PlayTurn维护。
    internal static IReadOnlyList<CardModel> FutureOf(Creature? creature)
    {
        if (creature == null || !IsBuilt(creature)) return None;
        var current = HandOf(creature);
        return DrawOf(creature).Where(c => !current.Any(now => ReferenceEquals(now, c))).ToList();
    }

    internal static int EnergyOf(Creature? creature)
        => creature != null && Decks.TryGetValue(creature, out var d) ? d.Energy : 0;

    // 供 RollMove 补丁消费：返回 true 表示「本回合空过，别推进状态机」
    internal static bool ConsumeHold(Creature creature) => HoldScript.Remove(creature);

    // 玩家回合开始：建堆/换阶段 → 对账 → 能量清零重算 → 刷新头顶展示
    internal static void PrepareTurn(Creature enemy)
    {
        if (enemy.IsPlayer || enemy.Monster == null) return;
        var deck = GetOrCreate(enemy);
        if (!deck.Built) BuildPool(enemy, deck);
        else RefreshPhase(enemy, deck);
        if (!deck.Built) return;   // 玩家上下文未就绪：下次 PrepareTurn 再试
        Resync(enemy, deck);
        // 正常抽下一手时才启用新废牌，已有废牌仍按先来后到的顺序打出。
        if (deck.PendingJunk.Count > 0)
        {
            deck.Queue.InsertRange(JunkPrefixLength(deck), deck.PendingJunk);
            deck.PendingJunk.Clear();
        }
        deck.Energy = MaxEnergy;
        IntentCardHook.FindOverlay(enemy)?.RefreshHand();
    }

    // 怪物行动：把队首那张打出去。
    internal static async Task PlayTurn(Creature actor)
    {
        if (!Decks.TryGetValue(actor, out var deck)) return;
        // 消耗/检索的连锁期间，不能重入同一敌人的意图执行或重复消耗应对卡。
        if (deck.Resolving) return;
        // 被强夺：本手空过（不推进脚本——下一回合 RollMove 会自然越过被抢走的招式）
        if (PendingSkip.TryGetValue(actor, out int skip) && skip > 0)
        {
            if (skip <= 1) PendingSkip.Remove(actor);
            else PendingSkip[actor] = skip - 1;
            return;
        }
        // 出牌前再对一次账：玩家回合内状态机可能被强制跳转（如击晕、换阶段）
        Resync(actor, deck);
        if (deck.Queue.Count == 0) return;

        var overlay = IntentCardHook.FindOverlay(actor);
        var card = deck.Queue[0];

        // 付不起费用（被削减能量时）：本回合空过，脚本不推进，牌留在原地
        int cost = CostOf(card);
        if (cost > deck.Energy)
        {
            HoldScript.Add(actor);
            return;
        }

        bool completed = false;
        deck.Resolving = true;
        try
        {
            deck.Energy -= cost;
            deck.Queue.RemoveAt(0);
            overlay?.OnCardPlayed(card);

            // 废牌（干涉层）：只占一个行动位；自伤也要全部结算后才通知界面补位。
            if (card is MonsterDisplayCard { IsPlaceholder: true } junk)
            {
                int selfDamage = junk.JunkSelfDamage;
                if (selfDamage > 0)
                {
                    await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), actor, selfDamage,
                        ValueProp.Unblockable | ValueProp.Unpowered, null, null, null);
                    junk.JunkPlayCount++;
                }
                if (junk.JunkPersists) deck.Queue.Add(junk);
                HoldScript.Add(actor);
            }
            else
            {
                // 应对先于整条原生招式；成功时连同攻击、强化、塞牌等全部跳过。
                // 被取消的行动仍正常回队，原生外层负责记录和推进，不额外Hold或RollMove。
                if (card is not MonsterMoveCard move || !await ZhuLeiZhuaYinCard.TryCancelIntent(actor, move))
                    await MonsterCardEffects.Play(actor, card);
                deck.Queue.Add(card);
            }
            completed = true;
        }
        finally
        {
            deck.Resolving = false;
            // 只通知显示层，不等待动画，不提前抽牌或推进状态机。
            if (overlay != null && Godot.GodotObject.IsInstanceValid(overlay)) overlay.OnCardResolved(completed);
        }
    }

    // 强夺：把队首那张从本阶段的牌堆里移除（换阶段重建时恢复）。
    internal static void RemoveFromHand(Creature creature, CardModel card)
    {
        if (Decks.TryGetValue(creature, out var d)) d.Queue.Remove(card);
    }

    // 强夺：让怪物空过一手。被抢走的那张已由 RemoveFromHand 从牌堆移除；
    // 这里不推进状态机——空过的那一回合游戏仍会调用 OnMovePerformed(NextMove)，
    // 于是下一回合 RollMove 自然越过被抢走的招式（本阶段内不再出现，换阶段重建时恢复）。
    internal static IReadOnlyList<CardModel> Stealable(Creature creature)
        => HandOf(creature).Where(c => c is not MonsterDisplayCard { IsPlaceholder: true }).ToList();
    internal static bool TryRemoveIntent(Creature creature, CardModel card)
    {
        if (!Stealable(creature).Contains(card) || !Decks.TryGetValue(creature, out var deck)) return false;
        if (!deck.Queue.Remove(card)) return false;
        RegisterSteal(creature);
        return true;
    }
    internal static void RegisterSteal(Creature creature)
        => PendingSkip[creature] = (PendingSkip.TryGetValue(creature, out int s) ? s : 0) + 1;

    // 塞废牌只改变后续抽牌池。当前手牌、头顶意图及本次行动均不变。
    internal static void AddJunk(Creature enemy, IntentCardTheft.JunkKind kind)
    {
        var player = enemy.CombatState?.Players.OrderBy(p => p.NetId).FirstOrDefault();
        if (player == null) return;
        var deck = GetOrCreate(enemy);
        deck.PendingJunk.Add(MonsterDisplayCard.CreateJunkCard(player, kind));
    }

    private static int JunkPrefixLength(Deck deck)
        => deck.Queue.TakeWhile(c => c is MonsterDisplayCard { IsPlaceholder: true }).Count();

    // 兼容旧调用：队列模型下「空手」只在队列为空时发生（异常情况），无需补抽
    internal static bool EnsureHand(Creature enemy) => false;

    // 玩家回合内状态机被强制换招（击晕 / boss 换阶段）时立刻对齐并刷新展示
    internal static void OnForcedMove(Creature enemy)
    {
        if (!Decks.TryGetValue(enemy, out var deck) || !deck.Built) return;
        Resync(enemy, deck);
        IntentCardHook.FindOverlay(enemy)?.RefreshHand();
    }

    private static Deck GetOrCreate(Creature enemy)
    {
        if (Decks.TryGetValue(enemy, out var existing)) return existing;
        var deck = new Deck();
        Decks[enemy] = deck;
        return deck;
    }

    private static void BuildPool(Creature enemy, Deck deck)
    {
        var player = enemy.CombatState?.Players.OrderBy(p => p.NetId).FirstOrDefault();
        if (player == null) return;   // 玩家上下文未就绪：下次再试（Built 保持 false，不接管）
        var junk = deck.Queue.Where(c => c is MonsterDisplayCard { IsPlaceholder: true }).ToList();
        deck.Queue.Clear();
        deck.Queue.AddRange(junk);
        deck.Queue.AddRange(IntentCardMapper.BuildMonsterPool(enemy, player));
        deck.Built = true;
        deck.PhaseKey = PhaseKeyOf(enemy);
    }

    // 可达招式集合也会因一次性开场招式结束、击晕或分支变化而改变。
    // 重建脚本时保留干涉层，避免尚未生效的废牌被下一次PrepareTurn静默清掉。
    private static void RefreshPhase(Creature enemy, Deck deck)
    {
        string key = PhaseKeyOf(enemy);
        if (key == deck.PhaseKey) return;
        deck.PhaseKey = key;
        deck.Built = false;
        BuildPool(enemy, deck);
        IntentCardHook.FindOverlay(enemy)?.RefreshHand();
    }

    // 对账：脚本队首必须等于状态机 NextMove；不等就按 NextMove 重建脚本（保留未打出的废牌）
    private static void Resync(Creature enemy, Deck deck)
    {
        // 有强夺空过待处理：暂不对账（否则会把被抢走的招式按 NextMove 排回来）
        if (PendingSkip.TryGetValue(enemy, out int pending) && pending > 0) return;
        var player = enemy.CombatState?.Players.OrderBy(p => p.NetId).FirstOrDefault();
        if (player == null || deck.Queue.Count == 0) return;
        var next = enemy.Monster?.NextMove;
        if (next == null) return;

        var scripts = deck.Queue.Where(c => c is not MonsterDisplayCard { IsPlaceholder: true }).ToList();
        if (scripts.Count > 0
            && scripts[0] is MonsterMoveCard head
            && ReferenceEquals(head.SourceMove, next))
        {
            return;   // 已对齐
        }

        // 未对齐：保留废牌（干涉层），脚本层按 NextMove 重排
        var junk = deck.Queue.Where(c => c is MonsterDisplayCard { IsPlaceholder: true }).ToList();
        deck.Queue.Clear();
        deck.Queue.AddRange(junk);
        deck.Queue.AddRange(IntentCardMapper.BuildMonsterPool(enemy, player));
    }

    private static string PhaseKeyOf(Creature enemy)
        => string.Join("|", IntentCardMapper.ReachableMoves(enemy)
            .Select(m => m.Id).OrderBy(id => id));

    // 出牌费用 = 招式卡的意图数（每意图 1 费，最低 1）；废牌等非招式卡按 1 费
    private static int CostOf(CardModel card)
        => card is MonsterMoveCard move ? Math.Max(1, move.Intents.Count) : 1;
}
