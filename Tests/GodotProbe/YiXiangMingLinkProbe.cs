using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Tool;

public static class YiXiangMingLinkProbe
{
    private static YiXiangMingZhiShiWuPower? _counter;
    private static bool _cancel, _fail, _ending, _changeCounterAfterFirst;
    private static int _damageCalls, _blockCalls, _plays;
    private static IReadOnlyList<Creature> _enemies = [];
    private static CardModel? _pick;

    public static async Task Check()
    {
        var h = new Harmony("scm.vaalmonica-links.godot-framework");
        try
        {
            // 与现有额外堆探针一样使用真实 Ritsu 动态牌堆，不加载游戏场景。
            var runtime = typeof(STS2RitsuLib.CardPiles.ModCardPileRegistry).Assembly;
            foreach (var (method, name, prefix) in new[] {
                (AccessTools.Method(typeof(CardPile), "Get"), "ModCardPileGetPatch", true),
                (AccessTools.Method(typeof(PileTypeExtensions), "IsCombatPile"), "ModCardPileIsCombatPatch", false),
                (AccessTools.PropertyGetter(typeof(Player), "Piles"), "ModCardPilePlayerPilesPatch", false),
                (AccessTools.PropertyGetter(typeof(PlayerCombatState), "AllPiles"), "ModCardPileAllPilesPatch", false) })
            {
                var patch = new HarmonyMethod(AccessTools.Method(runtime.GetType("STS2RitsuLib.CardPiles.Patches." + name, true)!, prefix ? "Prefix" : "Postfix"));
                h.Patch(method, prefix: prefix ? patch : null, postfix: prefix ? null : patch);
            }
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"), prefix: new HarmonyMethod(typeof(YiXiangMingLinkProbe), nameof(Ending)));
            h.Patch(AccessTools.Method(typeof(RunState), "IterateHookListeners", [typeof(ICombatState)]), prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"));
            h.Patch(AccessTools.Method(typeof(CombatState), "IterateHookListeners", Type.EmptyTypes), prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"));
            EquipmentHookProbe.Models = [];
            h.Patch(AccessTools.PropertyGetter(typeof(CombatState), "HittableEnemies"), prefix: new HarmonyMethod(typeof(YiXiangMingLinkProbe), nameof(Enemies)));
            var getPower = typeof(Creature).GetMethods().Single(m => m.Name == "GetPower" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0).MakeGenericMethod(typeof(YiXiangMingZhiShiWuPower));
            h.Patch(getPower, prefix: new HarmonyMethod(typeof(YiXiangMingLinkProbe), nameof(GetCounter)));
            h.Patch(AccessTools.Method(typeof(PowerCmd), "ModifyAmount"), prefix: new HarmonyMethod(typeof(YiXiangMingLinkProbe), nameof(Spend)));
            h.Patch(AccessTools.Method(typeof(CardCmd), "Exhaust"), prefix: new HarmonyMethod(typeof(YiXiangMingLinkProbe), nameof(Exhaust)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromHand"), prefix: new HarmonyMethod(typeof(YiXiangMingLinkProbe), nameof(ChooseMaterial)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromSimpleGrid"), prefix: new HarmonyMethod(typeof(YiXiangMingLinkProbe), nameof(ChooseSpell)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "Add", [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]), prefix: new HarmonyMethod(typeof(YiXiangMingLinkProbe), nameof(Add)));
            h.Patch(AccessTools.Method(typeof(CardCmd), "AutoPlay", [typeof(PlayerChoiceContext), typeof(CardModel), typeof(Creature), typeof(AutoPlayType), typeof(bool), typeof(bool)]), prefix: new HarmonyMethod(typeof(YiXiangMingLinkProbe), nameof(AutoPlay)));
            h.Patch(AccessTools.Method(typeof(DengChangHook), "OnCardAddedToHand"), prefix: new HarmonyMethod(typeof(YiXiangMingLinkProbe), nameof(SkipArrival)));
            h.Patch(AccessTools.Method(typeof(CreatureCmd), "Damage", [typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay)]), prefix: new HarmonyMethod(typeof(YiXiangMingLinkProbe), nameof(Damage)));
            h.Patch(AccessTools.Method(typeof(CreatureCmd), "GainBlock", [typeof(Creature), typeof(BlockVar), typeof(CardPlay), typeof(bool)]), prefix: new HarmonyMethod(typeof(YiXiangMingLinkProbe), nameof(Block)));

            var player = ArrivalEquipmentProbe.NewPlayer();
            AccessTools.Field(typeof(Player), "<Character>k__BackingField").SetValue(player, new MegaCrit.Sts2.Core.Models.Characters.Ironclad());
            AccessTools.Property(typeof(CombatState), "CurrentSide").SetValue(player.Creature.CombatState, CombatSide.Player);
            _counter = new YiXiangMingZhiShiWuPower();
            AccessTools.Property(typeof(AbstractModel), "IsMutable").SetValue(_counter, true);
            AccessTools.Property(typeof(PowerModel), "Owner").SetValue(_counter, player.Creature);
            _counter.SetAmount(2, true);
            var ctx = new BlockingPlayerChoiceContext();
            var angel = (YiXiangMingYiShenGuangYaoTianShiCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingYiShenGuangYaoTianShiCard), player, Entry.ExtraPile);
            var devil = (YiXiangMingShenYiFengBaoEMoCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingShenYiFengBaoEMoCard), player, Entry.ExtraPile);
            foreach (var card in new YiXiangMingLinkCard[] { angel, devil })
                if (card.Type != CardType.Attack || card.Rarity != CardRarity.Ancient || card.EnergyCost.Canonical != 0 || card.DynamicVars.Damage.IntValue != 25
                    || !card.Tags.Contains(SelectSpecialCardMod.Scripts.Custom.SelectTags.Link1)) throw new Exception("New Link1 metadata is wrong.");
            var material = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), player, PileType.Hand);
            if (angel.CanSummonFromExtraDeck(player)) throw new Exception("Two counters allowed a summon.");
            _counter.SetAmount(3, true);
            if (!angel.CanSummonFromExtraDeck(player) || !devil.CanSummonFromExtraDeck(player)) throw new Exception("Exactly three counters must allow summoning.");
            _cancel = true;
            if (await angel.PaySummonCost(ctx, player) || material.Pile?.Type != PileType.Hand) throw new Exception("Cancellation paid a summon cost.");
            _cancel = false; _fail = true;
            if (await angel.PaySummonCost(ctx, player) || !angel.CanSummonFromExtraDeck(player)) throw new Exception("Failed exhaustion consumed summon usage.");
            _fail = false;
            if (!await angel.PaySummonCost(ctx, player) || material.Pile?.Type != PileType.Exhaust || angel.CanSummonFromExtraDeck(player)) throw new Exception("Summon did not exhaust or retain once-per-combat usage.");
            var sameName = (YiXiangMingLinkCard)ArrivalEquipmentProbe.AddCard(angel.GetType(), player, Entry.ExtraPile);
            ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), player, PileType.Hand);
            if (sameName.CanSummonFromExtraDeck(player) || !devil.CanSummonFromExtraDeck(player)) throw new Exception("Summon usage is not independent per card name.");
            Move(angel, PileType.Hand); Move(devil, PileType.Hand);
            _enemies = [ArrivalEquipmentProbe.NewPlayer().Creature, ArrivalEquipmentProbe.NewPlayer().Creature];
            _changeCounterAfterFirst = true;
            await angel.DengChangSpecial(ctx, player);
            if (_damageCalls != 3) throw new Exception("Arrival damage failed to snapshot the initial counter count.");
            _counter.SetAmount(3, true);
            await devil.DengChangSpecial(ctx, player);
            if (_blockCalls != 3) throw new Exception("Arrival block failed to repeat three individual gains.");
            _changeCounterAfterFirst = false;
            _counter.SetAmount(3, true);
            if (!await angel.TryActivateSpecialEffect(ctx, player) || angel.ReturnCharges != 3 || _counter.Amount != 0) throw new Exception("Three-counter payment did not grant three returns.");
            var normal = new CardLocation(player, PileType.Discard, CardPilePosition.Bottom);
            for (int i = 3; i > 0; i--)
            {
                var location = angel.ModifyCardPlayResultLocation(angel, false, default, normal);
                angel.ModifyCardPlayResultLocation(angel, true, default, normal); // 预览/查询不扣次数。
                if (location.pileType != PileType.Hand || angel.ReturnCharges != i) throw new Exception("Return preview consumed charges.");
                Move(angel, PileType.Play);
                await angel.AfterModifyingCardPlayResultLocation(angel, location);
                Move(angel, PileType.Hand);
                await angel.AfterCardChangedPiles(angel, PileType.Play, null);
                await angel.AfterCardChangedPiles(angel, PileType.Play, null);
                if (angel.ReturnCharges != i - 1) throw new Exception("One actual return did not consume exactly one charge.");
            }
            if (angel.ModifyCardPlayResultLocation(angel, false, default, normal).pileType != PileType.Discard) throw new Exception("A fourth return was allowed.");
            _counter.SetAmount(3, true);
            await angel.TryActivateSpecialEffect(ctx, player);
            await angel.AfterSideTurnEnd(ctx, CombatSide.Enemy, [player.Creature]);
            if (angel.ReturnCharges != 3) throw new Exception("Another side cleared return charges.");
            await angel.AfterSideTurnEnd(ctx, CombatSide.Player, [player.Creature]);
            if (angel.ReturnCharges != 0 || angel.ModifyCardPlayResultLocation(angel, false, default, normal).pileType != PileType.Discard) throw new Exception("Unused returns survived the owner's turn end.");
            var spell = ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingJueZeCard), player, PileType.Exhaust);
            var removedSpell = ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingQingZhuCard), player, Entry.ChuWaiPile);
            _cancel = true;
            if (await devil.TryActivateSpecialEffect(ctx, player) || _plays != 0) throw new Exception("Canceled spell selection played a card.");
            _cancel = false;
            foreach (var card in new[] { spell, removedSpell })
            { _pick = card; if (!await devil.TryActivateSpecialEffect(ctx, player) || card.Pile?.Type != PileType.Discard) throw new Exception("Exhaust/removed spell did not autoplay with its normal destination."); }
            if (_plays != 2) throw new Exception("Storm Devil played the wrong number of spells.");
            _ending = true;
            if (angel.CanActivateSpecialEffect(player) || devil.CanActivateSpecialEffect(player)) throw new Exception("Ending combat allowed a special effect.");
        }
        finally { h.UnpatchAll(h.Id); _counter = null; _cancel = _fail = _ending = _changeCounterAfterFirst = false; _damageCalls = _blockCalls = _plays = 0; _pick = null; _enemies = []; EquipmentHookProbe.Models = []; }
        GD.Print("PASS: three-counter threshold, one material, cancellation/failure, per-name summon cap, repeated arrival snapshots, three actual returns and end-of-turn expiry, combined exhaust/removed spell selection.");
    }

    private static void Move(CardModel card, PileType pile) { card.RemoveFromCurrentPile(silent: true); pile.GetPile(card.Owner).AddInternal(card); }
    public static bool Ending(ref bool __result) { __result = _ending; return false; }
    public static bool Enemies(ref IReadOnlyList<Creature> __result) { __result = _enemies; return false; }
    public static bool GetCounter(Creature __instance, ref YiXiangMingZhiShiWuPower? __result) { __result = _counter?.Owner == __instance ? _counter : null; return false; }
    public static bool Spend(PowerModel power, decimal offset, ref Task<int> __result) { power.SetAmount(power.Amount + (int)offset, true); __result = Task.FromResult(power.Amount); return false; }
    public static bool ChooseMaterial(Player player, CardSelectorPrefs prefs, Func<CardModel, bool> filter, AbstractModel? source, ref Task<IEnumerable<CardModel>> __result)
    { if (source != null || prefs.MinSelect != 0 || !prefs.RequireManualConfirmation) throw new Exception("Summon selection must allow cancellation and restore the hand immediately."); __result = Task.FromResult<IEnumerable<CardModel>>(_cancel ? [] : PileType.Hand.GetPile(player).Cards.Where(filter).Take(1).ToList()); return false; }
    public static bool Exhaust(CardModel card, ref Task<CardPileAddResult?> __result)
    { var old = card.Pile; if (!_fail) Move(card, PileType.Exhaust); __result = Task.FromResult<CardPileAddResult?>(new CardPileAddResult { cardAdded = card, oldPile = old, targetPile = PileType.Exhaust, success = !_fail }); return false; }
    public static bool ChooseSpell(IReadOnlyList<CardModel> cardsIn, CardSelectorPrefs prefs, ref Task<IEnumerable<CardModel>> __result)
    { if (prefs.MinSelect != 1 || prefs.MaxSelect != 1 || !prefs.RequireManualConfirmation || (_pick != null && !_cancel && !cardsIn.Contains(_pick))) throw new Exception("Spell selection omitted a valid pile or manual confirmation."); __result = Task.FromResult<IEnumerable<CardModel>>(_cancel ? [] : [_pick!]); return false; }
    public static bool Add(CardModel card, PileType newPileType, ref Task<CardPileAddResult> __result)
    { Move(card, newPileType); __result = Task.FromResult(new CardPileAddResult { cardAdded = card, success = true, targetPile = newPileType }); return false; }
    public static bool AutoPlay(CardModel card, ref Task __result) { _plays++; Move(card, PileType.Discard); __result = Task.CompletedTask; return false; }
    public static bool SkipArrival(ref Task __result) { __result = Task.CompletedTask; return false; }
    public static bool Damage(IEnumerable<Creature> targets, decimal amount, ref Task<IEnumerable<DamageResult>> __result)
    { if (amount != 10 || targets.Count() != 2) throw new Exception("Wrong arrival damage/targets."); _damageCalls++; if (_changeCounterAfterFirst) _counter!.SetAmount(9, true); __result = Task.FromResult<IEnumerable<DamageResult>>([]); return false; }
    public static bool Block(BlockVar blockVar, ref Task<decimal> __result)
    { if (blockVar.BaseValue != 10) throw new Exception("Wrong repeated block amount."); _blockCalls++; if (_changeCounterAfterFirst) _counter!.SetAmount(0, true); __result = Task.FromResult(10m); return false; }
}
