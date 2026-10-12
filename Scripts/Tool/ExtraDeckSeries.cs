using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Tool;

// 额外堆的"应有内容"与补齐规则。
//
// 应有内容 = ① 局外选好的初始额外卡组 + ② 系列补给：
//   · 拥有珠泪系列的卡 → 每个珠泪额外怪兽按"拥有张数"补（上限 3）
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
        IsSeriesCard = c => c.Tags.Contains(SelectTags.ZhuLei),
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

    private static readonly Series[] AllSeries = [ZhuLeiSeries, CanHuanSeries];

    public static void Register()
    {
        // 战斗开始：满编（保证每场战斗都是全新的一副额外卡组）
        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(evt => Settle(evt.RunState));
        // 战斗结束：恢复原样（胜负都补一次，逃跑也走这里）
        RitsuLibFramework.SubscribeLifecycle<CombatEndedEvent>(evt => Settle(evt.RunState));
        RitsuLibFramework.SubscribeLifecycle<CombatVictoryEvent>(evt => Settle(evt.RunState));
        // 进新房间：领奖励／商店买卡之后补（系列卡变多 → 应有内容变多）
        RitsuLibFramework.SubscribeLifecycle<RoomEnteredEvent>(evt => Settle(evt.RunState));
    }

    // ── 供"从额外堆召唤"使用 ──

    /// <summary>额外堆里的全部卡（按堆内顺序）。</summary>
    public static List<CardModel> Cards(Player player)
        => Entry.ExtraPile.GetPile(player).Cards.ToList();

    /// <summary>在额外堆里找指定类型的一张（没有则 null）。</summary>
    public static CardModel? FindByType(Player player, Type type)
        => Cards(player).FirstOrDefault(c => c.GetType() == type);

    /// <summary>把用掉的那张额外怪兽移出额外堆（进不显示界面的「额外堆·已用」，战斗结束随战斗销毁）。</summary>
    public static async Task Consume(Player player, CardModel extraCard)
    {
        // 静默：「额外堆·已用」是无界面牌堆，没有可展示的目的地，飞一次反而让人看不懂。
        // 真正该看的演出是"召唤出来的副本飞进手牌"，那一步由调用方负责。
        await CardPileCmd.Add(extraCard, Entry.ExtraUsedPile, CardPilePosition.Bottom, null, skipVisuals: true);
    }

    /// <summary>
    /// 「额外怪兽 → 额外堆」的返回（方案 A）：
    ///   · 持久卡：从「额外堆·已用」里把**同类型**那张还回「额外堆」——这才是"返回额外堆"的实体，
    ///     玩家打开额外堆就能看到它回来了（静默，不需要额外演出）；
    ///   · 战斗副本：**退场**（移进「额外堆·已用」，隐藏且随战斗销毁），
    ///     并播一次**飞向额外堆**的演出，作为玩家唯一需要看到的反馈（观感＝"它回到额外卡组了"）。
    /// 这样额外堆里永远只会有持久卡，不会再出现"战斗副本被塞进持久牌堆 → 战斗结束变成废卡"的问题。
    /// </summary>
    public static async Task ReturnExtraMonster(Player player, CardModel combatCopy)
    {
        // ① 持久卡还原（静默）
        var usedPile = Entry.ExtraUsedPile.GetPile(player);
        var persistent = usedPile?.Cards.FirstOrDefault(c => c.GetType() == combatCopy.GetType());
        if (persistent != null)
        {
            await CardPileCmd.Add(persistent, Entry.ExtraPile, CardPilePosition.Bottom, null, skipVisuals: true);
        }

        // ② 战斗副本退场 + 唯一需要被看到的演出：飞向额外堆
        SelectToolCmd.PlayFlyToPile(combatCopy, Entry.ExtraPile);
        await CardPileCmd.Add(combatCopy, Entry.ExtraUsedPile, CardPilePosition.Bottom, null, skipVisuals: true);
    }

    /// <summary>把额外堆补齐到"应有内容"（只补不缺，不会删；幂等，可反复调用）。</summary>
    public static async Task EnsureFull(Player player)
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
                for (int i = 0; i < missing; i++)
                {
                    var card = player.RunState.CreateCard(canonical, player);
                    await CardPileCmd.Add(card, Entry.ExtraPile);
                }
                Entry.Logger.Info($"[ExtraDeckSeries] 额外堆补齐 {canonical.Title} ×{missing}（现有 {have}→{want}，上限 {MaxCopiesPerName}）");
            }
        }
        catch (Exception e)
        {
            Entry.Logger.Error($"[ExtraDeckSeries] 补齐额外堆失败：{e}");
        }
    }

    // ── 内部 ──

    private static void Settle(IRunState runState)
    {
        var me = LocalContext.GetMe(runState.Players);
        if (me == null) return;
        _ = EnsureFull(me);
    }

    // 应有内容（类型 → 张数）
    private static Dictionary<Type, int> Desired(Player player)
    {
        var want = new Dictionary<Type, int>();

        void Add(Type type, int count)
        {
            if (count <= 0) return;
            want.TryGetValue(type, out int current);
            want[type] = Math.Min(MaxCopiesPerName, current + count);
        }

        // ① 局外选好的初始额外卡组（每张 1 份）
        foreach (var key in ExtraDeckStore.Selected)
        {
            if (ExtraDeckStore.FindByKey(key) is { } card) Add(card.GetType(), 1);
        }

        // ② 系列补给：拥有的系列卡有几张，就给每个该系列额外怪兽几份（上限 3）
        foreach (var series in AllSeries)
        {
            int owned = CountOwned(player, series);
            foreach (var type in series.ExtraMonsters) Add(type, owned);
        }
        return want;
    }

    // 玩家"拥有"的该系列卡张数：只数卡组里的（额外堆不算），并且排除额外怪兽本身
    private static int CountOwned(Player player, Series series)
        => PileType.Deck.GetPile(player).Cards
            .Where(c => !c.Tags.Contains(SelectTags.ExtraMonster))
            .Count(series.IsSeriesCard);
}
