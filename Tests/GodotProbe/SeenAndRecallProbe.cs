using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Hook;

public static class SeenAndRecallProbe
{
    public static async Task Check()
    {
        // 原版 NCard 根节点尺寸为0，真实卡框是可见范围。
        var visual = new NCard();
        var frame = new Control { Name = "Frame", Position = new(-150, -211), Size = new(300, 422) };
        visual.AddChild(frame);
        frame.Owner = visual;
        frame.UniqueNameInOwner = true;
        var bounds = (Rect2)AccessTools.Method(typeof(RunSeenCardObserver), "VisibleBounds").Invoke(null, [visual])!;
        if (visual.GetGlobalRect().HasArea() || !bounds.HasArea() || bounds.Size != new Vector2(300, 422))
            throw new Exception("Zero-size card root still hides the visible frame from seen-card recording.");
        visual.Free();

        var h = new Harmony("scm.seen-recall.godot-framework");
        var history = CombatManager.Instance.History;
        history.Clear();
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsInProgress"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), "InProgress"));
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), "LiveCombat"));
            h.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Commands.CardPileCmd), "Add", [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(ZhuLeiResolutionProbe), nameof(ZhuLeiResolutionProbe.Add)));
            var player = ArrivalEquipmentProbe.NewPlayer();
            var rng = AccessTools.Property(typeof(RunState), "Rng");
            rng.SetValue(player.RunState, Activator.CreateInstance(rng.PropertyType, ["seen-recall-probe"]));
            var combat = player.Creature.CombatState!;
            var old = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.DefendIronclad), player, PileType.Discard);
            history.CardPlayFinished(combat, Play(old));
            combat.RoundNumber++;
            var attack = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), player, PileType.Discard);
            var exhausted = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.DefendIronclad), player, PileType.Exhaust);
            history.CardPlayFinished(combat, Play(attack));
            history.CardPlayFinished(combat, Play(attack)); // 同一实例重复打出不能重复加入手牌。
            history.CardPlayFinished(combat, Play(exhausted));
            var otherPlayer = ArrivalEquipmentProbe.NewPlayer();
            var other = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), otherPlayer, PileType.Discard);
            history.CardPlayFinished(combat, Play(other));
            // 常世明子在上述牌打出后才生成，没有机会监听它们的打出广播。
            var recall = (ChangShiMingZiCard)ArrivalEquipmentProbe.AddCard(typeof(ChangShiMingZiCard), player, PileType.Play);
            var invoke = AccessTools.Method(typeof(ChangShiMingZiCard), "OnPlay");
            var context = new BlockingPlayerChoiceContext();
            await (Task)invoke.Invoke(recall, [context, Play(recall)])!;
            var hand = PileType.Hand.GetPile(player);
            if (hand.Cards.Count != 2 || !hand.Cards.Contains(attack) || !hand.Cards.Contains(exhausted)
                || old.Pile?.Type != PileType.Discard || other.Pile?.Type != PileType.Discard
                || recall.Pile?.Type != PileType.Play)
                throw new Exception("Recall missed earlier plays, duplicated a card, or included another turn/player/self.");
            foreach (var card in hand.Cards)
            {
                var cost = card.EnergyCost.GetWithModifiers(CostModifiers.All);
                if (cost < 0 || cost > 3) throw new Exception("Recall cost is outside 0-3.");
            }
            hand.RemoveInternal(attack);
            PileType.Discard.GetPile(player).AddInternal(attack);
            await (Task)invoke.Invoke(recall, [context, Play(recall)])!;
            if (attack.Pile?.Type != PileType.Hand || hand.Cards.Count != 2)
                throw new Exception("Recall erased the native turn history after its first use.");

            history.Clear();
            var power = ArrivalEquipmentProbe.AddCard(typeof(YiCunJiaCard), player, PileType.Play);
            history.CardPlayFinished(combat, Play(power));
            power.Pile!.RemoveInternal(power);
            power.RemoveFromState(); // 原版打出的能力牌会被移出战斗。
            await (Task)invoke.Invoke(recall, [context, Play(recall)])!;
            var recalledPower = hand.Cards.OfType<YiCunJiaCard>().Single();
            if (ReferenceEquals(recalledPower, power) || recalledPower.HasBeenRemovedFromState
                || recalledPower.Owner != player || hand.Cards.Count != 3)
                throw new Exception("Recall attempted to reinsert a removed power card instead of creating a valid combat copy.");
        }
        finally { h.UnpatchAll(h.Id); history.Clear(); }
        GD.Print("PASS: zero-size card roots use the real frame's visible bounds; late-acquired Recall recovers earlier completed own plays including exhausted cards once each, excludes past turns/other players/current self, randomizes costs to 0-3, and preserves native history for another use.");
    }

    private static CardPlay Play(CardModel card) => new()
    {
        Card = card, Player = card.Owner, Target = null, ResultPile = PileType.Discard,
        Resources = default, IsAutoPlay = false, PlayIndex = 0, PlayCount = 1,
    };
}
