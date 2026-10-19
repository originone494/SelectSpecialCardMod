using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using SelectSpecialCardMod.Scripts.Myth;

namespace SelectSpecialCardMod.Scripts.Tool;

// 明确指定的开局特殊奖励，不受“所有奖励”替换设置影响。
internal interface INormalSpecialCardReward { }
internal interface IMythCardReward { }

// 互斥奖励模式共用同一入口；旧存档若同时为true，仍沿用原来的随机模式。
internal static class CardRewardReplacement
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MegaCrit.Sts2.Core.Runs.CardCreationOptions, object> NormalOptions = new();
    internal static void KeepSpecialOptions(MegaCrit.Sts2.Core.Runs.CardCreationOptions options)
        => NormalOptions.GetValue(options, _ => new object());
    internal static bool KeepsSpecialOptions(MegaCrit.Sts2.Core.Runs.CardCreationOptions options)
        => NormalOptions.TryGetValue(options, out _);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MegaCrit.Sts2.Core.Runs.CardCreationOptions, object> MythOptions = new();
    internal static void KeepMythOptions(MegaCrit.Sts2.Core.Runs.CardCreationOptions options)
        => MythOptions.GetValue(options, _ => new object());
    internal static bool KeepsMythOptions(MegaCrit.Sts2.Core.Runs.CardCreationOptions options)
        => MythOptions.TryGetValue(options, out _);

    internal static List<CardCreationResult> Roll(Player player, int count, Rng rng, bool forceSpecial = false, bool forceMyth = false)
    {
        var results = new List<CardCreationResult>();
        var candidates = forceMyth || (!forceSpecial && CardAvailability.RandomizesCardRewards(player.RunState)) ? null : SpecialCardRewards.Candidates(player);
        var used = new HashSet<Type>();
        try
        {
            for (int i = 0; i < count; i++) results.Add(new CardCreationResult(Draw(player, rng, candidates, used)));
            return results;
        }
        catch
        {
            foreach (var result in results) CleanUp(result.Card);
            throw;
        }
    }

    internal static void Replace(Player player, IList<CardCreationResult> cards, Rng rng, bool forceSpecial = false, bool forceMyth = false)
    {
        var candidates = forceMyth || (!forceSpecial && CardAvailability.RandomizesCardRewards(player.RunState)) ? null : SpecialCardRewards.Candidates(player);
        if (candidates is { Count: 0 })
        {
            Entry.Logger.Warn("[CardRewards] No obtainable special cards; keeping the original reward.");
            return;
        }
        bool Accepts(CardModel card) => candidates == null ? MythCardRewards.IsFixed(card)
            : candidates.Any(c => c.GetType() == card.GetType());
        var used = cards.Select(c => c.Card).Where(Accepts).Select(c => c.GetType()).ToHashSet();
        for (int i = 0; i < cards.Count; i++)
        {
            if (Accepts(cards[i].Card)) continue;
            var old = cards[i].Card;
            cards[i].ModifyCard(Draw(player, rng, candidates, used));
            CleanUp(old);
        }
    }

    private static CardModel Draw(Player player, Rng rng, List<CardModel>? candidates, HashSet<Type> used)
    {
        if (candidates == null) return MythCardRewards.Create(player, rng);
        var remaining = candidates.Where(c => !used.Contains(c.GetType())).ToList();
        // 奖励数量超过卡池容量时允许重复，仍保留原奖励数量。
        var canonical = rng.NextItem(remaining.Count > 0 ? remaining : candidates)
            ?? throw new InvalidOperationException("No obtainable special reward cards.");
        used.Add(canonical.GetType());
        return player.RunState.CreateCard(canonical, player);
    }

    private static void CleanUp(CardModel card)
    {
        // 不销毁已入堆的失窃归还实例，也不修改规范模板。
        if (card.IsMutable && card.Pile == null && !card.HasBeenRemovedFromState) card.RemoveFromState();
    }
}
