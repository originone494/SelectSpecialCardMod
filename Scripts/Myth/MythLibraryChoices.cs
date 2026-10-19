using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Commands;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Myth;

public static class MythLibraryChoices
{
    public static async Task<List<CardModel>> Select(FusionActionContext ctx, IEnumerable<CardModel> input, int count, bool optional = false)
    {
        var candidates = input.Where(c => c.Owner == ctx.Source.Owner && !c.HasBeenRemovedFromState).Distinct().ToList();
        var originalPiles = candidates.ToDictionary(c => c, c => c.Pile);
        count = Math.Min(count, candidates.Count);
        if (count <= 0 || !ctx.CanContinue) return [];
        var prompt = new LocString("card_selection", optional ? "SELECT_SPECIAL_CARD_MOD_MYTH_LIBRARY_SELECT_OPTIONAL" : "SELECT_SPECIAL_CARD_MOD_MYTH_LIBRARY_SELECT");
        prompt.Add("CardName", ctx.Source.Title); prompt.Add("Count", count);
        var prefs = new CardSelectorPrefs(prompt, optional ? 0 : count, count) { RequireManualConfirmation = true };
        var chosen = candidates.All(c => c.Pile?.Type == PileType.Hand)
            ? await CardSelectCmd.FromHand(ctx.ChoiceContext, ctx.Source.Owner, prefs, c => candidates.Contains(c), ctx.Source)
            : await CardSelectCmd.FromSimpleGrid(ctx.ChoiceContext, candidates, ctx.Source.Owner, prefs);
        var result = chosen.ToList();
        if (!ctx.CanContinue || result.Count > count || (!optional && result.Count != count) || result.Distinct().Count() != result.Count
            || result.Any(c => !candidates.Contains(c) || c.Pile != originalPiles[c] || c.HasBeenRemovedFromState || c.Owner != ctx.Source.Owner)) return [];
        return result;
    }
    public static bool Movable(CardModel card) => MaterialPayment.CanUseAsMaterial(card) && card.Pile?.Type != PileType.Play;
    public static void Free(CardModel card)
    { card.EnergyCost.SetThisTurn(0); card.SetStarCostThisTurn(0); card.InvokeEnergyCostChanged(); }
    public static int Cost(CardModel card) => Math.Max(0, card.EnergyCost.GetWithModifiers(CostModifiers.Local));
    public static bool TypeMatches(CardModel card, string type) => type == "Any" || card.Type.ToString() == type;
}
