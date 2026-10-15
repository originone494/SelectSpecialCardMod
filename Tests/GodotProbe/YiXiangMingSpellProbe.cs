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
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

public static class YiXiangMingSpellProbe
{
    private static readonly List<int> HpChanges = [];
    private static readonly List<(Func<CardModel, bool> Filter, RetrievalOptions Options)> Retrievals = [];
    private static int _draws;
    private static bool _fatalLoss;

    public static async Task Check()
    {
        var h = new Harmony("scm.vaalmonica-spells.godot-framework");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsInProgress"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), "InProgress"));
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), "LiveCombat"));
            h.Patch(AccessTools.Method(typeof(CombatManager), "IsPlayerReadyToEndTurn"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), "LiveCombat"));
            h.Patch(AccessTools.Method(typeof(CreatureCmd), "Heal"), prefix: new HarmonyMethod(typeof(YiXiangMingSpellProbe), nameof(Heal)));
            h.Patch(AccessTools.Method(typeof(CreatureCmd), "Damage", [typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(CardModel), typeof(CardPlay)]),
                prefix: new HarmonyMethod(typeof(YiXiangMingSpellProbe), nameof(Lose)));
            h.Patch(AccessTools.Method(typeof(CardRetrieval), "Retrieve"), prefix: new HarmonyMethod(typeof(YiXiangMingSpellProbe), nameof(Retrieve)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "Draw", [typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(YiXiangMingSpellProbe), nameof(Draw)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "Add", [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(ZhuLeiResolutionProbe), nameof(ZhuLeiResolutionProbe.Add)));

            foreach (var type in new[] { typeof(YiXiangMingJueZeCard), typeof(YiXiangMingQingZhuCard), typeof(YiXiangMingDiaoYinCard) })
            {
                foreach (int option in new[] { 0, 1 })
                {
                    Reset();
                    var selector = new OptionSelector { Option = option };
                    using var choiceScope = CardSelectCmd.UseSelector(selector);
                    var player = NewPlayer();
                    var spell = (YiXiangMingChoiceCard)ArrivalEquipmentProbe.AddCard(type, player, PileType.Play);
                    if (spell.Type != CardType.Skill || spell.Rarity != CardRarity.Ancient || spell.EnergyCost.Canonical != 0
                        || !spell.Tags.Contains(SelectTags.YiXiangMing) || !spell.Tags.Contains(SelectTags.YiXiangMingMoFa))
                        throw new Exception("New spell metadata is incorrect.");
                    var voice = (YiXiangMingVoiceCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianShiZhiShengCard), player, PileType.Hand);
                    if (YiXiangMingChoiceCard.HasYiXiangMingInPendulumZone(player))
                        throw new Exception("An ordinary edge card counted as placed in the pendulum zone.");
                    voice.LockedPendulumSide = PendulumEdgeSide.Left;
                    voice.PlacedAtEdgeByEffect = true;
                    if (!YiXiangMingChoiceCard.HasYiXiangMingInPendulumZone(player))
                        throw new Exception("A single effect-placed pendulum card did not enable choosing.");

                    var exhausted = new List<CardModel>();
                    if (spell is YiXiangMingDiaoYinCard)
                    {
                        exhausted.Add(ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), player, PileType.Exhaust));
                        exhausted.Add(ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianShiZhiShengCard), player, PileType.Exhaust));
                        exhausted.Add(ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.DefendIronclad), player, PileType.Exhaust));
                    }
                    await Invoke(spell);
                    if (selector.Choices != 1 || HpChanges.Count != 1 || HpChanges[0] != (option == 0 ? 1 : -1))
                        throw new Exception("The selected branch or its one-point life change was incorrect.");

                    if (spell is YiXiangMingJueZeCard && option == 0)
                    {
                        if (_draws != 2 || Retrievals.Count != 0) throw new Exception("Scelta first branch did not draw exactly two cards.");
                    }
                    else if (spell is YiXiangMingJueZeCard)
                    {
                        var retrieval = Retrievals.Single();
                        if (!retrieval.Filter(spell) || retrieval.Filter(voice) || retrieval.Options.Destination != RetrievalDestination.Hand
                            || retrieval.Options.Minimum != 1 || !retrieval.Options.Confirm)
                            throw new Exception("Scelta second branch has the wrong retrieval filter/destination.");
                    }
                    else if (spell is YiXiangMingQingZhuCard)
                    {
                        var retrieval = Retrievals.Single();
                        if (!retrieval.Filter(voice) || retrieval.Filter(spell) != (option == 0)
                            || retrieval.Options.Choice != (option == 0 ? RetrievalChoice.Random : RetrievalChoice.Player)
                            || retrieval.Options.Destination != (option == 0 ? RetrievalDestination.Hand : RetrievalDestination.Exhaust))
                            throw new Exception("Versare random/exhaust/self-exclusion rules are incorrect.");
                    }
                    else
                    {
                        var recovered = exhausted.Where(c => c.Pile?.Type == PileType.Hand).ToList();
                        if (recovered.Count != 1 || (option == 0 ? recovered[0].Type != CardType.Attack : recovered[0] != exhausted[1]))
                            throw new Exception("Intonare recovered an invalid card or the wrong number of cards.");
                        if (option == 0)
                        {
                            recovered[0].EnergyCost.AfterCardPlayedCleanup();
                            if (recovered[0].EnergyCost.GetWithModifiers(CostModifiers.All) != 0)
                                throw new Exception("Intonare's free cost did not last for the whole turn.");
                        }
                    }
                }

                Reset();
                var noZoneSelector = new OptionSelector();
                using var randomScope = CardSelectCmd.UseSelector(noZoneSelector);
                var noZonePlayer = NewPlayer();
                var randomSpell = (YiXiangMingChoiceCard)ArrivalEquipmentProbe.AddCard(type, noZonePlayer, PileType.Play);
                ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianShiZhiShengCard), noZonePlayer, PileType.Hand);
                await Invoke(randomSpell);
                if (noZoneSelector.Choices != 0 || HpChanges.Count != 1)
                    throw new Exception("An unplaced hand card enabled manual selection instead of a single random branch.");
            }

            Reset();
            var fatalPlayer = NewPlayer();
            var fatalSpell = (YiXiangMingChoiceCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingJueZeCard), fatalPlayer, PileType.Play);
            var placed = (YiXiangMingVoiceCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianShiZhiShengCard), fatalPlayer, PileType.Hand);
            placed.LockedPendulumSide = PendulumEdgeSide.Left;
            placed.PlacedAtEdgeByEffect = true;
            using var fatalScope = CardSelectCmd.UseSelector(new OptionSelector { Option = 1 });
            _fatalLoss = true;
            await Invoke(fatalSpell);
            if (HpChanges.Single() != -1 || Retrievals.Count != 0)
                throw new Exception("A lethal life payment continued into retrieval.");

            foreach (var token in new EffectChoiceCard[] { new EffectOneCard(), new EffectTwoCard() })
                if (token.ShouldShowInCardLibrary || token.CanBeGeneratedInCombat || token.CanBeGeneratedByModifiers || token.Rarity != CardRarity.Token)
                    throw new Exception("Choice tokens can enter the player's obtainable cards.");
        }
        finally { h.UnpatchAll(h.Id); Reset(); }
        GD.Print("PASS: all six spell branches, skill/Ancient/zero-cost tags, effect-placed single-card zone vs ordinary edge, mandatory choice cards, random fallback, recovery filters/free cost, lethal-loss stop and hidden choice tokens.");
    }

    private static Player NewPlayer()
    {
        var player = ArrivalEquipmentProbe.NewPlayer();
        var rng = AccessTools.Property(typeof(RunState), "Rng");
        rng.SetValue(player.RunState, Activator.CreateInstance(rng.PropertyType, ["vaalmonica-spell-probe"]));
        AccessTools.Field(typeof(Player), "<Character>k__BackingField").SetValue(player, new MegaCrit.Sts2.Core.Models.Characters.Ironclad());
        return player;
    }

    private static Task Invoke(YiXiangMingChoiceCard card) => (Task)AccessTools.Method(card.GetType(), "OnPlay").Invoke(card,
        [new BlockingPlayerChoiceContext(), new CardPlay { Card = card, Player = card.Owner, Target = null,
            ResultPile = PileType.Discard, Resources = default, IsAutoPlay = false, PlayIndex = 0, PlayCount = 1 }])!;

    private static void Reset() { HpChanges.Clear(); Retrievals.Clear(); _draws = 0; _fatalLoss = false; }
    public static bool Heal(decimal amount, ref Task __result)
    { if (amount != 1) throw new Exception("Wrong healing amount."); HpChanges.Add(1); __result = Task.CompletedTask; return false; }
    public static bool Lose(Creature target, decimal amount, ValueProp props, ref Task<IEnumerable<DamageResult>> __result)
    {
        if (amount != 1 || !props.HasFlag(ValueProp.Unblockable)) throw new Exception("Life loss does not bypass block.");
        HpChanges.Add(-1);
        if (_fatalLoss) AccessTools.Field(typeof(Creature), "_currentHp").SetValue(target, 0);
        __result = Task.FromResult<IEnumerable<DamageResult>>([]);
        return false;
    }
    public static bool Draw(decimal count, ref Task<IEnumerable<CardModel>> __result)
    { _draws += (int)count; __result = Task.FromResult<IEnumerable<CardModel>>([]); return false; }
    public static bool Retrieve(Func<CardModel, bool> filter, RetrievalOptions options, ref Task<List<CardModel>> __result)
    { Retrievals.Add((filter, options)); __result = Task.FromResult(new List<CardModel>()); return false; }

    private sealed class OptionSelector : ICardSelector
    {
        public int Option;
        public int Choices;
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            var cards = options.ToList();
            if (cards.All(c => c is EffectChoiceCard))
            {
                Choices++;
                if (cards.Count != 2 || cards[0] is not EffectOneCard || cards[1] is not EffectTwoCard)
                    throw new Exception("Missing or misordered numbered choices.");
                return Task.FromResult<IEnumerable<CardModel>>([cards[Option]]);
            }
            return Task.FromResult<IEnumerable<CardModel>>(cards.Take(maxSelect).ToList());
        }
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,
            IReadOnlyList<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative> alternatives)
            => throw new NotSupportedException();
    }
}
