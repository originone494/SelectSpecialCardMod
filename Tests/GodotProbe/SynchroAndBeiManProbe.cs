using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Tool;

// 执行真实杯满消耗效果、攻击指令构造和右键同调素材流程；选择和消耗边界可控。
public static class SynchroAndBeiManProbe
{
    private static int _selections, _cancelAt, _payments, _failPaymentAt;
    private static bool _ending, _endAfterFirst, _movePartnerAfterFirst;
    private static CardModel? _buffTarget, _partner;
    private static AttackCommand? _attack;
    private static HashSet<CardModel>? _allowedMaterials;
    private static List<CardModel> _retrieved = [];
    private static int _retrievals;

    public static async Task Check()
    {
        var h = new Harmony("scm.synchro-beiman.godot-framework");
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
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"),
                prefix: new HarmonyMethod(typeof(SynchroAndBeiManProbe), nameof(Ending)));
            h.Patch(AccessTools.Method(typeof(RunState), "IterateHookListeners", [typeof(ICombatState)]),
                prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"));
            h.Patch(AccessTools.Method(typeof(CombatState), "IterateHookListeners", Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"));
            EquipmentHookProbe.Models = [];
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromHand"),
                prefix: new HarmonyMethod(typeof(SynchroAndBeiManProbe), nameof(Choose)));
            h.Patch(AccessTools.Method(typeof(CardCmd), "Exhaust"),
                prefix: new HarmonyMethod(typeof(SynchroAndBeiManProbe), nameof(Exhaust)));
            h.Patch(AccessTools.Method(typeof(AttackCommand), "Execute", [typeof(PlayerChoiceContext)]),
                prefix: new HarmonyMethod(typeof(SynchroAndBeiManProbe), nameof(Attack)));
            h.Patch(AccessTools.Method(typeof(TongDiao), "Pace"),
                prefix: new HarmonyMethod(typeof(SynchroAndBeiManProbe), nameof(Pace)));
            h.Patch(AccessTools.Method(typeof(SelectToolCmd), nameof(SelectToolCmd.RetrieverCard)),
                prefix: new HarmonyMethod(typeof(SynchroAndBeiManProbe), nameof(Retrieve)));
            await CheckBuff();
            await CheckMaterials();
            await CheckTenpaiEffects();
            GD.Print("PASS: Bei Man damage, dragon synchro materials and boundaries, Paidra on-play synchro/special search, no-product selection suppression, and removal of Tenpai return/play limits.");
        }
        finally
        {
            h.UnpatchAll(h.Id); Reset(); EquipmentHookProbe.Models = [];
        }
    }

    private static CardModel Add(Player player, CardModel template, PileType pile)
    {
        var card = player.Creature.CombatState!.CreateCard(template, player);
        pile.GetPile(player).AddInternal(card, silent: true);
        return card;
    }

