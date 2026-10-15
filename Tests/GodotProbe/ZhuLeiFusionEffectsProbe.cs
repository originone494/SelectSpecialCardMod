using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Tool;

public static class ZhuLeiFusionEffectsProbe
{
    private static IReadOnlyList<Creature> _enemies = [];
    private static readonly List<decimal> WeakAmounts = [];
    private static int _plays;

    public static async Task Check()
    {
        var h = new Harmony("scm.tearlaments-fusion-effects.godot-framework");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), "LiveCombat"));
            h.Patch(AccessTools.PropertyGetter(typeof(CombatState), "HittableEnemies"), prefix: new HarmonyMethod(typeof(ZhuLeiFusionEffectsProbe), nameof(Enemies)));
            var vfx = typeof(ZhuLeiKaLeiDuoHaTeCard).Assembly.GetType("SelectSpecialCardMod.Scripts.Tool.ZhuChangEffectVfx", true)!;
            h.Patch(AccessTools.Method(vfx, "Play"), prefix: new HarmonyMethod(typeof(ZhuLeiFusionEffectsProbe), nameof(Present)));
            var applyWeak = typeof(PowerCmd).GetMethods().Single(m => m.Name == "Apply" && m.IsGenericMethodDefinition
                && m.GetParameters()[1].ParameterType == typeof(Creature)).MakeGenericMethod(typeof(WeakPower));
            h.Patch(applyWeak, prefix: new HarmonyMethod(typeof(ZhuLeiFusionEffectsProbe), nameof(Weak)));
            var player = ArrivalEquipmentProbe.NewPlayer();
            var rng = AccessTools.Property(typeof(RunState), "Rng");
            rng.SetValue(player.RunState, Activator.CreateInstance(rng.PropertyType, ["fusion-effects-probe"]));
            var ctx = new BlockingPlayerChoiceContext();
            _enemies = [ArrivalEquipmentProbe.NewPlayer().Creature, ArrivalEquipmentProbe.NewPlayer().Creature];
            var kaleido = (ZhuLeiKaLeiDuoHaTeCard)ArrivalEquipmentProbe.AddCard(typeof(ZhuLeiKaLeiDuoHaTeCard), player, PileType.Hand);
            WeakAmounts.Clear();
            await kaleido.AfterCardChangedPiles(kaleido, PileType.None, null);
            AssertWeak(2); // 融合副本因效果入手，两个敌人各获得2层。
            await kaleido.AfterCardChangedPiles(kaleido, PileType.Discard, null);
            AssertWeak(4); // 再次因效果回手也触发，没有额外同名战斗次数限制。

            var reno = ArrivalEquipmentProbe.AddCard(typeof(ZhuLeiLeiNuoHaTeCard), player, PileType.Discard);
            await kaleido.OnSongMu(ctx, player, reno);
            AssertWeak(6);
            await kaleido.OnSongMu(ctx, player, kaleido);
            await kaleido.OnSongMu(ctx, player, ArrivalEquipmentProbe.AddCard(typeof(ZhuLeiPiXianCard), player, PileType.Hand));
            AssertWeak(6); // 自身送墓回手不重复算，非怪兽送墓不触发。

            // 用真实抽牌标记窗口调用入手回调，抽牌不会触发驻场的登场分支。
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "Add", [typeof(CardModel), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(ZhuLeiFusionEffectsProbe), nameof(DrawAdd)));
            await DrawWindowPatch.AddDrawnCard(kaleido, kaleido.Pile!, CardPilePosition.Bottom, null, true);
            AssertWeak(6);

            h.Patch(AccessTools.Method(typeof(SongMuHook), nameof(SongMuHook.AutoPlayFromDiscard)),
                prefix: new HarmonyMethod(typeof(ZhuLeiFusionEffectsProbe), nameof(AutoPlay)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "Add", [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(ZhuLeiResolutionProbe), nameof(ZhuLeiResolutionProbe.Add)));
            var first = (ZhuLeiLuShaKaCard)ArrivalEquipmentProbe.AddCard(typeof(ZhuLeiLuShaKaCard), player, PileType.Discard);
            var second = (ZhuLeiLuShaKaCard)ArrivalEquipmentProbe.AddCard(typeof(ZhuLeiLuShaKaCard), player, PileType.Discard);
            _plays = 0;
            await first.SongMuSpecial(ctx, player);
            await second.SongMuSpecial(ctx, player);
            if (_plays != 2 || first.Pile?.Type != PileType.Hand || second.Pile?.Type != PileType.Hand)
                throw new Exception("Rulkallos instances shared their graveyard use or failed to return to hand.");
            MoveToDiscard(first);
            await first.SongMuSpecial(ctx, player);
            AccessTools.Property(typeof(PlayerCombatState), "TurnNumber").SetValue(player.PlayerCombatState, player.PlayerCombatState!.TurnNumber + 1);
            await first.SongMuSpecial(ctx, player);
            if (_plays != 2 || first.Pile?.Type != PileType.Discard)
                throw new Exception("Rulkallos reused its instance effect in the same or a later turn.");
            var clone = (ZhuLeiLuShaKaCard)player.Creature.CombatState!.CloneCard(first);
            PileType.Discard.GetPile(player).AddInternal(clone);
            await clone.SongMuSpecial(ctx, player);
            if (_plays != 3 || clone.Pile?.Type != PileType.Hand)
                throw new Exception("A new Rulkallos instance inherited the source's spent graveyard use.");
        }
        finally { h.UnpatchAll(h.Id); _enemies = []; WeakAmounts.Clear(); _plays = 0; }
        GD.Print("PASS: Kaleido arrival/retrieval and monster graveyard events apply two Weak to each enemy; repeated arrivals remain available and draws/non-monsters/self broadcasts skip; Rulkallos instances act independently, stay spent across turns, and cloned instances have a fresh use.");
    }

    private static void AssertWeak(int calls)
    {
        if (WeakAmounts.Count != calls || WeakAmounts.Any(n => n != 2))
            throw new Exception("Kaleido's Weak amount or trigger count changed.");
    }

    private static void MoveToDiscard(CardModel card)
    {
        card.Pile!.RemoveInternal(card);
        PileType.Discard.GetPile(card.Owner).AddInternal(card);
    }

    public static bool Enemies(ref IReadOnlyList<Creature> __result) { __result = _enemies; return false; }
    public static bool Present(ref Task<bool> __result) { __result = Task.FromResult(true); return false; }
    public static bool Weak(decimal amount, ref Task<WeakPower?> __result)
    {
        WeakAmounts.Add(amount);
        __result = Task.FromResult<WeakPower?>(null);
        return false;
    }
    public static bool AutoPlay(ref Task __result) { _plays++; __result = Task.CompletedTask; return false; }
    public static bool DrawAdd(CardModel card, ref Task<CardPileAddResult> __result)
    {
        __result = DrawAddCore(card);
        return false;
    }
    private static async Task<CardPileAddResult> DrawAddCore(CardModel card)
    {
        await card.AfterCardChangedPiles(card, PileType.Draw, null);
        return new CardPileAddResult { cardAdded = card, success = true, targetPile = PileType.Hand };
    }
}
