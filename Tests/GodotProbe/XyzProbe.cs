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
using MegaCrit.Sts2.Core.Saves.Runs;
using SelectSpecialCardMod.Scripts;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;
using STS2RitsuLib.Models.Capabilities;

public static class XyzProbe
{
    private static bool _cancel;
    private static int _overlayMoves, _failOverlayAt, _blockCalls;
    private static readonly List<CardModel> Exhausted = [];

    public static async Task Check()
    {
        var h = new Harmony("scm.xyz-materials.godot-framework");
        try
        {
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
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), "LiveCombat"));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromHand"), prefix: new HarmonyMethod(typeof(XyzProbe), nameof(SelectMaterials)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromSimpleGrid"), prefix: new HarmonyMethod(typeof(XyzProbe), nameof(SelectDetach)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "Add", [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]), prefix: new HarmonyMethod(typeof(XyzProbe), nameof(Add)));
            h.Patch(AccessTools.Method(typeof(CardCmd), "Exhaust"), prefix: new HarmonyMethod(typeof(XyzProbe), nameof(Exhaust)));
            h.Patch(AccessTools.Method(typeof(CreatureCmd), "GainBlock", [typeof(Creature), typeof(BlockVar), typeof(CardPlay), typeof(bool)]), prefix: new HarmonyMethod(typeof(XyzProbe), nameof(Block)));

            foreach (var link in new YiXiangMingLinkCard[] { new YiXiangMingYiShenGuangYaoTianShiCard(), new YiXiangMingShenYiFengBaoEMoCard() })
                if (!link.Tags.Contains(SelectTags.ExtraMonster)) throw new Exception("A Vaalmonica Link1 is missing its inherited extra-monster tag.");
            var player = ArrivalEquipmentProbe.NewPlayer();
            AccessTools.Field(typeof(Player), "<Character>k__BackingField").SetValue(player, new MegaCrit.Sts2.Core.Models.Characters.Ironclad());
            var ctx = new BlockingPlayerChoiceContext();
            var original = (No41NiShuiMoShouCard)ArrivalEquipmentProbe.AddCard(typeof(No41NiShuiMoShouCard), player, Entry.ExtraPile);
            var first = ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianShiZhiShengCard), player, PileType.Hand);
            var second = ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingEMoZhiShengCard), player, PileType.Hand);
            var attack = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), player, PileType.Hand);
            var skill = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.DefendIronclad), player, PileType.Hand);
            if (original.EnergyCost.Canonical != 1 || original.Type != CardType.Attack || original.Rarity != CardRarity.Ancient
                || original.StarLevel != 0 || original.XyzRank != 4 || !original.Keywords.Contains(CardKeyword.Retain)
                || !original.Tags.Contains(SelectTags.ExtraMonster) || !original.Tags.Contains(SelectTags.ChaoLiang)
                || original.DynamicVars.Damage.IntValue != 21 || original.DynamicVars.Block.IntValue != 20)
                throw new Exception("No.41 metadata or independent rank is wrong.");
            if (!original.CanSummonFromExtraDeck(player) || original.IsValidMaterial(attack)) throw new Exception("Four-star material filtering is wrong.");
            _cancel = true;
            if (await original.PaySummonCost(ctx, player) || first.Pile?.Type != PileType.Hand || original.MaterialGroupId != 0) throw new Exception("Canceled overlay changed cards.");
            _cancel = false;
            if (await XyzMaterials.Stage(original, [first, first])) throw new Exception("One physical card was counted twice.");
            int removals = 0;
            PileType.Hand.GetPile(player).CardRemoved += card => { if (card == first || card == second) removals++; };
            if (!await original.PaySummonCost(ctx, player) || original.XyzMaterialCards.Count != 2 || Exhausted.Count != 0
                || first.Pile?.Type != Entry.XyzMaterialPile || second.Pile?.Type != Entry.XyzMaterialPile || removals != 2)
                throw new Exception("Overlay must preserve the two real cards without exhausting and remove both hand nodes.");
            var marker = first.Capabilities().All.OfType<XyzMaterialCapability>().Single();
            var loadedMarker = new XyzMaterialCapability(); loadedMarker.LoadState(marker.SaveState(), 1);
            if (loadedMarker.Group != marker.Group || marker.Group <= 0) throw new Exception("Material binding JSON did not round-trip.");
            var materialClone = first.CreateClone();
            var markerClone = (XyzMaterialCapability)marker.CloneFor(materialClone);
            if (markerClone.Group != 0 || marker.Group <= 0) throw new Exception("Cloning a material stole or duplicated its original binding.");
            var copy = (No41NiShuiMoShouCard)player.Creature.CombatState!.CloneCard(original);
            if (copy.MaterialGroupId != 0) throw new Exception("A normal monster clone inherited somebody else's materials.");
            original.TransferSummonState(copy);
            await AddCore(copy, PileType.Hand, false);
            if (original.MaterialGroupId != 0 || copy.XyzMaterialCards.Count != 2 || !copy.XyzMaterialCards.Contains(first)) throw new Exception("Summon did not transfer material identities to the battle copy.");
            var saved = new SavedProperties
            {
                ints = [new SavedProperties.SavedProperty<int>(nameof(copy.MaterialGroupId), copy.MaterialGroupId)],
                strings = [new SavedProperties.SavedProperty<string>(nameof(copy.MaterialOwnerKey), copy.MaterialOwnerKey)],
            };
            var restored = (No41NiShuiMoShouCard)copy.CreateClone(); saved.Fill(restored);
            if (restored.XyzMaterialCards.Count != 2) throw new Exception("Restored monster binding cannot find its real materials.");
            restored.MaterialGroupId = 0;
            object[] args = { new AbstractModel[] { copy, first, second, marker, attack } };
            AccessTools.Method(typeof(XyzMaterialHooksPatch), "Postfix").Invoke(null, args);
            var listeners = ((IEnumerable<AbstractModel>)args[0]).ToList();
            if (listeners.Contains(first) || listeners.Contains(second) || listeners.Contains(marker) || !listeners.Contains(copy) || !listeners.Contains(attack))
                throw new Exception("Overlaid card/capability effects remained active or ordinary cards disappeared.");

            var rollback = (No41NiShuiMoShouCard)ArrivalEquipmentProbe.AddCard(typeof(No41NiShuiMoShouCard), player, Entry.ExtraPile);
            var third = ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianShiZhiShengCard), player, PileType.Hand);
            var fourth = ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingEMoZhiShengCard), player, PileType.Hand);
            _overlayMoves = 0; _failOverlayAt = 2;
            if (await XyzMaterials.Stage(rollback, [third, fourth]) || third.Pile?.Type != PileType.Hand || fourth.Pile?.Type != PileType.Hand
                || rollback.MaterialGroupId != 0 || copy.XyzMaterialCards.Count != 2)
                throw new Exception("Partial overlay failure leaked cards or damaged another host's materials.");
            _failOverlayAt = 0;
            if (copy.ShouldPlay(attack, AutoPlayType.None) || copy.ShouldPlay(attack, AutoPlayType.Default)
                || !copy.ShouldPlay(copy, AutoPlayType.Default) || !copy.ShouldPlay(skill, AutoPlayType.None))
                throw new Exception("Resident attack restrictions do not cover manual/auto play or allow the correct exceptions.");
            var otherAttack = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), ArrivalEquipmentProbe.NewPlayer(), PileType.Hand);
            if (!copy.ShouldPlay(otherAttack, AutoPlayType.None)) throw new Exception("No.41 blocked another player's attack.");
            await copy.BeforeSideTurnEnd(ctx, CombatSide.Enemy, [player.Creature]);
            if (_blockCalls != 0) throw new Exception("Enemy turn end granted block.");
            await copy.BeforeSideTurnEnd(ctx, CombatSide.Player, [player.Creature]);
            await copy.BeforeSideTurnEnd(ctx, CombatSide.Player, [player.Creature]);
            if (_blockCalls != 1) throw new Exception("Resident block did not occur exactly once at owner turn end.");
            SetTurn(player, 1);
            await copy.AfterPlayerTurnStart(ctx, player);
            await copy.AfterPlayerTurnStart(ctx, player);
            if (copy.XyzMaterialCards.Count != 1 || Exhausted.Count != 1 || !copy.ShouldRetainThisTurn) throw new Exception("First upkeep did not exhaust exactly one material and retain the monster.");
            SetTurn(player, 2); await copy.AfterPlayerTurnStart(ctx, player);
            if (copy.XyzMaterialCards.Count != 0 || Exhausted.Count != 2 || copy.Pile?.Type != PileType.Hand) throw new Exception("Second material removal prematurely exhausted the host.");
            if (!await XyzMaterials.Stage(rollback, [third, fourth]) || rollback.MaterialGroupId == copy.MaterialGroupId) throw new Exception("An empty active monster's group ID was reused.");
            var played = (No41NiShuiMoShouCard)player.Creature.CombatState!.CloneCard(rollback); rollback.TransferSummonState(played);
            await AddCore(played, PileType.Hand, false);
            await AddCore(played, PileType.Play, false);
            if (played.XyzMaterialCards.Count != 2) throw new Exception("Entering the play area detached materials too early.");
            await AddCore(played, PileType.Discard, false);
            if (played.XyzMaterialCards.Count != 0 || third.Pile?.Type != PileType.Exhaust || fourth.Pile?.Type != PileType.Exhaust) throw new Exception("Leaving hand/play did not exhaust the remaining real materials.");
            SetTurn(player, 3); await copy.AfterPlayerTurnStart(ctx, player);
            if (copy.Pile?.Type != PileType.Exhaust || !copy.ShouldPlay(attack, AutoPlayType.Default)) throw new Exception("Empty-material upkeep failed to exhaust No.41 and lift its attack restriction.");
            await AddCore(third, PileType.Hand, false); await AddCore(fourth, PileType.Hand, false);
            if (!await XyzMaterials.Stage(rollback, [third, fourth])) throw new Exception("Could not prepare remove-from-combat fixture.");
            var removed = (No41NiShuiMoShouCard)player.Creature.CombatState!.CloneCard(rollback);
            rollback.TransferSummonState(removed); await AddCore(removed, PileType.Hand, false);
            removed.RemoveFromCurrentPile(silent: true);
            // 原版 RemoveFromCombat 广播前已移出牌堆，宿主自身不在监听器列表里。
            await (Task)AccessTools.Method(typeof(XyzMaterialBindingCleanupPatch), "Complete").Invoke(null,
                [Task.CompletedTask, removed, PileType.Hand])!;
            if (third.Pile?.Type != PileType.Exhaust || fourth.Pile?.Type != PileType.Exhaust || removed.MaterialGroupId != 0)
                throw new Exception("Global departure cleanup missed a monster absent from the listener list.");
            await AddCore(third, PileType.Hand, false); await AddCore(fourth, PileType.Hand, false);
            if (!await XyzMaterials.Stage(rollback, [third, fourth])) throw new Exception("Could not prepare staged-copy cancellation fixture.");
            var canceledCopy = (No41NiShuiMoShouCard)player.Creature.CombatState!.CloneCard(rollback);
            rollback.TransferSummonState(canceledCopy);
            await rollback.CancelSummonState(ctx, player, canceledCopy);
            if (third.Pile?.Type != PileType.Hand || fourth.Pile?.Type != PileType.Hand || canceledCopy.MaterialGroupId != 0
                || Entry.XyzMaterialPile.GetPile(player).Cards.Count != 0) throw new Exception("Failed summon after transfer leaked staged materials.");
        }
        finally { h.UnpatchAll(h.Id); _cancel = false; _overlayMoves = _failOverlayAt = _blockCalls = 0; Exhausted.Clear(); }
        GD.Print("PASS: inherited extra tags, No.41 rank/retain, exact two-card overlay, cancellation/partial rollback, identity transfer, binding save/clone isolation, inactive material hooks, independent stacks, upkeep exhaustion, block, manual/auto attack lock and departure cleanup.");
    }

    private static void SetTurn(Player player, int turn) => AccessTools.Property(typeof(PlayerCombatState), "TurnNumber").SetValue(player.PlayerCombatState, turn);
    public static bool SelectMaterials(Player player, CardSelectorPrefs prefs, Func<CardModel, bool> filter, AbstractModel? source, ref Task<IEnumerable<CardModel>> __result)
    { if (source != null || prefs.MinSelect != 2 || prefs.MaxSelect != 2 || !prefs.RequireManualConfirmation || !prefs.Cancelable) throw new Exception("Overlay must select two hand cards together with cancellation and immediate hand restoration."); __result = Task.FromResult<IEnumerable<CardModel>>(_cancel ? [] : PileType.Hand.GetPile(player).Cards.Where(filter).Take(2).ToList()); return false; }
    public static bool SelectDetach(IReadOnlyList<CardModel> cardsIn, CardSelectorPrefs prefs, ref Task<IEnumerable<CardModel>> __result)
    { if (prefs.MinSelect != 1 || prefs.MaxSelect != 1 || !prefs.RequireManualConfirmation) throw new Exception("Detach must require one explicit material selection."); __result = Task.FromResult<IEnumerable<CardModel>>(cardsIn.Take(1).ToList()); return false; }
    public static bool Add(CardModel card, PileType newPileType, bool skipVisuals, ref Task<CardPileAddResult> __result)
    { __result = AddCore(card, newPileType, skipVisuals); return false; }
    private static async Task<CardPileAddResult> AddCore(CardModel card, PileType pile, bool silent)
    {
        var old = card.Pile;
        if (pile == Entry.XyzMaterialPile && ++_overlayMoves == _failOverlayAt)
            return new CardPileAddResult { success = false, cardAdded = card, oldPile = old, targetPile = pile };
        card.RemoveFromCurrentPile(silent); pile.GetPile(card.Owner).AddInternal(card, silent: silent);
        await card.AfterCardChangedPiles(card, old?.Type ?? PileType.None, null);
        if (old?.Type == Entry.XyzMaterialPile && !XyzMaterials.IsOverlaid(card)) XyzMaterials.ClearBinding(card);
        return new CardPileAddResult { success = true, cardAdded = card, oldPile = old, targetPile = pile };
    }
    public static bool Exhaust(CardModel card, ref Task<CardPileAddResult?> __result) { __result = ExhaustCore(card); return false; }
    private static async Task<CardPileAddResult?> ExhaustCore(CardModel card) { var result = await AddCore(card, PileType.Exhaust, false); Exhausted.Add(card); return result; }
    public static bool Block(BlockVar blockVar, ref Task<decimal> __result)
    { if (blockVar.BaseValue != 20) throw new Exception("No.41 block is not 20."); _blockCalls++; __result = Task.FromResult(20m); return false; }
}
