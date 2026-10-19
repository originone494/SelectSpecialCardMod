using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Tool;

// 执行真实标签、伤害倍率和同调流程；只替换战斗状态、选择和命令边界。
public static class CardRuleRegressionProbe
{
    private enum Scenario
    {
        Normal, Cancel, EndDuringWait, SourceLeavesDuringChoice, FirstPaymentFails,
        SecondPaymentFails, EndAfterFirst, EndAfterSecond, MaterialLeavesAfterFirst,
        ExtraLeavesAfterFirst, CombatChangesAfterFirst, EndDuringExtraConsume
    }

    private static Scenario _scenario;
    private static bool _ending;
    private static int _payments, _consumes, _summons, _selections;
    private static CardModel? _chosen, _extra;
    private static IReadOnlyList<Creature> _enemies = [];

    public static async Task Check()
    {
        var h = new Harmony("scm.card-rule-regressions.godot-framework");
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
                prefix: new HarmonyMethod(typeof(CardRuleRegressionProbe), nameof(Ending)));
            h.Patch(AccessTools.PropertyGetter(typeof(CombatState), "HittableEnemies"),
                prefix: new HarmonyMethod(typeof(CardRuleRegressionProbe), nameof(Enemies)));
            await CheckHuanLu();
            CheckArena();

            h.Patch(AccessTools.Method(typeof(TongDiao), "Pace"),
                prefix: new HarmonyMethod(typeof(CardRuleRegressionProbe), nameof(Pace)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromHand"),
                prefix: new HarmonyMethod(typeof(CardRuleRegressionProbe), nameof(Select)));
            h.Patch(AccessTools.Method(typeof(CardCmd), "Exhaust"),
                prefix: new HarmonyMethod(typeof(CardRuleRegressionProbe), nameof(Exhaust)));
            h.Patch(AccessTools.Method(typeof(ExtraDeckSeries), "Consume"),
                prefix: new HarmonyMethod(typeof(CardRuleRegressionProbe), nameof(Consume)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "Add",
                [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(CardRuleRegressionProbe), nameof(Add)));
            foreach (var scenario in Enum.GetValues<Scenario>())
                await CheckTongDiao(scenario);
            await CheckTongDiao(Scenario.Normal, fromPlay: true);
            await CheckTongDiao(Scenario.Cancel, fromPlay: true);
            GD.Print("PASS: per-instance Genroku tags, allied/enemy arena damage, and 12 synchro payment/cancellation/combat-boundary scenarios.");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            _ending = false; _chosen = _extra = null; _enemies = [];
        }
    }

    private static async Task CheckHuanLu()
    {
        var player = ArrivalEquipmentProbe.NewPlayer();
        var combat = player.Creature.CombatState!;
        var template = ModelDb.Card<TianBeiLongHuanLuCard>();
        var templateTags = template.Tags.ToHashSet(); // 先读取模板，重现标签缓存被预热的场景。
        var first = (TianBeiLongHuanLuCard)combat.CreateCard(template, player);
        var second = (TianBeiLongHuanLuCard)combat.CreateCard(template, player);
        _ = first.Tags.ToArray(); _ = second.Tags.ToArray();
        await first.DengChangSpecial(new BlockingPlayerChoiceContext(), player);
        var fresh = (TianBeiLongHuanLuCard)combat.CreateCard(template, player);
        var leveledClone = (TianBeiLongHuanLuCard)combat.CloneCard(first);
        foreach (var card in new[] { template, second, fresh })
            if (card.StarLevel != 3 || !card.Tags.Contains(SelectTags.Xing3)
                || card.Tags.Contains(SelectTags.Xing4) || card.Tags.Contains(SelectTags.TiaoZheng))
                throw new Exception("Genroku arrival changed the canonical card or an independent copy.");
        foreach (var card in new[] { first, leveledClone })
            if (card.StarLevel != 4 || card.Tags.Contains(SelectTags.Xing3)
                || !card.Tags.Contains(SelectTags.Xing4) || !card.Tags.Contains(SelectTags.TiaoZheng)
                || !card.Keywords.Contains(SelectKeywords.TiaoZheng))
                throw new Exception("Genroku's own level/tuner tags or its leveled clone became inconsistent.");
        if (!templateTags.SetEquals(template.Tags)) throw new Exception("Canonical Genroku tags were mutated.");
    }

    private static void CheckArena()
    {
        var player = ArrivalEquipmentProbe.NewPlayer();
        AccessTools.Field(typeof(Creature), "<Side>k__BackingField").SetValue(player.Creature, CombatSide.Player);
        Creature MakeMonster(CombatSide side)
        {
            var creature = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
            AccessTools.Field(typeof(Creature), "<Monster>k__BackingField").SetValue(creature, ModelDb.Monsters.First());
            AccessTools.Field(typeof(Creature), "<Side>k__BackingField").SetValue(creature, side);
            creature.CombatState = player.Creature.CombatState;
            return creature;
        }
        var enemy = MakeMonster(CombatSide.Enemy);
        var pet = MakeMonster(CombatSide.Player);
        var power = ModelDb.Power<KaiSaiDouJiChangPower>().ToMutable();
        AccessTools.Property(typeof(PowerModel), "Owner").SetValue(power, player.Creature);
        foreach (int count in new[] { 0, 1, 2 })
        {
            _enemies = Enumerable.Range(0, count).Select(_ => MakeMonster(CombatSide.Enemy)).ToList();
            foreach (var props in new[] { ValueProp.Move, ValueProp.Unpowered })
            {
                if (power.ModifyDamageMultiplicative(player.Creature, 10, props, enemy, null, null) != (count > 1 ? 0 : 1)
                    || power.ModifyDamageMultiplicative(enemy, 10, props, pet, null, null) != 1
                    || power.ModifyDamageMultiplicative(enemy, 10, props, player.Creature, null, null) != 1
                    || power.ModifyDamageMultiplicative(player.Creature, 10, props, null, null, null) != 1)
                    throw new Exception("Arena suppressed an ally or failed to suppress multiple enemies.");
            }
        }
    }

