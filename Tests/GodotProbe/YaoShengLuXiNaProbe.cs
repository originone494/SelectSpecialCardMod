using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Tool;

public static class YaoShengLuXiNaProbe
{
    private static CardModel Add(Type type, Player player)
    {
        var card = player.Creature.CombatState!.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(type)), player);
        PileType.Hand.GetPile(player).AddInternal(card);
        return card;
    }

    public static void Check()
    {
        var wrapper = AccessTools.Method(typeof(CardModel), nameof(CardModel.OnPlayWrapper));
        if (Harmony.GetPatchInfo(wrapper)?.Prefixes.Any(p => p.PatchMethod.DeclaringType == typeof(YaoShengLuXiNaHandPositionPatch)) != true)
            throw new Exception("Lucina's pre-play hand snapshot patch is missing.");
        var center = AccessTools.Method(typeof(YaoShengHuaShiLuXiNaCard), "IsAtHandCenter");
        int[][] centerPositions = [[0], [0, 1], [1], [1, 2], [2], [2, 3], [3], [3, 4], [4], [4, 5]];
        foreach (var type in new[] { typeof(YaoShengHuaShiLuXiNaCard), typeof(YaoShengBoShiDiNaCard), typeof(YaoShengYueShiFuErTuNaCard), typeof(YaoShengFengShiLeiQiNaCard) })
        {
            var started = AccessTools.Field(type, "_startedInCenter");
            for (int count = 1; count <= 10; count++)
            for (int index = 0; index < count; index++)
            {
                var player = ArrivalEquipmentProbe.NewPlayer();
                CardModel card = null!;
                for (int i = 0; i < count; i++)
                {
                    var added = Add(i == index ? type : typeof(StrikeIronclad), player);
                    if (i == index) card = added;
                }
                bool expected = centerPositions[count - 1].Contains(index);
                if (!Equals(center.Invoke(null, [card]), expected)) throw new Exception($"Incorrect hand center: {count}/{index}.");
                YaoShengLuXiNaHandPositionPatch.Prefix(card);
                if (!Equals(started.GetValue(card), expected)) throw new Exception("Play-start snapshot missed the hand position.");
                card.RemoveFromCurrentPile();
                PileType.Play.GetPile(player).AddInternal(card);
                if (!Equals(started.GetValue(card), expected) || !Equals(center.Invoke(null, [card]), false))
                    throw new Exception("Moving the card into Play erased its initial position or still counts as in hand.");
                YaoShengLuXiNaHandPositionPatch.Prefix(card);
                if (!Equals(started.GetValue(card), false)) throw new Exception("A later play outside the hand reused an earlier center snapshot.");
            }
        }
        var canonical = ModelDb.Card<YaoShengHuaShiLuXiNaCard>();
        if (canonical.Type != CardType.Attack || canonical.Rarity != CardRarity.Ancient || canonical.EnergyCost.Canonical != 2
            || canonical.StarLevel != 6 || canonical.DynamicVars.Damage.IntValue != 25 || canonical.DynamicVars["StrengthDown"].IntValue != 8
            || !canonical.Tags.Contains(SelectTags.YaoSheng) || !canonical.Tags.Contains(SelectTags.YaoShengGuaiShou) || !canonical.Tags.Contains(SelectTags.Xing6)
            || CardClassification.IsExtraMonster(canonical)) throw new Exception("Lucina metadata or monster classification is incorrect.");
        var tina = ModelDb.Card<YaoShengBoShiDiNaCard>();
        if (tina.Type != CardType.Attack || tina.Rarity != CardRarity.Ancient || tina.EnergyCost.Canonical != 2
            || tina.StarLevel != 6 || tina.DynamicVars.Damage.IntValue != 14 || tina.DynamicVars.Block.IntValue != 10
            || !tina.GainsBlock || !tina.Tags.Contains(SelectTags.YaoSheng) || !tina.Tags.Contains(SelectTags.YaoShengGuaiShou)
            || !tina.Tags.Contains(SelectTags.Xing6) || CardClassification.IsExtraMonster(tina))
            throw new Exception("Tina metadata, block recognition or monster classification is incorrect.");
        if (canonical.Id == tina.Id) throw new Exception("Lucina cannot retrieve Tina because their card IDs collide.");
        var fortuna = ModelDb.Card<YaoShengYueShiFuErTuNaCard>();
        if (fortuna.Type != CardType.Attack || fortuna.Rarity != CardRarity.Ancient || fortuna.EnergyCost.Canonical != 2
            || fortuna.StarLevel != 6 || fortuna.DynamicVars.Damage.IntValue != 16
            || !fortuna.Tags.Contains(SelectTags.YaoSheng) || !fortuna.Tags.Contains(SelectTags.YaoShengGuaiShou)
            || !fortuna.Tags.Contains(SelectTags.Xing6) || CardClassification.IsExtraMonster(fortuna))
            throw new Exception("Fortuna's metadata or monster classification is incorrect.");
        var removable = AccessTools.Method(typeof(YaoShengYueShiFuErTuNaCard), "IsRemovableBuff");
        if (!Equals(removable.Invoke(null, [ModelDb.Power<StrengthPower>().ToMutable(8)]), true)
            || !Equals(removable.Invoke(null, [ModelDb.Power<StrengthPower>().ToMutable(-8)]), false)
            || !Equals(removable.Invoke(null, [ModelDb.Power<WeakPower>().ToMutable(1)]), false))
            throw new Exception("Fortuna confuses negative strength or Weak with removable buffs.");
        var choice = ModelDb.Card<NamedEffectChoiceCard>();
        if (choice.Rarity != CardRarity.Token || choice.ShouldShowInCardLibrary)
            throw new Exception("Enemy/power choice tokens leaked into the player card library.");
        CheckOutline();
        var regina = ModelDb.Card<YaoShengFengShiLeiQiNaCard>();
        if (regina.Type != CardType.Attack || regina.Rarity != CardRarity.Ancient || regina.EnergyCost.Canonical != 2
            || regina.StarLevel != 6 || regina.DynamicVars.Damage.IntValue != 18 || regina.DynamicVars["Draw"].IntValue != 3
            || !regina.Tags.Contains(SelectTags.YaoSheng) || !regina.Tags.Contains(SelectTags.YaoShengGuaiShou)
            || !regina.Tags.Contains(SelectTags.Xing6) || CardClassification.IsExtraMonster(regina))
            throw new Exception("Regina's metadata or monster classification is incorrect.");
        CheckPreviousPlayAndFreeCost();
        GD.Print("PASS: four Elfnote cards' 220 position cases, pre-play snapshots, outline, buff signs, previous-own-play history and turn-only free cost.");
    }

    private static void CheckPreviousPlayAndFreeCost()
    {
        var h = new Harmony("scm.elfnote-regina-history.godot-framework");
        var history = MegaCrit.Sts2.Core.Combat.CombatManager.Instance.History;
        history.Clear();
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(MegaCrit.Sts2.Core.Combat.CombatManager), "IsInProgress"),
                prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(ArrivalEquipmentProbe.InProgress)));
            var player = ArrivalEquipmentProbe.NewPlayer();
            var other = ArrivalEquipmentProbe.NewPlayer();
            foreach (var p in new[] { player, other })
                AccessTools.Field(typeof(Player), "<Character>k__BackingField").SetValue(p, new MegaCrit.Sts2.Core.Models.Characters.Ironclad());
            var combat = player.Creature.CombatState!;
            var regina = Add(typeof(YaoShengFengShiLeiQiNaCard), player);
            var lucina = Add(typeof(YaoShengHuaShiLuXiNaCard), player);
            var strike = Add(typeof(StrikeIronclad), player);
            var teammate = Add(typeof(YaoShengBoShiDiNaCard), other);
            var previous = AccessTools.Method(typeof(YaoShengFengShiLeiQiNaCard), "PreviousOwnPlay");
            CardPlay Play(CardModel card, bool automatic = false) => new()
            { Card = card, Player = card.Owner, Target = null, ResultPile = PileType.Discard,
                Resources = default, IsAutoPlay = automatic, PlayIndex = 0, PlayCount = 1 };
            var current = Play(regina);
            history.CardPlayStarted(combat, current);
            if (previous.Invoke(null, [current]) != null) throw new Exception("Regina incorrectly counts herself as the previous card.");

            history.Clear();
            var own = Play(lucina);
            history.CardPlayStarted(combat, own);
            history.CardPlayStarted(combat, Play(strike, true));
            history.CardPlayStarted(combat, Play(teammate));
            combat.RoundNumber++;
            current = Play(regina);
            history.CardPlayStarted(combat, current);
            if (!ReferenceEquals(previous.Invoke(null, [current]), own))
                throw new Exception("Previous play lost owner filtering, automatic-play exclusion, nested starts or cross-turn history.");
            history.CardPlayStarted(combat, Play(strike, true));
            if (!ReferenceEquals(previous.Invoke(null, [current]), own))
                throw new Exception("A later draw-triggered play retroactively changed Regina's previous card.");

            history.Clear();
            history.CardPlayStarted(combat, Play(lucina));
            var intervening = Play(strike);
            history.CardPlayStarted(combat, intervening);
            history.CardPlayStarted(combat, Play(lucina, true));
            current = Play(regina);
            history.CardPlayStarted(combat, current);
            if (!ReferenceEquals(previous.Invoke(null, [current]), intervening))
                throw new Exception("An automatic Elfnote card replaced the previous manual non-Elfnote card.");

            history.Clear();
            history.CardPlayStarted(combat, Play(lucina, true));
            current = Play(regina);
            history.CardPlayStarted(combat, current);
            if (previous.Invoke(null, [current]) != null)
                throw new Exception("An automatic play counts as a manual predecessor when none exists.");

            AccessTools.Method(typeof(YaoShengFengShiLeiQiNaCard), "MakeFreeForTurn").Invoke(null, [lucina]);
            if (lucina.EnergyCost.GetWithModifiers(CostModifiers.Local) != 0)
                throw new Exception("Regina did not make the retrieved monster free.");
            lucina.EndOfTurnCleanup();
            if (lucina.EnergyCost.GetWithModifiers(CostModifiers.Local) != 2)
                throw new Exception("Regina's free cost persists beyond this turn.");
        }
        finally { h.UnpatchAll(h.Id); history.Clear(); }
    }

    private static void CheckOutline()
    {
        var h = new Harmony("scm.elfnote-outline.godot-framework");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(MegaCrit.Sts2.Core.Combat.CombatManager), "IsOverOrEnding"),
                prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(ArrivalEquipmentProbe.LiveCombat)));
            var player = ArrivalEquipmentProbe.NewPlayer();
            var left = Add(typeof(StrikeIronclad), player);
            var center = Add(typeof(YaoShengYueShiFuErTuNaCard), player);
            var rightCenter = Add(typeof(YaoShengBoShiDiNaCard), player);
            if (!UiConditions.YaoShengCenter(center) || UiConditions.YaoShengCenter(left)
                || UiConditions.YaoShengCenter(rightCenter))
                throw new Exception("Only a central Elfnote card should request gold.");
            var extra = Add(typeof(DefendIronclad), player);
            if (!UiConditions.YaoShengCenter(center) || !UiConditions.YaoShengCenter(rightCenter)
                || UiConditions.YaoShengCenter(left) || UiConditions.YaoShengCenter(extra))
                throw new Exception("An even hand must highlight both central Elfnote cards and neither outside card.");
            var fifth = Add(typeof(DefendIronclad), player);
            if (UiConditions.YaoShengCenter(center) || !UiConditions.YaoShengCenter(rightCenter))
                throw new Exception("Changing from four to five cards did not restore a single central gold outline.");
            fifth.RemoveFromCurrentPile();
            if (!UiConditions.YaoShengCenter(center) || !UiConditions.YaoShengCenter(rightCenter))
                throw new Exception("Both central gold outlines did not return after changing from five to four cards.");
            extra.RemoveFromCurrentPile();
            if (!UiConditions.YaoShengCenter(center)) throw new Exception("Gold does not return after hand size changes.");
            PileType.Hand.GetPile(player).MoveToBottomInternal(center);
            if (UiConditions.YaoShengCenter(center)) throw new Exception("A noncentral Elfnote card still requests gold.");
            center.RemoveFromCurrentPile();
            PileType.Play.GetPile(player).AddInternal(center);
            if (UiConditions.YaoShengCenter(center)) throw new Exception("A card outside the hand still requests gold.");
        }
        finally { h.UnpatchAll(h.Id); }
    }
}
