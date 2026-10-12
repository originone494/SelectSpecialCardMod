using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Intent;

// 意图 → 展示卡：按意图动作拆卡。
//   攻击意图 → 单段伤害固定拆 3 张（23 → 8/8/7）；
//   多段攻击拆段数不拆伤害：段数拆成 3 份，每张「造成x点伤害N次」，次数之和=总段数；
//   处决/施加/格挡/治疗/召唤/…"？" → 文本卡（原版意图文案）。
// 数值读取走游戏 API：AttackIntent.GetSingleDamage(targets, owner)（含力量等修正，
// 与怪物意图图标显示的数值一致）。意图的伤害存在 DamageCalc lambda 里，没有可读字段，
// 反射属性/字段读不到（会静默拿到 0）。
public static class IntentCardMapper
{
    public static List<CardModel> BuildIntentCards(Creature? enemy, Player player)
    {
        if (enemy == null) return new List<CardModel>();
        return BuildIntentCards(enemy.Monster?.NextMove?.Intents, enemy, player);
    }

    // 核心：把一组意图映射成展示卡
    private static List<CardModel> BuildIntentCards(IReadOnlyList<AbstractIntent>? intents, Creature enemy, Player player)
    {
        var result = new List<CardModel>();
        if (intents == null || player.Creature?.CombatState == null) return result;

        var targets = new[] { player.Creature };
        foreach (var intent in intents)
        {
            // 处决（DeathBlowIntent）也是 AttackIntent 子类，必须排在攻击分支之前单独成卡，
            // 否则 TextCard 里的 IntentType.DeathBlow 分支永远进不来（死代码）。
            if (intent is AttackIntent attack && intent is not DeathBlowIntent)
            {
                int single = SafeSingleDamage(attack, targets, enemy);
                if (single <= 0)
                {
                    // 动态伤害（CalculatedDamageVar 等）读不到数值：SplitDamage(0) 会产出 0 张卡，
                    // 攻击意图将完全无展示。退化为单张攻击卡，描述用意图原文，不显示假数值。
                    var fallback = CreateAttack(player, 0, Math.Max(1, attack.Repeats));
                    ((MonsterAttackCard)fallback).IntentText = SafeLabel(attack, targets, enemy);
                    result.Add(fallback);
                    continue;
                }
                int repeats = Math.Max(1, attack.Repeats);
                if (repeats == 1)
                {
                    // 单段：伤害固定拆 3 张（23 → 8/8/7；5 → 2/2/1）
                    foreach (int part in SplitDamage(single))
                        result.Add(CreateAttack(player, part, 1));
                }
                else
                {
                    // 多段：拆段数不拆伤害——段数按同样规则拆成 3 份，每张「造成x点伤害N次」，
                    // 次数之和=总段数（3x8 → 「造成3点伤害3次」x2 + 「造成3点伤害2次」；
                    // 分到 1 次的卡描述自动退化为「造成3点伤害」，由描述补丁按 Repeats 选模板）
                    foreach (int parts in SplitDamage(repeats))
                        result.Add(CreateAttack(player, single, parts));
                }
            }
            else
            {
                result.Add(TextCard(player, intent, targets, enemy));
            }
        }
        return result;
    }

    // 路线 A：怪物牌堆的卡池——**当前阶段可达**的招式各一张招式卡（1 MoveState = 1 卡，不拆分）。
    // 招式内的意图整体保留，如"攻击8 + 强化"仍是一手完整的动作。
    public static List<CardModel> BuildMonsterPool(Creature? enemy, Player player)
    {
        var result = new List<CardModel>();
        if (enemy == null || player.Creature?.CombatState == null) return result;

        var targets = new[] { player.Creature };
        foreach (var move in OrderedMoves(enemy))
        {
            var card = player.Creature.CombatState!.CreateCard<MonsterMoveCard>(player);
            card.InitMove(move, move.Intents);
            card.IntentText = DescribeMove(move.Intents, targets, enemy);
            card.CardTitle = MoveTitle(enemy, move);
            card.ArtPath = MoveArtPath(enemy, move.Intents, targets);
            card.MonsterKind = enemy.Monster?.Id.Entry ?? "";              // 观测记录键：同种怪跨战斗共用
            card.MonsterSource = enemy;                                    // 怪物那张卡的实时数值来源
            card.LiveText = () => DescribeMoveLive(card);                  // 数值实时化（力量/虚弱等）
            result.Add(card);
        }
        return result;
    }

