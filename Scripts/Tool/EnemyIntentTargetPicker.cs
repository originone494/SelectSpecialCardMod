using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Intent;

namespace SelectSpecialCardMod.Scripts.Tool;

internal static class EnemyIntentTargetPicker
{
    public static async Task<Creature?> Pick(PlayerChoiceContext context, Player player,
        CardModel source, CardSelectorPrefs prefs)
    {
        if (source.CombatState == null) return null;
        var options = new List<CardModel>();
        var targets = new Dictionary<CardModel, Creature>();
        foreach (var enemy in ZhuLeiAiChangTargets.Get(source.CombatState))
        {
            foreach (var card in IntentCardMapper.BuildIntentCards(enemy, player,
                         ZhuLeiAiChangTargets.Allowed))
            {
                if (card is MonsterDisplayCard display)
                    display.IntentText = $"{enemy.Name}（#{enemy.CombatId}）：{display.IntentText}";
                options.Add(card);
                targets[card] = enemy;
            }
        }
        if (options.Count == 0) return null;
        var picked = (await CardSelectCmd.FromSimpleGrid(
            context, options, player, prefs with { RequireManualConfirmation = true }))
            .FirstOrDefault();
        return picked != null && targets.TryGetValue(picked, out var target) ? target : null;
    }
}
