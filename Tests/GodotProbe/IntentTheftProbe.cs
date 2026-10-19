using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Intent;

// 使用真实意图卡复制、塞牌、牌堆重建和废牌打出入口；无真实怪物回合演出。
public static class IntentTheftProbe
{
    private static CardModel? _resolvedScript;
    private static Task? _scriptGate;

    public static async Task Check()
    {
        var harmony = new Harmony("scm.intent-theft.deferred-junk-probe");
        Deck.ClearAll();
        try
        {
            var effects = typeof(MonsterMoveCard).Assembly.GetType("SelectSpecialCardMod.Scripts.Intent.MonsterCardEffects", true)!;
            harmony.Patch(AccessTools.Method(effects, "Play"), prefix: new HarmonyMethod(typeof(IntentTheftProbe), nameof(RecordScript)));
            var player = ArrivalEquipmentProbe.NewPlayer();
            AccessTools.Field(typeof(RunState), "_players").SetValue(player.RunState, new List<Player> { player });
            var combat = player.Creature.CombatState!;

            // 复合招式费用归零，但敌人原卡与其他副本仍按意图数收费。
            foreach (int count in new[] { 1, 2, 4 })
            {
                var source = combat.CreateCard<MonsterMoveCard>(player);
                var intents = Enumerable.Range(0, count).Select(_ => (AbstractIntent)new StatusIntent(2)).ToList();
                source.InitMove(null, intents);
                var copied = (MonsterMoveCard)Copy(player, source);
                if (source.EnergyCost.GetWithModifiers(CostModifiers.Local) != count || copied.EnergyCost.GetWithModifiers(CostModifiers.Local) != 0
                    || copied.Intents.Count != count || !ReferenceEquals(source.SourceMove, copied.SourceMove))
                    throw new Exception("Intent theft changed the source, lost intents or did not make the copy cost 0.");
            }
            var attack = combat.CreateCard<MonsterAttackCard>(player);
            attack.IntentDamage = 27;
            attack.Repeats = 2;
            var attackCopy = (MonsterAttackCard)Copy(player, attack);
            if (attackCopy.EnergyCost.GetWithModifiers(CostModifiers.Local) != 0 || attackCopy.IntentDamage != 27 || attackCopy.Repeats != 2)
                throw new Exception("Legacy attack intent copy lost its cost, damage or repeats.");

            // 尚未建堆时只暂存废牌，不因塞牌接管当前行动；下一次正常抽牌才启用。
            var enemy = Enemy("status-target");
            var artifact = ModelDb.Power<ArtifactPower>().ToMutable(2);
            artifact.ApplyInternal(enemy, 2, true);
            var status = combat.CreateCard<MonsterStatusCard>(player);
            await Play(status, enemy);
            if (Deck.IsBuilt(enemy) || Deck.HandOf(enemy).Count != 0
                || Deck.DrawOf(enemy).Single() is not MonsterDisplayCard { IsPlaceholder: true } junk
                || junk.JunkPersists || artifact.Amount != 2)
                throw new Exception("Status junk changed an unbuilt enemy's current action or was treated as an Artifact-blocked debuff.");
            Deck.PrepareTurn(enemy);
            if (!ReferenceEquals(Deck.HandOf(enemy).Single(), junk))
                throw new Exception("Status junk was not drawn at the next normal preparation.");

            // 阶段键改变时仍重建脚本，但不能抹掉尚未打出的废牌；重复重建不能复制废牌。
            ForceRebuild(enemy);
            ForceRebuild(enemy);
            if (!ReferenceEquals(Deck.HandOf(enemy).Single(), junk)
                || Deck.DrawOf(enemy).Count(c => c is MonsterDisplayCard { IsPlaceholder: true }) != 1)
                throw new Exception("Script rebuild erased or duplicated pending junk.");
            await Deck.PlayTurn(enemy);
            if (Deck.DrawOf(enemy).Contains(junk) || !Deck.ConsumeHold(enemy))
                throw new Exception("Status junk did not skip one action and preserve the next scripted move.");

            // 仅隐藏UI不够：塞牌后，本次实际行动仍必须执行原来已确定的招式。
            var ready = Enemy("announced-intent");
            Deck.PrepareTurn(ready);
            var announced = combat.CreateCard<MonsterMoveCard>(player);
            announced.InitMove(ready.Monster!.NextMove, [new DefendIntent()]);
            var readyQueue = QueueOf(ready);
            readyQueue.Clear();
            readyQueue.Add(announced);
            await Play(status, ready);
            if (!ReferenceEquals(Deck.HandOf(ready).Single(), announced))
                throw new Exception("Adding junk replaced the announced intent.");
            var laterJunk = Deck.DrawOf(ready).Single(c => c is MonsterDisplayCard { IsPlaceholder: true });
            _resolvedScript = null;
            await Deck.PlayTurn(ready);
            if (!ReferenceEquals(_resolvedScript, announced) || Deck.ConsumeHold(ready) || !Deck.DrawOf(ready).Contains(laterJunk))
                throw new Exception("Adding junk changed the current action despite preserving its preview.");
            Deck.PrepareTurn(ready);
            _resolvedScript = null;
            await Deck.PlayTurn(ready);
            if (_resolvedScript != null || !Deck.ConsumeHold(ready) || Deck.DrawOf(ready).Contains(laterJunk))
                throw new Exception("Deferred junk did not skip the later action exactly once.");

            // 同一敌人先被强夺：先结算强夺空过，废牌留给之后一次行动，互不覆盖。
            var stolen = Enemy("stolen-target");
            Deck.PrepareTurn(stolen);
            Deck.RegisterSteal(stolen);
            await Play(status, stolen);
            var pendingJunk = Deck.DrawOf(stolen).Single(c => c is MonsterDisplayCard { IsPlaceholder: true });
            if (Deck.HandOf(stolen).Count != 0)
                throw new Exception("A stolen action was incorrectly replaced by a premature junk preview.");
            await Deck.PlayTurn(stolen);
            if (!Deck.DrawOf(stolen).Contains(pendingJunk) || Deck.ConsumeHold(stolen))
                throw new Exception("Theft skip incorrectly consumed the separate status action.");
            ForceRebuild(stolen);
            await Deck.PlayTurn(stolen);
            if (Deck.DrawOf(stolen).Contains(pendingJunk) || !Deck.ConsumeHold(stolen))
                throw new Exception("Status junk disappeared after the stolen action or failed to skip its own action.");

            // 多张按顺序生效；诅咒仍保留，与一次性状态牌不同。
            var multiple = Enemy("multiple-junk");
            await Play(status, multiple);
            await Play(status, multiple);
            var queued = Deck.DrawOf(multiple).Where(c => c is MonsterDisplayCard { IsPlaceholder: true }).ToList();
            if (Deck.HandOf(multiple).Any(c => c is MonsterDisplayCard { IsPlaceholder: true }))
                throw new Exception("Multiple new status cards changed the current hand.");
            for (int i = 0; i < 2; i++)
            {
                Deck.PrepareTurn(multiple);
                if (!ReferenceEquals(Deck.HandOf(multiple).First(), queued[i]))
                    throw new Exception("Status cards changed insertion order.");
                await Deck.PlayTurn(multiple);
                if (!Deck.ConsumeHold(multiple)) throw new Exception("A status card did not hold the script.");
            }
            var curse = combat.CreateCard<MonsterCardDebuffCard>(player);
            await Play(curse, multiple);
            var curseJunk = Deck.DrawOf(multiple).Single(c => c is MonsterDisplayCard { IsPlaceholder: true });
            Deck.PrepareTurn(multiple);
            await Deck.PlayTurn(multiple);
            if (!Deck.DrawOf(multiple).Contains(curseJunk) || !Deck.ConsumeHold(multiple))
                throw new Exception("Curse junk lost its existing persistence.");

            // 结算门卫必须跨越完整异步效果；补位通知不能只等飞卡或第一个命令启动。
            var waiting = Enemy("resolving-timing");
            Deck.PrepareTurn(waiting);
            var waitingCard = combat.CreateCard<MonsterMoveCard>(player);
            waitingCard.InitMove(waiting.Monster!.NextMove, [new DefendIntent()]);
            var waitingQueue = QueueOf(waiting);
            waitingQueue.Clear(); waitingQueue.Add(waitingCard);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _scriptGate = gate.Task;
            var playing = Deck.PlayTurn(waiting);
            bool held = Deck.IsResolving(waiting) && !playing.IsCompleted && !waitingQueue.Contains(waitingCard);
            gate.SetResult();
            await playing;
            _scriptGate = null;
            if (!held || Deck.IsResolving(waiting) || !ReferenceEquals(waitingQueue.Last(), waitingCard))
                throw new Exception("Intent completion was announced before the awaited effect and queue rotation finished.");

            gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _scriptGate = gate.Task;
            playing = Deck.PlayTurn(waiting);
            gate.SetException(new InvalidOperationException("probe effect failed"));
            try { await playing; throw new Exception("The expected effect failure was swallowed."); }
            catch (InvalidOperationException ex) when (ex.Message == "probe effect failed") { }
            _scriptGate = null;
            if (Deck.IsResolving(waiting) || waitingQueue.Contains(waitingCard))
                throw new Exception("Failed effect left the resolving guard stuck or rotated the failed card as completed.");

            Creature Enemy(string name)
                => new(ModelDb.Monster<Vantom>().ToMutable(), CombatSide.Enemy, name) { CombatState = combat };
            GD.Print("PASS: stolen copies cost 0; new junk leaves the announced intent and current action unchanged, activates at normal draw, survives rebuilds and bypasses Artifact; theft skips, multiple-card order and persistent Curses stay independent.");
        }
        finally { harmony.UnpatchAll(harmony.Id); Deck.ClearAll(); _resolvedScript = null; _scriptGate = null; }
    }

