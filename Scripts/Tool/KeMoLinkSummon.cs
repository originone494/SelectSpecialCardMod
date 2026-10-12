using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「连接召唤」的素材选择工具：逐张选 + 动态筛选（和大圣棺的融合素材选择同一套思路）。
//
// 规则（游戏王连接召唤的口径）：
//   · 每张卡的"连接值"＝带 Link 标签的取它的标记数（Link1=1 / Link2=2 / Link3=3…），其他卡＝1；
//   · 需要凑出「连接值合计 = 目标等级」；且**至少要 2 张卡**（1 张卡凑满 = 那张卡自己的等级，
//     不构成召唤，所以"连接值 = 目标等级"的单卡一开始就被排除在候选之外）。
//   例：Link3 → 3 张普通（1+1+1），或 1 张 Link2 + 1 张普通（2+1）；单独的 Link3 不算。
public static class KeMoLinkSummon
{
    /// <summary>一张卡算几个素材。</summary>
    public static int MaterialValue(CardModel card)
    {
        if (card.Tags.Contains(SelectTags.Link3)) return 3;
        if (card.Tags.Contains(SelectTags.Link2)) return 2;
        if (card.Tags.Contains(SelectTags.Link1)) return 1;
        return 1;
    }

    /// <summary>
    /// 这些卡能不能凑出：连接值合计 = targetValue、张数 ≥ minCards、
    /// 且 requireGuangEMo 时选中的卡里至少 1 张「光恶魔」（"包含光恶魔"的口径）。
    /// </summary>
    public static bool CanPay(IReadOnlyList<CardModel> pool, int targetValue, int minCards = 2, bool requireGuangEMo = false)
        => Feasible(pool, targetValue, minCards, requireGuangEMo);

    /// <summary>
    /// 逐张选素材：每一步只给"选它之后剩下的连接值仍然凑得齐"的卡；已选过的卡不再出现。
    /// 取消（返回 null）表示放弃本次召唤。
    /// </summary>
    public static async Task<List<CardModel>?> SelectMaterial(
        PlayerChoiceContext ctx, Player player, IReadOnlyList<CardModel> pool,
        int targetValue, int minCards, bool requireGuangEMo, LocString prompt, CardModel source)
    {
        var chosen = new List<CardModel>();
        int remaining = targetValue;
        int minRemaining = minCards;
        bool guangOk = !requireGuangEMo;

        while (remaining > 0 || minRemaining > 0)
        {
            var available = pool.Where(c => !chosen.Contains(c)).ToList();
            var allowed = available.Where(c =>
            {
                int v = MaterialValue(c);
                if (v > remaining) return false;
                bool nextGuang = guangOk || IsGuangEMo(c);
                var rest = available.Where(x => x != c).ToList();
                return Feasible(rest, remaining - v, minRemaining - 1, !nextGuang);
            }).ToList();
            if (allowed.Count == 0) return null;

            var p = new LocString(prompt.LocTable, prompt.LocEntryKey);
            p.Add("CardName", source.Title);
            // RequireManualConfirmation：候选只剩 1 张时也必须玩家自己点（游戏默认会自动选）
            var picked = (await CardSelectCmd.FromSimpleGrid(
                ctx, allowed, player,
                new CardSelectorPrefs(p, 1, 1) { RequireManualConfirmation = true })).FirstOrDefault();
            if (picked == null) return null;

            chosen.Add(picked);
            remaining -= MaterialValue(picked);
            minRemaining = Math.Max(0, minRemaining - 1);
            if (IsGuangEMo(picked)) guangOk = true;
        }

        return chosen;
    }

    private static bool IsGuangEMo(CardModel card) => card.Tags.Contains(SelectTags.GuangEMo);

    // 回溯：池里各取最多一次，能不能凑出「合计 = target、张数 ≥ minCards、needGuang 时至少 1 张光恶魔」
    private static bool Feasible(IReadOnlyList<CardModel> pool, int target, int minCards, bool needGuang)
    {
        if (target <= 0) return target == 0 && minCards <= 0 && !needGuang;

        bool Search(int i, int sum, int count, bool guangOk)
        {
            if (sum == target && count >= minCards && guangOk) return true;
            if (i >= pool.Count || sum > target) return false;

            int v = MaterialValue(pool[i]);
            bool g = IsGuangEMo(pool[i]);
            if (sum + v <= target && Search(i + 1, sum + v, count + 1, guangOk || g)) return true;
            return Search(i + 1, sum, count, guangOk);
        }

        return Search(0, 0, 0, !needGuang);
    }
}
