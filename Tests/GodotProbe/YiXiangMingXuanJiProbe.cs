using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;
using STS2RitsuLib.Combat.HandSize;

public static class YiXiangMingXuanJiProbe
{
    private static bool _ending, _failMove, _emptyChoice;
    private static int _exhausts, _generated;
    private static Func<CardModel, bool>? _retrievalFilter;
    private static RetrievalOptions? _retrievalOptions;

    public static async Task Check()
    {
        var h = new Harmony("scm.vaalmonica-selettrice.godot-framework");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsInProgress"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), "InProgress"));
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"), prefix: new HarmonyMethod(typeof(YiXiangMingXuanJiProbe), nameof(Ending)));
            h.Patch(AccessTools.Method(typeof(RunState), "IterateHookListeners", [typeof(ICombatState)]), prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"));
            h.Patch(AccessTools.Method(typeof(MaxHandSizeCalculator), "Calculate"), prefix: new HarmonyMethod(typeof(YiXiangMingXuanJiProbe), nameof(HandLimit)));
            h.Patch(AccessTools.Method(typeof(CardRetrieval), "Retrieve"), prefix: new HarmonyMethod(typeof(YiXiangMingXuanJiProbe), nameof(Retrieve)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "Add", [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(YiXiangMingXuanJiProbe), nameof(Add)));

            var player = NewPlayer();
            var ctx = new BlockingPlayerChoiceContext();
            var card = (YiXiangMingTianMoZhiShengXuanJiCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianMoZhiShengXuanJiCard), player, PileType.Hand);
            if (card.Type != CardType.Attack || card.Rarity != CardRarity.Ancient || card.EnergyCost.Canonical != 1
                || card.StarLevel != 4 || card.DynamicVars.Damage.IntValue != 12
                || !card.Tags.Contains(SelectTags.YiXiangMing) || !card.Tags.Contains(SelectTags.YiXiangMingGuaiShou)
                || !card.Tags.Contains(SelectTags.Xing4)) throw new Exception("Selettrice metadata is wrong.");
            if (!await card.TryActivateSpecialEffect(ctx, player) || _retrievalOptions is not { Minimum: 1, Maximum: 1, Confirm: true }
                || _retrievalFilter!(card) || _retrievalFilter(new YiXiangMingTianMoZhiShengXuanJiCard())
                || !_retrievalFilter(new YiXiangMingJueZeCard()) || _retrievalFilter(new MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad()))
                throw new Exception("Retrieval must require exactly one other-named Vaalmonica card.");
            _emptyChoice = true;
            if (await card.TryActivateSpecialEffect(ctx, player)) throw new Exception("An empty retrieval consumed an activation.");
            _emptyChoice = false;
            Move(card, PileType.Exhaust);
            var angel = (YiXiangMingVoiceCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianShiZhiShengCard), player, PileType.Hand);
            var devil = (YiXiangMingVoiceCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingEMoZhiShengCard), player, PileType.Hand);
            if (card.CanActivateShiJi(player)) throw new Exception("Ordinary hand edges counted as pendulum placement.");
            angel.LockedPendulumSide = PendulumEdgeSide.Left;
            angel.PlacedAtEdgeByEffect = true;
            if (card.CanActivateShiJi(player)) throw new Exception("One placed card enabled the two-card condition.");
            devil.LockedPendulumSide = PendulumEdgeSide.Right;
            devil.PlacedAtEdgeByEffect = true;
            if (!card.CanActivateShiJi(player) || card.CanActivateShiJi(NewPlayer())) throw new Exception("Two-card zone/owner eligibility is wrong.");
            _ending = true;
            await card.ActivateShiJi(ctx, player);
            if (card.ShiJiUsed || card.Pile?.Type != PileType.Exhaust) throw new Exception("Ending combat spent timing usage.");
            _ending = false;
            var fillers = Enumerable.Range(0, 8).Select(_ => ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), player, PileType.Hand)).ToList();
            await card.ActivateShiJi(ctx, player);
            if (card.ShiJiUsed || card.Pile?.Type != PileType.Exhaust) throw new Exception("Full hand spent timing usage.");
            foreach (var filler in fillers) Move(filler, PileType.Discard);
            _failMove = true;
            await card.ActivateShiJi(ctx, player);
            if (card.ShiJiUsed || card.Pile?.Type != PileType.Exhaust) throw new Exception("Rejected return consumed timing usage.");
            _failMove = false;
            await card.ActivateShiJi(ctx, player);
            if (!card.ShiJiUsed || card.Pile?.Type != PileType.Hand) throw new Exception("Timing did not return the same card instance.");
            Move(card, PileType.Exhaust);
            await card.ActivateShiJi(ctx, player);
            if (card.CanActivateShiJi(player) || card.Pile?.Type != PileType.Exhaust) throw new Exception("The same instance used timing twice.");
            var other = (YiXiangMingTianMoZhiShengXuanJiCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianMoZhiShengXuanJiCard), player, PileType.Exhaust);
            if (!other.CanActivateShiJi(player)) throw new Exception("Timing usage leaked to another same-name instance.");
            var clone = (YiXiangMingTianMoZhiShengXuanJiCard)card.CreateClone();
            if (clone.ShiJiUsed) throw new Exception("A new clone inherited timing usage.");
            var prop = typeof(YiXiangMingTianMoZhiShengXuanJiCard).GetProperty(nameof(card.ShiJiUsed))!;
            if (!Attribute.IsDefined(prop, typeof(SavedPropertyAttribute))) throw new Exception("Timing usage cannot survive save/reload.");
            new SavedProperties { bools = [new SavedProperties.SavedProperty<bool>(prop.Name, true)] }.Fill(other);
            if (other.CanActivateShiJi(player)) throw new Exception("Restored timing usage was ignored.");

            // 保留实际发动方法，只替换选牌和场景命令，核对取消/支付失败不会生成另一张声音。
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromHand"), prefix: new HarmonyMethod(typeof(YiXiangMingXuanJiProbe), nameof(SelectCost)));
            h.Patch(AccessTools.Method(typeof(CardCmd), "Exhaust"), prefix: new HarmonyMethod(typeof(YiXiangMingXuanJiProbe), nameof(Exhaust)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "AddGeneratedCardToCombat"), prefix: new HarmonyMethod(typeof(YiXiangMingXuanJiProbe), nameof(Generated)));
            foreach (var type in new[] { typeof(YiXiangMingTianShiZhiShengCard), typeof(YiXiangMingEMoZhiShengCard) })
            {
                var owner = NewPlayer();
                var voice = (YiXiangMingVoiceCard)ArrivalEquipmentProbe.AddCard(type, owner, PileType.Hand);
                var cost = ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingJueZeCard), owner, PileType.Hand);
                if (voice.StarLevel != 4 || !voice.Tags.Contains(SelectTags.Xing4)) throw new Exception("Voice card lost its four-star tag.");
                _exhausts = _generated = 0;
                _emptyChoice = true;
                if (await voice.TryActivateSpecialEffect(ctx, owner) || _exhausts != 0 || _generated != 0) throw new Exception("Canceled payment generated a counterpart.");
                _emptyChoice = false; _failMove = true;
                if (await voice.TryActivateSpecialEffect(ctx, owner) || _generated != 0 || cost.Pile?.Type != PileType.Hand) throw new Exception("Failed exhaust generated a counterpart.");
                _failMove = false;
                if (!await voice.TryActivateSpecialEffect(ctx, owner) || cost.Pile?.Type != PileType.Exhaust || _generated != 1
                    || !voice.PlacedAtEdgeByEffect || PileType.Discard.GetPile(owner).Cards.Contains(cost))
                    throw new Exception("Voice payment did not exhaust before placing its pair.");
            }
        }
        finally
        {
            h.UnpatchAll(h.Id);
            _ending = _failMove = _emptyChoice = false;
            EquipmentHookProbe.Models = [];
        }
        GD.Print("PASS: Selettrice retrieval, per-instance timing/persistence, genuine two-card zone, full/failed returns, voice four-star tags and exhaust payment/cancellation.");
    }

    private static Player NewPlayer()
    {
        var player = ArrivalEquipmentProbe.NewPlayer();
        AccessTools.Field(typeof(Player), "<Character>k__BackingField").SetValue(player, new MegaCrit.Sts2.Core.Models.Characters.Ironclad());
        return player;
    }
    private static void Move(CardModel card, PileType destination)
    { card.RemoveFromCurrentPile(silent: true); destination.GetPile(card.Owner).AddInternal(card); }
    public static bool Ending(ref bool __result) { __result = _ending; return false; }
    public static bool HandLimit(ref int __result) { __result = 10; return false; }
    public static bool Retrieve(Func<CardModel, bool> filter, RetrievalOptions options, ref Task<List<CardModel>> __result)
    {
        _retrievalFilter = filter; _retrievalOptions = options;
        __result = Task.FromResult(_emptyChoice ? new List<CardModel>() : [new YiXiangMingJueZeCard()]); return false;
    }
    public static bool Add(CardModel card, PileType newPileType, ref Task<CardPileAddResult> __result)
    {
        if (card is YiXiangMingTianMoZhiShengXuanJiCard x && newPileType == PileType.Hand
            && (!x.ShiJiUsed || x.CanActivateShiJi(x.Owner))) throw new Exception("Timing was not reserved before a reentrant move.");
        var old = card.Pile;
        if (!_failMove) Move(card, newPileType);
        __result = Task.FromResult(new CardPileAddResult { cardAdded = card, oldPile = old, targetPile = newPileType, success = !_failMove }); return false;
    }
    public static bool SelectCost(Player player, CardSelectorPrefs prefs, Func<CardModel, bool> filter, AbstractModel? source, ref Task<IEnumerable<CardModel>> __result)
    {
        if (source != null || prefs.MinSelect != 1 || prefs.MaxSelect != 1 || !prefs.RequireManualConfirmation) throw new Exception("Independent exhaust selection has an incorrect UI lifecycle.");
        __result = Task.FromResult<IEnumerable<CardModel>>(_emptyChoice ? [] : PileType.Hand.GetPile(player).Cards.Where(filter).Take(1).ToList()); return false;
    }
    public static bool Exhaust(CardModel card, ref Task<CardPileAddResult?> __result)
    {
        _exhausts++;
        var old = card.Pile;
        if (!_failMove) Move(card, PileType.Exhaust);
        __result = Task.FromResult<CardPileAddResult?>(new CardPileAddResult { cardAdded = card, oldPile = old, targetPile = PileType.Exhaust, success = !_failMove }); return false;
    }
    public static bool Generated(CardModel card, PileType newPileType, ref Task<CardPileAddResult> __result)
    { _generated++; Move(card, newPileType); __result = Task.FromResult(new CardPileAddResult { cardAdded = card, targetPile = newPileType, success = true }); return false; }
}
