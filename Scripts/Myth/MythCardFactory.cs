using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Random;
using STS2RitsuLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace SelectSpecialCardMod.Scripts.Myth;

public static class MythCardFactory
{
    private static readonly HashSet<string> Conditions = ["none", "hand_at_least", "hand_at_most", "has_attack", "has_skill", "has_block", "hp_below_half", "pay_hp", "pay_all_hand", "pay_discard_hand"];
    public static Rng RngFor(Player player) => RitsuLibFramework.GetModRunRng(player, Entry.ModId, "myth_mutation");
    public static MythCardSnapshot Generate(Player player) => Generate(RngFor(player));
    public static async Task CreateAndGrant(Player player)
    {
        var combat = player.Creature.CombatState;
        if (combat == null || CombatManager.Instance.IsOverOrEnding || !player.Creature.IsAlive) return;
        var snapshot = Generate(player);
        var card = combat.CreateCard<MythMutationCard>(player);
        try
        {
            card.Bind(snapshot);
            if (player.Creature.CombatState != combat || CombatManager.Instance.IsOverOrEnding || !player.Creature.IsAlive) return;
            await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
            var final = card.CaptureSnapshot();
            Entry.Logger.Info($"[MythMutation] {card.Title}; condition={final.Condition.Id}; effect={final.Effect.Id}; type={final.CardType}; cost={final.EnergyCost}; art={final.Art.Id}; upgrade={final.UpgradeLevel}; series={string.Join(",", final.SeriesTags!)}");
        }
        finally
        {
            // 战斗生成卡没有永久原件；入手被阻止且未落入任何牌堆时清理实体。
            if (card.Pile == null && !card.HasBeenRemovedFromState) card.RemoveFromState();
        }
    }
    public static MythCardSnapshot Generate(Rng rng)
    {
        int category = rng.NextInt(100);
        string kind = category < 70 ? "none" : category < 85 ? "restriction" : "payment";
        var options = MythConditionRegistry.All.Where(c => c.Kind == kind).ToList();
        var condition = rng.NextItem(options)!;
        var effect = rng.NextItem(MythEffectRegistry.All.ToList())!;
        string type = new[] { "Attack", "Skill", "Power" }[rng.NextInt(3)];
        int cost = rng.NextInt(4);
        var art = rng.NextItem(MythArtManifest.All.ToList())!;
        var cv = Roll(condition.Parameters, rng); var ev = Roll(effect.Parameters, rng);
        var result = new MythCardSnapshot
        {
            Condition = condition, Effect = effect, ConditionValues = cv, EffectValues = ev,
            Actions = Resolve(effect, ev), CardType = type, EnergyCost = cost, Art = art, Exhaust = rng.NextInt(2) == 1,
            SeriesTags = MythSeriesTags.FromSource(effect.SourceCard),
        };
        ValidateSnapshot(result);
        return result;
    }
    private static Dictionary<string, int> Roll(Dictionary<string, MythRange> ranges, Rng rng)
        => ranges.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => rng.NextInt(p.Value.Min, p.Value.Max + 1));
    public static List<MythResolvedAction> Resolve(MythEffectTemplate effect, Dictionary<string, int> values)
        => effect.Actions.Select(a => a.Arguments.Length > 0 ? MythLibraryImport.Resolve(a, values) : new MythResolvedAction(a.Op, Value(a.Amount, values), Value(a.Repeat, values), a.Filter, a.Amount, a.Repeat)).ToList();
    private static int Value(string key, Dictionary<string, int> values)
        => values.TryGetValue(key, out int value) ? value : int.TryParse(key, out value) ? value : throw new InvalidOperationException("Missing myth parameter: " + key);
    private static void Identity(string id, string source)
    {
        if (!Regex.IsMatch(id, "^[a-z][a-z0-9_.]{0,95}$")) throw new InvalidOperationException("Invalid myth ID.");
        if (source.Length > 0 && !typeof(Entry).Assembly.GetTypes().Any(t => t.Name == source && typeof(MegaCrit.Sts2.Core.Models.CardModel).IsAssignableFrom(t)))
            throw new InvalidOperationException("Unknown myth source: " + source);
    }
    private static void Ranges(Dictionary<string, MythRange> ranges)
    {
        if (ranges.Count > 8 || ranges.Any(p => !Regex.IsMatch(p.Key, "^[a-z_]+$") || p.Value.Min < 1 || p.Value.Max < p.Value.Min || p.Value.Max > 100))
            throw new InvalidOperationException("Invalid myth parameter range.");
    }
    public static void ValidateCondition(MythConditionTemplate c)
    {
        Identity(c.Id, c.SourceCard); Ranges(c.Parameters);
        if (c.Actions != null)
        {
            MythLibraryConditions.Validate(c); ValidateText(c.Text, c.Parameters.Keys); return;
        }
        if (!Conditions.Contains(c.Op) || c.Id != c.Op || c.Kind != (c.Op == "none" ? "none" : c.Op.StartsWith("pay_") ? "payment" : "restriction")
            || (c.Kind != "none" && (c.NamePart.Length == 0 || c.Text.Length == 0))) throw new InvalidOperationException("Invalid myth condition.");
        if ((c.Op is "hand_at_least" or "hand_at_most") && !c.Parameters.ContainsKey("count")
            || c.Op == "pay_hp" && !c.Parameters.ContainsKey("hp")) throw new InvalidOperationException("Missing condition parameter.");
        ValidateText(c.Text, c.Parameters.Keys);
    }
    public static void ValidateEffect(MythEffectTemplate e)
    {
        Identity(e.Id, e.SourceCard); Ranges(e.Parameters);
        if (e.SourceCard.Length == 0 || e.NamePart.Length == 0 || e.Description.Length == 0 || e.Actions.Count is < 1 or > 6
            || e.Target is not ("self" or "enemy" or "all_enemies" or "any_creature" or "choose_card") || e.Upgrade.Any(p => !e.Parameters.ContainsKey(p.Key) || p.Value < 0 || p.Value > 10)
            || e.UpgradeCostDelta is < -1 or > 0) throw new InvalidOperationException("Invalid myth effect.");
        var values = e.Parameters.ToDictionary(p => p.Key, p => p.Value.Min);
        var actions = Resolve(e, values);
        foreach (var a in actions) MythEffectExecutor.Validate(a);
        if (!actions.Any(a => a.Op.StartsWith("lib_")) && actions.Any(MythEffectExecutor.RequiresEnemy) != (e.Target == "enemy")) throw new InvalidOperationException("Myth target mismatch.");
        ValidateText(e.Description, e.Parameters.Keys);
    }
    private static void ValidateText(string text, IEnumerable<string> keys)
    {
        var allowed = keys.ToHashSet(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(text, "\\{([a-z_]+)\\}"))
            if (!allowed.Contains(match.Groups[1].Value)) throw new InvalidOperationException("Unknown myth text parameter.");
        string remainder = Regex.Replace(text, "\\{([a-z_]+)\\}", "");
        if (remainder.Contains('{') || remainder.Contains('}')) throw new InvalidOperationException("Invalid myth text placeholder.");
    }
    public static void ValidateSnapshot(MythCardSnapshot s)
    {
        if (s.SchemaVersion != 1 || s.CardType is not ("Attack" or "Skill" or "Power") || s.EnergyCost is < 0 or > 3
            || s.UpgradeLevel is < 0 or > 1 || !MythArtManifest.ValidPath(s.Art.Path)) throw new InvalidOperationException("Unsupported myth snapshot.");
        ValidateCondition(s.Condition); ValidateEffect(s.Effect);
        MythSeriesTags.Validate(s.SeriesTags);
        if (!s.Condition.Parameters.Keys.ToHashSet().SetEquals(s.ConditionValues.Keys)
            || !s.Effect.Parameters.Keys.ToHashSet().SetEquals(s.EffectValues.Keys)
            || s.ConditionValues.Values.Concat(s.EffectValues.Values).Any(v => v is < 1 or > 1000)
            || !Resolve(s.Effect, s.EffectValues).SequenceEqual(s.Actions)) throw new InvalidOperationException("Invalid fixed myth values.");
    }
    public static void Validate()
    { _ = MythConditionRegistry.All; _ = MythEffectRegistry.All; _ = MythArtManifest.All; }
}
