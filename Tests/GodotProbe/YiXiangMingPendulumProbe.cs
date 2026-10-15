using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using SelectSpecialCardMod.Scripts.Cards.Special;

public static class YiXiangMingPendulumProbe
{
    private static bool _ending;
    private static bool _activePendulumReady;
    public static bool Ending(ref bool __result) { __result = _ending; return false; }
    public static bool ActivePendulumReady(ref bool __result) { __result = _activePendulumReady; return false; }

    public static async Task Check()
    {
        var h = new Harmony("scm.pendulum-retain.godot-framework");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsInProgress"),
                prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(ArrivalEquipmentProbe.InProgress)));
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"),
                prefix: new HarmonyMethod(typeof(YiXiangMingPendulumProbe), nameof(Ending)));
            h.Patch(AccessTools.Method(typeof(RunState), "IterateHookListeners", [typeof(ICombatState)]),
                prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"));
            h.Patch(AccessTools.Method(typeof(CombatState), "IterateHookListeners", Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"));

            var player = ArrivalEquipmentProbe.NewPlayer();
            AccessTools.Field(player.GetType(), "<Character>k__BackingField").SetValue(player, new MegaCrit.Sts2.Core.Models.Characters.Ironclad());
            var hand = PileType.Hand.GetPile(player);
            var filler = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), player, PileType.Hand);
            var angel = (YiXiangMingVoiceCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianShiZhiShengCard), player, PileType.Hand);
            var devil = (YiXiangMingVoiceCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingEMoZhiShengCard), player, PileType.Hand);
            EquipmentHookProbe.Models = [angel, devil];
            if (angel.ShouldRetainThisTurn || devil.ShouldRetainThisTurn || devil.PendulumOutlineColor != null
                || angel.Keywords.Contains(CardKeyword.Unplayable) || devil.Keywords.Contains(CardKeyword.Unplayable))
                throw new Exception("Ordinary unplaced voice cards acquired pendulum retention or outline.");

            var placement = typeof(YiXiangMingVoiceCard).Assembly.GetType("SelectSpecialCardMod.Scripts.Tool.YiXiangMingHandPlacement", true)!;
            bool CanPlace(Player owner) => (bool)AccessTools.Method(placement, "CanPlacePair").Invoke(null, [owner])!;
            if (!CanPlace(player)) throw new Exception("Ordinary edge cards incorrectly blocked voice activation.");
            if (!await (Task<bool>)AccessTools.Method(placement, "PutAtEdges").Invoke(null, [player, angel, devil])!)
                throw new Exception("Voice placement was rejected.");
            if (hand.Cards[0] != angel || hand.Cards[^1] != devil
                || !angel.ShouldRetainThisTurn || !devil.ShouldRetainThisTurn
                || !angel.CanApplyPendulumEffect || !devil.CanApplyPendulumEffect
                || angel.PendulumOutlineColor != Colors.DodgerBlue || devil.PendulumOutlineColor != Colors.DodgerBlue)
                throw new Exception("Passive voice cards should apply their effects while retaining the blue placement outline.");

            // 只模拟未来主动灵摆的发动资格，验证金边不依赖被动适用条件。
            h.Patch(AccessTools.Method(typeof(YiXiangMingVoiceCard), nameof(YiXiangMingVoiceCard.CanActivatePendulumEffect)),
                prefix: new HarmonyMethod(typeof(YiXiangMingPendulumProbe), nameof(ActivePendulumReady)));
            _activePendulumReady = true;
            if (angel.PendulumOutlineColor != NCardHighlight.gold || devil.PendulumOutlineColor != NCardHighlight.gold)
                throw new Exception("An available active pendulum effect did not show gold.");
            _ending = true;
            if (angel.PendulumOutlineColor != Colors.DodgerBlue)
                throw new Exception("An active effect bypassed placement/combat eligibility when choosing gold.");
            _ending = false;
            _activePendulumReady = false;
            if (angel.PendulumOutlineColor != Colors.DodgerBlue || devil.PendulumOutlineColor != Colors.DodgerBlue)
                throw new Exception("Unavailable active pendulum effects failed to return to blue.");
            if (CanPlace(player) || angel.CanActivateSpecialEffect(player) || devil.CanActivateSpecialEffect(player))
                throw new Exception("Voice activation was allowed while placed cards occupied the two sides.");

            if (!angel.Keywords.Contains(CardKeyword.Unplayable) || !devil.Keywords.Contains(CardKeyword.Unplayable)
                || angel.CanPlay(out var reason, out _) || !reason.HasFlag(UnplayableReason.HasUnplayableKeyword)
                || MegaCrit.Sts2.Core.Hooks.Hook.ShouldPlay(angel.CombatState!, angel, out _, AutoPlayType.None)
                || MegaCrit.Sts2.Core.Hooks.Hook.ShouldPlay(devil.CombatState!, devil, out _, AutoPlayType.Default))
                throw new Exception("Locked pendulum cards were not blocked by native manual/auto play checks.");
            await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), angel, null);
            AssertLockedPair();
            if (!angel.PlacedAtEdgeByEffect || angel.Pile != hand)
                throw new Exception("Attempted autoplay removed or unlocked a pendulum card.");
            var blockedAdd = await CardPileCmd.Add(devil, PileType.Play, skipVisuals: true);
            if (blockedAdd.success || devil.Pile != hand || !devil.PlacedAtEdgeByEffect)
                throw new Exception("Moving to Play bypassed the pendulum play lock.");
            var allowedCard = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), player, PileType.Hand);
            var mixed = await CardPileCmd.Add(new CardModel[] { angel, allowedCard }, PileType.Play.GetPile(player), skipVisuals: true);
            if (mixed.Count != 2 || mixed[0].success || mixed[0].cardAdded != angel
                || !mixed[1].success || mixed[1].cardAdded != allowedCard || allowedCard.Pile?.Type != PileType.Play)
                throw new Exception("Play lock broke batch result ordering or blocked an ordinary card.");
            AssertLockedPair();

            hand.MoveToBottomInternal(filler);
            hand.MoveToBottomInternal(angel);
            hand.MoveToTopInternal(devil);
            AssertLockedPair();

            // 原版节点的最小容器验证显示次序；不加载卡面场景，也不执行依赖 Hitbox 的扇形布局。
            var handNode = new NPlayerHand();
            try
            {
                var container = new Control();
                handNode.AddChild(container);
                AccessTools.Property(typeof(NPlayerHand), nameof(NPlayerHand.CardHolderContainer)).SetValue(handNode, container);
                var devilHolder = AddHolder(devil);
                AddHolder(filler);
                var angelHolder = AddHolder(angel);
                AccessTools.Method(placement, "SyncVisibleOrder").Invoke(null, [handNode]);
                if (container.GetChild(0) != angelHolder || container.GetChild(container.GetChildCount() - 1) != devilHolder)
                    throw new Exception("Visible hand order did not keep the pendulum cards at their locked sides.");
                container.MoveChild(angelHolder, container.GetChildCount() - 1);
                container.MoveChild(devilHolder, 0);
                AccessTools.Method(placement, "SyncVisibleOrder").Invoke(null, [handNode]);
                if (container.GetChild(0) != angelHolder || container.GetChild(container.GetChildCount() - 1) != devilHolder)
                    throw new Exception("Visible reorder bypassed the pendulum locks.");

                NHandCardHolder AddHolder(CardModel model)
                {
                    var holder = new NHandCardHolder();
                    var cardNode = new NCard();
                    AccessTools.Field(typeof(NCard), "_model").SetValue(cardNode, model);
                    AccessTools.Property(typeof(NCardHolder), nameof(NCardHolder.CardNode)).SetValue(holder, cardNode);
                    holder.AddChild(cardNode);
                    container.AddChild(holder);
                    return holder;
                }
            }
            finally { handNode.Free(); }
            var incoming = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.DefendIronclad), player, PileType.Hand);
            AssertLockedPair();
            var atTop = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), player, PileType.Draw);
            atTop.RemoveFromCurrentPile(silent: true);
            hand.AddInternal(atTop, 0, silent: true);
            AssertLockedPair();
            if (hand.Cards.ToList().IndexOf(incoming) >= hand.Cards.ToList().IndexOf(devil) || hand.Cards.ToList().IndexOf(atTop) <= hand.Cards.ToList().IndexOf(angel))
                throw new Exception("Incoming hand cards were not inserted between the locked cards.");

            _ending = true;
            if (devil.CanApplyPendulumEffect || devil.PendulumOutlineColor != Colors.DodgerBlue || !devil.ShouldRetainThisTurn)
                throw new Exception("An unavailable pendulum effect should show blue and preserve retention/position.");
            AssertLockedPair(checkColor: false);
            _ending = false;

            var replacementLeft = (YiXiangMingVoiceCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianShiZhiShengCard), player, PileType.Hand);
            var replacementRight = (YiXiangMingVoiceCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingEMoZhiShengCard), player, PileType.Hand);
            if (await (Task<bool>)AccessTools.Method(placement, "PutAtEdges").Invoke(null, [player, replacementLeft, replacementRight])!
                || replacementLeft.PlacedAtEdgeByEffect || replacementRight.PlacedAtEdgeByEffect)
                throw new Exception("A new pair displaced occupied locked edge slots.");
            AssertLockedPair();

            // 探针未加载全游戏模型缓存；使用同一属性的原版存档载荷验证实际 Fill 恢复。
            var property = typeof(YiXiangMingVoiceCard).GetProperty(nameof(YiXiangMingVoiceCard.PlacedAtEdgeByEffect))!;
            if (!Attribute.IsDefined(property, typeof(SavedPropertyAttribute)))
                throw new Exception("Placement marker is missing native saved-property metadata.");
            var saved = new SavedProperties
            {
                bools = [new SavedProperties.SavedProperty<bool>(property.Name, angel.PlacedAtEdgeByEffect)],
                ints = [new SavedProperties.SavedProperty<int>(nameof(YiXiangMingVoiceCard.LockedPendulumSide), (int)angel.LockedPendulumSide)],
            };
            var restoredPlayer = ArrivalEquipmentProbe.NewPlayer();
            var restored = (YiXiangMingVoiceCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingTianShiZhiShengCard), restoredPlayer, PileType.Hand);
            saved.Fill(restored);
            var restoredHand = PileType.Hand.GetPile(restoredPlayer);
            var restoredIncoming = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), restoredPlayer, PileType.Hand);
            restoredHand.MoveToTopInternal(restoredIncoming);
            if (!restored.PlacedAtEdgeByEffect || !restored.Keywords.Contains(CardKeyword.Retain) || !restored.ShouldRetainThisTurn
                || !restored.Keywords.Contains(CardKeyword.Unplayable)
                || restored.LockedPendulumSide != PendulumEdgeSide.Left || restoredHand.Cards[0] != restored)
                throw new Exception("Native saved-property restoration lost placement retention.");
            if (CanPlace(restoredPlayer) || restored.CanActivateSpecialEffect(restoredPlayer)
                || await restored.TryActivateSpecialEffect(new BlockingPlayerChoiceContext(), restoredPlayer))
                throw new Exception("A sole placed left card incorrectly allowed voice activation.");
            EquipmentHookProbe.Models = [angel, devil];

            // 使用实际入堆命令与 AfterCardChangedPiles 分发，确认离手清除放置状态和新增保留。
            await CardPileCmd.Add(angel, PileType.Discard, skipVisuals: true);
            if (angel.PlacedAtEdgeByEffect || angel.Keywords.Contains(CardKeyword.Retain) || angel.PendulumOutlineColor != null
                || angel.Keywords.Contains(CardKeyword.Unplayable)
                || angel.LockedPendulumSide != PendulumEdgeSide.None)
                throw new Exception("Leaving hand retained a stale pendulum grant or outline.");
            await CardPileCmd.Add(angel, PileType.Hand, skipVisuals: true);
            if (angel.PlacedAtEdgeByEffect || angel.Keywords.Contains(CardKeyword.Unplayable)
                || !angel.ShouldPlay(angel, AutoPlayType.None) || hand.Cards[^1] != devil)
                throw new Exception("Returning a released card restored its old lock or displaced the remaining right lock.");
            if (CanPlace(player) || angel.CanActivateSpecialEffect(player) || devil.CanActivateSpecialEffect(player)
                || await angel.TryActivateSpecialEffect(new BlockingPlayerChoiceContext(), player))
                throw new Exception("A sole placed right card incorrectly allowed voice activation.");
            await CardPileCmd.Add(devil, PileType.Discard, skipVisuals: true);
            if (!CanPlace(player)) throw new Exception("Clearing both placed cards did not restore the placement condition.");

            var alreadyRetained = (YiXiangMingVoiceCard)ArrivalEquipmentProbe.AddCard(typeof(YiXiangMingEMoZhiShengCard), player, PileType.Hand);
            alreadyRetained.AddKeyword(CardKeyword.Retain);
            alreadyRetained.AddKeyword(CardKeyword.Unplayable);
            alreadyRetained.PlacedAtEdgeByEffect = true;
            EquipmentHookProbe.Models = [alreadyRetained];
            await CardPileCmd.Add(alreadyRetained, PileType.Discard, skipVisuals: true);
            if (alreadyRetained.PlacedAtEdgeByEffect || !alreadyRetained.Keywords.Contains(CardKeyword.Retain)
                || !alreadyRetained.Keywords.Contains(CardKeyword.Unplayable))
                throw new Exception("Placement cleanup removed pre-existing Retain.");

            void AssertLockedPair(bool checkColor = true)
            {
                if (hand.Cards[0] != angel || hand.Cards[^1] != devil
                    || angel.LockedPendulumSide != PendulumEdgeSide.Left || devil.LockedPendulumSide != PendulumEdgeSide.Right)
                    throw new Exception("Locked pendulum positions changed after a hand mutation.");
                if (checkColor && (angel.PendulumOutlineColor != Colors.DodgerBlue || devil.PendulumOutlineColor != Colors.DodgerBlue))
                    throw new Exception("Passive locked voice cards lost their blue placement outline.");
            }
        }
        finally { h.UnpatchAll(h.Id); EquipmentHookProbe.Models = []; _ending = false; _activePendulumReady = false; }
        GD.Print("PASS: passive voice pendulum effects remain applicable with blue outlines; only ready active pendulum effects show gold; activation conditions, locks/Unplayable, native play guards, UI order, saved restoration and cleanup remain correct.");
    }
}
