using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「异解」除外堆供货袋：战斗开始时把全部异解本家卡与随机填充卡洗匀成出货序列，
// 异解卡的所有"向除外堆加入随机卡"效果改为从袋中弹出，保证整场战斗异解卡的密度稳定（约10%）。
//
// - 每个玩家一个独立队列（生成卡 owner = 该玩家）；
// - 按战斗 ID 区分：换战斗自动重洗；
// - 袋抽干时自动重洗新袋（长战不断供）；
// - 洗匀/弹出全部使用 RunState.Rng.Niche（多人种子同步）。
public static class YiJieExileBag
{
    // 异解密度 ≈ 1 / (1 + FillerPerYiJieCard) = 10%
    private const int FillerPerYiJieCard = 9;

    private sealed record BagRecord(CombatId? Combat, Queue<CardModel> Bag);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Player, BagRecord> Bags = new();

    // 取当前玩家当前战斗的出货队列（不存在/换战斗则重洗）
    public static Queue<CardModel> GetBag(Player player)
    {
        var combatId = CombatManager.Instance.CurrentCombatId;
        if (Bags.TryGetValue(player, out var entry) && entry.Combat == combatId)
            return entry.Bag;

        var bag = BuildBag(player);
        Bags.Remove(player);
        Bags.Add(player, new BagRecord(combatId, bag));
        return bag;
    }

    // 构建出货序列：全部异解本家卡各 1 份 + 随机填充卡（排除异解本家，保证密度精确），洗匀
    private static Queue<CardModel> BuildBag(Player player)
    {
        bool IsYiJie(CardModel c)
            => c.Tags.Contains(SelectTags.YiJie)
            || c.Tags.Contains(SelectTags.YiJieGuaiShou)
            || c.Tags.Contains(SelectTags.YiJieMoXian);

        var yiJie = ModelDb.AllCards.Where(c => IsYiJie(c) && CardAvailability.IsAllowed(c, player.RunState)).ToList();
        // 填充池排除不可获取卡（Token 衍生卡/图鉴隐藏机制卡/怪物卡模板，判据统一在 SelectToolCmd）
        var fillerPool = ModelDb.AllCards.Where(c => !IsYiJie(c) && !SelectToolCmd.IsNonObtainable(c)
            && CardAvailability.IsAllowed(c, player.RunState)).ToList();

        var seq = new List<CardModel>(yiJie);
        int filler = yiJie.Count * FillerPerYiJieCard;
        for (int i = 0; i < filler && fillerPool.Count > 0; i++)
        {
            var pick = player.RunState.Rng.Niche.NextItem(fillerPool);
            if (pick != null) seq.Add(pick);
        }

        // Fisher–Yates 洗匀
        for (int i = seq.Count - 1; i > 0; i--)
        {
            int j = player.RunState.Rng.Niche.NextInt(i + 1);
            (seq[i], seq[j]) = (seq[j], seq[i]);
        }
        return new Queue<CardModel>(seq);
    }

    // 弹出 amount 张卡牌定义（袋空自动重洗新袋，长战不断供）
    public static List<CardModel> DrawDefinitions(Player player, int amount)
    {
        var bag = GetBag(player);
        var outDefs = new List<CardModel>();
        while (outDefs.Count < amount)
        {
            if (bag.Count == 0)
            {
                bag = BuildBag(player);
                Bags.Remove(player);
                Bags.Add(player, new BagRecord(CombatManager.Instance.CurrentCombatId, bag));
                if (bag.Count == 0) break;
            }
            outDefs.Add(bag.Dequeue());
        }
        return outDefs;
    }

    // 向除外堆加入 amount 张卡：从出货袋弹出的定义生成战斗副本并入堆。
    // 供所有异解卡的"向除外堆加入随机卡"效果调用（替代纯随机入堆）。
    public static async Task<IReadOnlyList<CardPileAddResult>> DrawToChuWai(Player player, int amount, float previewTime = 2f)
    {
        if (amount <= 0) return Array.Empty<CardPileAddResult>();

        var defs = DrawDefinitions(player, amount);
        if (defs.Count == 0) return Array.Empty<CardPileAddResult>();

        return await SelectToolCmd.AddGeneratedCopiesToPile(player, defs, Entry.ChuWaiPile, previewTime);
    }
}
