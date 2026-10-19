using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Intent;

// 实际只读预览：验证顺序、当前行动隔离、待抽废牌及出牌后轮转，不执行AI或消费随机数。
public static class IntentForecastProbe
{
    private static readonly Type DeckType = typeof(MonsterMoveCard).Assembly.GetType("SelectSpecialCardMod.Scripts.Intent.MonsterDeck", true)!;
    private static object? Call(string name, params object?[] args) => AccessTools.Method(DeckType, name).Invoke(null, args);
    private static IReadOnlyList<CardModel> Future(Creature? enemy) => (IReadOnlyList<CardModel>)Call("FutureOf", enemy)!;

    public static void Check()
    {
        Call("ClearAll");
        try
        {
            var player = ArrivalEquipmentProbe.NewPlayer();
            AccessTools.Field(typeof(RunState), "_players").SetValue(player.RunState, new List<Player> { player });
            var combat = player.Creature.CombatState!;
            var enemy = new Creature(ModelDb.Monster<Vantom>().ToMutable(), CombatSide.Enemy, "forecast") { CombatState = combat };
            var deck = Call("GetOrCreate", enemy)!;
            AccessTools.Field(deck.GetType(), "Built").SetValue(deck, true);
            AccessTools.Field(deck.GetType(), "Energy").SetValue(deck, 3);
            var queue = (IList<CardModel>)AccessTools.Field(deck.GetType(), "Queue").GetValue(deck)!;
            var pending = (IList<CardModel>)AccessTools.Field(deck.GetType(), "PendingJunk").GetValue(deck)!;
            var a = Move("A");
            var b = Move("B");
            var c = Move("C");
            queue.Add(a); queue.Add(b); queue.Add(c);
            Expect([b, c]); // 当前A只显示为亮色卡，不额外回填到灰色队列。
            queue.RemoveAt(0); queue.Add(a); // 实际打出A后，B成为当前行动。
            Expect([c, a]);
            var repeatedMove = Move("A"); // 两个真实行动即使同名，也不能按卡名误删。
            queue.Clear(); queue.Add(a); queue.Add(repeatedMove); queue.Add(b);
            Expect([repeatedMove, b]);
            queue.Clear(); queue.Add(a); queue.Add(b); queue.Add(c);

            var firstJunk = Junk(persists: false);
            var secondJunk = Junk(persists: false);
            pending.Add(firstJunk); pending.Add(secondJunk);
            var originalQueue = queue.ToArray();
            var originalPending = pending.ToArray();
            for (int i = 0; i < 20; i++) Expect([firstJunk, secondJunk, b, c]);
            if (!queue.SequenceEqual(originalQueue) || !pending.SequenceEqual(originalPending)
                || (int)Call("EnergyOf", enemy)! != 3
                || !ReferenceEquals(((IReadOnlyList<CardModel>)Call("HandOf", enemy)!).Single(), a))
                throw new Exception("Reading the forecast changed current intents, activated junk or spent energy.");

            // 强夺后没有本回合亮色卡，不能继续把被抢走的A列为后续行动。
            if (!(bool)Call("TryRemoveIntent", enemy, a)!) throw new Exception("The fixture could not steal the current action.");
            Expect([firstJunk, secondJunk, b, c]);
            var skips = (IDictionary)AccessTools.Field(DeckType, "PendingSkip").GetValue(null)!;
            Future(enemy); Future(enemy);
            if ((int)skips[enemy]! != 1) throw new Exception("Forecast consumed the pending stolen-action skip.");
            Call("Clear", enemy);

            deck = Call("GetOrCreate", enemy)!;
            AccessTools.Field(deck.GetType(), "Built").SetValue(deck, true);
            queue = (IList<CardModel>)AccessTools.Field(deck.GetType(), "Queue").GetValue(deck)!;
            pending = (IList<CardModel>)AccessTools.Field(deck.GetType(), "PendingJunk").GetValue(deck)!;
            queue.Add(firstJunk); queue.Add(secondJunk); queue.Add(a); queue.Add(b);
            var newerJunk = Junk(persists: false);
            pending.Add(newerJunk);
            Expect([secondJunk, newerJunk, a, b]); // 当前一次性废牌不循环，旧废牌先于新废牌。
            queue.Clear(); pending.Clear();
            var curse = Junk(persists: true);
            queue.Add(curse); queue.Add(a);
            Expect([a]); // 保留型诅咒也只有实际打出后才轮转，不复制当前灰卡。
            queue.Clear(); queue.Add(a);
            Expect([]); // 只有一招时不显示与当前意图相同的额外灰卡。
            if (Future(null).Count != 0) throw new Exception("No-enemy forecast was not empty.");

            // 折叠占位不会随大量后续行动无限增长；没有入树时也不会创建NCard资源。
            var stack = new IntentFutureStackNode();
            try
            {
                stack.SetModels([a, b, c], interactive: true);
                var three = stack.Footprint;
                stack.SetModels(Enumerable.Range(0, 100).Select(_ => (CardModel)a).ToList(), interactive: true);
                if (stack.Footprint != three || stack.GetChildCount() != 0)
                    throw new Exception("Collapsed preview grew with the whole deck or eagerly created card views.");
                stack.SetModels(Array.Empty<CardModel>(), interactive: false);
                if (stack.Footprint != Vector2.Zero) throw new Exception("Empty forecast retained its layout footprint.");
            }
            finally { stack.Free(); }

            // 卡飞出后保留当前槽位，灰色叠卡不能因当前行暂时为空而跳到中央。
            var overlay = new IntentCardOverlayNode();
            try
            {
                var row = new Control();
                var forecast = new IntentFutureStackNode();
                forecast.SetModels([b, c], interactive: false);
                overlay.AddChild(row); overlay.AddChild(forecast);
                AccessTools.Field(typeof(IntentCardOverlayNode), "_row").SetValue(overlay, row);
                AccessTools.Field(typeof(IntentCardOverlayNode), "_future").SetValue(overlay, forecast);
                AccessTools.Field(typeof(IntentCardOverlayNode), "_reservedCurrentSize").SetValue(overlay, new Vector2(135, 190));
                AccessTools.Method(typeof(IntentCardOverlayNode), "LayoutRow").Invoke(overlay, null);
                var before = row.Size;
                var stackBefore = forecast.Position;
                if (!Mathf.IsZeroApprox(row.Position.X + 135f * 0.5f))
                    throw new Exception("Current intent is centered on the combined current/future width instead of the creature anchor.");
                AccessTools.Field(typeof(IntentCardOverlayNode), "_reservedCurrentSize").SetValue(overlay, Vector2.Zero);
                row.AddChild(new Control { CustomMinimumSize = new Vector2(135, 190) });
                AccessTools.Method(typeof(IntentCardOverlayNode), "LayoutRow").Invoke(overlay, null);
                if (row.Size != before || forecast.Position != stackBefore)
                    throw new Exception("The current-intent slot or folded-stack anchor moved while filling the reserved slot.");
                forecast.SetModels([b], interactive: false);
                AccessTools.Method(typeof(IntentCardOverlayNode), "LayoutRow").Invoke(overlay, null);
                if (!Mathf.IsZeroApprox(row.Position.X + 135f * 0.5f))
                    throw new Exception("Changing the future stack width shifted the current intent away from the creature center.");

                // 选牌/地图等界面统一隐藏整个展示，含脱离悬浮层的飞卡和能量标签；恢复不清空队列。
                var flight = new Control { TopLevel = true };
                var energy = new Label();
                try
                {
                    var visibility = AccessTools.Method(typeof(IntentCardOverlayNode), "SetCombatVisibility");
                    AccessTools.Field(typeof(IntentCardOverlayNode), "_energyLabel").SetValue(overlay, energy);
                    AccessTools.Method(typeof(IntentCardOverlayNode), "TrackDetachedVisual").Invoke(overlay, [flight]);
                    visibility.Invoke(overlay, [true]);
                    energy.Visible = true;
                    var footprint = forecast.Footprint;
                    var childCount = row.GetChildCount();
                    visibility.Invoke(overlay, [false]);
                    if (overlay.Visible || flight.Visible || energy.Visible)
                        throw new Exception("Hiding intents left an overlay, detached flight or energy label visible.");
                    visibility.Invoke(overlay, [true]);
                    if (!overlay.Visible || !flight.Visible || forecast.Footprint != footprint || row.GetChildCount() != childCount)
                        throw new Exception("Restoring intents lost the current card or folded forecast.");
                }
                finally
                {
                    AccessTools.Field(typeof(IntentCardOverlayNode), "_energyLabel").SetValue(overlay, null);
                    flight.Free(); energy.Free();
                }

                // 效果已结算、飞出演出未结束时仍然等待；不能因效果很快就提前补位。
                AccessTools.Field(typeof(IntentCardOverlayNode), "_resolvingIntent").SetValue(overlay, true);
                AccessTools.Field(typeof(IntentCardOverlayNode), "_intentFlightFinished").SetValue(overlay, false);
                overlay.OnCardResolved(true);
                if (!(bool)AccessTools.Field(typeof(IntentCardOverlayNode), "_resolvingIntent").GetValue(overlay)!
                    || !(bool)AccessTools.Field(typeof(IntentCardOverlayNode), "_intentEffectFinished").GetValue(overlay)!)
                    throw new Exception("Intent promotion did not wait for the outgoing-card animation after effects completed.");
                overlay.OnCardResolved(false);
                if ((bool)AccessTools.Field(typeof(IntentCardOverlayNode), "_resolvingIntent").GetValue(overlay)!)
                    throw new Exception("An interrupted intent retained its visual resolving guard.");
            }
            finally { overlay.Free(); }
            GD.Print("PASS: forecast is read-only, excludes the current action without duplicating single-move enemies, preserves deferred junk/theft skips and post-play order, and bounds the collapsed stack footprint.");

            MonsterMoveCard Move(string title)
            {
                var card = combat.CreateCard<MonsterMoveCard>(player);
                card.InitMove(null, [new DefendIntent()]);
                card.CardTitle = title;
                return card;
            }
            MonsterDisplayCard Junk(bool persists)
            {
                MonsterDisplayCard card = persists ? combat.CreateCard<MonsterCardDebuffCard>(player) : combat.CreateCard<MonsterStatusCard>(player);
                card.IsPlaceholder = true;
                card.JunkPersists = persists;
                return card;
            }
            void Expect(IReadOnlyList<CardModel> expected)
            {
                var actual = Future(enemy);
                if (actual.Count != expected.Count || actual.Zip(expected).Any(pair => !ReferenceEquals(pair.First, pair.Second)))
                    throw new Exception("Future-action order, current-card exclusion or loop handling is incorrect.");
            }
        }
        finally { Call("ClearAll"); }
    }
}
