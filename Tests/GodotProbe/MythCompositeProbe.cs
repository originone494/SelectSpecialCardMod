using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Tool;

// 保留实际 OnPlay、支付、抽取、绑定、复制/存档和模块顺序；替换 UI、入堆与动作命令边界。
public static class MythCompositeProbe
{
    private static int _choices, _payments, _stopAt;
    private static bool _ending, _failAdd;
    private static ShenHuaCompositeCard? _generated;
    private static IReadOnlyList<Creature> _enemies = [];
    private static AttackCommand? _attack;
    private static readonly List<string> Executed = [];
    private static string _discardScenario = "";
    private static int _discardChoices;
    private static int _mutationGrants;
    private static bool _observeBlockDamage, _throwAttack;
    private static decimal _resolvedDamage;
    private static TaskCompletionSource<AttackCommand>? _pausedAttack;

    public static async Task Check()
    {
        var h = new Harmony("scm.myth-composite.godot-framework");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"),
                prefix: new HarmonyMethod(typeof(MythCompositeProbe), nameof(Ending)));
            h.Patch(AccessTools.Method(typeof(RunState), "IterateHookListeners", [typeof(ICombatState)]),
                prefix: new HarmonyMethod(typeof(EquipmentHookProbe), nameof(EquipmentHookProbe.Prefix)));
            h.Patch(AccessTools.Method(typeof(CombatState), "IterateHookListeners", Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(EquipmentHookProbe), nameof(EquipmentHookProbe.Prefix)));
            EquipmentHookProbe.Models = [];
            CheckSourceIdentity();
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromHand"), prefix: new HarmonyMethod(typeof(MythCompositeProbe), nameof(Choose)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromCombatPile", [typeof(PlayerChoiceContext), typeof(CardPile), typeof(Player), typeof(CardSelectorPrefs), typeof(Func<CardModel, bool>)]),
                prefix: new HarmonyMethod(typeof(MythCompositeProbe), nameof(ChooseDiscard)));
            h.Patch(AccessTools.Method(typeof(CardCmd), "Exhaust"), prefix: new HarmonyMethod(typeof(MythCompositeProbe), nameof(Exhaust)));
            h.Patch(AccessTools.Method(typeof(SelectSpecialCardMod.Scripts.Myth.MythCardFactory), "CreateAndGrant"),
                prefix: new HarmonyMethod(typeof(MythCompositeProbe), nameof(GrantMutation)));
            CheckLibrary();
            CheckPersistence();
            CheckTraits();
            await CheckDirectGeneration();
            await CheckDiscardExhaust();
            h.Patch(AccessTools.PropertyGetter(typeof(CombatState), "HittableEnemies"),
                prefix: new HarmonyMethod(typeof(MythCompositeProbe), nameof(Enemies)));
            h.Patch(AccessTools.Method(typeof(AttackCommand), "Execute", [typeof(PlayerChoiceContext)]),
                prefix: new HarmonyMethod(typeof(MythCompositeProbe), nameof(Attack)));
            await CheckDamageAndTargets();
            await CheckBlockDamage();
            h.Patch(AccessTools.Method(typeof(FusionActions), nameof(FusionActions.Execute)),
                prefix: new HarmonyMethod(typeof(MythCompositeProbe), nameof(Action)));
            await CheckRunner();
        }
        finally { h.UnpatchAll(h.Id); Reset(); EquipmentHookProbe.Models = []; }
        GD.Print("PASS: myth module uniqueness and seeded reproducibility, zero-cost direct generation without material selection/payment, persistent bindings, copies and ordered replay/ending guard. Command/UI boundaries are simulated.");
    }

    private static Player NewPlayer()
    {
        var player = ArrivalEquipmentProbe.NewPlayer();
        var run = (RunState)player.RunState;
        AccessTools.Field(typeof(RunState), "_allCards").SetValue(run, new List<CardModel>());
        var rng = AccessTools.Property(typeof(RunState), "Rng");
        rng.SetValue(run, Activator.CreateInstance(rng.PropertyType, ["myth-fixed-seed"]));
        return player;
    }

    private static void CheckSourceIdentity()
    {
        var main = ModelDb.Card<SuiYuanShenHuaHeTiCard>();
        var legacy = ModelDb.Card<ShenHuaCompositeCard>();
        if (main.Id.Entry != SelectSpecialCardMod.Scripts.Myth.MythConsoleCardAlias.SourceCardId
            || legacy.Id.Entry != SelectSpecialCardMod.Scripts.Myth.MythConsoleCardAlias.LegacyTemplateId
            || main.EnergyCost.CostsX || main.EnergyCost.Canonical != 0 || main.Title != "随缘神话合体"
            || !main.CanonicalKeywords.Contains(CardKeyword.Exhaust)
            || main.Description.GetFormattedText() != "将1张拥有随机效果的神话卡加入[gold]手牌[/gold]。")
            throw new Exception("Myth source card identity, zero cost, title, Exhaust or description is incorrect.");
        if ((bool)AccessTools.Property(typeof(ShenHuaCompositeCard), "IsPlayable").GetValue(legacy)!
            || string.IsNullOrWhiteSpace(legacy.GetDescriptionForPile(PileType.None)))
            throw new Exception("Unbound legacy myth template is playable or has a blank description.");
        foreach (var pile in new[] { "hand", "deck", "discard" })
        {
            string[] original = [legacy.Id.Entry.ToLowerInvariant(), pile];
            var arguments = original;
            SelectSpecialCardMod.Scripts.Myth.MythConsoleCardAlias.Prefix(ref arguments);
            if (arguments[0] != main.Id.Entry || arguments[1] != pile || original[0] != legacy.Id.Entry.ToLowerInvariant()
                || ReferenceEquals(arguments, original)) throw new Exception("Legacy console alias failed or mutated the original command.");
        }
        foreach (var original in new[] { Array.Empty<string>(), new[] { main.Id.Entry }, new[] { "STRIKE_IRONCLAD", "deck" } })
        {
            var arguments = original;
            SelectSpecialCardMod.Scripts.Myth.MythConsoleCardAlias.Prefix(ref arguments);
            if (!ReferenceEquals(original, arguments)) throw new Exception("Myth alias changed an unrelated console command.");
        }
        var player = NewPlayer();
        var bound = (ShenHuaCompositeCard)Add(player, legacy, PileType.Hand);
        bound.BindModules([FusionEffectLibrary.Find("siren")!]);
        if (!(bool)AccessTools.Property(typeof(ShenHuaCompositeCard), "IsPlayable").GetValue(bound)!)
            throw new Exception("Legacy-template protection disabled a valid saved myth recipe.");
    }

    private static CardModel Add(Player player, CardModel template, PileType pile)
    {
        var card = player.Creature.CombatState!.CreateCard(template, player);
        pile.GetPile(player).AddInternal(card, silent: true);
        return card;
    }

    private static CardPlay Play(CardModel card, Creature? target = null) => new()
    {
        Card = card, Player = card.Owner, Target = target, Resources = default,
        ResultPile = PileType.Discard, IsAutoPlay = false, PlayIndex = 0, PlayCount = 1,
    };

    private static void CheckLibrary()
    {
        FusionEffectLibrary.Validate();
        if (FusionEffectLibrary.All.Count != 64 || FusionEffectLibrary.All.Select(m => m.Series).Distinct().Count() != 8)
            throw new Exception("The draft library inventory changed; review the module checklist.");
        for (int count = 0; count <= FusionEffectLibrary.All.Count; count++)
        {
            var a = NewPlayer(); var b = NewPlayer();
            var first = FusionEffectLibrary.Roll(a, count); var second = FusionEffectLibrary.Roll(b, count);
            if (first.Count != count || first.Select(m => m.Id).Distinct().Count() != count
                || !first.Select(m => m.Id).SequenceEqual(second.Select(m => m.Id))
                || JsonSerializer.Serialize(a.RunState.Rng.Niche.ToSerializable()) != JsonSerializer.Serialize(b.RunState.Rng.Niche.ToSerializable()))
                throw new Exception("Myth count, uniqueness or deterministic random consumption failed.");
            if (count >= 8 && first.Take(8).Select(m => m.Series).Distinct().Count() != 8)
                throw new Exception("First myth family cycle lost its diversity.");
        }
    }

    private static void CheckPersistence()
    {
        var player = NewPlayer();
        var card = (ShenHuaCompositeCard)Add(player, ModelDb.Card<ShenHuaCompositeCard>(), PileType.Hand);
        card.BindModules([FusionEffectLibrary.Find("siren")!, FusionEffectLibrary.Find("xiaomei")!]);
        var beforeRng = JsonSerializer.Serialize(player.RunState.Rng.Niche.ToSerializable());
        string key = FusionEffectLibrary.VarKey("siren", 0);
        card.DynamicVars[key].BaseValue += 5;
        var clone = (ShenHuaCompositeCard)player.Creature.CombatState!.CloneCard(card);
        var loaded = (ShenHuaCompositeCard)CardModel.FromSerializable(card.ToSerializable());
        foreach (var copy in new[] { clone, loaded })
            if (copy.ModuleIdsJson != card.ModuleIdsJson || copy.Title != card.Title || !copy.Tags.SequenceEqual(card.Tags)
                || copy.TargetType != TargetType.AnyEnemy || !copy.GainsBlock || !copy.IsUpgraded
                || copy.Keywords.Contains(CardKeyword.Exhaust) || copy.EnergyCost.Canonical != 0)
                throw new Exception("Saved/copied myth card lost its fixed identity or gameplay properties.");
        // 原版克隆应复制修改后的变量；普通存档对基础变量的保留范围沿用原版规则。
        if (clone.DynamicVars[key].BaseValue != card.DynamicVars[key].BaseValue
            || beforeRng != JsonSerializer.Serialize(player.RunState.Rng.Niche.ToSerializable()))
            throw new Exception("Cloning reset a modified value or consumed new random modules.");
        if (ModelDb.Card<ShenHuaCompositeCard>().Modules.Count != 0 || ModelDb.Card<ShenHuaCompositeCard>().Tags.Any())
            throw new Exception("A generated card contaminated the canonical template.");
        var result = (CardLocation)AccessTools.Method(typeof(CardModel), "GetResultLocationForCardPlay").Invoke(card, null)!;
        if (result.pileType != PileType.Discard) throw new Exception("Generated myth card no longer discards after play.");
        ExpectInvalid(() => card.BindModules([FusionEffectLibrary.Find("melo")!]));
        var empty = (ShenHuaCompositeCard)ModelDb.Card<ShenHuaCompositeCard>().ToMutable();
        ExpectInvalid(() => empty.ModuleIdsJson = "[\"melo\",\"melo\"]");
        ExpectInvalid(() => empty.ModuleIdsJson = "[\"unknown-module\"]");
        empty.BindModules([FusionEffectLibrary.Find("diaoyin")!]);
        if (empty.TargetType != TargetType.Self || empty.GainsBlock) throw new Exception("Self-only myth card requires an enemy.");
    }

    private static async Task CheckDirectGeneration()
    {
        foreach (int count in new[] { 0, 3 })
        {
            Reset();
            var player = NewPlayer();
            var source = (SuiYuanShenHuaHeTiCard)Add(player, ModelDb.Card<SuiYuanShenHuaHeTiCard>(), PileType.Play);
            source.EnergyCost.CapturedXValue = 99; // 旧的捕获值不应再影响0费卡的效果。
            for (int i = 0; i < count; i++) Add(player, ModelDb.Card<StrikeIronclad>(), PileType.Hand);
            if (count > 0)
            {
                var placed = (YiXiangMingTianShiZhiShengCard)Add(player, ModelDb.Card<YiXiangMingTianShiZhiShengCard>(), PileType.Hand);
                placed.PlacedAtEdgeByEffect = true;
            }
            var before = PileType.Hand.GetPile(player).Cards.ToList();
            await (Task)AccessTools.Method(typeof(SuiYuanShenHuaHeTiCard), "OnPlay").Invoke(source, [new BlockingPlayerChoiceContext(), Play(source)])!;
            if (_mutationGrants != 1 || _choices != 0 || _payments != 0
                || !before.SequenceEqual(PileType.Hand.GetPile(player).Cards) || source.EnergyCost.CostsX || source.EnergyCost.Canonical != 0)
                throw new Exception("Zero-cost myth generation selected/paid materials, changed hand cards or failed to generate exactly once.");
        }
    }

    private static void CheckTraits()
    {
        var player = NewPlayer();
        string rng = JsonSerializer.Serialize(player.RunState.Rng.Niche.ToSerializable());
        for (int traits = 0; traits < 8; traits++)
        {
            var card = (ShenHuaCompositeCard)Add(player, ModelDb.Card<ShenHuaCompositeCard>(), PileType.Hand);
            card.BindModules([FusionEffectLibrary.Find("diaoyin")!]);
            // 先访问旧值，复现生成/升级已缓存费用和关键词的实际路径。
            _ = card.Keywords.Count; _ = card.EnergyCost.GetWithModifiers(CostModifiers.Local);
            card.GeneratedTraits = traits;
            foreach (var copy in new[] { card, (ShenHuaCompositeCard)player.Creature.CombatState!.CloneCard(card),
                (ShenHuaCompositeCard)CardModel.FromSerializable(card.ToSerializable()) })
            {
                if (copy.GeneratedTraits != traits || copy.GeneratedEnergyCost != (traits & 3)
                    || copy.EnergyCost.GetWithModifiers(CostModifiers.Local) != (traits & 3)
                    || copy.Keywords.Contains(CardKeyword.Exhaust) != ((traits & 4) != 0)
                    || copy.ModuleIdsJson != card.ModuleIdsJson)
                    throw new Exception("Myth trait combination was lost through caching, cloning or saving.");
                var result = (CardLocation)AccessTools.Method(typeof(CardModel), "GetResultLocationForCardPlay").Invoke(copy, null)!;
                if (result.pileType != ((traits & 4) != 0 ? PileType.Exhaust : PileType.Discard))
                    throw new Exception("Generated Exhaust trait did not change native play destination.");
            }
            card.EnergyCost.SetThisTurn(0);
            var freeClone = (ShenHuaCompositeCard)player.Creature.CombatState!.CloneCard(card);
            if (freeClone.EnergyCost.GetWithModifiers(CostModifiers.Local) != 0)
                throw new Exception("Cloning overwrote a temporary free-cost effect.");
            card.EndOfTurnCleanup();
            if (card.EnergyCost.GetWithModifiers(CostModifiers.Local) != (traits & 3))
                throw new Exception("Generated cost expired at turn end.");
            ExpectInvalid(() => card.GeneratedTraits = traits ^ 4);
            ExpectInvalid(card.RollGeneratedTraits);
        }
        if (rng != JsonSerializer.Serialize(player.RunState.Rng.Niche.ToSerializable()))
            throw new Exception("Reading/copying/loading myth traits consumed random numbers.");

        // 两份同种子的独立状态必须以相同顺序生成费用和消耗，不由本地UI决定。
        var first = NewPlayer(); var second = NewPlayer();
        for (int i = 0; i < 32; i++)
        {
            var a = (ShenHuaCompositeCard)Add(first, ModelDb.Card<ShenHuaCompositeCard>(), PileType.Hand);
            var b = (ShenHuaCompositeCard)Add(second, ModelDb.Card<ShenHuaCompositeCard>(), PileType.Hand);
            a.RollGeneratedTraits(); b.RollGeneratedTraits();
            if (a.GeneratedTraits is < 0 or > 7 || a.GeneratedTraits != b.GeneratedTraits)
                throw new Exception("Myth generated traits diverged between identical states.");
        }
        if (JsonSerializer.Serialize(first.RunState.Rng.Niche.ToSerializable()) != JsonSerializer.Serialize(second.RunState.Rng.Niche.ToSerializable()))
            throw new Exception("Myth trait rolls consumed different random numbers.");

        var legacy = (ShenHuaCompositeCard)Add(player, ModelDb.Card<ShenHuaCompositeCard>(), PileType.Hand);
        legacy.BindModules([FusionEffectLibrary.Find("melo")!]);
        var oldSave = legacy.ToSerializable();
        oldSave.Props?.ints?.RemoveAll(p => p.name == nameof(ShenHuaCompositeCard.GeneratedTraits));
        var loaded = (ShenHuaCompositeCard)CardModel.FromSerializable(oldSave);
        if (loaded.GeneratedEnergyCost != 0 || loaded.Keywords.Contains(CardKeyword.Exhaust) || loaded.ModuleIdsJson != legacy.ModuleIdsJson)
            throw new Exception("A legacy myth card was rerolled when loading.");
    }

    private static async Task CheckDiscardExhaust()
    {
        foreach (string scenario in new[] { "choose", "empty", "cancel", "stale", "ending" })
        {
            Reset(); _discardScenario = scenario;
            var player = NewPlayer();
            var source = (ShenHuaCompositeCard)Add(player, ModelDb.Card<ShenHuaCompositeCard>(), PileType.Play);
            var module = FusionEffectLibrary.Find("jushe")!;
            source.BindModules([module]);
            var hand = Add(player, ModelDb.Card<StrikeIronclad>(), PileType.Hand);
            var discarded = scenario == "empty" ? null : Add(player, ModelDb.Card<StrikeIronclad>(), PileType.Discard);
            var ctx = new FusionActionContext(source, new BlockingPlayerChoiceContext(), Play(source));
            await FusionActions.Execute(module.Actions[1], FusionEffectLibrary.VarKey(module.Id, 1), ctx);
            if (_discardChoices != (scenario == "empty" ? 0 : 1)
                || _payments != (scenario == "choose" ? 1 : 0) || hand.Pile?.Type != PileType.Hand)
                throw new Exception("Discard-pile exhaust selected/consumed the wrong pile or ignored interruption: " + scenario);
            if (scenario == "choose" && discarded?.Pile?.Type != PileType.Exhaust)
                throw new Exception("Jushe did not actually exhaust the selected discard-pile card.");
            if (scenario == "stale" && discarded?.Pile?.Type != PileType.Hand)
                throw new Exception("Jushe moved a stale selection back into the exhaust pile.");
        }
    }

    private static async Task CheckRunner()
    {
        Reset();
        var player = NewPlayer();
        var card = (ShenHuaCompositeCard)Add(player, ModelDb.Card<ShenHuaCompositeCard>(), PileType.Play);
        card.BindModules([FusionEffectLibrary.Find("siren")!, FusionEffectLibrary.Find("jueze")!]);
        string ids = card.ModuleIdsJson;
        string[] expected = ["Myth_siren_0", "Myth_siren_1", "Myth_jueze_0", "Myth_jueze_1"];
        for (int repeat = 0; repeat < 2; repeat++)
        {
            Executed.Clear();
            await FusionEffectRunner.Execute(card, new BlockingPlayerChoiceContext(), Play(card));
            if (!Executed.SequenceEqual(expected) || card.ModuleIdsJson != ids) throw new Exception("Myth replay changed modules or action order.");
        }
        Executed.Clear(); _stopAt = 2;
        await FusionEffectRunner.Execute(card, new BlockingPlayerChoiceContext(), Play(card));
        if (!Executed.SequenceEqual(expected.Take(2))) throw new Exception("Combat-ending myth effects did not stop.");
    }

    private static async Task CheckDamageAndTargets()
    {
        Reset();
        var player = NewPlayer(); var combat = player.Creature.CombatState!;
        var card = (ShenHuaCompositeCard)Add(player, ModelDb.Card<ShenHuaCompositeCard>(), PileType.Play);
        var module = FusionEffectLibrary.Find("melo")!;
        card.BindModules([module]);
        var first = new Creature(ModelDb.Monster<Vantom>().ToMutable(), CombatSide.Enemy, "first") { CombatState = combat };
        var second = new Creature(ModelDb.Monster<Vantom>().ToMutable(), CombatSide.Enemy, "second") { CombatState = combat };
        AccessTools.Field(typeof(Creature), "_currentHp").SetValue(first, 10);
        AccessTools.Field(typeof(Creature), "_currentHp").SetValue(second, 10);
        _enemies = [first, second];
        var play = Play(card, first);
        var ctx = new FusionActionContext(card, new BlockingPlayerChoiceContext(), play);
        string key = FusionEffectLibrary.VarKey(module.Id, 0);
        card.DynamicVars[key].PreviewValue = 13;
        if (!module.Describe(card).Contains("13")) throw new Exception("Myth damage text ignored the native dynamic preview.");
        await FusionActions.Execute(module.Actions[0], key, ctx);
        if (_attack?.ModelSource != card || _attack.CardPlay != play
            || Convert.ToDecimal(AccessTools.Field(typeof(AttackCommand), "_damagePerHit").GetValue(_attack)) != 8
            || AccessTools.Field(typeof(AttackCommand), "_singleTarget").GetValue(_attack) != first)
            throw new Exception("Myth attack lost its source/target or applied preview modifiers twice.");
        AccessTools.Field(typeof(Creature), "_currentHp").SetValue(first, 0);
        if (ctx.Target() != second || ctx.Target() != second) throw new Exception("Dead myth target was not replaced consistently.");
        AccessTools.Field(typeof(Creature), "_currentHp").SetValue(second, 0);
        if (ctx.Target() != null) throw new Exception("Myth targeting accepted a dead enemy.");
        _ending = true; _attack = null;
        await FusionActions.Execute(module.Actions[0], key, ctx);
        if (_attack != null) throw new Exception("An ended combat constructed another attack.");
    }

    private static async Task CheckBlockDamage()
    {
        Reset();
        var player = NewPlayer(); var combat = player.Creature.CombatState!;
        var creature = new Creature(player, 50, 50) { CombatState = combat };
        AccessTools.Field(typeof(Player), "<Creature>k__BackingField").SetValue(player, creature);
        var card = (ShenHuaCompositeCard)Add(player, ModelDb.Card<ShenHuaCompositeCard>(), PileType.Play);
        var conditional = FusionEffectLibrary.Find("shensheng")!;
        var ordinary = FusionEffectLibrary.Find("melo")!;
        card.BindModules([conditional, ordinary]);
        var target = new Creature(ModelDb.Monster<Vantom>().ToMutable(), CombatSide.Enemy, "blocked") { CombatState = combat };
        _enemies = [target];
        var strength = ModelDb.Power<StrengthPower>().ToMutable(3);
        strength.ApplyInternal(creature, 3, true);
        EquipmentHookProbe.Models = [card, strength]; _observeBlockDamage = true;
        var play = Play(card, target);
        var ctx = new FusionActionContext(card, new BlockingPlayerChoiceContext(), play);
        string key = FusionEffectLibrary.VarKey(conditional.Id, 0), normalKey = FusionEffectLibrary.VarKey(ordinary.Id, 0);
        try
        {
            foreach (int block in new[] { 0, 1, 50, 0 })
            {
                AccessTools.Field(typeof(Creature), "_block").SetValue(target, block);
                card.DynamicVars[key].UpdateCardPreview(card, CardPreviewMode.Normal, target, true);
                card.DynamicVars[normalKey].UpdateCardPreview(card, CardPreviewMode.Normal, target, true);
                decimal expected = block > 0 ? 42 : 21;
                await FusionActions.Execute(conditional.Actions[0], key, ctx);
                if (card.DynamicVars[key].PreviewValue != expected || _resolvedDamage != expected
                    || card.DynamicVars[normalKey].PreviewValue != 11
                    || Convert.ToDecimal(AccessTools.Field(typeof(AttackCommand), "_damagePerHit").GetValue(_attack)) != 18)
                    throw new Exception("Conditional block damage did not double after Strength or disagreed with preview.");
                await FusionActions.Execute(ordinary.Actions[0], normalKey, ctx);
                if (_resolvedDamage != 11) throw new Exception("Shensheng doubled another module's damage.");
            }
            AccessTools.Field(typeof(Creature), "_block").SetValue(target, 1);
            _pausedAttack = new();
            var pending = FusionActions.Execute(conditional.Actions[0], key, ctx);
            if (pending.IsCompleted) throw new Exception("The attack boundary did not pause.");
            card.DynamicVars[normalKey].UpdateCardPreview(card, CardPreviewMode.Normal, target, true);
            if (card.DynamicVars[normalKey].PreviewValue != 11) throw new Exception("Async damage scope leaked into another UI preview.");
            _pausedAttack.SetResult(_attack!); await pending; _pausedAttack = null;
            _throwAttack = true;
            bool failed = false;
            try { await FusionActions.Execute(conditional.Actions[0], key, ctx); }
            catch (InvalidOperationException) { failed = true; }
            _throwAttack = false;
            if (!failed) throw new Exception("The conditional-damage interruption fixture did not throw.");
            await FusionActions.Execute(ordinary.Actions[0], normalKey, ctx);
            if (_resolvedDamage != 11) throw new Exception("Failed conditional attack leaked a multiplier.");

            var search = FusionEffectLibrary.Find("mojianke")!.Actions[1];
            if (!search.Matches(ModelDb.Card<KeMoYongShengCard>()) || search.Matches(ModelDb.Card<KeMoMoJianKeCard>())
                || search.Matches(ModelDb.Card<KeMoLianGeCard>()))
                throw new Exception("Mojianke retrieval does not distinguish Kemo spells, monsters and traps.");
        }
        finally { _pausedAttack?.TrySetResult(_attack!); EquipmentHookProbe.Models = []; Reset(); }
    }

    private static void ExpectInvalid(System.Action action)
    {
        try { action(); }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return; }
        throw new Exception("Invalid or rerolled myth binding was accepted.");
    }
    private static void Reset()
    {
        _choices = _payments = _stopAt = 0;
        _ending = _failAdd = false;
        _generated = null; _enemies = []; _attack = null; Executed.Clear();
        _discardChoices = 0; _discardScenario = ""; _mutationGrants = 0;
        _observeBlockDamage = _throwAttack = false; _resolvedDamage = 0; _pausedAttack = null;
    }
    private static void Move(CardModel card, PileType pile)
    { card.RemoveFromCurrentPile(silent: true); pile.GetPile(card.Owner).AddInternal(card, silent: true); }
    public static bool Ending(ref bool __result) { __result = _ending; return false; }
    public static bool Enemies(ref IReadOnlyList<Creature> __result) { __result = _enemies; return false; }
    public static bool Attack(AttackCommand __instance, ref Task<AttackCommand> __result)
    {
        _attack = __instance;
        if (_observeBlockDamage)
        {
            var source = (CardModel)__instance.ModelSource!;
            var target = (Creature?)AccessTools.Field(typeof(AttackCommand), "_singleTarget").GetValue(__instance);
            decimal damage = Convert.ToDecimal(AccessTools.Field(typeof(AttackCommand), "_damagePerHit").GetValue(__instance));
            _resolvedDamage = MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage(source.Owner.RunState, source.CombatState,
                target, source.Owner.Creature, damage, ValueProp.Move, source, __instance.CardPlay,
                MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType.All, CardPreviewMode.Normal, out _);
        }
        __result = _throwAttack ? Task.FromException<AttackCommand>(new InvalidOperationException("fixture conditional damage interruption"))
            : _pausedAttack?.Task ?? Task.FromResult(__instance);
        return false;
    }
    public static bool Choose(Player player, Func<CardModel, bool> filter, AbstractModel? source, CardSelectorPrefs prefs,
        ref Task<IEnumerable<CardModel>> __result)
    {
        _choices++;
        throw new Exception("Zero-cost myth generation must not open a material selection.");
    }
    public static bool Exhaust(CardModel card, ref Task<CardPileAddResult?> __result)
    {
        _payments++; var old = card.Pile; Move(card, PileType.Exhaust);
        __result = Task.FromResult<CardPileAddResult?>(new CardPileAddResult { success = true, cardAdded = card, oldPile = old, targetPile = PileType.Exhaust });
        return false;
    }
    public static bool ChooseDiscard(CardPile pile, Func<CardModel, bool> filter, CardSelectorPrefs prefs,
        ref Task<IEnumerable<CardModel>> __result)
    {
        _discardChoices++;
        if (pile.Type != PileType.Discard || prefs.MinSelect != 1 || prefs.MaxSelect != 1 || !prefs.RequireManualConfirmation)
            throw new Exception("Jushe lost discard-pile selection or mandatory confirmation.");
        var selected = pile.Cards.Where(filter).Take(1).ToArray();
        if (_discardScenario == "stale") Move(selected.Single(), PileType.Hand);
        if (_discardScenario == "ending") _ending = true;
        __result = Task.FromResult<IEnumerable<CardModel>>(_discardScenario == "cancel" ? [] : selected);
        return false;
    }
    public static bool GrantMutation(ref Task __result)
    { _mutationGrants++; __result = Task.CompletedTask; return false; }
    public static bool AddGenerated(CardModel card, PileType newPileType, ref Task<CardPileAddResult> __result)
    {
        _generated = (ShenHuaCompositeCard)card;
        if (!_failAdd) Move(card, newPileType);
        __result = Task.FromResult(new CardPileAddResult { success = !_failAdd, cardAdded = card, targetPile = newPileType });
        return false;
    }
    public static bool Action(string key, ref Task __result)
    {
        Executed.Add(key);
        if (_stopAt > 0 && Executed.Count == _stopAt) _ending = true;
        __result = Task.CompletedTask; return false;
    }
}
