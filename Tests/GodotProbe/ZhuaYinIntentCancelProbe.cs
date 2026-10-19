using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Intent;
using SelectSpecialCardMod.Scripts.Tool;

// 保留真实牌堆入口、应对选择顺序、原生招式调用与状态机；替换消耗命令和检索UI边界。
public static class ZhuaYinIntentCancelProbe
{
    private static readonly Type Deck = typeof(MonsterMoveCard).Assembly.GetType("SelectSpecialCardMod.Scripts.Intent.MonsterDeck", true)!;
    private static int _payments, _retrievals, _nativeEffects;
    private static bool _failPayment;
    private static TaskCompletionSource<List<CardModel>>? _retrievalGate;
    private static CardModel? _response;

    public static async Task Check()
    {
        var harmony = new Harmony("scm.zhua-yin.whole-intent-probe");
        Call("ClearAll");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(CardCmd), nameof(CardCmd.Exhaust)),
                prefix: new HarmonyMethod(typeof(ZhuaYinIntentCancelProbe), nameof(Exhaust)));
            harmony.Patch(AccessTools.Method(typeof(SelectToolCmd), nameof(SelectToolCmd.RetrieverCard)),
                prefix: new HarmonyMethod(typeof(ZhuaYinIntentCancelProbe), nameof(Retrieve)));

            // 先收到远端玩家，再收到本机玩家的两种排列，仍选择NetId较小玩家的第一张爪音。
            foreach (bool reverse in new[] { false, true })
            {
                Reset();
                var fixture = Fixture(reverse);
                var first = Claw(fixture.First, PileType.Hand);
                var spare = Claw(fixture.First, PileType.Hand);
                var other = Claw(fixture.Second, PileType.Hand);
                var (enemy, move, card, queue) = Action(fixture.Combat, fixture.First,
                    [new SingleAttackIntent(9), new BuffIntent(), new StatusIntent(2)]);
                _retrievalGate = new();
                var running = Play(enemy);
                if (running.IsCompleted || _payments != 1 || _retrievals != 1 || _response != first
                    || first.Pile?.Type != PileType.Exhaust || spare.Pile?.Type != PileType.Hand
                    || other.Pile?.Type != PileType.Hand || _nativeEffects != 0
                    || !(bool)Call("IsResolving", enemy)! || queue.Contains(card))
                    throw new Exception("Claw did not exhaust exactly one deterministic responder before the whole intent and retrieval.");
                await Play(enemy); // 模拟检索/消耗连锁重入，不得又消费一张爪音。
                if (_payments != 1 || _retrievals != 1) throw new Exception("Reentrant intent execution consumed another Claw.");
                _retrievalGate.SetResult([]);
                await running;
                _retrievalGate = null;
                if (_nativeEffects != 0 || !ReferenceEquals(queue.Last(), card)
                    || (bool)Call("IsResolving", enemy)! || (bool)Call("ConsumeHold", enemy)!
                    || !ReferenceEquals(enemy.Monster!.NextMove, move) || !move.CanTransitionAway)
                    throw new Exception("Cancellation ran native attack/attached effects, retained a lock, or changed the next action into another stun.");

                // 原生外层在await之后调用OnMovePerformed；取消后必须前进到原本后继，不能重放/再空过。
                enemy.Monster.MoveStateMachine.OnMovePerformed(move);
                var successor = enemy.Monster.MoveStateMachine.RollMove([fixture.First.Creature], enemy,
                    new MegaCrit.Sts2.Core.Random.Rng(314));
                if (successor.Id != "DISTRACT_MOVE") throw new Exception("Canceled intent failed to advance to its original successor.");
            }

            // 消耗失败不得取消；无爪音、非攻击意图、已经离手的爪音也不能阻止原生招式。
            foreach (var scenario in new[] { "failed-payment", "no-card", "non-attack", "exhausted-card", "dead-owner", "zero-damage-attack" })
            {
                Reset();
                var fixture = Fixture(false);
                ZhuLeiZhuaYinCard? claw = scenario == "no-card" ? null : Claw(fixture.First,
                    scenario == "exhausted-card" ? PileType.Exhaust : PileType.Hand);
                if (scenario == "dead-owner") fixture.First.Creature.SetCurrentHpInternal(0);
                _failPayment = scenario == "failed-payment";
                AbstractIntent[] intents = scenario == "non-attack" ? [new BuffIntent()] : [new SingleAttackIntent(scenario == "zero-damage-attack" ? 0 : 9)];
                var (enemy, _, _, _) = Action(fixture.Combat, fixture.First, intents);
                await Play(enemy);
                bool canceled = scenario == "zero-damage-attack";
                if (_nativeEffects != (canceled ? 0 : 1) || _retrievals != (canceled ? 1 : 0)
                    || (claw != null && claw.Pile?.Type != (canceled || scenario == "exhausted-card" ? PileType.Exhaust : PileType.Hand)))
                    throw new Exception("Claw's payment/hand/alive/attack condition is incorrect: " + scenario);
            }

            // 异步检索失败也必须释放执行门卫，不能转而执行已经取消的攻击。
            Reset();
            var last = Fixture(false);
            Claw(last.First, PileType.Hand);
            var (interrupted, _, interruptedCard, interruptedQueue) = Action(last.Combat, last.First, [new SingleAttackIntent(9)]);
            _retrievalGate = new();
            var pending = Play(interrupted);
            _retrievalGate.SetException(new InvalidOperationException("probe retrieval failed"));
            try { await pending; throw new Exception("Expected retrieval failure was swallowed."); }
            catch (InvalidOperationException ex) when (ex.Message == "probe retrieval failed") { }
            if ((bool)Call("IsResolving", interrupted)! || _nativeEffects != 0 || interruptedQueue.Contains(interruptedCard))
                throw new Exception("Interrupted response retained the lock or executed/rotated the canceled intent as successful.");

            GD.Print("PASS: Claw cancels the whole native intent before its effects, exhausts one deterministic multiplayer responder, awaits retrieval, blocks reentry, advances without another stun, and preserves failed-payment/nonattack/dead-owner behavior.");
        }
        finally { harmony.UnpatchAll(harmony.Id); Call("ClearAll"); Reset(); }
    }

    private static (Player First, Player Second, CombatState Combat) Fixture(bool reverse)
    {
        Call("ClearAll");
        var first = ArrivalEquipmentProbe.NewPlayer();
        var second = ArrivalEquipmentProbe.NewPlayer();
        AccessTools.Field(typeof(Player), "<NetId>k__BackingField").SetValue(first, 11UL);
        AccessTools.Field(typeof(Player), "<NetId>k__BackingField").SetValue(second, 22UL);
        var combat = (CombatState)first.Creature.CombatState!;
        second.Creature.CombatState = combat;
        AccessTools.Field(typeof(Player), "_runState").SetValue(second, first.RunState);
        AccessTools.Field(typeof(RunState), "_players").SetValue(first.RunState,
            reverse ? new List<Player> { second, first } : new List<Player> { first, second });
        return (first, second, combat);
    }

    private static (Creature Enemy, MoveState Move, MonsterMoveCard Card, IList<CardModel> Queue) Action(CombatState combat, Player owner, AbstractIntent[] intents)
    {
        var enemy = new Creature(ModelDb.Monster<EyeWithTeeth>().ToMutable(), CombatSide.Enemy, "claw-probe") { CombatState = combat };
        enemy.Monster!.SetUpForCombat();
        enemy.SetCurrentHpInternal(6);
        var move = new MoveState("CLAW_TEST", _ => { _nativeEffects++; return Task.CompletedTask; }, intents)
        {
            MustPerformOnceBeforeTransitioning = true,
            FollowUpStateId = "DISTRACT_MOVE",
        };
        enemy.Monster.SetMoveImmediate(move, forceTransition: true);
        // 原版入口由MonsterDeckTurnPatch设置此标记，直接测牌堆时复现同一前置条件。
        AccessTools.Field(typeof(MoveState), "_performedAtLeastOnce").SetValue(move, true);
        combat.AddCreature(enemy);
        Call("PrepareTurn", enemy);
        var decks = (IDictionary)AccessTools.Field(Deck, "Decks").GetValue(null)!;
        var deck = decks[enemy]!;
        var queue = (IList<CardModel>)AccessTools.Field(deck.GetType(), "Queue").GetValue(deck)!;
        var card = combat.CreateCard<MonsterMoveCard>(owner);
        card.InitMove(move, intents);
        queue.Clear(); queue.Add(card);
        return (enemy, move, card, queue);
    }

    private static ZhuLeiZhuaYinCard Claw(Player player, PileType pile)
        => (ZhuLeiZhuaYinCard)ArrivalEquipmentProbe.AddCard(typeof(ZhuLeiZhuaYinCard), player, pile);
    private static object? Call(string name, params object[] args) => AccessTools.Method(Deck, name).Invoke(null, args);
    private static Task Play(Creature enemy) => (Task)Call("PlayTurn", enemy)!;
    private static void Reset() { _payments = _retrievals = _nativeEffects = 0; _failPayment = false; _retrievalGate = null; _response = null; }

    public static bool Exhaust(CardModel card, ref Task<CardPileAddResult?> __result)
    {
        _payments++;
        var old = card.Pile;
        if (!_failPayment) { card.RemoveFromCurrentPile(silent: true); PileType.Exhaust.GetPile(card.Owner).AddInternal(card); }
        __result = Task.FromResult<CardPileAddResult?>(new() { success = !_failPayment, oldPile = old, cardAdded = card, targetPile = PileType.Exhaust });
        return false;
    }
    public static bool Retrieve(Player player, CardModel? source, int amount, bool isDiscard, ref Task<List<CardModel>> __result)
    {
        if (source is not ZhuLeiZhuaYinCard || source.Owner != player || source.Pile?.Type != PileType.Exhaust || amount != 1 || !isDiscard)
            throw new Exception("Claw's retrieval/discard no longer follows self-exhaustion.");
        _retrievals++; _response = source;
        __result = _retrievalGate?.Task ?? Task.FromResult(new List<CardModel>());
        return false;
    }
}