    private static async Task CheckTongDiao(Scenario scenario, bool fromPlay = false)
    {
        _scenario = scenario; _ending = false;
        _payments = _consumes = _summons = _selections = 0;
        var player = ArrivalEquipmentProbe.NewPlayer();
        var combat = player.Creature.CombatState!;
        var source = combat.CreateCard(ModelDb.Card<TianBeiLongBaiBanCard>(), player);
        _chosen = combat.CreateCard(ModelDb.Card<TianBeiLongHuanLuCard>(), player);
        await ((TianBeiLongHuanLuCard)_chosen).DengChangSpecial(new BlockingPlayerChoiceContext(), player);
        // 额外堆查询仍执行真实生产代码，只替换最终支付和移动命令。
        _extra = combat.CreateCard(ModelDb.Card<CanHuanShengLongShuangChaTianLongCard>(), player);
        Move(source, fromPlay ? PileType.Play : PileType.Hand); Move(_chosen, PileType.Hand); Move(_extra, Entry.ExtraPile);
        await TongDiao.TryTongDiao(new BlockingPlayerChoiceContext(), player, source);
        int expectedPayments = scenario switch
        {
            Scenario.Cancel or Scenario.EndDuringWait or Scenario.SourceLeavesDuringChoice => 0,
            Scenario.FirstPaymentFails or Scenario.EndAfterFirst or Scenario.MaterialLeavesAfterFirst
                or Scenario.ExtraLeavesAfterFirst or Scenario.CombatChangesAfterFirst => 1,
            _ => 2
        };
        bool consume = scenario is Scenario.Normal or Scenario.EndDuringExtraConsume;
        if (_payments != expectedPayments || _consumes != (consume ? 1 : 0)
            || _summons != (scenario == Scenario.Normal ? 1 : 0)
            || _extra.Pile?.Type != (consume ? Entry.ExtraUsedPile
                : scenario == Scenario.ExtraLeavesAfterFirst ? PileType.Exhaust : Entry.ExtraPile))
            throw new Exception("Synchro continued after an invalid payment/changed battle: " + scenario);
        if (scenario == Scenario.EndDuringWait && _selections != 0)
            throw new Exception("Synchro opened selection after combat ended during pacing.");
        if (scenario == Scenario.Normal && (_chosen.Pile?.Type != PileType.Exhaust || source.Pile?.Type != PileType.Exhaust))
            throw new Exception("Successful synchro did not exhaust both real materials.");
    }

    private static void Move(CardModel card, PileType pile)
    { card.RemoveFromCurrentPile(silent: true); pile.GetPile(card.Owner).AddInternal(card, silent: true); }

    public static bool Ending(ref bool __result) { __result = _ending; return false; }
    public static bool Enemies(ref IReadOnlyList<Creature> __result) { __result = _enemies; return false; }
    public static bool Pace(ref Task __result)
    { if (_scenario == Scenario.EndDuringWait) _ending = true; __result = Task.CompletedTask; return false; }
    public static bool Select(Player player, Func<CardModel, bool> filter, ref Task<IEnumerable<CardModel>> __result)
    {
        _selections++;
        if (_chosen == null || !filter(_chosen)) throw new Exception("Valid tuner was absent from synchro selection.");
        if (_scenario == Scenario.SourceLeavesDuringChoice)
            Move(PileType.Hand.GetPile(player).Cards.OfType<TianBeiLongBaiBanCard>().Single(), PileType.Discard);
        __result = Task.FromResult<IEnumerable<CardModel>>(_scenario == Scenario.Cancel ? [] : [_chosen]);
        return false;
    }
    public static bool Exhaust(CardModel card, ref Task<CardPileAddResult?> __result)
    {
        int index = ++_payments;
        var old = card.Pile;
        bool failed = (_scenario == Scenario.FirstPaymentFails && index == 1)
            || (_scenario == Scenario.SecondPaymentFails && index == 2);
        if (!failed) Move(card, PileType.Exhaust);
        if ((_scenario == Scenario.EndAfterFirst && index == 1) || (_scenario == Scenario.EndAfterSecond && index == 2)) _ending = true;
        if (index == 1)
        {
            if (_scenario == Scenario.MaterialLeavesAfterFirst) Move(_chosen!, PileType.Discard);
            if (_scenario == Scenario.ExtraLeavesAfterFirst) Move(_extra!, PileType.Exhaust);
            if (_scenario == Scenario.CombatChangesAfterFirst) card.Owner.Creature.CombatState = ArrivalEquipmentProbe.NewPlayer().Creature.CombatState;
        }
        __result = Task.FromResult<CardPileAddResult?>(failed && index == 1 ? null
            : new CardPileAddResult { success = !failed, cardAdded = card, oldPile = old, targetPile = PileType.Exhaust });
        return false;
    }
    public static bool Consume(CardModel extraCard, ref Task __result)
    {
        _consumes++; Move(extraCard, Entry.ExtraUsedPile);
        if (_scenario == Scenario.EndDuringExtraConsume) _ending = true;
        __result = Task.CompletedTask; return false;
    }
    public static bool Add(CardModel card, PileType newPileType, ref Task<CardPileAddResult> __result)
    {
        _summons++; var old = card.Pile; Move(card, newPileType);
        __result = Task.FromResult(new CardPileAddResult { success = true, cardAdded = card, oldPile = old, targetPile = newPileType });
        return false;
    }
}
