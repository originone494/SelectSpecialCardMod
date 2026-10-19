using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Myth;
using SelectSpecialCardMod.Scripts.Tool;

// 运行时探针，保留生成/快照/升级/支付/战斗生成逻辑；只替换牌堆命令和详细效果边界。
public static class MythMutationProbe
{
    private static MythCardSnapshot _fixed = null!;
    private static int _effects, _payments, _factoryCalls;
    private static bool _ending, _interrupt, _failHand;
    private static CardModel? _partner;
    private static MythMutationCard? _battleCopy;
    private static bool _preventHp;

    public static async Task Check()
    {
        var h = new Harmony("scm.myth-mutation.godot-framework");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"), prefix: new HarmonyMethod(typeof(MythMutationProbe), nameof(Ending)));
            h.Patch(AccessTools.Method(typeof(RunState), "IterateHookListeners", [typeof(ICombatState)]), prefix: new HarmonyMethod(typeof(EquipmentHookProbe), nameof(EquipmentHookProbe.Prefix)));
            h.Patch(AccessTools.Method(typeof(CombatState), "IterateHookListeners", Type.EmptyTypes), prefix: new HarmonyMethod(typeof(EquipmentHookProbe), nameof(EquipmentHookProbe.Prefix)));
            EquipmentHookProbe.Models = [];
            CheckGeneration();
            await MythFullLibraryProbe.Check();
            CheckConditions();
            h.Patch(AccessTools.Method(typeof(CardCmd), "Exhaust"), prefix: new HarmonyMethod(typeof(MythMutationProbe), nameof(Exhaust)));
            h.Patch(AccessTools.Method(typeof(FusionActions), "Execute"), prefix: new HarmonyMethod(typeof(MythMutationProbe), nameof(Effect)));
            h.Patch(AccessTools.Method(typeof(CreatureCmd), "Damage", [typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(CardModel), typeof(CardPlay)]),
                prefix: new HarmonyMethod(typeof(MythMutationProbe), nameof(HpPayment)));
            await CheckPayments();
            h.Patch(AccessTools.Method(typeof(MythCardFactory), "Generate", [typeof(Player)]), prefix: new HarmonyMethod(typeof(MythMutationProbe), nameof(Generate)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "Add", [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(MythMutationProbe), nameof(Add)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "AddGeneratedCardToCombat", [typeof(CardModel), typeof(PileType), typeof(Player), typeof(CardPilePosition)]),
                prefix: new HarmonyMethod(typeof(MythMutationProbe), nameof(AddGenerated)));
            await CheckCombatOnlyGrant();
        }
        finally { h.UnpatchAll(h.Id); Reset(); EquipmentHookProbe.Models = []; }
        GD.Print("PASS: 1000 deterministic mutation generations, category probabilities, costs/types/art/values, 100 clone/save/upgrade/tag round trips, condition/payment guards and combat-only grants. UI/command boundaries simulated.");
    }
    private static Player Player()
    {
        var player = ArrivalEquipmentProbe.NewPlayer();
        var run = (RunState)player.RunState;
        AccessTools.Field(typeof(RunState), "_allCards").SetValue(run, new List<CardModel>());
        AccessTools.Field(typeof(Player), "_runPiles").SetValue(player, new CardPile[] { new(PileType.Deck) });
        var combat = player.Creature.CombatState;
        AccessTools.Field(typeof(Player), "<Creature>k__BackingField").SetValue(player, new Creature(player, 50, 50) { CombatState = combat });
        return player;
    }
    private static MythMutationCard Card(Player owner, MythCardSnapshot snapshot, PileType pile = PileType.Hand)
    {
        var card = owner.Creature.CombatState!.CreateCard<MythMutationCard>(owner);
        card.Bind(snapshot); pile.GetPile(owner).AddInternal(card, silent: true); return card;
    }
    private static CardPlay Play(CardModel card) => new() { Card = card, Player = card.Owner, Target = null, Resources = default,
        ResultPile = PileType.Discard, IsAutoPlay = false, PlayIndex = 0, PlayCount = 1 };
    private static MythCardSnapshot WithCondition(string id)
    {
        // 旧条件保留成内嵌快照夹具，确认替换库后旧卡仍可加载/结算。
        string kind = id == "none" ? "none" : id.StartsWith("pay_") ? "payment" : "restriction";
        var condition = new MythConditionTemplate { Id = id, Op = id, Kind = kind, NamePart = "旧条件", Text = kind == "none" ? "" : "旧条件",
            Parameters = id is "hand_at_least" or "hand_at_most" ? new() { ["count"] = new(id == "hand_at_least" ? 2 : 1, 4) }
                : id == "pay_hp" ? new() { ["hp"] = new(2, 6) } : [] };
        return _fixed with { Condition = condition, ConditionValues = condition.Parameters.ToDictionary(p => p.Key, p => p.Value.Min) };
    }
    private static void CheckGeneration()
    {
        MythCardFactory.Validate();
        var expectedSeries = new Dictionary<string, string[]>
        {
            ["ZhuLeiSaiRenCard"] = ["ZhuLei", "ZhuLeiGuaiShou"],
            ["ZhuLeiJuSheCard"] = ["JuShe", "ZhuLei", "ZhuLeiGuaiShou"],
            ["ZhuLeiShuiXianCard"] = ["ZhuLei", "ZhuLeiGuaiShou"],
            ["YiJieFuYinCard"] = ["YiJie", "YiJieMoXian"],
            ["KeMoShenShengGuanCard"] = ["GuangEMo", "KeMo", "KeMoGuaiShou"],
            ["YiXiangMingJueZeCard"] = ["YiXiangMing", "YiXiangMingMoFa"],
            ["YaoJingChuanJiBaiXueCard"] = [],
        };
        foreach (var p in expectedSeries)
            if (!MythSeriesTags.FromSource(p.Key).SequenceEqual(p.Value))
                throw new Exception("Missing series tag or inherited a functional/extra-monster tag: " + p.Key);
        var a = new Rng(123456UL); var b = new Rng(123456UL);
        var counts = new Dictionary<string, int>(); var types = new HashSet<string>(); var costs = new HashSet<int>();
        var owner = Player();
        for (int i = 0; i < 1000; i++)
        {
            var first = MythCardFactory.Generate(a); var second = MythCardFactory.Generate(b);
            string json = MythJson.Write(first);
            if (json != MythJson.Write(second)) throw new Exception("Identical mutation seeds diverged.");
            counts[first.Condition.Kind] = counts.GetValueOrDefault(first.Condition.Kind) + 1;
            types.Add(first.CardType); costs.Add(first.EnergyCost);
            foreach (var p in first.EffectValues)
                if (p.Value < first.Effect.Parameters[p.Key].Min || p.Value > first.Effect.Parameters[p.Key].Max) throw new Exception("Mutation random value outside template range.");
            if (i >= 100) continue;
            var card = Card(owner, first);
            var copy = (MythMutationCard)owner.Creature.CombatState!.CloneCard(card);
            var loaded = (MythMutationCard)CardModel.FromSerializable(card.ToSerializable());
            foreach (var result in new[] { card, copy, loaded })
                if (result.SnapshotJson != json || result.Type.ToString() != first.CardType
                    || !result.Tags.ToHashSet().SetEquals(MythSeriesTags.Resolve(first.SeriesTags))
                    || !first.SeriesTags!.SequenceEqual(MythSeriesTags.FromSource(first.Effect.SourceCard))
                    || result.EnergyCost.GetWithModifiers(CostModifiers.Local) != first.EnergyCost
                    || result.GainsBlock != first.Actions.Any(MythEffectExecutor.IsBlock)
                    || result.Keywords.Contains(CardKeyword.Exhaust) != first.Exhaust || result.IsUpgraded)
                    throw new Exception("Mutation fixed properties lost during creation/clone/save or cosmetic auto-upgrade applied.");
            if (first.Condition.Kind == "none" && (MythCardText.Description(card).StartsWith("\n") || !card.Title.Contains("·异变")))
                throw new Exception("Unconditional mutation text has an extra condition line.");
            card.UpgradeInternal(); card.FinalizeUpgradeInternal();
            var upgraded = card.CaptureSnapshot();
            if (upgraded.Condition.Id != first.Condition.Id || upgraded.Effect.Id != first.Effect.Id || upgraded.Art != first.Art
                || upgraded.CardType != first.CardType || upgraded.UpgradeLevel != 1 || !upgraded.SeriesTags!.SequenceEqual(first.SeriesTags!))
                throw new Exception("Mutation upgrade rerolled fixed properties.");
            foreach (var p in first.EffectValues)
                if (upgraded.EffectValues[p.Key] != p.Value + first.Effect.Upgrade.GetValueOrDefault(p.Key)) throw new Exception("Mutation upgrade did not follow template increments.");
            var reload = (MythMutationCard)CardModel.FromSerializable(card.ToSerializable());
            if (reload.SnapshotJson != card.SnapshotJson || reload.CurrentUpgradeLevel != 1) throw new Exception("Saved mutation upgrade applied twice.");
            var changed = card.CaptureSnapshot(); changed.EffectValues.Clear(); changed.SeriesTags!.Clear();
            if (card.CaptureSnapshot().EffectValues.Count != first.EffectValues.Count
                || !card.CaptureSnapshot().SeriesTags!.SequenceEqual(first.SeriesTags!))
                throw new Exception("Public mutation snapshot exposed mutable instance state.");
        }
        if (counts["none"] is < 620 or > 780 || counts["restriction"] is < 90 or > 210 || counts["payment"] is < 90 or > 210
            || types.Count != 3 || costs.Count != 4) throw new Exception("Mutation category/cost/type coverage failed.");
        var effect = new MythEffectTemplate { Id = "pure_block", SourceCard = "ZhuLeiXiaoMeiCard", NamePart = "小美", Description = "获得{block}点格挡。",
            Parameters = new() { ["block"] = new(8, 18) }, Upgrade = new() { ["block"] = 4 }, Actions = [new() { Op = "block", Amount = "block" }] };
        _fixed = new MythCardSnapshot { Condition = new() { Id = "none", Op = "none", Kind = "none" },
            ConditionValues = [], Effect = effect, EffectValues = effect.Parameters.ToDictionary(p => p.Key, p => p.Value.Min),
            Actions = MythCardFactory.Resolve(effect, effect.Parameters.ToDictionary(p => p.Key, p => p.Value.Min)),
            CardType = "Skill", EnergyCost = 2, Art = MythArtManifest.All[0], SeriesTags = MythSeriesTags.FromSource(effect.SourceCard) };
        var legacy = Card(owner, _fixed with { SeriesTags = null });
        if (!legacy.CaptureSnapshot().SeriesTags!.SequenceEqual(_fixed.SeriesTags!) || !legacy.Tags.ToHashSet().SetEquals(MythSeriesTags.Resolve(_fixed.SeriesTags)))
            throw new Exception("Snapshot without saved series tags did not migrate from its effect source.");
        // 已冻结标签不随来源再次推导；不合规标签不能注入快照。
        var frozen = Card(owner, _fixed with { SeriesTags = [] });
        if (frozen.Tags.Any() || ((MythMutationCard)owner.Creature.CombatState!.CloneCard(frozen)).Tags.Any())
            throw new Exception("Saved empty series tags were recomputed.");
        foreach (var invalid in new[] { new List<string> { "SpecialEffect" }, new List<string> { "ZhuLei", "ZhuLei" } })
        {
            bool rejected = false;
            try { MythCardFactory.ValidateSnapshot(_fixed with { SeriesTags = invalid }); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Mutation accepted functional or duplicate series tags.");
        }
    }
    private static void CheckConditions()
    {
        var owner = Player();
        var minimum = Card(owner, WithCondition("hand_at_least"));
        if (MythConditionEvaluator.CanPlay(minimum)) throw new Exception("Minimum-hand restriction ignored.");
        var other = ArrivalEquipmentProbe.AddCard(typeof(StrikeIronclad), owner, PileType.Hand);
        if (!MythConditionEvaluator.CanPlay(minimum)) throw new Exception("Minimum-hand restriction rejected a legal hand.");
        var hp = Card(owner, WithCondition("pay_hp"));
        AccessTools.Field(typeof(Creature), "_currentHp").SetValue(owner.Creature, 2);
        if (MythConditionEvaluator.CanPlay(hp)) throw new Exception("Lethal extra HP payment allowed.");
        AccessTools.Field(typeof(Creature), "_currentHp").SetValue(owner.Creature, 3);
        if (!MythConditionEvaluator.CanPlay(hp)) throw new Exception("Payable extra HP rejected.");
        var below = Card(owner, WithCondition("hp_below_half"));
        AccessTools.Field(typeof(Creature), "_currentHp").SetValue(owner.Creature, 25);
        if (MythConditionEvaluator.CanPlay(below)) throw new Exception("Exactly half HP counted as below half.");
        AccessTools.Field(typeof(Creature), "_currentHp").SetValue(owner.Creature, 24);
        if (!MythConditionEvaluator.CanPlay(below)) throw new Exception("Below-half restriction rejected legal HP.");
        var block = Card(owner, WithCondition("has_block"));
        if (MythConditionEvaluator.CanPlay(block)) throw new Exception("No-block restriction ignored.");
        AccessTools.Field(typeof(Creature), "_block").SetValue(owner.Creature, 1);
        if (!MythConditionEvaluator.CanPlay(block)) throw new Exception("Positive block rejected.");
        var isolated = Player();
        var maximum = Card(isolated, WithCondition("hand_at_most"));
        if (!MythConditionEvaluator.CanPlay(maximum)) throw new Exception("Maximum-hand rejected a legal hand.");
        ArrivalEquipmentProbe.AddCard(typeof(StrikeIronclad), isolated, PileType.Hand);
        if (MythConditionEvaluator.CanPlay(maximum)) throw new Exception("Maximum-hand limit ignored.");
        isolated = Player();
        var attack = Card(isolated, WithCondition("has_attack"));
        if (MythConditionEvaluator.CanPlay(attack)) throw new Exception("Attack-in-hand restriction ignored.");
        ArrivalEquipmentProbe.AddCard(typeof(StrikeIronclad), isolated, PileType.Hand);
        if (!MythConditionEvaluator.CanPlay(attack)) throw new Exception("Attack-in-hand not recognized.");
        isolated = Player();
        var skill = Card(isolated, WithCondition("has_skill") with { CardType = "Attack" });
        if (MythConditionEvaluator.CanPlay(skill)) throw new Exception("Skill-in-hand restriction ignored.");
        ArrivalEquipmentProbe.AddCard(typeof(DefendIronclad), isolated, PileType.Hand);
        if (!MythConditionEvaluator.CanPlay(skill)) throw new Exception("Skill-in-hand not recognized.");
    }
    private static async Task CheckPayments()
    {
        foreach (bool interrupt in new[] { false, true })
        {
            Reset(); _interrupt = interrupt;
            var owner = Player(); var card = Card(owner, WithCondition("pay_all_hand"), PileType.Play);
            ArrivalEquipmentProbe.AddCard(typeof(StrikeIronclad), owner, PileType.Hand);
            _partner = ArrivalEquipmentProbe.AddCard(typeof(StrikeIronclad), owner, PileType.Hand);
            await MythEffectExecutor.Execute(card, new BlockingPlayerChoiceContext(), Play(card));
            if (_payments != (interrupt ? 1 : 2) || _effects != (interrupt ? 0 : 1)) throw new Exception("Mutation effect ran before/without complete payment.");
        }
        Reset();
        var player = Player(); var source = Card(player, WithCondition("pay_all_hand"), PileType.Play);
        await MythEffectExecutor.Execute(source, new BlockingPlayerChoiceContext(), Play(source));
        if (_effects != 0 || _payments != 0) throw new Exception("Empty-hand payment was treated as paid.");
        foreach (bool prevent in new[] { false, true })
        {
            Reset(); _preventHp = prevent;
            player = Player(); source = Card(player, WithCondition("pay_hp"), PileType.Play);
            // 命令边界模拟完整损血后已被其他连锁回满，与支付被临时阻止区分。
            await MythEffectExecutor.Execute(source, new BlockingPlayerChoiceContext(), Play(source));
            if (_effects != (prevent ? 0 : 1)) throw new Exception("HP payment confused post-payment healing with prevented damage.");
        }
    }
    private static async Task CheckCombatOnlyGrant()
    {
        foreach (bool fail in new[] { false, true })
        {
            Reset(); _failHand = fail;
            var player = Player();
            await MythCardFactory.CreateAndGrant(player);
            if (_factoryCalls != 1 || _battleCopy?.SnapshotJson != MythJson.Write(_fixed)
                || _battleCopy.DeckVersion != null || _battleCopy.IsUpgraded
                || PileType.Deck.GetPile(player).Cards.Any() || player.RunState.ContainsCard(_battleCopy))
                throw new Exception("Mutation grant added a permanent card or changed its fixed result.");
            if (fail ? _battleCopy?.HasBeenRemovedFromState != true : _battleCopy?.Pile?.Type != PileType.Hand)
                throw new Exception("Mutation hand addition failure leaked an instance.");
            player.Creature.CombatState = new CombatState(runState: (RunState)player.RunState);
            if (player.Creature.CombatState.ContainsCard(_battleCopy!) || PileType.Deck.GetPile(player).Cards.Any() || _factoryCalls != 1)
                throw new Exception("Mutation was registered for the next battle.");
        }
    }
    private static void Reset() { _effects = _payments = _factoryCalls = 0; _ending = _interrupt = _failHand = _preventHp = false; _partner = null; _battleCopy = null; }
    private static void Move(CardModel card, PileType type) { card.RemoveFromCurrentPile(silent: true); type.GetPile(card.Owner).AddInternal(card, silent: true); }
    public static bool Ending(ref bool __result) { __result = _ending; return false; }
    public static bool Effect(ref Task __result) { _effects++; __result = Task.CompletedTask; return false; }
    public static bool Generate(ref MythCardSnapshot __result) { _factoryCalls++; __result = _fixed; return false; }
    public static bool HpPayment(Creature target, decimal amount, ref Task<IEnumerable<DamageResult>> __result)
    {
        __result = Task.FromResult<IEnumerable<DamageResult>>([new DamageResult(target, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move)
            { UnblockedDamage = _preventHp ? 0 : (int)amount }]);
        return false;
    }
    public static bool Exhaust(CardModel card, ref Task<CardPileAddResult?> __result)
    {
        _payments++; var old = card.Pile; Move(card, PileType.Exhaust);
        if (_interrupt && _payments == 1) Move(_partner!, PileType.Discard);
        __result = Task.FromResult<CardPileAddResult?>(new() { success = true, cardAdded = card, oldPile = old, targetPile = PileType.Exhaust }); return false;
    }
    public static bool Add(CardModel card, PileType newPileType, ref Task<CardPileAddResult> __result)
    { throw new Exception("Combat-only mutation must not invoke permanent deck addition."); }
    public static bool AddGenerated(CardModel card, PileType newPileType, ref Task<CardPileAddResult> __result)
    {
        _battleCopy = (MythMutationCard)card; if (!_failHand) Move(card, newPileType);
        __result = Task.FromResult(new CardPileAddResult { success = !_failHand, cardAdded = card, targetPile = newPileType }); return false;
    }
}
