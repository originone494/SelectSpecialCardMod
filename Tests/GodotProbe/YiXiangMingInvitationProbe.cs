using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using SelectSpecialCardMod.Scripts;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Tool;
using STS2RitsuLib.Models.Capabilities;

// 用可控的选牌与命令边界验证真实卡牌分支、持久/临时实体隔离和两阶段行动。
public static class YiXiangMingInvitationProbe
{
    private static readonly List<string> Events = [];
    private static bool _cancel, _failPlay;
    private static CardModel? _retrieved;
    private static string? _extraAddFailure;
    private static CardModel? _failedExtraCard;

    public static async Task Check()
    {
        var h = new Harmony("scm.vaalmonica-invitation.godot-framework");
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
            EquipmentHookProbe.Models = [];
            h.Patch(AccessTools.Method(typeof(RunState), "IterateHookListeners", [typeof(ICombatState)]), prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"));
            h.Patch(AccessTools.Method(typeof(CombatState), "IterateHookListeners", Type.EmptyTypes), prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "Add", [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]), prefix: new HarmonyMethod(typeof(YiXiangMingInvitationProbe), nameof(Add)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "AddGeneratedCardToCombat"), prefix: new HarmonyMethod(typeof(YiXiangMingInvitationProbe), nameof(Add)));
            h.Patch(AccessTools.Method(typeof(CardRetrieval), "Retrieve"), prefix: new HarmonyMethod(typeof(YiXiangMingInvitationProbe), nameof(Retrieve)));
            var ctx = new BlockingPlayerChoiceContext();

            var owner = NewPlayer();
            var spell = (YiXiangMingYaoQingCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingYaoQingCard), owner, PileType.Play);
            _retrieved = ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianMoZhiShengXuanJiCard), owner, PileType.Hand);
            if (spell.Type != CardType.Skill || spell.Rarity != CardRarity.Ancient || spell.EnergyCost.Canonical != 0)
                throw new Exception("Invitare metadata is wrong.");
            var selector = new InvitationSelector { Option = 0 };
            using (CardSelectCmd.UseSelector(selector)) await Invoke(spell);
            if (selector.Choices != 1 || _retrieved.EnergyCost.GetWithModifiers(CostModifiers.All) != 0)
                throw new Exception("Invitare must choose without a pendulum zone and make its retrieved monster free.");
            _retrieved.EnergyCost.AfterCardPlayedCleanup();
            if (_retrieved.EnergyCost.GetWithModifiers(CostModifiers.All) != 0)
                throw new Exception("Free cost expired after a single play instead of lasting this turn.");

            selector = new InvitationSelector { Option = 1 };
            using (CardSelectCmd.UseSelector(selector)) await Invoke(spell);
            var temporary = Entry.ExtraPile.GetPile(owner).Cards.Single();
            if (selector.Ordered.Count != 2 || selector.Ordered[0].Id == selector.Ordered[1].Id
                || selector.Ordered[0].Pile?.Type != PileType.Hand || temporary.Id != selector.Ordered[1].Id
                || !owner.RunState.ContainsCard(temporary) || temporary.CombatState != null || !PendulumExtraDeck.IsTemporary(temporary))
                throw new Exception("Ordered retrieval did not create distinct hand/combat-only extra-deck cards.");
            var marker = temporary.Capabilities().All.OfType<TemporaryPendulumExtraCapability>().Single();
            var restored = ModelCapabilityRegistry.Create<TemporaryPendulumExtraCapability>(); restored.LoadState(marker.SaveState(), 1);
            var copy = owner.Creature.CombatState!.CloneCard(temporary);
            if (!restored.Temporary || PendulumExtraDeck.IsTemporary(copy) || !PendulumExtraDeck.IsTemporary(temporary))
                throw new Exception("Temporary provenance failed persistence or leaked onto a summoned clone.");
            var configured = owner.RunState.CreateCard(ModelDb.Card<YiXiangMingTianShiZhiShengCard>(), owner);
            Entry.ExtraPile.GetPile(owner).AddInternal(configured);
            PendulumExtraDeck.ClearTemporary(owner);
            if (owner.RunState.ContainsCard(temporary) || Entry.ExtraPile.GetPile(owner).Cards.Count != 1
                || configured.Pile?.Type != Entry.ExtraPile || selector.Ordered[0].Pile?.Type != PileType.Hand)
                throw new Exception("Combat cleanup removed configured extra cards or retained a temporary asset.");

            // 注册表已有规范实例后，重复创建必须得到独立标记，且不污染规范状态。
            await PendulumExtraDeck.AddTemporary(owner, ModelDb.Card<YiXiangMingTianShiZhiShengCard>());
            await PendulumExtraDeck.AddTemporary(owner, ModelDb.Card<YiXiangMingTianShiZhiShengCard>());
            var repeated = Entry.ExtraPile.GetPile(owner).Cards.Where(PendulumExtraDeck.IsTemporary).ToList();
            var markers = repeated.Select(c => c.Capabilities().All.OfType<TemporaryPendulumExtraCapability>().Single()).ToList();
            if (repeated.Count != 2 || ReferenceEquals(markers[0], markers[1])
                || ModelDb.GetById<TemporaryPendulumExtraCapability>(ModelDb.GetId<TemporaryPendulumExtraCapability>()).Temporary)
                throw new Exception("Repeated temporary creation reused/mutated a registered canonical capability.");
            PendulumExtraDeck.ClearTemporary(owner);
            foreach (var failure in new[] { "refused", "throw" })
            {
                _extraAddFailure = failure;
                _failedExtraCard = null;
                bool threw = false;
                try { await PendulumExtraDeck.AddTemporary(owner, ModelDb.Card<YiXiangMingTianShiZhiShengCard>()); }
                catch (InvalidOperationException ex) when (ex.Message == "probe extra add failed") { threw = true; }
                finally { _extraAddFailure = null; }
                if (threw != (failure == "throw") || _failedExtraCard == null
                    || owner.RunState.ContainsCard(_failedExtraCard) || _failedExtraCard.Pile != null
                    || Entry.ExtraPile.GetPile(owner).Cards.Count != 1)
                    throw new Exception("Failed temporary extra-deck creation leaked a run card or disturbed configured cards.");
            }

            var noSupport = NewPlayer();
            var unsupported = (YiXiangMingYaoQingCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingYaoQingCard), noSupport, PileType.Play);
            ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianShiZhiShengCard), noSupport, PileType.Hand);
            selector = new InvitationSelector { Option = 1 };
            using (CardSelectCmd.UseSelector(selector)) await Invoke(unsupported);
            if (selector.Ordered.Count != 0 || Entry.ExtraPile.GetPile(noSupport).Cards.Count != 0)
                throw new Exception("A pendulum monster satisfied the non-pendulum hand requirement.");

            // 候选条件使用真实实现；只替代游戏场景依赖的打出资格和命令。
            h.Patch(AccessTools.Method(typeof(PendulumSummon), "CanOperate"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), "InProgress"));
            h.Patch(AccessTools.Method(typeof(PendulumSummon), "CanPlayFree"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), "InProgress"));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromHand"), prefix: new HarmonyMethod(typeof(YiXiangMingInvitationProbe), nameof(Hand)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromSimpleGrid"), prefix: new HarmonyMethod(typeof(YiXiangMingInvitationProbe), nameof(Extra)));
            h.Patch(AccessTools.Method(typeof(CardCmd), "AutoPlay"), prefix: new HarmonyMethod(typeof(YiXiangMingInvitationProbe), nameof(Play)));
            foreach (var mode in new[] { "both", "extra-only", "cancel", "blocked" })
            {
                var player = NewPlayer();
                var left = (YiXiangMingVoiceCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianShiZhiShengCard), player, PileType.Hand);
                var right = (YiXiangMingVoiceCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingEMoZhiShengCard), player, PileType.Hand);
                left.LockedPendulumSide = PendulumEdgeSide.Left; left.PlacedAtEdgeByEffect = true;
                right.LockedPendulumSide = PendulumEdgeSide.Right; right.PlacedAtEdgeByEffect = true;
                if (mode == "both") ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianMoZhiShengXuanJiCard), player, PileType.Hand);
                await PendulumExtraDeck.AddTemporary(player, ModelDb.Card<YiXiangMingTianShiZhiShengCard>());
                var original = Entry.ExtraPile.GetPile(player).Cards.Single();
                if (!PendulumSummon.HasCandidates(player) || PendulumSummon.ExtraCandidates(player).Single() != original
                    || PendulumSummon.IsExtraCandidate(original, player, 4, 5))
                    throw new Exception("Extra-only candidate discovery or strict scale bounds failed.");
                Events.Clear(); _cancel = mode == "cancel"; _failPlay = mode == "blocked";
                await PendulumSummon.Execute(ctx, player);
                bool used = mode == "both" || mode == "extra-only";
                if (PendulumSummon.IsUsed(player) != used) throw new Exception("Usage did not follow an actual play start.");
                if (mode == "both" && !Events.SequenceEqual(new[] { "hand", "play:YiXiangMingTianMoZhiShengXuanJiCard", "extra", "play:YiXiangMingTianShiZhiShengCard" }))
                    throw new Exception("Extra selection opened before all hand summons resolved.");
                if (mode == "extra-only" && Events[0] != "extra") throw new Exception("An empty hand stage blocked extra summons.");
                if (!used && (original.Pile?.Type != Entry.ExtraPile || !player.RunState.ContainsCard(original)))
                    throw new Exception("Canceled/blocked extra summon consumed its original asset.");
                if (used && player.RunState.ContainsCard(original)) throw new Exception("A summoned temporary asset remained in the run.");
                if (used)
                {
                    Events.Clear(); await PendulumSummon.Execute(ctx, player);
                    if (Events.Count != 0) throw new Exception("A second summon was allowed in the same combat.");
                }
            }
        }
        finally { h.UnpatchAll(h.Id); _retrieved = _failedExtraCard = null; _extraAddFailure = null; _cancel = _failPlay = false; Events.Clear(); }
        GD.Print("PASS: invitation branches/ordered destinations, temporary assets/clone isolation, sequential hand-extra summon, extra-only/cancel/blocked cases and once-per-combat use.");
    }

    private static Player NewPlayer()
    {
        var player = ArrivalEquipmentProbe.NewPlayer();
        AccessTools.Field(typeof(Player), "<Character>k__BackingField").SetValue(player, new MegaCrit.Sts2.Core.Models.Characters.Ironclad());
        AccessTools.Field(typeof(RunState), "_allCards").SetValue(player.RunState, new List<CardModel>());
        return player;
    }
    private static Task Invoke(YiXiangMingYaoQingCard card) => (Task)AccessTools.Method(typeof(YiXiangMingYaoQingCard), "OnPlay").Invoke(card,
        [new BlockingPlayerChoiceContext(), MakePlay(card)])!;
    private static CardPlay MakePlay(CardModel card) => new()
    {
        Card = card, Player = card.Owner, ResultPile = PileType.Discard, Target = null,
        Resources = default, IsAutoPlay = true, PlayIndex = 0, PlayCount = 1,
    };
    private static CardPileAddResult Move(CardModel card, PileType pile)
    {
        var old = card.Pile; card.RemoveFromCurrentPile(silent: true); pile.GetPile(card.Owner).AddInternal(card, silent: true);
        return new() { success = true, cardAdded = card, oldPile = old, targetPile = pile };
    }
    public static bool Add(CardModel card, PileType newPileType, ref Task<CardPileAddResult> __result)
    {
        if (newPileType == Entry.ExtraPile && _extraAddFailure != null)
        {
            _failedExtraCard = card;
            __result = _extraAddFailure == "throw"
                ? Task.FromException<CardPileAddResult>(new InvalidOperationException("probe extra add failed"))
                : Task.FromResult(new CardPileAddResult { success = false, cardAdded = card, oldPile = card.Pile, targetPile = newPileType });
            return false;
        }
        __result = Task.FromResult(Move(card, newPileType)); return false;
    }
    public static bool Retrieve(Func<CardModel, bool> filter, RetrievalOptions options, ref Task<List<CardModel>> __result)
    {
        if (_retrieved == null || !filter(_retrieved) || options.Minimum != 1 || !options.Confirm)
            throw new Exception("Invitation retrieval is optional or has an incorrect filter.");
        __result = Task.FromResult(new List<CardModel> { _retrieved }); return false;
    }
    public static bool Hand(Player player, CardSelectorPrefs prefs, Func<CardModel, bool> filter, AbstractModel? source, ref Task<IEnumerable<CardModel>> __result)
    {
        if (source != null || prefs.MinSelect != 0 || !prefs.RequireManualConfirmation) throw new Exception("Incorrect hand-selection UI contract.");
        Events.Add("hand"); __result = Task.FromResult<IEnumerable<CardModel>>(_cancel ? [] : PileType.Hand.GetPile(player).Cards.Where(filter).ToList()); return false;
    }
    public static bool Extra(IReadOnlyList<CardModel> cardsIn, CardSelectorPrefs prefs, ref Task<IEnumerable<CardModel>> __result)
    {
        if (prefs.MinSelect != 0 || !prefs.RequireManualConfirmation) throw new Exception("Extra selection cannot skip/confirm.");
        Events.Add("extra"); __result = Task.FromResult<IEnumerable<CardModel>>(_cancel ? [] : cardsIn.ToList()); return false;
    }
    public static bool Play(CardModel card, ref Task __result)
    {
        Events.Add("play:" + card.GetType().Name);
        if (!_failPlay)
        {
            var history = CombatManager.Instance.History;
            var combat = card.Owner.Creature.CombatState!;
            var play = MakePlay(card);
            // 注入原版实际开始事件，避免完整攻击/场景动画依赖。
            ((List<CombatHistoryEntry>)AccessTools.Field(typeof(CombatHistory), "_entries").GetValue(history)!)
                .Add(new CardPlayStartedEntry(play, combat.RoundNumber, combat.CurrentSide, history, combat.Players));
            ((Action?)AccessTools.Field(typeof(CombatHistory), "Changed").GetValue(history))?.Invoke();
            Move(card, PileType.Discard);
        }
        __result = Task.CompletedTask; return false;
    }
    private sealed class InvitationSelector : ICardSelector
    {
        public int Option, Choices;
        public readonly List<CardModel> Ordered = [];
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            var cards = options.ToList();
            if (cards.All(c => c is EffectChoiceCard))
            {
                if (cards.Cast<EffectChoiceCard>().Any(c => c.AssetProfile.VisualStyle != STS2RitsuLib.Scaffolding.Content.CardVisualStyle.Ancient
                    || c.AssetProfile.PortraitPath?.EndsWith("YiXiangMingYaoQingCard.png") != true))
                    throw new Exception("Invitare choices did not inherit its illustration/layout.");
                Choices++; return Task.FromResult<IEnumerable<CardModel>>([cards[Option]]);
            }
            if (minSelect != 1 || maxSelect != 1 || cards.Any(c => Ordered.Any(p => p.Id == c.Id)))
                throw new Exception("Ordered retrieval allowed zero/multiple cards or a duplicate name.");
            Ordered.Add(cards[0]); return Task.FromResult<IEnumerable<CardModel>>([cards[0]]);
        }
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
            IReadOnlyList<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative> alternatives)
            => throw new NotSupportedException();
    }
}
