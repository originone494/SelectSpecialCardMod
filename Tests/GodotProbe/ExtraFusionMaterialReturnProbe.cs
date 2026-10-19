using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Tool;

// 保留真实融合选材/支付、来源追踪和持久卡恢复；只替换选择UI、移堆命令与飞行演出边界。
public static class ExtraFusionMaterialReturnProbe
{
    private static CardModel? _fusion, _material;
    private static bool _failMove;

    public static async Task Check()
    {
        var h = new Harmony("scm.extra-fusion-material.return-probe");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"),
                prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(ArrivalEquipmentProbe.LiveCombat)));
            h.Patch(AccessTools.Method(typeof(LocalContext), nameof(LocalContext.IsMe), [typeof(Player)]),
                prefix: new HarmonyMethod(typeof(ExtraFusionMaterialReturnProbe), nameof(Remote)));
            h.Patch(AccessTools.Method(typeof(SelectToolCmd), nameof(SelectToolCmd.PlayFlyToPile)),
                prefix: new HarmonyMethod(typeof(ExtraFusionMaterialReturnProbe), nameof(NoFlight)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "Add",
                [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(ExtraFusionMaterialReturnProbe), nameof(Add)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.AddGeneratedCardToCombat)),
                prefix: new HarmonyMethod(typeof(ExtraFusionMaterialReturnProbe), nameof(Generated)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseACardScreen)),
                prefix: new HarmonyMethod(typeof(ExtraFusionMaterialReturnProbe), nameof(ChooseFusion)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGrid)),
                prefix: new HarmonyMethod(typeof(ExtraFusionMaterialReturnProbe), nameof(ChooseMaterial)));

            // 水仙登场检索梅洛并丢弃之后：用梅洛+手牌水仙融合露莎卡。
            var player = Player();
            var persistentWater = Persistent<ZhuLeiShuiXianCard>(player, Entry.ExtraUsedPile);
            var persistentRulkallos = Persistent<ZhuLeiLuShaKaCard>(player, Entry.ExtraPile);
            var water = ExtraDeckSeries.CreateSummonedCopy(player, persistentWater);
            PileType.Hand.GetPile(player).AddInternal(water);
            var merrli = ArrivalEquipmentProbe.AddCard(typeof(ZhuLeiMeiLuoCard), player, PileType.Discard);
            _fusion = persistentRulkallos; _material = water;
            int removed = 0, finished = 0, changed = 0;
            var hand = PileType.Hand.GetPile(player);
            hand.CardRemoved += c => { if (ReferenceEquals(c, water)) removed++; };
            hand.CardRemoveFinished += () => finished++;
            hand.ContentsChanged += () => changed++;
            await ZhuLeiFusion.TryFusion(new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(), player, merrli);
            if (water.Pile?.Type != Entry.ExtraUsedPile || hand.Cards.Contains(water)
                || persistentWater.Pile?.Type != Entry.ExtraPile || merrli.Pile?.Type != PileType.Draw
                || persistentRulkallos.Pile?.Type != Entry.ExtraUsedPile
                || hand.Cards.OfType<ZhuLeiLuShaKaCard>().Count() != 1
                || Entry.ExtraPile.GetPile(player).Cards.Count(c => c is ZhuLeiShuiXianCard) != 1
                || removed != 1 || finished != 1 || changed != 2)
                throw new Exception("Merrli+Kitkallos fusion failed to restore the persistent Kitkallos, retire its combat copy, notify hand removal or add exactly one Rulkallos.");
            if (await ExtraDeckSeries.ReturnExtraMonster(player, water) || removed != 1 || finished != 1)
                throw new Exception("Repeated return duplicated extra monsters or removal notifications.");

            // 手、Play、弃牌、消耗堆均需补齐静默来源通知；原卡仍是持久资产，副本不进入持久额外堆。
            foreach (var source in new[] { PileType.Hand, PileType.Play, PileType.Discard, PileType.Exhaust })
            {
                player = Player();
                var persistent = Persistent<ZhuLeiShuiXianCard>(player, Entry.ExtraUsedPile);
                var copy = ExtraDeckSeries.CreateSummonedCopy(player, persistent);
                var old = source.GetPile(player);
                old.AddInternal(copy);
                int gone = 0, done = 0, contents = 0;
                old.CardRemoved += c => { if (ReferenceEquals(c, copy)) gone++; };
                old.CardRemoveFinished += () => done++;
                old.ContentsChanged += () => contents++;
                if (!await ExtraDeckSeries.ReturnExtraMonster(player, copy)
                    || gone != 1 || done != 1 || contents != 1 || old.Cards.Contains(copy)
                    || persistent.Pile?.Type != Entry.ExtraPile || copy.Pile?.Type != Entry.ExtraUsedPile
                    || !player.RunState.ContainsCard(persistent) || player.RunState.ContainsCard(copy)
                    || persistent.HasBeenRemovedFromState || persistent.IsInCombat)
                    throw new Exception("Silent extra-material return broke source notifications, identity or persistence: " + source);
            }

            // 移堆命令失败不能误发手牌移除通知。
            player = Player();
            var failing = ArrivalEquipmentProbe.AddCard(typeof(ZhuLeiShuiXianCard), player, PileType.Hand);
            int failuresRemoved = 0;
            PileType.Hand.GetPile(player).CardRemoved += c => { if (ReferenceEquals(c, failing)) failuresRemoved++; };
            _failMove = true;
            if (await ExtraDeckSeries.ReturnExtraMonster(player, failing) || failuresRemoved != 0 || failing.Pile?.Type != PileType.Hand)
                throw new Exception("Failed extra return removed a hand card or reported success.");
            GD.Print("PASS: Merrli+hand Kitkallos fusion adds one Rulkallos, restores the original extra monster and retires/notifies its combat copy; all four source piles, repeat returns, persistence and failed movement remain correct. Actual hand visuals need in-game verification.");
        }
        finally { h.UnpatchAll(h.Id); _fusion = _material = null; _failMove = false; }
    }

    private static Player Player()
    {
        var player = ArrivalEquipmentProbe.NewPlayer();
        AccessTools.Field(typeof(RunState), "_allCards").SetValue(player.RunState, new List<CardModel>());
        AccessTools.Field(typeof(RunState), "_players").SetValue(player.RunState, new List<Player> { player });
        return player;
    }
    private static T Persistent<T>(Player player, PileType pile) where T : CardModel, new()
    {
        var card = new T();
        AccessTools.Property(typeof(AbstractModel), "IsMutable").SetValue(card, true);
        card.Owner = player;
        ((List<CardModel>)AccessTools.Field(typeof(RunState), "_allCards").GetValue(player.RunState)!).Add(card);
        pile.GetPile(player).AddInternal(card);
        return card;
    }
    public static bool Remote(ref bool __result) { __result = false; return false; }
    public static bool NoFlight() => false;
    public static bool ChooseFusion(IReadOnlyList<CardModel> cards, ref Task<CardModel?> __result)
    {
        if (_fusion == null || !cards.Contains(_fusion)) throw new Exception("Rulkallos was missing from the actual fusion candidates.");
        __result = Task.FromResult(_fusion); return false;
    }
    public static bool ChooseMaterial(IReadOnlyList<CardModel> cardsIn, ref Task<IEnumerable<CardModel>> __result)
    {
        if (_material == null || !cardsIn.Contains(_material)) throw new Exception("Kitkallos was missing from the actual material candidates.");
        __result = Task.FromResult<IEnumerable<CardModel>>([_material]); return false;
    }
    public static bool Add(CardModel card, PileType newPileType, bool skipVisuals, ref Task<CardPileAddResult> __result)
    { __result = Task.FromResult(Move(card, newPileType, skipVisuals)); return false; }
    public static bool Generated(CardModel card, PileType newPileType, ref Task<CardPileAddResult> __result)
    { __result = Task.FromResult(Move(card, newPileType, silent: false)); return false; }
    private static CardPileAddResult Move(CardModel card, PileType target, bool silent)
    {
        var old = card.Pile;
        if (!_failMove) { card.RemoveFromCurrentPile(silent); target.GetPile(card.Owner).AddInternal(card); }
        return new() { cardAdded = card, oldPile = old, targetPile = target, success = !_failMove };
    }
}