    // 实时招式文案（怪物自己那张卡）：攻击用怪物侧实时数值，其余意图用 观测/静态表 的具体能力与层数。
    internal static string DescribeMoveLive(MonsterMoveCard card)
    {
        string? overridden = MoveDescriptionOverride(card);
        if (overridden != null) return overridden;

        var source = card.MonsterSource;
        if (source == null) return card.IntentText;
        var me = IntentCardHook.LocalPlayer?.Creature;
        var targets = me != null ? new[] { me } : Array.Empty<Creature>();
        var fx = EffectsFor(card);
        var parts = new List<string>();
        foreach (var intent in card.Intents)
        {
            if (intent is AttackIntent atk)
            {
                int damage = SafeSingleDamage(atk, targets, source);
                int repeats = Math.Max(1, atk.Repeats);
                parts.Add(repeats > 1
                    ? MonsterText.Of(MonsterText.DamageMulti, ("Damage", damage.ToString()), ("Repeats", repeats.ToString()))
                    : MonsterText.Of(MonsterText.Damage, ("Damage", damage.ToString())));
            }
            else
            {
               parts.Add(NonAttackText(intent, targets, source, fx));
           }
       }
        return string.Join(MonsterText.IntentSeparator, parts);
    }

    // 玩家侧招式文案（抢来的副本用）：伤害按「玩家作为攻击者」实时计算——和玩家自己的攻击牌一样
    // 吃力量/遗物加成；目标侧的易伤不在这里体现（和普通攻击牌一致，打出去结算时再叠）。
    // 非攻击意图同样写出 观测/静态表 的具体能力与层数（看到的 = 打出的）。
    internal static string DescribeMovePlayerSide(MonsterMoveCard card)
    {
        // 人工覆盖表写了描述就用它（这是作者为"玩家版"手写的卡面文案）
        var manual = MonsterManualTable.Lookup(card.MonsterKind, card.SourceMove?.Id ?? "");
        if (manual != null)
        {
            string text = MonsterManualTable.DescriptionOf(manual);
            if (!string.IsNullOrEmpty(text)) return text;
        }

        // 其次看这张招式卡有没有单独写描述（cards 表）
        string? overridden = MoveDescriptionOverride(card);
        if (overridden != null) return overridden;

        var me = card.Owner?.Creature;
        var targets = me != null ? new[] { me } : Array.Empty<Creature>();
        var fx = EffectsFor(card);
        var parts = new List<string>();
        foreach (var intent in card.Intents)
        {
            if (intent is AttackIntent atk)
            {
                int baseValue = RawDamageOf(atk);
                int damage = PlayerSideDamage(card, atk, baseValue);
                int repeats = Math.Max(1, atk.Repeats);
                // 和原版攻击牌一致：伤害被加成就变绿、被削减就变红，相等则不上色
                string number = HighlightChange(baseValue, damage);
                parts.Add(repeats > 1
                    ? MonsterText.Of(MonsterText.DamageMulti, ("Damage", number), ("Repeats", repeats.ToString()))
                    : MonsterText.Of(MonsterText.Damage, ("Damage", number)));
            }
            else if (me != null)
            {
                // 格挡和攻击一样按玩家侧算（含敏捷/遗物），并绿/红高亮；结算时传基础值，
                // 引擎会再套一次同样的修正 → 显示的 = 打出的。其余意图用通用处理。
                if (intent.IntentType == IntentType.Defend && fx is { Block: > 0 })
                {
                    int shown = PlayerSideBlock(card, fx.Block);
                    parts.Add(MonsterText.Of(MonsterText.Block,
                        ("Block", HighlightChange(fx.Block, shown))));
                }
                else
                {
                   parts.Add(NonAttackText(intent, targets, me, fx));
               }
           }
       }
        return string.Join(MonsterText.IntentSeparator, parts);
    }

    // 非攻击意图文案：有 观测/静态表 的具体数值就写出来（能力种类 + 层数），
    // 没有则退回意图的通用文案（SafeLabel）。两侧（怪物卡 / 副本）共用。
    private static string NonAttackText(AbstractIntent intent, Creature[] targets, Creature owner, MonsterMoveEffects? fx)
    {
        switch (intent.IntentType)
        {
            case IntentType.Defend when fx is { Block: > 0 }:
                return MonsterText.Of(MonsterText.Block, ("Block", fx.Block.ToString()));
            case IntentType.Heal when fx is { Heal: > 0 }:
                return MonsterText.Of(MonsterText.Heal, ("Heal", fx.Heal.ToString()));
            case IntentType.Buff when fx is { SelfPowers.Count: > 0 }:
                return string.Join(MonsterText.IntentSeparator, fx.SelfPowers.Select(p => MonsterText.Of(MonsterText.GainPower,
                    ("Amount", p.Amount.ToString()), ("Power", PowerName(p.Power)))));
            case IntentType.Debuff:
            case IntentType.DebuffStrong:
                if (fx is { PlayerPowers.Count: > 0 })
                    return string.Join(MonsterText.IntentSeparator, fx.PlayerPowers.Select(p => MonsterText.Of(MonsterText.ApplyPower,
                        ("Amount", p.Amount.ToString()), ("Power", PowerName(p.Power)))));
                break;
        }
        return SafeLabel(intent, targets, owner);
    }

