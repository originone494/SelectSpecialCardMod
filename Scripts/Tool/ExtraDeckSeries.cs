using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Tool;

// 额外堆的"应有内容"与补齐规则。
//
// 应有内容 = ① 局外选好的初始额外卡组 + ② 系列补给：
//   · 拥有珠泪系列的卡（含支援卡「珍珠世界」）→ 每个珠泪额外怪兽按"拥有张数"补（上限 3）
//   · 拥有天杯龙／灿幻系列的卡 → 每个灿幻额外怪兽按"拥有张数"补（上限 3）
//   额外堆内**同名卡不超过 3 张**。
//
// 战斗中的消耗与恢复：
//   · 融合／同调从额外堆取卡时，那张副本会被移到「额外堆·已用」（不显示界面的战斗内牌堆），
//     所以战斗中的额外堆数量会真的减少；
//   · 战斗结束（以及进入新房间、战斗开始、开局）按上面的"应有内容"补齐 —— 少的补上、
//     多的不动，所以战斗结束后就恢复成原来的样子。
//
// 「拥有」只数卡组里的系列卡（额外堆不算），并排除额外怪兽本身：额外怪兽不该进卡组，
// 而且召唤出来的怪兽是战斗副本（战斗结束消失），不会污染计数。
public static class ExtraDeckSeries
{
    /// <summary>额外堆内同名卡上限。</summary>
    public const int MaxCopiesPerName = 3;

    private sealed class Series
    {
        public required string Key;
        public required Func<CardModel, bool> IsSeriesCard;   // 属于该系列（额外怪兽除外）
        public required List<Type> ExtraMonsters;
    }

    private static readonly Series ZhuLeiSeries = new()
    {
        Key = "珠泪",
        // 珍珠世界没有「珠泪」标签，但它是检索珠泪怪兽的系列入口。
        // 仅将其纳入补给；不添加标签，避免改变检索候选与打出珠泪卡的伤害判定。
        IsSeriesCard = c => c.Tags.Contains(SelectTags.ZhuLei) || c is ZhenZhuShiJieCard,
        ExtraMonsters =
        [
            typeof(ZhuLeiShuiXianCard),
            typeof(ZhuLeiKaLeiDuoHaTeCard),
            typeof(ZhuLeiLuShaKaCard),
        ],
    };

    private static readonly Series CanHuanSeries = new()
    {
        Key = "天杯龙/灿幻",
        IsSeriesCard = c => c.Tags.Contains(SelectTags.TianBeiLong)
                            || c.Tags.Contains(SelectTags.CanHuanMoFa)
                            || c.Tags.Contains(SelectTags.CanHuanGuaiShou),
        ExtraMonsters =
        [
            typeof(CanHuanShengLongShuangChaTianLongCard),
            typeof(CanHuanChaoLongSanJiTianLongCard),
        ],
    };

