using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Cards.Special;

// 执行真实增益候选/选择/清除流程；只替换界面、敌人列表和移除的等待演出。
public static class FortunaBuffProbe
{
    private static IReadOnlyList<Creature> _enemies = [];
    private static List<CardModel> _options = [];
    private static int _choices;
    private static bool _cancel;
    private static string _pickTitle = "";
    private static Action? _duringChoice;

    public static async Task Check()
    {
        var h = new Harmony("scm.fortuna-buff-choice.godot-framework");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"),
                prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(ArrivalEquipmentProbe.LiveCombat)));
            h.Patch(AccessTools.PropertyGetter(typeof(CombatState), "Enemies"),
                prefix: new HarmonyMethod(typeof(FortunaBuffProbe), nameof(Enemies)));
            h.Patch(AccessTools.PropertyGetter(typeof(CombatState), "HittableEnemies"),
                prefix: new HarmonyMethod(typeof(FortunaBuffProbe), nameof(NoHittableEnemies)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromSimpleGrid"),
                prefix: new HarmonyMethod(typeof(FortunaBuffProbe), nameof(Select)));
            h.Patch(AccessTools.Method(typeof(PowerCmd), "Remove", [typeof(PowerModel)]),
                prefix: new HarmonyMethod(typeof(FortunaBuffProbe), nameof(Remove)));

            var player = ArrivalEquipmentProbe.NewPlayer();
            var combat = player.Creature.CombatState!;
            var card = combat.CreateCard<YaoShengYueShiFuErTuNaCard>(player);
            var target = new Creature(ModelDb.Monster<Vantom>().ToMutable(), CombatSide.Enemy, "target") { CombatState = combat };
            var other = new Creature(ModelDb.Monster<Vantom>().ToMutable(), CombatSide.Enemy, "other") { CombatState = combat };
            _enemies = [target, other];
            var otherStrength = ModelDb.Power<StrengthPower>().ToMutable(9);
            otherStrength.ApplyInternal(other, 9, true);

            async Task Resolve(int expectedChoices)
            {
                _choices = 0; _options = [];
                await (Task)AccessTools.Method(typeof(YaoShengYueShiFuErTuNaCard), "RemoveChosenBuff")
                    .Invoke(card, [new BlockingPlayerChoiceContext(), combat, target])!;
                if (_choices != expectedChoices || !other.Powers.Contains(otherStrength))
                    throw new Exception("Fortuna opened the wrong number of screens or cleared another enemy's buff.");
                if (_options.Any(c => !c.HasBeenRemovedFromState))
                    throw new Exception("Fortuna's temporary buff options leaked after selection.");
            }

            // 单个增益仍必须打开一次界面并确认，完整移除8层。
            var strength = ModelDb.Power<StrengthPower>().ToMutable(8);
            strength.ApplyInternal(target, 8, true);
            _pickTitle = strength.Title.GetFormattedText();
            await Resolve(1);
            if (target.Powers.Contains(strength)) throw new Exception("Fortuna did not clear every layer of Strength.");

            // 原目标无增益时不转向另一个有增益的敌人。
            await Resolve(0);
            strength = ModelDb.Power<StrengthPower>().ToMutable(8);
            strength.ApplyInternal(target, 8, true);
            var dexterity = ModelDb.Power<DexterityPower>().ToMutable(2);
            dexterity.ApplyInternal(target, 2, true);
            var artifact = ModelDb.Power<ArtifactPower>().ToMutable(2);
            artifact.ApplyInternal(target, 2, true);
            var ritual = ModelDb.Power<RitualPower>().ToMutable(2);
            ritual.ApplyInternal(target, 2, true);
            var weak = ModelDb.Power<WeakPower>().ToMutable(2);
            weak.ApplyInternal(target, 2, true);
            await Resolve(1);
            if (_options.Count != 4 || target.Powers.Contains(strength)
                || !target.Powers.Contains(dexterity) || !target.Powers.Contains(artifact)
                || !target.Powers.Contains(ritual) || !target.Powers.Contains(weak))
                throw new Exception("Fortuna filtered buffs incorrectly or removed an unselected power.");

            _cancel = true;
            await Resolve(1);
            if (!target.Powers.Contains(dexterity) || !target.Powers.Contains(artifact) || !target.Powers.Contains(ritual))
                throw new Exception("An empty selection removed a buff.");
            _cancel = false;

            _pickTitle = dexterity.Title.GetFormattedText();
            _duringChoice = () => target.CombatState = null;
            await Resolve(1);
            if (!target.Powers.Contains(dexterity)) throw new Exception("Fortuna removed a power from a target that left combat.");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            _enemies = []; _options = []; _cancel = false; _duringChoice = null; _pickTitle = "";
        }
        GD.Print("PASS: Fortuna selects only the attacked enemy's buff; one and four options require confirmation; other enemies, unselected powers, cancellations and stale targets remain intact.");
    }

    public static bool Enemies(ref IReadOnlyList<Creature> __result) { __result = _enemies; return false; }
    public static bool NoHittableEnemies(ref IReadOnlyList<Creature> __result) { __result = []; return false; }

    public static bool Select(IReadOnlyList<CardModel> cardsIn, CardSelectorPrefs prefs, ref Task<IEnumerable<CardModel>> __result)
    {
        _choices++; _options = cardsIn.ToList();
        if (!prefs.RequireManualConfirmation || prefs.MinSelect != 1 || prefs.MaxSelect != 1
            || cardsIn.Any(c => c is not NamedEffectChoiceCard))
            throw new Exception("Fortuna bypassed manual buff selection or showed enemy options.");
        var picked = cardsIn.FirstOrDefault(c => c.Title == _pickTitle) ?? cardsIn.First();
        _duringChoice?.Invoke();
        __result = Task.FromResult<IEnumerable<CardModel>>(_cancel ? [] : [picked]);
        return false;
    }

    public static bool Remove(PowerModel power, ref Task __result)
    {
        power.RemoveInternal();
        __result = power.AfterRemoved(power.Owner);
        return false;
    }
}
