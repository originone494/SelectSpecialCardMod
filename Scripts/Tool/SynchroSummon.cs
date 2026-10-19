using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Tool;

// 右键额外堆同调：一张龙族调整 + 一张非调整龙族，星级之和等于同调怪兽星级。
public static class SynchroSummon
{
    private static bool IsTuner(CardModel card)
        => card.Tags.Contains(SelectTags.TiaoZheng) || card.Keywords.Contains(SelectKeywords.TiaoZheng);
    private static int Level(CardModel card) => card is SelectSpecialBaseCard monster ? monster.StarLevel : 0;

    private static bool IsMaterial(CardModel card, Player player)
        => card.Owner == player && card.CombatState == player.Creature.CombatState
            && card.Pile == PileType.Hand.GetPile(player) && card.Type == CardType.Attack
            && card.Tags.Contains(SelectTags.LongZu) && Level(card) > 0 && MaterialPayment.CanUseAsMaterial(card);

    private static bool IsAvailable(SelectSpecialBaseCard monster, Player player)
        => monster.Owner == player && player.Creature.CombatState != null && player.PlayerCombatState != null
            && player.Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding
            && !monster.HasBeenRemovedFromState && monster.Pile == Entry.ExtraPile.GetPile(player)
            && monster.StarLevel > 0 && monster.Tags.Contains(SelectTags.TongBuGuaiShou);

    public static bool CanSummon(SelectSpecialBaseCard monster, Player player)
    {
        if (!IsAvailable(monster, player)) return false;
        var hand = PileType.Hand.GetPile(player).Cards;
        return hand.Any(tuner => IsMaterial(tuner, player) && IsTuner(tuner)
            && hand.Any(other => other != tuner && IsMaterial(other, player) && !IsTuner(other)
                && Level(tuner) + Level(other) == monster.StarLevel));
    }

    public static async Task<bool> PayMaterials(PlayerChoiceContext context, SelectSpecialBaseCard monster, Player player)
    {
        if (!CanSummon(monster, player)) return false;
        var combat = player.Creature.CombatState;
        var combatId = CombatManager.Instance.CurrentCombatId;
        int targetLevel = monster.StarLevel;
        bool IsCurrent() => player.Creature.CombatState == combat && CombatManager.Instance.CurrentCombatId == combatId
            && IsAvailable(monster, player) && monster.StarLevel == targetLevel;
        bool IsPartner(CardModel tuner, CardModel other) => other != tuner && IsMaterial(other, player)
            && !IsTuner(other) && Level(tuner) + Level(other) == targetLevel;
        bool IsEligibleTuner(CardModel card) => IsMaterial(card, player) && IsTuner(card)
            && PileType.Hand.GetPile(player).Cards.Any(other => IsPartner(card, other));

        var tuner = (await CardSelectCmd.FromHand(context, player, Prefs("TUNER"),
            filter: IsEligibleTuner, source: null!)).FirstOrDefault();
        if (!IsCurrent() || tuner == null || !IsEligibleTuner(tuner)) return false;

        var other = (await CardSelectCmd.FromHand(context, player, Prefs("NON_TUNER"),
            filter: card => IsPartner(tuner, card), source: null!)).FirstOrDefault();
        if (!IsCurrent() || other == null || !IsMaterial(tuner, player) || !IsTuner(tuner)
            || !IsPartner(tuner, other)) return false;

        // 两次选择均可取消；确认两张素材前不支付。每次消耗后都可能触发连锁。
        int tunerLevel = Level(tuner), otherLevel = Level(other);
        foreach (var material in new[] { tuner, other })
        {
            if (!IsCurrent() || !IsMaterial(material, player)
                || (material == tuner ? !IsTuner(material) || Level(material) != tunerLevel
                    : IsTuner(material) || Level(material) != otherLevel)) return false;
            var paid = await CardCmd.Exhaust(context, material);
            if (paid?.success != true || !IsCurrent()) return false;
        }
        return true;

        CardSelectorPrefs Prefs(string stage)
        {
            var prompt = CardPrompts.Named(new LocString("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_SYNCHRO_" + stage), monster);
            prompt.Add("Level", targetLevel);
            return new CardSelectorPrefs(prompt, 1, 1) { Cancelable = true, RequireManualConfirmation = true };
        }
    }
}
