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

// 连接怪兽可以按 1 或自身连接等级计数；每张实体只能占一个素材位。
public static class KeMoLinkSummon
{
    /// <summary>连接等级计数的最大值；非连接怪兽为 1。</summary>
    public static int MaterialValue(CardModel card) => Math.Max(1, KeMoEquip.LinkRating(card));

    private static IEnumerable<int> MaterialValues(CardModel card)
    {
        yield return 1;
        int rating = MaterialValue(card);
        if (rating > 1) yield return rating;
    }

    private static LocString CountAsLinkPrompt { get; } =
        new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_LINK_MATERIAL_VALUE");

    public static bool CanPay(IReadOnlyList<CardModel> pool, int targetValue, int minCards = 2, bool requireGuangEMo = false)
        => Feasible(pool.Distinct().ToList(), targetValue, minCards, requireGuangEMo);

    /// <summary>逐张选择；两种计数都能完成召唤时，让玩家明确选择计数方式。取消不支付素材。</summary>
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
            var available = pool.Distinct().Where(c => !chosen.Contains(c)).ToList();
            var valuesFor = new Dictionary<CardModel, List<int>>();
            foreach (var card in available)
            {
                var rest = available.Where(c => c != card).ToList();
                bool nextGuang = guangOk || IsGuangEMo(card);
                var values = MaterialValues(card).Where(v => v <= remaining
                    && Feasible(rest, remaining - v, minRemaining - 1, !nextGuang)).ToList();
                if (values.Count > 0) valuesFor[card] = values;
            }
            if (valuesFor.Count == 0) return null;

            var p = new LocString(prompt.LocTable, prompt.LocEntryKey);
            p.Add("CardName", source.Title);
            var picked = (await CardSelectCmd.FromSimpleGrid(
                ctx, valuesFor.Keys.ToList(), player,
                new CardSelectorPrefs(p, 0, 1) { RequireManualConfirmation = true })).FirstOrDefault();
            if (picked == null) return null;

            var validValues = valuesFor[picked];
            int value = validValues[0];
            if (validValues.Count > 1)
            {
                var countPrompt = new LocString(CountAsLinkPrompt.LocTable, CountAsLinkPrompt.LocEntryKey);
                countPrompt.Add("MaterialName", picked.Title);
                countPrompt.Add("LinkRating", MaterialValue(picked));
                bool useRating = await SelectToolCmd.AskYesNo(ctx, player, countPrompt, source);
                value = useRating ? MaterialValue(picked) : 1;
            }

            chosen.Add(picked);
            remaining -= value;
            minRemaining = Math.Max(0, minRemaining - 1);
            if (IsGuangEMo(picked)) guangOk = true;
        }
        return guangOk ? chosen : null;
    }

    private static bool IsGuangEMo(CardModel card) => card.Tags.Contains(SelectTags.GuangEMo);

    private static bool Feasible(IReadOnlyList<CardModel> pool, int target, int minCards, bool needGuang)
    {
        if (target <= 0) return target == 0 && minCards <= 0 && !needGuang;
        var visited = new HashSet<(int Index, int Sum, int Count, bool Guang)>();

        bool Search(int i, int sum, int count, bool guangOk)
        {
            if (sum == target && count >= minCards && guangOk) return true;
            if (i >= pool.Count || sum >= target) return false;
            if (!visited.Add((i, sum, Math.Min(count, Math.Max(0, minCards)), guangOk))) return false;

            foreach (int value in MaterialValues(pool[i]))
            {
                if (sum + value <= target
                    && Search(i + 1, sum + value, count + 1, guangOk || IsGuangEMo(pool[i]))) return true;
            }
            return Search(i + 1, sum, count, guangOk);
        }
        return Search(0, 0, 0, !needGuang);
    }
}
