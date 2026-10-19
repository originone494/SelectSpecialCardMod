using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Myth;

public static class MythLibraryConditions
{
    private static readonly IReadOnlyDictionary<string, int> Arity = new Dictionary<string, int>
    {
        ["remove_all_buff_powers"] = 0, ["pay_discard_all_other_hand"] = 0, ["pay_exhaust_all_other_hand"] = 0,
        ["pay_discard_other"] = 1, ["pay_discard_other_type"] = 2, ["pay_exhaust_other_type"] = 2,
        ["pay_exhaust_from_discard"] = 1, ["pay_exhaust_draw_top"] = 1, ["pay_exhaust_other_hand"] = 1,
        ["pay_banish_from_exhaust"] = 1, ["pay_banish_other_hand"] = 1, ["pay_lose_hp"] = 1,
        ["pay_return_banished_to_draw"] = 1, ["pay_return_exhaust_to_draw"] = 1,
        ["require_attacks_played_at_least"] = 1, ["require_banished_count_at_least"] = 1,
        ["require_discard_count_at_least"] = 1, ["require_draw_count_at_least"] = 1,
        ["require_enemy_attack_intent"] = 0, ["require_enemy_count_at_least"] = 1, ["require_enemy_has_block"] = 0,
        ["require_enemy_hp_below_percent"] = 1, ["require_exhaust_count_at_least"] = 1, ["require_hand_card_min_cost"] = 1,
        ["require_hand_count_at_least"] = 1, ["require_has_enemy_minion"] = 0, ["require_no_artifact"] = 0,
        ["require_no_other_attacks"] = 0, ["require_other_attacks_at_least"] = 1, ["require_other_card_type"] = 1,
        ["require_own_power_types_at_least"] = 1,
    };
    public static void Validate(MythConditionTemplate c)
    {
        if (c.Kind == "none")
        { if (c.Actions!.Count != 0 || c.Op != "none") throw new InvalidOperationException("Invalid empty condition."); return; }
        if (c.Kind is not ("restriction" or "payment") || c.Actions!.Count != 1 || c.NamePart.Length == 0 || c.Text.Length == 0)
            throw new InvalidOperationException("Invalid library condition.");
        var a = c.Actions.Single();
        if (!a.Op.StartsWith("lib_")) throw new InvalidOperationException("Invalid library condition action.");
        string op = a.Op[4..];
        var args = MythJson.Read<string[]>(a.Arguments);
        if (!a.Op.StartsWith("lib_") || !Arity.TryGetValue(op, out int arity) || args.Length != arity
            || (c.Kind == "restriction") != op.StartsWith("require_")) throw new InvalidOperationException("Unsupported condition: " + a.Op);
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            bool typeArg = i == 0 && (op.EndsWith("_type") || op == "require_other_card_type");
            if (typeArg ? arg is not ("Attack" or "Skill" or "Power")
                : arg.StartsWith('$') ? !c.Parameters.ContainsKey(arg[1..]) : !int.TryParse(arg, out int n) || n < 1 || n > 100)
                throw new InvalidOperationException("Invalid condition argument.");
        }
    }
    private static (string Op, string[] Args) Action(MythMutationCard card)
    {
        var s = card.FixedSnapshot!;
        var a = s.Condition.Actions!.Single();
        return (a.Op[4..], MythJson.Read<string[]>(a.Arguments).Select(k => k.StartsWith('$') ? s.ConditionValues[k[1..]].ToString() : k).ToArray());
    }
    public static int Attacks(MythMutationCard card) => CombatManager.Instance.History.CardPlaysFinished
        .Count(e => e.HappenedThisTurn(card.Owner.Creature.CombatState!) && e.CardPlay.Player == card.Owner && e.CardPlay.Card.Type == CardType.Attack);
    public static bool CanPlay(MythMutationCard card)
    {
        var s = card.FixedSnapshot!;
        if (card.Owner == null || card.Owner.Creature.CombatState == null || s.Condition.Kind == "none") return true;
        var (op, args) = Action(card); int N(int i = 0) => int.Parse(args[i]);
        var owner = card.Owner; var creature = owner.Creature;
        var hand = PileType.Hand.GetPile(owner).Cards; var others = hand.Where(c => c != card).ToList();
        var enemies = creature.CombatState!.HittableEnemies.Where(c => c.IsAlive).ToList();
        if (s.Condition.Kind == "payment")
        {
            if (op == "remove_all_buff_powers") return true;
            if (op == "pay_lose_hp") return creature.CurrentHp > N() && MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage(owner.RunState, creature.CombatState,
                creature, creature, N(), ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move,
                card, null, MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType.All, CardPreviewMode.Normal, out _) >= N();
            var eligible = PaymentCards(card, op, args);
            return op.EndsWith("all_other_hand") ? others.Count > 0 && eligible.Count == others.Count : eligible.Count >= N(args.Length - 1);
        }
        return op switch
        {
            "require_attacks_played_at_least" => Attacks(card) >= N(),
            "require_banished_count_at_least" => Entry.ChuWaiPile.GetPile(owner).Cards.Count >= N(),
            "require_discard_count_at_least" => PileType.Discard.GetPile(owner).Cards.Count >= N(),
            "require_draw_count_at_least" => PileType.Draw.GetPile(owner).Cards.Count >= N(),
            "require_exhaust_count_at_least" => PileType.Exhaust.GetPile(owner).Cards.Count >= N(),
            "require_enemy_count_at_least" => enemies.Count >= N(),
            "require_enemy_attack_intent" => enemies.Any(e => e.Monster?.NextMove.Intents.Any(i => i is AttackIntent) == true),
            "require_enemy_has_block" => enemies.Any(e => e.Block > 0),
            "require_enemy_hp_below_percent" => enemies.Any(e => (long)e.CurrentHp * 100 <= (long)e.MaxHp * N()),
            "require_hand_card_min_cost" => hand.Any(c => MythLibraryChoices.Cost(c) >= N()),
            "require_hand_count_at_least" => others.Count + (hand.Contains(card) || card.PlayedFromHand && card.Pile?.Type == PileType.Play ? 1 : 0) >= N(),
            "require_has_enemy_minion" => enemies.Any(e => e.HasPower<MinionPower>()),
            "require_no_artifact" => !creature.HasPower<ArtifactPower>(),
            "require_no_other_attacks" => others.All(c => c.Type != CardType.Attack),
            "require_other_attacks_at_least" => others.Count(c => c.Type == CardType.Attack) >= N(),
            "require_other_card_type" => others.Any(c => MythLibraryChoices.TypeMatches(c, args[0])),
            "require_own_power_types_at_least" => creature.Powers.Select(p => p.Id.ToString()
                + (p is MythOngoingPower myth ? ":" + myth.Operation : "")).Distinct().Count() >= N(),
            _ => false,
        };
    }
    private static List<CardModel> PaymentCards(MythMutationCard card, string op, string[] args)
    {
        var pile = op switch
        {
            "pay_exhaust_from_discard" => PileType.Discard, "pay_exhaust_draw_top" => PileType.Draw,
            "pay_banish_from_exhaust" or "pay_return_exhaust_to_draw" => PileType.Exhaust,
            "pay_return_banished_to_draw" => Entry.ChuWaiPile, _ => PileType.Hand,
        };
        var cards = pile.GetPile(card.Owner).Cards.Where(c => c != card && MythLibraryChoices.Movable(c));
        if (op.EndsWith("_type")) cards = cards.Where(c => MythLibraryChoices.TypeMatches(c, args[0]));
        if (op == "pay_exhaust_draw_top") cards = cards.Take(int.Parse(args[0]));
        return cards.ToList();
    }
    public static async Task<bool> Pay(MythMutationCard card, FusionActionContext ctx)
    {
        if (!ctx.CanContinue || !CanPlay(card)) return false;
        if (card.FixedSnapshot!.Condition.Kind != "payment") return true;
        var (op, args) = Action(card); var creature = card.Owner.Creature;
        if (op == "remove_all_buff_powers")
        {
            foreach (var power in creature.Powers.Where(p => p.Type == PowerType.Buff).ToList())
            { if (!ctx.CanContinue) return false; await PowerCmd.Remove(power); }
            return ctx.CanContinue && creature.Powers.All(p => p.Type != PowerType.Buff);
        }
        if (op == "pay_lose_hp")
        {
            int amount = int.Parse(args[0]);
            var paid = await CreatureCmd.Damage(ctx.ChoiceContext, creature, amount, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, card, ctx.CardPlay);
            return ctx.CanContinue && paid.Where(r => r.Receiver == creature).Sum(r => r.UnblockedDamage) >= amount;
        }
        var options = PaymentCards(card, op, args); int count = op.EndsWith("all_other_hand") ? options.Count : int.Parse(args[^1]);
        var chosen = op.EndsWith("all_other_hand") || op == "pay_exhaust_draw_top" ? options : await MythLibraryChoices.Select(ctx, options, count);
        if (!ctx.CanContinue || chosen.Count != count || !CanPlay(card) || chosen.Any(c => !PaymentCards(card, op, args).Contains(c))) return false;
        var originalPiles = chosen.ToDictionary(c => c, c => c.Pile!);
        foreach (var c in chosen)
        {
            if (!ctx.CanContinue || c.Pile != originalPiles[c] || !MythLibraryChoices.Movable(c)) return false;
            if (op.StartsWith("pay_discard")) await CardCmd.Discard(ctx.ChoiceContext, c);
            else if (op.StartsWith("pay_exhaust"))
            { if (!await MaterialPayment.Exhaust(ctx.ChoiceContext, card.Owner, [c], 1, c.Pile!.Type)) return false; }
            else if (op.StartsWith("pay_banish")) await CardPileCmd.Add(c, Entry.ChuWaiPile);
            else await CardPileCmd.Add(c, PileType.Draw, CardPilePosition.Random);
            if (c.Pile == originalPiles[c]) return false;
        }
        return ctx.CanContinue;
    }
}