    // 取该招式可用的数值：观测优先、逐字段回退到静态表（与打出结算同一份逻辑）
    internal static MonsterMoveEffects? EffectsFor(MonsterMoveCard card)
    {
        string moveId = card.SourceMove?.Id ?? "";
        // 人工覆盖表优先（作者明确指定的整条效果）
        var manual = MonsterManualTable.Lookup(card.MonsterKind, moveId);
        if (manual != null) return MonsterManualTable.ToEffects(manual);
        var observed = MonsterEffectRecord.Get(card.MonsterKind, moveId);
        var fromTable = MonsterEffectTable.Lookup(card.MonsterKind, moveId, FindSource(card));
        return observed == null && fromTable == null ? null : MonsterMoveEffects.Merge(observed, fromTable);
    }

    // 静态表里的表达式要在「这只怪」的实例上求值：
    //   · 怪物自己那张卡直接持有本体（MonsterSource）；
    //   · 抢来的副本没有本体引用 → 按种类在敌方场上现找一只同种怪。
    internal static Creature? FindSource(MonsterMoveCard card)
    {
        if (card.MonsterSource != null) return card.MonsterSource;
        if (string.IsNullOrEmpty(card.MonsterKind)) return null;
        var combat = card.Owner?.Creature?.CombatState;
        if (combat == null) return null;
        foreach (var enemy in combat.Enemies)
            if (enemy.Monster?.Id.Entry == card.MonsterKind) return enemy;
        return null;
    }

    // 能力显示名（本地化），取不到就退回类名
    private static string PowerName(string className)
    {
        try
        {
            var model = MonsterPowerLookup.FindCanonical(className);
            if (model == null) return className;
            string title = model.Title.GetFormattedText();
            return string.IsNullOrWhiteSpace(title) ? className : title;
        }
        catch
        {
            return className;
        }
    }

    // 原始伤害（招式自带的数值，不含力量等修正）
    private static int RawDamageOf(AttackIntent atk)
    {
        try
        {
            return Math.Max(0, (int)(atk.DamageCalc?.Invoke() ?? 0m));
        }
        catch
        {
            return 0;
        }
    }

    // 与原版一致的高亮：比基础值高 = [green]，低 = [red]，相等不上色（卡面描述是 BBCode）
    private static string HighlightChange(int baseValue, int shown)
    {
        string text = shown.ToString();
        if (shown == baseValue) return text;
        return shown > baseValue ? $"[green]{text}[/green]" : $"[red]{text}[/red]";
    }

    // 玩家侧单段伤害：与游戏内攻击牌的显示同源——Hook.ModifyDamage(owner=玩家, target=null)，
    // 即只含玩家侧（力量/遗物）修正，不含目标侧（易伤）。打出结算时引擎会再叠目标侧。
    private static int PlayerSideDamage(CardModel card, AttackIntent atk, decimal raw)
    {
        try
        {
            var owner = card.Owner;
            var runState = owner?.RunState;
            if (owner == null || runState == null) return Math.Max(0, (int)raw);
            // 注意：本 mod 有 SelectSpecialCardMod.Scripts.Hook 命名空间，会遮蔽 Hook 类，必须写全名
            decimal value = MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage(runState, card.CombatState, null, owner.Creature, raw,
                ValueProp.Move, card, null, ModifyDamageHookType.All, CardPreviewMode.None, out _);
            return Math.Max(0, (int)value);
        }
        catch
        {
            return 0;
        }
    }

    // 玩家侧格挡：与游戏内防御牌的显示同源——Hook.ModifyBlock(owner=玩家)，实时（含敏捷等）。
    // 结算时往 GainBlock 传基础值，引擎会套一次同样的修正 → 显示的 = 打出的。
    private static int PlayerSideBlock(CardModel card, decimal baseBlock)
    {
        try
        {
            var owner = card.Owner;
            if (owner == null || card.CombatState is not { } combat) return (int)baseBlock;
            decimal value = MegaCrit.Sts2.Core.Hooks.Hook.ModifyBlock(combat, owner.Creature, baseBlock,
                ValueProp.Move, card, null, out _);
            return Math.Max(0, (int)value);
        }
        catch
        {
            return (int)baseBlock;
        }
    }

