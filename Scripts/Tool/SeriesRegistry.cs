using MegaCrit.Sts2.Core.Models;
namespace SelectSpecialCardMod.Scripts.Tool;
public sealed record ExtraDeckSeriesRule(string Id, Func<CardModel, bool> Matches, IReadOnlyList<Type> ExtraMonsters);
public static class SeriesRegistry
{
    private static readonly Dictionary<string, ExtraDeckSeriesRule> Rules = [];
    public static IEnumerable<ExtraDeckSeriesRule> All => Rules.Values.OrderBy(r => r.Id, StringComparer.Ordinal);
    public static void Register(ExtraDeckSeriesRule rule)
    {
        if (Rules.ContainsKey(rule.Id)) throw new InvalidOperationException($"Duplicate series rule: {rule.Id}");
        if (rule.ExtraMonsters.Any(t => !typeof(CardModel).IsAssignableFrom(t))) throw new ArgumentException("Invalid extra monster type.");
        Rules.Add(rule.Id, rule);
    }
}