    private static readonly Series YiXiangMingSeries = new()
    {
        Key = "异响鸣",
        IsSeriesCard = c => c.Tags.Contains(SelectTags.YiXiangMing),
        ExtraMonsters =
        [
            typeof(YiXiangMingYiShenGuangYaoTianShiCard),
            typeof(YiXiangMingShenYiFengBaoEMoCard),
        ],
    };

    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        foreach (var series in new[] { ZhuLeiSeries, CanHuanSeries, YiXiangMingSeries })
            SeriesRegistry.Register(new ExtraDeckSeriesRule(series.Key, series.IsSeriesCard, series.ExtraMonsters));
        _registered = true;
        // RunSetup hooks await reconciliation on all peers, rather than local fire-and-forget callbacks.
    }

    // ── 供"从额外堆召唤"使用 ──

    /// <summary>额外堆里的全部卡（按堆内顺序）。</summary>
    public static List<CardModel> Cards(Player player)
        => FeatureStatus.Checked && !FeatureStatus.RunSetup ? [] : Entry.ExtraPile.GetPile(player).Cards.ToList();

    /// <summary>在额外堆里找指定类型的一张（没有则 null）。</summary>
    public static CardModel? FindByType(Player player, Type type)
        => Cards(player).FirstOrDefault(c => c.GetType() == type);

    /// <summary>把用掉的那张额外怪兽移出额外堆（进不显示界面的「额外堆·已用」，战斗结束随战斗销毁）。</summary>
    public static async Task Consume(Player player, CardModel extraCard)
    {
        // 静默：「额外堆·已用」是无界面牌堆，没有可展示的目的地，飞一次反而让人看不懂。
        // 真正该看的演出是"召唤出来的副本飞进手牌"，那一步由调用方负责。
        if (extraCard.Owner != player || extraCard.Pile?.Type != Entry.ExtraPile || extraCard.HasBeenRemovedFromState)
            throw new InvalidOperationException("Extra monster is no longer available.");
        var result = await CardPileCmd.Add(extraCard, Entry.ExtraUsedPile, CardPilePosition.Bottom, null, skipVisuals: true);
        if (!result.success) throw new InvalidOperationException("Could not consume extra monster.");
    }

    /// <summary>
    /// 「额外怪兽 → 额外堆」的返回（方案 A）：
    ///   · 持久卡：从「额外堆·已用」里把**同类型**那张还回「额外堆」——这才是"返回额外堆"的实体，
    ///     玩家打开额外堆就能看到它回来了（静默，不需要额外演出）；
    ///   · 战斗副本：**退场**（移进「额外堆·已用」，隐藏且随战斗销毁），
    ///     并播一次**飞向额外堆**的演出，作为玩家唯一需要看到的反馈（观感＝"它回到额外卡组了"）。
    /// 这样额外堆里永远只会有持久卡，不会再出现"战斗副本被塞进持久牌堆 → 战斗结束变成废卡"的问题。
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CardModel, CardModel> Origins = new();
    public static CardModel CreateSummonedCopy(Player player, CardModel persistent)
    {
        var combat = player.Creature.CombatState ?? throw new InvalidOperationException("No combat for summon.");
        var copy = combat.CloneCard(persistent);
        Origins.Add(copy, persistent);
        return copy;
    }
    public static async Task<bool> ReturnExtraMonster(Player player, CardModel combatCopy)
    {
        if (combatCopy.Owner != player || combatCopy.HasBeenRemovedFromState
            || combatCopy.Pile?.Type == Entry.ExtraUsedPile) return false;

        // ① 只恢复 RunState 实际拥有的持久卡；已用堆也包含退场的战斗副本。
        var usedPile = Entry.ExtraUsedPile.GetPile(player);
        Origins.TryGetValue(combatCopy, out var persistent);
        if (persistent == null)
            persistent = usedPile?.Cards.FirstOrDefault(c => c != combatCopy
                && c.GetType() == combatCopy.GetType() && player.RunState.ContainsCard(c)); // legacy/generated copy
        if (persistent?.Pile?.Type != Entry.ExtraUsedPile) persistent = null;
        if (persistent != null)
        {
            RestorePersistentExtraCard(player, persistent, Entry.ExtraPile.GetPile(player));
            if (persistent.Pile?.Type != Entry.ExtraPile) return false;
        }

        // ② 战斗副本退场 + 唯一需要被看到的演出：飞向额外堆
        SelectToolCmd.PlayFlyToPile(combatCopy, Entry.ExtraPile);
        var result = await CardPileCmd.Add(combatCopy, Entry.ExtraUsedPile, CardPilePosition.Bottom, null, skipVisuals: true);
        if (!result.success || combatCopy.Pile?.Type != Entry.ExtraUsedPile) return false;
        CompleteSilentExtraReturn(result);
        return true;
    }

    internal static void CompleteSilentExtraReturn(CardPileAddResult result)
    {
        if (!result.success || result.cardAdded.Pile?.Type != Entry.ExtraUsedPile) return;
        var oldPile = result.oldPile;
        if (oldPile == null || ReferenceEquals(result.cardAdded.Pile, oldPile)) return;

        // 静默移堆也抑制来源移除通知；飞行特效只是另一张卡面，不能清理手牌/Play的原节点。
        bool visibleSource = oldPile.Type is PileType.Hand or PileType.Play
            && LocalContext.IsMe(result.cardAdded.Owner);
        var node = visibleSource ? NCard.FindOnTable(result.cardAdded, oldPile.Type) : null;
        oldPile.InvokeCardRemoved(result.cardAdded);
        oldPile.InvokeCardRemoveFinished();
        oldPile.InvokeContentsChanged();

        if (!visibleSource) return;
        if (oldPile.Type == PileType.Hand && NPlayerHand.Instance is { } hand
            && hand.GetCardHolder(result.cardAdded) != null)
            hand.Remove(result.cardAdded);
        if (node != null && GodotObject.IsInstanceValid(node)) node.QueueFreeSafely();
    }

    internal static void RestorePersistentExtraCard(Player player, CardModel persistent, CardPile extraPile)
    {
        var usedPile = persistent.Pile;
        if (persistent.Owner != player || persistent.HasBeenRemovedFromState
            || !player.RunState.ContainsCard(persistent)
            || usedPile?.Type != Entry.ExtraUsedPile || extraPile.Type != Entry.ExtraPile
            || !ReferenceEquals(extraPile, Entry.ExtraPile.GetPile(player)))
            throw new InvalidOperationException("Cannot restore an unavailable persistent extra monster.");

        // 已用堆同时保存持久资产和战斗副本。持久资产没有加入 CombatState，
        // 原版 CardPileCmd.Add 会因来源堆属于战斗牌堆而拒绝它。
        // 这里只搬回 RunState 拥有的原实体；战斗副本仍走下面的原版入堆命令。
        // 同步完成账本转移并发送牌堆通知，不将持久资产注册成战斗卡，
        // 避免战斗结束时将它清理成 HasBeenRemovedFromState。
        usedPile.RemoveInternal(persistent);
        extraPile.AddInternal(persistent);
        extraPile.InvokeCardAddFinished();
    }

    /// <summary>把额外堆补齐到"应有内容"（只补不缺，不会删；幂等，可反复调用）。</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Player, SemaphoreSlim> Gates = new();
    public static async Task EnsureFull(Player player)
    {
        var gate = Gates.GetValue(player, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try { await EnsureFullCore(player); }
        finally { gate.Release(); }
    }
    private static async Task EnsureFullCore(Player player)
    {
        try
        {
            var pile = Entry.ExtraPile.GetPile(player);

            // 清掉历史遗留的"废卡"：战斗副本曾被塞进额外堆 → 战斗结束被标成 HasBeenRemovedFromState，
            // 之后入堆一律静默失败。这类卡没有任何用途，直接移出（静默）。
            foreach (var bad in pile.Cards.Where(c => c.HasBeenRemovedFromState).ToList())
            {
                Entry.Logger.Warn($"[ExtraDeckSeries] 清理额外堆里的废卡：{bad.Id.Entry}");
                bad.RemoveFromCurrentPile(silent: true);
            }

            foreach (var (type, want) in Desired(player))
            {
                int have = pile.Cards.Count(c => c.GetType() == type);
                int missing = want - have;
                if (missing <= 0) continue;

                var canonical = ModelDb.AllCards.FirstOrDefault(c => c.GetType() == type);
                if (canonical == null)
                {
                    Entry.Logger.Error($"[ExtraDeckSeries] 找不到卡牌模板：{type.Name}");
                    continue;
                }
                // 有界补齐：只补 missing 张。原来的 while（"堆里还差就再加一张"）一旦遇到
                // "命令返回成功但卡没进额外堆"，就会在进入房间的钩子里原地死循环。
                int added = 0;
                for (int i = 0; i < missing; i++)
                {
                    var card = player.RunState.CreateCard(canonical, player);
                    var result = await CardPileCmd.Add(card, Entry.ExtraPile);
                    if (!result.success) throw new InvalidOperationException($"Cannot replenish {card.Id}");
                    if (card.Pile?.Type != Entry.ExtraPile)
                    {
                        Entry.Logger.Error($"[ExtraDeckSeries] 额外堆补齐未落位：{card.Id.Entry}（实际去向 {card.Pile?.Type.ToString() ?? "none"}），停止本次补齐");
                        break;
                    }
                    added++;
                }
                Entry.Logger.Info($"[ExtraDeckSeries] 额外堆补齐 {canonical.Title} ×{added}（现有 {have}→{pile.Cards.Count(c => c.GetType() == type)}，上限 {MaxCopiesPerName}）");
            }
        }
        catch (Exception e)
        {
            Entry.Logger.Error($"[ExtraDeckSeries] 补齐额外堆失败：{e}");
            throw;
        }
    }

    // ── 内部 ──

    // 应有内容（类型 → 张数）
    private static Dictionary<Type, int> Desired(Player player)
    {
        var want = new Dictionary<Type, int>();

        void Add(Type type, int count)
        {
            if (count <= 0) return;
            if (!CardAvailability.IsAllowedType(type, player.RunState)) return;
            want.TryGetValue(type, out int current);
            want[type] = Math.Min(MaxCopiesPerName, current + count);
        }

        // ① 本局冻结的初始额外卡组，局外配置变化只作用于下一局。
        foreach (var key in ExtraDeckRunState.Initial(player))
        {
            if (ExtraDeckStore.FindByKey(key) is { } card) Add(card.GetType(), 1);
        }

        // ② 系列补给：拥有的系列卡有几张，就给每个该系列额外怪兽几份（上限 3）
        foreach (var series in SeriesRegistry.All)
        {
            int owned = CountOwned(player, series);
            foreach (var type in series.ExtraMonsters) Add(type, owned);
        }
        return want;
    }

    // 玩家"拥有"的该系列卡张数：只数卡组里的（额外堆不算），并且排除额外怪兽本身
    private static int CountOwned(Player player, ExtraDeckSeriesRule series)
        => PileType.Deck.GetPile(player).Cards
            .Where(c => !CardClassification.IsExtraMonster(c))
            .Count(series.Matches);
}