    // 招式卡显示名。查找顺序：
    //   ① 本 mod 的本地化表（cards 表的 SELECT_SPECIAL_CARD_MOD_MOVE_{怪物}_{招式}.title）——改名/补名只改这里；
    //   ② 游戏本体的图鉴文案（monsters 表的 {怪物}.moves.{招式}.title，如「生成」「重踏」）；
    //   ③ 主意图类型兜底（如「攻击·强化」），保证卡与卡之间不重名。
    private static string MoveTitle(Creature enemy, MoveState move)
    {
        var monsterId = enemy.Monster?.Id.Entry;
        if (!string.IsNullOrEmpty(monsterId))
        {
            var keys = MoveKeyCandidates(move.Id).ToList();

            // ① mod 自己的卡名表：作者想改招式卡名就把正文写在 localization/<语言>/cards.json
            foreach (var key in keys)
            {
                string? text = LocalizedText("cards", "SELECT_SPECIAL_CARD_MOD_MOVE_" + monsterId + "_" + key + ".title");
                if (text != null) return text;
            }

            // ② 游戏本体的招式名（图鉴键约定：招式 id 去掉 "_MOVE"，GetBestiaryMoveName 同款）
            foreach (var key in keys)
            {
                string? text = LocalizedText("monsters", monsterId + ".moves." + key + ".title");
                if (text != null) return text;
            }
        }
        var names = (move.Intents ?? Array.Empty<AbstractIntent>())
            .Select(IntentShortName)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .ToList();
        return names.Count > 0 ? string.Join("·", names) : "";
    }

    // 招式 id 的候选键：原样 + 去掉 "_MOVE"（"WRIGGLE_MOVE"→"WRIGGLE"、"X_MOVE_2"→"X_2"）
    private static IEnumerable<string> MoveKeyCandidates(string moveId)
    {
        yield return moveId;
        string stripped = moveId.Replace("_MOVE", "");
        if (stripped.Length > 0 && stripped != moveId) yield return stripped;
    }

