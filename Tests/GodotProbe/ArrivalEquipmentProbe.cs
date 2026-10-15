using System.Collections;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Models.Capabilities;
using SelectSpecialCardMod.Scripts;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Tool;

// 不加载游戏资源；保留实际登场入口、抽牌标记、次数、防重入与装备组件逻辑。
public static class ArrivalEquipmentProbe
{
    private static TaskCompletionSource<bool>? _presentation;
    private static int _presentationCalls;
    private static int _arrivalCalls;
    private static int _exhaustCalls;

    public static async Task CheckArrivalPresentation()
    {
        var h = new Harmony("scm.arrival-presentation.godot-framework");
        var types = typeof(Entry).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(IDengChangCard).IsAssignableFrom(t)).ToArray();
        if (types.Length == 0) throw new Exception("No arrival cards found.");
        try
        {
            var vfx = typeof(Entry).Assembly.GetType("SelectSpecialCardMod.Scripts.Tool.SpecialEffectVfx", true)!;
            h.Patch(AccessTools.Method(vfx, "Play"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(Present)));
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(LiveCombat)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "Add", [typeof(CardModel), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(DrawAdd)));
            foreach (var type in types)
            {
                h.Patch(AccessTools.Method(type, "DengChangSpecial"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(Arrival)));
                var player = NewPlayer();
                var card = AddCard(type, player, PileType.Hand);
                _presentation = new(); _presentationCalls = 0; _arrivalCalls = 0;
                // 真实 DrawWindowPatch 的标记窗口必须阻止动画与登场效果。
                await DrawWindowPatch.AddDrawnCard(card, card.Pile!, CardPilePosition.Bottom, null, true);
                if (_presentationCalls != 0 || _arrivalCalls != 0) throw new Exception("Drawing triggered arrival: " + type.Name);
                var pending = DengChangHook.OnCardAddedToHand(card);
                if (pending.IsCompleted || _arrivalCalls != 0 || _presentationCalls != 1)
                    throw new Exception("Arrival did not wait for the presentation: " + type.Name);
                await DengChangHook.OnCardAddedToHand(card);
                if (_presentationCalls != 1) throw new Exception("Arrival presentation reentered: " + type.Name);
                _presentation.SetResult(true);
                await pending;
                if (_arrivalCalls != 1 || card.Pile?.Type != PileType.Hand) throw new Exception("Arrival effect/hand changed: " + type.Name);
                await DengChangHook.OnCardAddedToHand(card);
                await DengChangHook.OnCardAddedToHand(AddCard(type, player, PileType.Hand));
                if (_presentationCalls != 1 || _arrivalCalls != 1) throw new Exception("Arrival once-per-name rule failed: " + type.Name);

                _presentation = new(); _arrivalCalls = 0;
                var interrupted = DengChangHook.OnCardAddedToHand(AddCard(type, NewPlayer(), PileType.Hand));
                _presentation.SetResult(false);
                await interrupted;
                if (_arrivalCalls != 0) throw new Exception("Interrupted arrival executed: " + type.Name);

                _presentation = new();
                var movedCard = AddCard(type, NewPlayer(), PileType.Hand);
                var moved = DengChangHook.OnCardAddedToHand(movedCard);
                movedCard.Pile!.RemoveInternal(movedCard);
                _presentation.SetResult(true);
                await moved;
                if (_arrivalCalls != 0) throw new Exception("Removed arrival source executed: " + type.Name);
            }
        }
        finally { h.UnpatchAll(h.Id); _presentation = null; }
        GD.Print($"PASS: all {types.Length} arrival cards await presentation; draws skip it; reentry and same-name repeats are blocked; interrupted/removed sources skip effects.");
    }

    public static async Task CheckEquipmentLifecycle()
    {
        var h = new Harmony("scm.equipment-discard-lifecycle.godot-framework");
        var mapping = (IDictionary)AccessTools.Field(typeof(KeMoEquip), "EquipCards").GetValue(null)!;
        var runtime = typeof(ModelCapabilityRegistry).Assembly;
        var capabilityPatch = runtime.GetType("STS2RitsuLib.Models.Capabilities.Patches.ModelCapabilityHookListenerPatches+RunStateHookListenersPatch", true)!;
        var combatCapabilityPatch = runtime.GetType("STS2RitsuLib.Models.Capabilities.Patches.ModelCapabilityHookListenerPatches+CombatStateHookListenersPatch", true)!;
        try
        {
            h.Patch(AccessTools.Method(typeof(KeMoEquip), "ExhaustEquipment"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(Exhaust)));
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsInProgress"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(InProgress)));
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsEnding"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(LiveCombat)));
            h.Patch(AccessTools.Method(typeof(RunState), "IterateHookListeners", [typeof(ICombatState)]),
                prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"), postfix: new HarmonyMethod(AccessTools.Method(capabilityPatch, "Postfix")));
            h.Patch(AccessTools.Method(typeof(CombatState), "IterateHookListeners", Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"), postfix: new HarmonyMethod(AccessTools.Method(combatCapabilityPatch, "Postfix")));

            var target = EquippedCard();
            await Move(target, PileType.Discard); CheckRetained(target);
            await Move(target, PileType.Draw); CheckRetained(target);
            await Move(target, PileType.Hand); CheckRetained(target);
            if (target.DynamicVars.Damage.PreviewValue != target.DynamicVars.Damage.BaseValue + 18)
                throw new Exception("Redrawn equipment damage preview lost its bonus.");
            await Move(target, PileType.Play); CheckRetained(target);
            await AfterPlay(target, PileType.Discard); CheckRetained(target);
            await Move(target, PileType.Discard); CheckRetained(target);
            await Move(target, PileType.Exhaust); CheckConsumed(target);

            // 原有非弃牌去向的打出仍在 AfterCardPlayed 消耗，而非打出前消耗。
            target = EquippedCard();
            await Move(target, PileType.Play); CheckRetained(target);
            await AfterPlay(target, PileType.Exhaust); CheckConsumed(target);

            // 原有手牌直接回抽牌堆仍卸装，与弃牌堆洗回抽牌堆区分。
            target = EquippedCard();
            await Move(target, PileType.Draw); CheckConsumed(target);

            target = EquippedCard();
            await Move(target, PileType.Discard); CheckRetained(target);
            await Move(target, Entry.ChuWaiPile); CheckConsumed(target);

            target = EquippedCard();
            await Move(target, PileType.Discard);
            await Move(target, PileType.Draw); CheckRetained(target);
            await Move(target, PileType.Play); CheckRetained(target); // 从抽牌堆自动打出
            await AfterPlay(target, PileType.Discard); CheckRetained(target);
            await Move(target, PileType.Discard); CheckRetained(target);
            await Move(target, PileType.None); CheckConsumed(target);

            target = EquippedCard();
            await Move(target, PileType.Play);
            await AfterPlay(target, PileType.Discard); CheckRetained(target);
            await Move(target, PileType.Hand); CheckConsumed(target); // 实际去向被改为回手

            target = EquippedCard();
            target.AddKeyword(SelectSpecialCardMod.Scripts.Custom.SelectKeywords.ChuWai);
            await Move(target, PileType.Play); CheckRetained(target);
            var exileLocation = MegaCrit.Sts2.Core.Hooks.Hook.ModifyCardPlayResultLocation(target.CombatState!, target, false, default,
                new CardLocation(target.Owner, PileType.Discard, CardPilePosition.Bottom), out _);
            if (exileLocation.pileType != Entry.ChuWaiPile) throw new Exception("Exile keyword did not set the play result.");
            await AfterPlay(target, exileLocation.pileType); CheckConsumed(target);
            await Move(target, exileLocation.pileType); CheckConsumed(target);
            if (mapping.Count != 0) throw new Exception("Equipment mappings leaked after detach.");
        }
        finally { h.UnpatchAll(h.Id); EquipmentHookProbe.Models = []; }
        GD.Print("PASS: stacked equipment survives discard, shuffle, redraw and discard-result plays; previews retain bonuses; exhaust, exile, removal and other old non-discard paths consume equipment exactly once.");

        CardModel EquippedCard()
        {
            var player = NewPlayer();
            var card = AddCard(typeof(KeMoMoJianKeCard), player, PileType.Hand);
            EquipmentHookProbe.Models = [card];
            _exhaustCalls = 0;
            foreach (int rating in new[] { 1, 2 })
            {
                var equipment = AddCard(typeof(KeMoZhenHunGuanCard), player, Entry.EquipPile);
                var cap = new KeMoEquipCapability { LinkRating = rating, EquipName = "fixture" };
                card.AddCapability(cap);
                mapping.Add(cap, equipment);
            }
            return card;
        }

        void CheckRetained(CardModel card)
        {
            var caps = card.Capabilities().All.OfType<KeMoEquipCapability>().ToArray();
            if (caps.Length != 2 || _exhaustCalls != 0 || caps.Any(c => !mapping.Contains(c)
                || ((CardModel)mapping[c]!).Pile?.Type != Entry.EquipPile))
                throw new Exception("Equipment was lost on a preserving transition.");
        }

        void CheckConsumed(CardModel card)
        {
            if (KeMoEquip.EquipCount(card) != 0 || _exhaustCalls != 2 || Entry.EquipPile.GetPile(card.Owner).Cards.Count != 0
                || PileType.Exhaust.GetPile(card.Owner).Cards.Count(c => c is KeMoZhenHunGuanCard) != 2)
                throw new Exception("Equipment cleanup did not consume both actual equipment cards exactly once.");
        }
    }

    private static async Task Move(CardModel card, PileType destination)
    {
        if (destination == PileType.None)
            await CardPileCmd.RemoveFromCombat(card, skipVisuals: true);
        else
        {
            var result = await CardPileCmd.Add(card, destination, skipVisuals: true);
            if (!result.success || card.Pile?.Type != destination) throw new Exception("Native fixture move failed.");
        }
    }

    private static async Task AfterPlay(CardModel card, PileType result)
    {
        var play = new CardPlay
        {
            Card = card, Player = card.Owner, ResultPile = result, Target = null,
            Resources = default, IsAutoPlay = false, PlayIndex = 0, PlayCount = 1,
        };
        await MegaCrit.Sts2.Core.Hooks.Hook.AfterCardPlayed(card.CombatState!, new BlockingPlayerChoiceContext(), play);
    }

    internal static Player NewPlayer()
    {
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        var creature = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
        creature.CombatState = new CombatState(runState: run);
        AccessTools.Field(typeof(Creature), "_currentHp").SetValue(creature, 10);
        AccessTools.Field(typeof(Player), "<Creature>k__BackingField").SetValue(player, creature);
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        AccessTools.Field(typeof(Player), "_runPiles").SetValue(player, Array.Empty<CardPile>());
        AccessTools.Property(typeof(Player), "PlayerCombatState").SetValue(player, new PlayerCombatState(player));
        return player;
    }

    internal static CardModel AddCard(Type type, Player player, PileType pile)
    {
        var card = (CardModel)Activator.CreateInstance(type)!;
        AccessTools.Property(typeof(AbstractModel), "IsMutable").SetValue(card, true);
        player.Creature.CombatState!.AddCard(card, player);
        pile.GetPile(player).AddInternal(card);
        return card;
    }

    public static bool Present(ref Task<bool> __result) { _presentationCalls++; __result = _presentation!.Task; return false; }
    public static bool LiveCombat(ref bool __result) { __result = false; return false; }
    public static bool InProgress(ref bool __result) { __result = true; return false; }
    public static bool Arrival(ref Task __result) { _arrivalCalls++; __result = Task.CompletedTask; return false; }
    public static bool DrawAdd(CardModel card, ref Task<CardPileAddResult> __result) { __result = DrawAddCore(card); return false; }
    private static async Task<CardPileAddResult> DrawAddCore(CardModel card)
    {
        await DengChangHook.OnCardAddedToHand(card);
        return new CardPileAddResult { cardAdded = card, success = true, targetPile = PileType.Hand };
    }
    public static bool Exhaust(CardModel equipCard, ref Task<CardPileAddResult?> __result)
    {
        _exhaustCalls++;
        var oldPile = equipCard.Pile!;
        oldPile.RemoveInternal(equipCard);
        PileType.Exhaust.GetPile(equipCard.Owner).AddInternal(equipCard);
        __result = Task.FromResult<CardPileAddResult?>(new CardPileAddResult
            { cardAdded = equipCard, success = true, oldPile = oldPile, targetPile = PileType.Exhaust });
        return false;
    }
}
