using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using SelectSpecialCardMod.Scripts.Intent;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Myth;

public static class MythIntentTheft
{
    public static async Task Execute(FusionActionContext ctx, int count)
    {
        var target = ctx.Target(); if (target == null) return;
        var candidates = MonsterDeck.Stealable(target).ToList(); count = Math.Min(count, candidates.Count);
        if (count <= 0) return;
        var selected = (await CardSelectCmd.FromSimpleGrid(ctx.ChoiceContext, candidates, ctx.Source.Owner,
            new CardSelectorPrefs(new LocString("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_QIANG_DUO"), count, count)
            { RequireManualConfirmation = true })).ToList();
        if (!ctx.CanContinue || !target.IsAlive || selected.Count != count || selected.Distinct().Count() != count || selected.Any(c => !candidates.Contains(c))) return;
        foreach (var card in selected)
        {
            if (!ctx.CanContinue || !target.IsAlive || !MonsterDeck.TryRemoveIntent(target, card)) return;
            IntentCardHook.FindOverlay(target)?.StealCard(card);
            if (!MonsterDeck.IsTracked(target)) foreach (var kind in Kinds(card)) IntentCardTheft.Record(target, kind);
            var copy = IntentCardMapper.CreateCopy(ctx.Source.Owner, card);
            try
            {
                if (copy is MonsterMoveCard move) await MonsterEffectSnapshot.Capture(ctx.ChoiceContext, ctx.Source.Owner, move);
                copy.AddKeyword(CardKeyword.Exhaust); await CardPileCmd.Add(copy, PileType.Hand);
            }
            finally { if (copy.Pile == null && !copy.HasBeenRemovedFromState) copy.RemoveFromState(); }
        }
    }
    private static IReadOnlyList<IntentCardTheft.TheftKind> Kinds(CardModel card) => card switch
    {
        MonsterDeathBlowCard => [IntentCardTheft.TheftKind.Attack, IntentCardTheft.TheftKind.DeathBlow],
        MonsterAttackCard => [IntentCardTheft.TheftKind.Attack], MonsterDefendCard => [IntentCardTheft.TheftKind.Block],
        MonsterBuffCard => [IntentCardTheft.TheftKind.Buff], MonsterDebuffCard or MonsterCardDebuffCard => [IntentCardTheft.TheftKind.Debuff],
        MonsterHealCard => [IntentCardTheft.TheftKind.Heal], MonsterStatusCard => [IntentCardTheft.TheftKind.Status],
        MonsterSummonCard => [IntentCardTheft.TheftKind.Summon],
        MonsterUnknownCard { SourceType: IntentType.Escape } => [IntentCardTheft.TheftKind.Escape], _ => [],
    };
}