    // 取一条本地化文本；不存在或取不到时返回 null（区分"空字符串"和"没有这条"）
    private static string? LocalizedText(string table, string key)
    {
        try
        {
            var loc = new LocString(table, key);
            if (!loc.Exists()) return null;
            string text = loc.GetFormattedText();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    // 招式卡的卡面描述覆盖：cards 表的
    //   SELECT_SPECIAL_CARD_MOD_MOVE_{怪物种类}_{招式}.description
    // 没写就返回 null，由调用方按观测/静态表自动拼（默认行为：数字实时变化）。
    // 写了就以作者写的为准（静态文本，数字不会再变）。
    internal static string? MoveDescriptionOverride(MonsterMoveCard card)
    {
        string monsterId = card.MonsterKind;
        string moveId = card.SourceMove?.Id ?? "";
        if (string.IsNullOrEmpty(monsterId) || string.IsNullOrEmpty(moveId)) return null;

        foreach (var key in MoveKeyCandidates(moveId))
        {
            string? text = LocalizedText("cards",
                "SELECT_SPECIAL_CARD_MOD_MOVE_" + monsterId + "_" + key + ".description");
            if (text != null) return text;
        }
        return null;
    }

    // 意图类型 → 本地化键后缀（文案正文在 localization/zhs/cards.json 里）
    private static string IntentKeySuffix(IntentType type) => type switch
    {
        IntentType.Attack => "ATTACK",
        IntentType.DeathBlow => "DEATH_BLOW",
        IntentType.Defend => "DEFEND",
        IntentType.Buff => "BUFF",
        IntentType.Debuff => "DEBUFF",
        IntentType.DebuffStrong => "DEBUFF_STRONG",
        IntentType.Heal => "HEAL",
        IntentType.Summon => "SUMMON",
        IntentType.StatusCard => "STATUS",
        IntentType.CardDebuff => "CARD_DEBUFF",
        IntentType.Escape => "ESCAPE",
        IntentType.Stun => "STUN",
        IntentType.Sleep => "SLEEP",
        IntentType.Hidden => "HIDDEN",
        _ => "",
    };

    // 意图短名（卡名兜底用）
    private static string IntentShortName(AbstractIntent intent)
    {
        string suffix = IntentKeySuffix(intent.IntentType);
        return suffix.Length > 0
            ? MonsterText.OrDefault(MonsterText.IntentNamePrefix + suffix, "")
            : "";
    }

    // 招式卡卡图：取该招式主意图的图标（攻击意图按当前伤害选对应分档），
    // 全部用游戏自带素材，不需要新增资源。
    private static string MoveArtPath(Creature enemy, IReadOnlyList<AbstractIntent>? intents, Creature[] targets)
    {
        var primary = intents?.FirstOrDefault();
        if (primary == null) return "";
        try
        {
            var paths = primary.AssetPaths?.ToList();
            if (paths == null || paths.Count == 0) return "";
            if (primary is AttackIntent atk && paths.Count >= 5)
            {
                int total = atk.GetTotalDamage(targets, enemy);
                int index = total < 5 ? 1 : total < 10 ? 2 : total < 20 ? 3 : total >= 40 ? 5 : 4;
                return paths[index - 1];
            }
            return paths[0];
        }
        catch
        {
            return "";
        }
    }

    // 有序招式序列（牌堆脚本层的初始顺序）：从当前招式起，沿 FollowUp / 分支的声明顺序做深度优先遍历，
    // 每个招式只收一次。这是「不洗牌」的具体体现——线性/循环怪的顺序与原版脚本一致，随机分支按声明顺序展开。
    public static List<MoveState> OrderedMoves(Creature? enemy)
    {
        var result = new List<MoveState>();
        var machine = enemy?.Monster?.MoveStateMachine;
        MonsterState? start = enemy?.Monster?.NextMove;
        if (machine == null || start == null) return result;

        // 眩晕：未注册进状态机的 STUNNED 状态——只返回它自己（头顶显示「被击晕」），
        // 不能回退成全招式，否则会把 boss 的整条脚本解锁。
        if (start is MoveState stun && stun.Id == MonsterModel.stunnedMoveId)
            return new List<MoveState> { stun };

        var visited = new HashSet<MonsterState>();
        Walk(start);
        return result.Count > 0 ? result : AllMoves(machine);

        void Walk(MonsterState state)
        {
            if (state == null || !visited.Add(state)) return;
            if (state is MoveState move) result.Add(move);
            foreach (var nextId in NextStateIds(state))
            {
                if (string.IsNullOrEmpty(nextId)) continue;
                if (!machine.States.TryGetValue(nextId, out var next)) continue;
                Walk(next);
            }
        }
    }

    // 当前阶段可达的招式集合：从怪物「下一次要用的招式」出发，沿 FollowUpState / 分支状态 BFS。
    // 阶段切换（boss 半血时 ForceCurrentState 跳到新链）会改变这个集合，
    // 因此卡池只含本阶段该有的招式——不会一开场就把三阶段的大招抽出来。
    public static List<MoveState> ReachableMoves(Creature? enemy)
    {
        var result = new List<MoveState>();
        var machine = enemy?.Monster?.MoveStateMachine;
        MonsterState? start = enemy?.Monster?.NextMove;
        if (machine == null) return result;

        // 眩晕：STUNNED 状态未注册进状态机——只返回它自己，避免回退成全招式
        if (start is MoveState stun && stun.Id == MonsterModel.stunnedMoveId)
            return new List<MoveState> { stun };

        // 兜底：NextMove 缺失或不在当前状态机里（ResetStateMachine 整表重建后 NextMove 可能
        // 仍指向旧状态机），BFS 会走不出任何招式——退回全量招式，宁可多给几张也不要空牌池。
        if (start == null || !machine.States.ContainsKey(start.Id)) return AllMoves(machine);

        var visited = new HashSet<MonsterState>();
        var queue = new Queue<MonsterState>();
        visited.Add(start);
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            if (state is MoveState move && !result.Contains(move)) result.Add(move);
            foreach (var nextId in NextStateIds(state))
            {
                if (string.IsNullOrEmpty(nextId)) continue;
                if (!machine.States.TryGetValue(nextId, out var next)) continue;
                if (!visited.Add(next)) continue;
                queue.Enqueue(next);
            }
        }
        // BFS 没走到任何招式（入口状态不是 MoveState 且无后续）：同样退回全量
        return result.Count > 0 ? result : AllMoves(machine);
    }

    // 全量招式（状态机里所有 MoveState）：仅作兜底
    private static List<MoveState> AllMoves(MonsterMoveStateMachine machine)
    {
        var result = new List<MoveState>();
        foreach (var state in machine.States.Values)
            if (state is MoveState move) result.Add(move);
        return result;
    }

    // 某状态的后续状态 id：MoveState 走 FollowUpState；随机/条件分支枚举其分支表
    private static IEnumerable<string?> NextStateIds(MonsterState state)
    {
        switch (state)
        {
            case MoveState move:
                yield return move.FollowUpState?.Id ?? move.FollowUpStateId;
                break;
            case RandomBranchState random:
                foreach (var branch in random.States) yield return branch.stateId;
                break;
            case ConditionalBranchState conditional:
                foreach (var id in ConditionalBranchIds(conditional)) yield return id;
                break;
        }
    }

    // ConditionalBranchState 的分支表是私有字段（元素为私有 struct），只能反射读取
    private static IEnumerable<string?> ConditionalBranchIds(ConditionalBranchState state)
    {
        var field = typeof(ConditionalBranchState).GetField("States",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field?.GetValue(state) is not System.Collections.IEnumerable list) yield break;
        foreach (var item in list)
        {
            if (item == null) continue;
            var idField = item.GetType().GetField("id");
            yield return idField?.GetValue(item) as string;
        }
    }

    // 招式文案：各意图描述用「；」连接（攻击自行拼数值，其余取意图现成文案）
    private static string DescribeMove(IReadOnlyList<AbstractIntent> intents, Creature[] targets, Creature enemy)
    {
        var parts = new List<string>();
        foreach (var intent in intents)
        {
            string text = DescribeIntent(intent, targets, enemy);
            if (!string.IsNullOrWhiteSpace(text)) parts.Add(text);
        }
        return string.Join(MonsterText.IntentSeparator, parts);
    }

    // 单条意图文案：攻击意图的标签常只是纯数字，自行拼"造成X点伤害[N次]"
    private static string DescribeIntent(AbstractIntent intent, Creature[] targets, Creature enemy)
    {
        if (intent is AttackIntent attack)
        {
            int single = SafeSingleDamage(attack, targets, enemy);
            int repeats = Math.Max(1, attack.Repeats);
            return repeats > 1
                ? MonsterText.Of(MonsterText.DamageMulti, ("Damage", single.ToString()), ("Repeats", repeats.ToString()))
                : MonsterText.Of(MonsterText.Damage, ("Damage", single.ToString()));
        }
        return SafeLabel(intent, targets, enemy);
    }

    // 路线 A：右键面板的牌堆预览——只展示抽牌堆 → 弃牌堆（不含当前手牌，
    // 手牌已经在怪物头顶展示，重复显示会与"当前意图"混淆）。
    public static List<CardModel> BuildDeckPreview(Creature? enemy)
    {
        var result = new List<CardModel>();
        if (enemy == null) return result;
        result.AddRange(MonsterDeck.DrawOf(enemy));
        result.AddRange(MonsterDeck.DiscardOf(enemy));
        return result;
    }

    // 意图卡池（右键敌人查看）。分两段：
    //   前段 = **严格抽牌队列**：废牌池（按塞入顺序）→ 本手原版卡（按意图顺序）。
    //          队列最前面的 N 张（N = 本手卡数）就是本回合抽到的手牌，顺序与头顶显示一致。
    //   后段 = 其余招式意图（仅展示，不参与本轮抽取）。
    public static List<CardModel> BuildPool(Creature? enemy, Player player)
    {
        var result = new List<CardModel>();
        if (enemy == null || player.Creature?.CombatState == null) return result;

        // 前段：严格队列——顺序必须与 RebuildCards 的抽牌顺序保持一致
        foreach (var kind in IntentCardTheft.SnapshotJunk(enemy))
            result.Add(MonsterDisplayCard.CreateJunkCard(player, kind));
        result.AddRange(BuildIntentCards(enemy, player));

        // 后段：其余招式意图
        result.AddRange(BuildRepertoire(enemy, player));
        return result;
    }

    // 其余招式意图：该怪所有招式里、当前这一手未出现过的意图（按内容去重）。仅作展示。
    private static List<CardModel> BuildRepertoire(Creature enemy, Player player)
    {
        var result = new List<CardModel>();
        var machine = enemy.Monster?.MoveStateMachine;
        if (machine == null) return result;

        var targets = new[] { player.Creature };
        var seen = new HashSet<string>();
        var current = enemy.Monster?.NextMove?.Intents;
        if (current != null)
        {
            foreach (var intent in current) seen.Add(IntentKey(intent, targets, enemy));
        }

        foreach (var state in machine.States.Values)
        {
            if (state is not MoveState move) continue;
            foreach (var intent in move.Intents)
            {
                if (!seen.Add(IntentKey(intent, targets, enemy))) continue;
                result.AddRange(BuildIntentCards(new[] { intent }, enemy, player));
            }
        }
        return result;
    }

    // 意图内容指纹：类型 + 段数 + 伤害（攻击）/ 动作文案（非攻击）。用于招式间去重。
    private static string IntentKey(AbstractIntent intent, Creature[] targets, Creature owner)
    {
        var key = new System.Text.StringBuilder(intent.GetType().Name);
        if (intent is AttackIntent attack)
            key.Append(':').Append(Math.Max(1, attack.Repeats)).Append('x').Append(SafeSingleDamage(attack, targets, owner));
        else
            key.Append(':').Append(SafeLabel(intent, targets, owner));
        return key.ToString();
    }

    // 内容签名：意图类型 + 现成意图文案（已含数值）。意图有变化文案必然变化。
    public static string BuildIntentSignature(IReadOnlyList<AbstractIntent> intents, Player? player, Creature? enemy)
    {
        var sb = new System.Text.StringBuilder();
        var targets = player?.Creature;
        foreach (var intent in intents)
        {
            sb.Append(intent.GetType().Name).Append(':');
            if (intent is AttackIntent attack && targets != null && enemy != null)
            {
                sb.Append(Math.Max(1, attack.Repeats)).Append('x')
                  .Append(SafeSingleDamage(attack, new[] { targets }, enemy));
            }
            sb.Append('|');
        }
        return sb.ToString();
    }

    private static CardModel CreateAttack(Player player, int damage, int repeats)
    {
        var card = player.Creature.CombatState!.CreateCard<MonsterAttackCard>(player);
        card.IntentDamage = damage;
        card.Repeats = repeats;
        return card;
    }

    // 平均拆成 3 份（余数给前面的卡），0 份去掉：伤害 11 → 4/4/3；5 → 2/2/1；1 → 1；段数 8 → 3/3/2。
    // 唯一事实源：MonsterAttackSplitPatch（实际攻击拆分）也引用本方法，保证卡面份数与攻击次数严格一致。
    internal static List<int> SplitDamage(int value)
    {
        int q = value / 3, r = value % 3;
        var parts = new List<int>(3);
        for (int i = 0; i < 3; i++)
        {
            int part = q + (i < r ? 1 : 0);
            if (part > 0) parts.Add(part);
        }
        return parts;
    }

    // 非攻击意图 → 按意图类型映射到怪物卡池的对应模板卡（卡名/卡型各不相同，
    // 图鉴里各是一张独立的卡），动作文本由 hover 描述注入。
    private static CardModel TextCard(Player player, AbstractIntent intent, Creature[] targets, Creature enemy)
    {
        CardModel card = intent.IntentType switch
        {
            IntentType.Defend => Create<MonsterDefendCard>(player),
            IntentType.Buff => Create<MonsterBuffCard>(player),
            IntentType.Debuff => Create<MonsterDebuffCard>(player),
            IntentType.DebuffStrong => Create<MonsterDebuffCard>(player),
            IntentType.Heal => Create<MonsterHealCard>(player),
            IntentType.Summon => Create<MonsterSummonCard>(player),
            IntentType.StatusCard => Create<MonsterStatusCard>(player),
            IntentType.CardDebuff => Create<MonsterCardDebuffCard>(player),
            IntentType.DeathBlow => Create<MonsterDeathBlowCard>(player),
            _ => Create<MonsterUnknownCard>(player),
        };
        // 处决卡：记录处决意图的伤害数值（DeathBlowIntent 是单段攻击意图，
        // 打出的副本卡按该数值对敌人造成伤害后自毁）
        if (card is MonsterDeathBlowCard deathBlow && intent is DeathBlowIntent deathBlowIntent)
            deathBlow.IntentDamage = Math.Max(1, SafeSingleDamage(deathBlowIntent, targets, enemy));
        // 兜底卡记录原始意图类型——「强夺」按它区分逃跑（可空过）与其他未知类
        if (card is MonsterUnknownCard unknown) unknown.SourceType = intent.IntentType;
        ((MonsterDisplayCard)card).IntentText = SafeLabel(intent, targets, enemy);
        return card;
    }

    private static T Create<T>(Player player) where T : SelectSpecialBaseCard
        => player.Creature.CombatState!.CreateCard<T>(player);

    // 「强夺」：复制一张意图展示卡（同类型同内容），供玩家加入手牌打出。
    // 动态属性（伤害/段数/动作文本）逐项搬运——展示卡的效果数据都在实例属性上。
    // 注意：CreateCard 只接受模板类型（内部取注册单例克隆），把已是 mutable 的展示卡
    // 实例直接传进去会抛 MutableModelException——必须按运行时类型走泛型重载。
    internal static CardModel CreateCopy(Player player, CardModel source)
    {
        var combat = player.Creature.CombatState!;
        CardModel copy = source switch
        {
            MonsterAttackCard => combat.CreateCard<MonsterAttackCard>(player),
            MonsterDefendCard => combat.CreateCard<MonsterDefendCard>(player),
            MonsterBuffCard => combat.CreateCard<MonsterBuffCard>(player),
            MonsterDebuffCard => combat.CreateCard<MonsterDebuffCard>(player),
            MonsterHealCard => combat.CreateCard<MonsterHealCard>(player),
            MonsterSummonCard => combat.CreateCard<MonsterSummonCard>(player),
            MonsterStatusCard => combat.CreateCard<MonsterStatusCard>(player),
            MonsterCardDebuffCard => combat.CreateCard<MonsterCardDebuffCard>(player),
            MonsterDeathBlowCard => combat.CreateCard<MonsterDeathBlowCard>(player),
            MonsterUnknownCard => combat.CreateCard<MonsterUnknownCard>(player),
            MonsterMoveCard => combat.CreateCard<MonsterMoveCard>(player),
            _ => throw new InvalidOperationException($"未知的意图展示卡类型: {source.GetType().Name}"),
        };
        if (copy is MonsterMoveCard move && source is MonsterMoveCard moveSrc)
        {
            move.InitMove(moveSrc.SourceMove, moveSrc.Intents);
            move.CardTitle = moveSrc.CardTitle;
            move.ArtPath = moveSrc.ArtPath;
            move.MonsterKind = moveSrc.MonsterKind;
            // 人工覆盖表写了卡名就用它（玩家版卡面）
            var manual = MonsterManualTable.Lookup(moveSrc.MonsterKind, moveSrc.SourceMove?.Id ?? "");
            if (manual != null)
            {
                string title = MonsterManualTable.TitleOf(manual);
                if (!string.IsNullOrEmpty(title)) move.CardTitle = title;
            }
            // 副本按「玩家自己的攻击牌」显示：伤害实时按玩家侧加成算（与打出时同一套算法）。
            move.LiveText = () => DescribeMovePlayerSide(move);
        }
        if (copy is MonsterAttackCard atk && source is MonsterAttackCard src)
        {
            atk.IntentDamage = src.IntentDamage;
            atk.Repeats = src.Repeats;
        }
        // 处决副本卡：伤害数值同样要搬运，否则打出时按默认值结算
        if (copy is MonsterDeathBlowCard dbl && source is MonsterDeathBlowCard dbs)
            dbl.IntentDamage = dbs.IntentDamage;
        if (copy is MonsterDisplayCard dst && source is MonsterDisplayCard stc)
            dst.IntentText = stc.IntentText;
        return copy;
    }

    // 单段伤害：GetSingleDamage 内部跑 ModifyDamage hook（力量等修正），与意图图标数值一致
    private static int SafeSingleDamage(AttackIntent attack, Creature[] targets, Creature owner)
    {
        try
        {
            return attack.GetSingleDamage(targets, owner);
        }
        catch
        {
            return 0;
        }
    }

    // 意图动作文案：优先取 hover 提示的完整描述——原版意图图标的 label 本来就常是短文本
    // 或纯数字（塞状态牌意图只显示"2"，完整句子"将2张状态牌加入你的牌堆"在描述里，hover 才可见）。
    // 失败退化为图标短文本，再按意图类型兜底。
    private static string SafeLabel(AbstractIntent intent, Creature[] targets, Creature owner)
    {
        try
        {
            var tip = intent.GetHoverTip(targets, owner);
            if (!string.IsNullOrWhiteSpace(tip.Description)) return tip.Description;
        }
        catch
        {
            // hover 提示失败走 label
        }
        try
        {
            var text = intent.GetIntentLabel(targets, owner)?.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        catch
        {
            // 格式化失败走类型兜底
        }
        // 兜底文案也走本地化（键在 localization/zhs/cards.json）；连键都没有时退回意图类型名
        string suffix = IntentKeySuffix(intent.IntentType);
        return suffix.Length > 0
            ? MonsterText.OrDefault(MonsterText.IntentLabelPrefix + suffix, intent.IntentType.ToString())
            : intent.IntentType.ToString();
    }
}