    private static async Task CheckBuff()
    {
        var player = ArrivalEquipmentProbe.NewPlayer();
        var ctx = new BlockingPlayerChoiceContext();
        var field = (BeiManDeCanHuanZhuangCard)Add(player, ModelDb.Card<BeiManDeCanHuanZhuangCard>(), PileType.Exhaust);
        foreach (var template in new CardModel[] { ModelDb.Card<CanHuanShengLongShuangChaTianLongCard>(), ModelDb.Card<CanHuanChaoLongSanJiTianLongCard>() })
        {
            Reset();
            var target = Add(player, template, PileType.Hand);
            var sibling = Add(player, template, PileType.Hand);
            decimal initial = target.DynamicVars.Damage.BaseValue;
            _buffTarget = target;
            await field.AfterCardExhausted(ctx, field, false);
            if (target.DynamicVars.Damage.BaseValue != initial * 2 || target.DynamicVars.Damage.PreviewValue != initial * 2
                || sibling.DynamicVars.Damage.BaseValue != initial || template.DynamicVars.Damage.BaseValue != initial)
                throw new Exception("Bei Man did not double the chosen dragon or changed another instance.");
            var play = new CardPlay { Card = target, Player = player, Target = player.Creature, Resources = default,
                IsAutoPlay = false, ResultPile = PileType.Discard, PlayIndex = 0, PlayCount = 1 };
            await (Task)AccessTools.Method(target.GetType(), "OnPlay").Invoke(target, [ctx, play])!;
            if (_attack?.ModelSource != target
                || Convert.ToDecimal(AccessTools.Field(typeof(AttackCommand), "_damagePerHit").GetValue(_attack)) != initial * 2)
                throw new Exception("The actual attack command ignored the doubled damage.");
            await field.AfterCardExhausted(ctx, field, false);
            if (target.DynamicVars.Damage.BaseValue != initial * 4) throw new Exception("A second Bei Man effect did not stack on the chosen instance.");
        }
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", ".."));
        var texts = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
            System.IO.File.ReadAllText(System.IO.Path.Combine(root, "SelectSpecialCardMod", "localization", "zhs", "cards.json")))!;
        foreach (var key in new[] { "CAN_HUAN_SHENG_LONG_SHUANG_CHA_TIAN_LONG", "CAN_HUAN_CHAO_LONG_SAN_JI_TIAN_LONG" })
            if (!texts[$"SELECT_SPECIAL_CARD_MOD_CARD_{key}_CARD.description"].Contains("{Damage:diff()}点伤害"))
                throw new Exception("A synchro dragon still uses hard-coded damage text.");
    }

    private static async Task CheckMaterials()
    {
        foreach (var scenario in new[] { "success", "cancel-tuner", "cancel-partner", "fail-first", "fail-second", "end-after-first", "move-partner" })
        {
            Reset();
            var player = ArrivalEquipmentProbe.NewPlayer();
            var red = Add(player, ModelDb.Card<TianBeiLongHongZhongCard>(), PileType.Hand);
            var green = Add(player, ModelDb.Card<TianBeiLongLvFaCard>(), PileType.Hand);
            var dragon = (CanHuanShengLongShuangChaTianLongCard)Add(player, ModelDb.Card<CanHuanShengLongShuangChaTianLongCard>(), Entry.ExtraPile);
            var large = (CanHuanChaoLongSanJiTianLongCard)Add(player, ModelDb.Card<CanHuanChaoLongSanJiTianLongCard>(), Entry.ExtraPile);
            if (!dragon.CanSummonFromExtraDeck(player) || large.CanSummonFromExtraDeck(player)
                || !ExtraDeckSummonHook.SummonableIn(player).Contains(dragon))
                throw new Exception("Red Center + Green did not expose the seven-star dragon in the extra-deck menu.");
            _partner = green;
            _cancelAt = scenario == "cancel-tuner" ? 1 : scenario == "cancel-partner" ? 2 : 0;
            _failPaymentAt = scenario == "fail-first" ? 1 : scenario == "fail-second" ? 2 : 0;
            _endAfterFirst = scenario == "end-after-first";
            _movePartnerAfterFirst = scenario == "move-partner";
            bool paid = await dragon.PaySummonCost(new BlockingPlayerChoiceContext(), player);
            int expected = scenario.StartsWith("cancel") ? 0 : scenario is "fail-first" or "end-after-first" or "move-partner" ? 1 : 2;
            if (paid != (scenario == "success") || _payments != expected || dragon.Pile?.Type != Entry.ExtraPile)
                throw new Exception("Synchro cancellation/payment boundary failed: " + scenario);
            if (scenario == "success" && (red.Pile?.Type != PileType.Exhaust || green.Pile?.Type != PileType.Exhaust))
                throw new Exception("Successful manual synchro did not consume both real materials.");
        }
        Reset();
        var owner = ArrivalEquipmentProbe.NewPlayer();
        var tuner = Add(owner, ModelDb.Card<CanHuanShengLongShuangChaTianLongCard>(), PileType.Hand);
        _partner = Add(owner, ModelDb.Card<TianBeiLongLvFaCard>(), PileType.Hand);
        var ten = (CanHuanChaoLongSanJiTianLongCard)Add(owner, ModelDb.Card<CanHuanChaoLongSanJiTianLongCard>(), Entry.ExtraPile);
        if (!ten.CanSummonFromExtraDeck(owner) || !await ten.PaySummonCost(new BlockingPlayerChoiceContext(), owner)
            || tuner.Pile?.Type != PileType.Exhaust || _partner.Pile?.Type != PileType.Exhaust)
            throw new Exception("Seven-star dragon tuner + three-star Green failed to summon the ten-star dragon.");
        Reset();
        owner = ArrivalEquipmentProbe.NewPlayer();
        Add(owner, ModelDb.Card<TianBeiLongLvFaCard>(), PileType.Hand);
        Add(owner, ModelDb.Card<TianBeiLongBaiBanCard>(), PileType.Hand);
        var seven = (CanHuanShengLongShuangChaTianLongCard)Add(owner, ModelDb.Card<CanHuanShengLongShuangChaTianLongCard>(), Entry.ExtraPile);
        if (seven.CanSummonFromExtraDeck(owner)) throw new Exception("Two non-tuners were accepted as synchro materials.");
    }

    private static void Reset()
    {
        _selections = _cancelAt = _payments = _failPaymentAt = 0;
        _ending = _endAfterFirst = _movePartnerAfterFirst = false;
        _buffTarget = _partner = null; _attack = null;
        _allowedMaterials = null; _retrieved = []; _retrievals = 0;
    }

    private static async Task CheckTenpaiEffects()
    {
        Reset();
        var ctx = new BlockingPlayerChoiceContext();
        var player = ArrivalEquipmentProbe.NewPlayer();
        var white = Add(player, ModelDb.Card<TianBeiLongBaiBanCard>(), PileType.Play);
        var red = Add(player, ModelDb.Card<TianBeiLongHongZhongCard>(), PileType.Hand);
        var sevenTuner = Add(player, ModelDb.Card<CanHuanShengLongShuangChaTianLongCard>(), PileType.Hand);
        await PlayWhite();
        if (_selections != 0 || _payments != 0 || _retrievals != 0 || white.Pile?.Type != PileType.Play)
            throw new Exception("Paidra searched, returned to hand or opened synchro selection with no extra-deck product.");

        var ten = Add(player, ModelDb.Card<CanHuanChaoLongSanJiTianLongCard>(), Entry.ExtraPile);
        _cancelAt = 1; _allowedMaterials = [sevenTuner];
        await PlayWhite();
        if (_selections != 1 || _payments != 0 || white.Pile?.Type != PileType.Play)
            throw new Exception("Paidra in Play failed to offer the available ten-star product, or cancellation consumed materials.");
        Move(ten, PileType.Exhaust);
        Add(player, ModelDb.Card<CanHuanShengLongShuangChaTianLongCard>(), Entry.ExtraPile);
        _selections = 0; _allowedMaterials = [red];
        await PlayWhite();
        if (_selections != 1 || _payments != 0 || _retrievals != 0 || white.Pile?.Type != PileType.Play)
            throw new Exception("Paidra did not filter materials by the actual available extra-deck product.");

        // 四张天杯龙均不自动回手、不限制本回合再次打出；保留红中的每次检索。
        foreach (var template in new CardModel[] { ModelDb.Card<TianBeiLongBaiBanCard>(), ModelDb.Card<TianBeiLongHongZhongCard>(),
                     ModelDb.Card<TianBeiLongLvFaCard>(), ModelDb.Card<TianBeiLongHuanLuCard>() })
        {
            Reset();
            var owner = ArrivalEquipmentProbe.NewPlayer();
            var card = Add(owner, template, PileType.Play);
            for (int i = 0; i < 2; i++)
            {
                await card.AfterCardPlayed(ctx, MakePlay(card));
                if (card.Pile?.Type != PileType.Play
                    || !(bool)AccessTools.PropertyGetter(card.GetType(), "IsPlayable").Invoke(card, null)!)
                    throw new Exception("Tenpai retained its return-to-hand or once-per-turn play rule: " + card.GetType().Name);
            }
            if (_retrievals != (card is TianBeiLongHongZhongCard ? 2 : 0) || _selections != 0)
                throw new Exception("After-play effects changed beyond the removed Tenpai rule.");
        }

        Reset();
        var searchOwner = ArrivalEquipmentProbe.NewPlayer();
        var paidra = (TianBeiLongBaiBanCard)Add(searchOwner, ModelDb.Card<TianBeiLongBaiBanCard>(), PileType.Hand);
        if (!paidra.CanActivateSpecialEffect(searchOwner)
            || await paidra.TryActivateSpecialEffect(ctx, searchOwner) || !paidra.CanActivateSpecialEffect(searchOwner))
            throw new Exception("Paidra special search required a synchro product or cancellation spent its combat use.");
        _retrieved = [Add(searchOwner, ModelDb.Card<BeiManDeCanHuanZhuangCard>(), PileType.Hand)];
        if (!await paidra.TryActivateSpecialEffect(ctx, searchOwner) || paidra.CanActivateSpecialEffect(searchOwner)
            || await paidra.TryActivateSpecialEffect(ctx, searchOwner) || _retrievals != 2)
            throw new Exception("Paidra special search did not enforce its combat limit.");

        async Task PlayWhite() => await (Task)AccessTools.Method(typeof(TianBeiLongBaiBanCard), "OnPlay")
            .Invoke(white, [ctx, MakePlay(white)])!;
        CardPlay MakePlay(CardModel card) => new() { Card = card, Player = card.Owner, Target = card.Owner.Creature,
            Resources = default, IsAutoPlay = false, ResultPile = PileType.Discard, PlayIndex = 0, PlayCount = 1 };
    }
    private static void Move(CardModel card, PileType pile)
    { card.RemoveFromCurrentPile(silent: true); pile.GetPile(card.Owner).AddInternal(card, silent: true); }
    public static bool Ending(ref bool __result) { __result = _ending; return false; }
    public static bool Choose(Player player, Func<CardModel, bool> filter, AbstractModel? source, CardSelectorPrefs prefs,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (source != null || prefs.MinSelect != 1 || prefs.MaxSelect != 1 || !prefs.RequireManualConfirmation)
            throw new Exception("Independent selection lost its mandatory range, confirmation or immediate hand restoration.");
        _selections++;
        if (_allowedMaterials != null && !_allowedMaterials.SetEquals(PileType.Hand.GetPile(player).Cards.Where(filter)))
            throw new Exception("Synchro offered a material without a matching extra-deck product.");
        var picked = _buffTarget ?? PileType.Hand.GetPile(player).Cards.FirstOrDefault(filter);
        if (picked != null && !filter(picked)) throw new Exception("Selection omitted its valid target.");
        __result = Task.FromResult<IEnumerable<CardModel>>(_selections == _cancelAt || picked == null ? [] : [picked]);
        return false;
    }
    public static bool Exhaust(CardModel card, ref Task<CardPileAddResult?> __result)
    {
        _payments++; var old = card.Pile; bool success = _payments != _failPaymentAt;
        if (success) Move(card, PileType.Exhaust);
        if (_payments == 1 && _endAfterFirst) _ending = true;
        if (_payments == 1 && _movePartnerAfterFirst) Move(_partner!, PileType.Discard);
        __result = Task.FromResult<CardPileAddResult?>(new CardPileAddResult { success = success, cardAdded = card, oldPile = old, targetPile = PileType.Exhaust });
        return false;
    }
    public static bool Attack(AttackCommand __instance, ref Task<AttackCommand> __result)
    { _attack = __instance; __result = Task.FromResult(__instance); return false; }
    public static bool Pace(ref Task __result) { __result = Task.CompletedTask; return false; }
    public static bool Retrieve(Func<CardModel, bool> filter, ref Task<List<CardModel>> __result)
    {
        _retrievals++;
        if (_retrieved.Any(c => !filter(c))) throw new Exception("Paidra search used the wrong series filter.");
        __result = Task.FromResult(_retrieved.ToList()); return false;
    }
}