    public static bool RecordScript(CardModel card, ref Task __result)
    {
        _resolvedScript = card;
        __result = _scriptGate ?? Task.CompletedTask;
        return false;
    }

    private static IList<CardModel> QueueOf(Creature enemy)
    {
        var decks = (IDictionary)AccessTools.Field(Deck.ModelType, "Decks").GetValue(null)!;
        var deck = decks[enemy]!;
        return (IList<CardModel>)AccessTools.Field(deck.GetType(), "Queue").GetValue(deck)!;
    }

    private static Task Play(CardModel card, Creature target)
        => (Task)AccessTools.Method(card.GetType(), "OnPlay").Invoke(card,
            [new BlockingPlayerChoiceContext(), new CardPlay { Card = card, Player = card.Owner, Target = target,
                ResultPile = PileType.Exhaust, Resources = default, IsAutoPlay = false, PlayIndex = 0, PlayCount = 1 }])!;

    private static CardModel Copy(Player player, CardModel source)
        => (CardModel)AccessTools.Method(typeof(IntentCardMapper), "CreateCopy").Invoke(null, [player, source])!;

    private static class Deck
    {
        internal static readonly Type ModelType = typeof(MonsterMoveCard).Assembly.GetType("SelectSpecialCardMod.Scripts.Intent.MonsterDeck", true)!;
        private static object? Call(string method, params object[] args) => AccessTools.Method(ModelType, method).Invoke(null, args);
        internal static void ClearAll() => Call("ClearAll");
        internal static void PrepareTurn(Creature enemy) => Call("PrepareTurn", enemy);
        internal static void RegisterSteal(Creature enemy) => Call("RegisterSteal", enemy);
        internal static bool IsBuilt(Creature enemy) => (bool)Call("IsBuilt", enemy)!;
        internal static bool IsResolving(Creature enemy) => (bool)Call("IsResolving", enemy)!;
        internal static bool ConsumeHold(Creature enemy) => (bool)Call("ConsumeHold", enemy)!;
        internal static IReadOnlyList<CardModel> HandOf(Creature enemy) => (IReadOnlyList<CardModel>)Call("HandOf", enemy)!;
        internal static IReadOnlyList<CardModel> DrawOf(Creature enemy) => (IReadOnlyList<CardModel>)Call("DrawOf", enemy)!;
        internal static Task PlayTurn(Creature enemy) => (Task)Call("PlayTurn", enemy)!;
    }

    private static void ForceRebuild(Creature enemy)
    {
        var decks = (IDictionary)AccessTools.Field(Deck.ModelType, "Decks").GetValue(null)!;
        var deck = decks[enemy]!;
        AccessTools.Field(deck.GetType(), "PhaseKey").SetValue(deck, "probe-old-phase");
        Deck.PrepareTurn(enemy);
    }
}
