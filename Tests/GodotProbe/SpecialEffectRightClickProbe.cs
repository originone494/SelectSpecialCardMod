using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using STS2RitsuLib.Interactions.RightClick;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Hook;

// 检查实际注册的本机预检与同步执行条件，选牌期间不能先入队、结束后才执行。
public static class SpecialEffectRightClickProbe
{
    private static NPlayerHand? _hand;
    private static NCombatRoom? _room;
    private static NPlayerHand.Mode _mode;
    private static bool _current, _inCardPlay;

    public static void Check()
    {
        var h = new Harmony("scm.special-effect-right-click.selection-probe");
        var hand = new NPlayerHand();
        var room = new NCombatRoom();
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(NPlayerHand), nameof(NPlayerHand.Instance)), prefix: new HarmonyMethod(typeof(SpecialEffectRightClickProbe), nameof(Hand)));
            h.Patch(AccessTools.PropertyGetter(typeof(NCombatRoom), nameof(NCombatRoom.Instance)), prefix: new HarmonyMethod(typeof(SpecialEffectRightClickProbe), nameof(Room)));
            h.Patch(AccessTools.PropertyGetter(typeof(NPlayerHand), nameof(NPlayerHand.CurrentMode)), prefix: new HarmonyMethod(typeof(SpecialEffectRightClickProbe), nameof(Mode)));
            h.Patch(AccessTools.PropertyGetter(typeof(NPlayerHand), nameof(NPlayerHand.InCardPlay)), prefix: new HarmonyMethod(typeof(SpecialEffectRightClickProbe), nameof(InPlay)));
            h.Patch(AccessTools.Method(typeof(ActiveScreenContext), nameof(ActiveScreenContext.IsCurrent)), prefix: new HarmonyMethod(typeof(SpecialEffectRightClickProbe), nameof(Current)));
            h.Patch(AccessTools.Method(typeof(KeMoMoJianKeCard), nameof(KeMoMoJianKeCard.CanActivateSpecialEffect)), prefix: new HarmonyMethod(typeof(SpecialEffectRightClickProbe), nameof(Eligible)));
            SpecialEffectHook.Init();
            var binding = AccessTools.Field(typeof(SpecialEffectHook), "_binding").GetValue(null)!;
            var preflight = (Func<ModRightClickContext, bool>?)AccessTools.Property(binding.GetType(), "CanHandleLocal").GetValue(binding);
            var execute = (Func<ModRightClickExecutionContext, bool>?)AccessTools.Property(binding.GetType(), "CanExecute").GetValue(binding);
            if (preflight == null || execute == null) throw new Exception("Right-click binding lacks a local preflight or synchronized execution guard.");

            var player = ArrivalEquipmentProbe.NewPlayer();
            var card = ArrivalEquipmentProbe.AddCard(typeof(KeMoMoJianKeCard), player, PileType.Hand);
            var trigger = new ModRightClickTrigger(false, null, ModRightClickSource.HandCard, null);
            var local = new ModRightClickContext(player, card, trigger);
            var synced = new ModRightClickExecutionContext(player, card, trigger, null, null);
            _hand = hand; _room = room; _mode = NPlayerHand.Mode.Play; _current = true; _inCardPlay = false;
            Expect(true);
            foreach (var mode in new[] { NPlayerHand.Mode.SimpleSelect, NPlayerHand.Mode.UpgradeSelect, NPlayerHand.Mode.None })
            { _mode = mode; Expect(false); }
            // 远端可以正在自己的选牌界面，但同步后的游戏规则判据不能因此分歧。
            if (!execute(synced)) throw new Exception("Local selection UI leaked into the synchronized special-effect guard.");
            _mode = NPlayerHand.Mode.Play; Expect(true);
            _current = false; Expect(false); // 网格选择、效果选择、地图等都不在当前战斗界面。
            _current = true; _inCardPlay = true; Expect(false);
            _inCardPlay = false; _room = null; Expect(false);
            _room = room; _hand = null; Expect(false);
            _hand = hand; Expect(true);
            var pileTrigger = new ModRightClickTrigger(false, null, ModRightClickSource.CombatPileCard, PileType.Hand);
            if (preflight(new ModRightClickContext(player, card, pileTrigger))
                || execute(new ModRightClickExecutionContext(player, card, pileTrigger, null, null)))
                throw new Exception("A pile/preview card was treated as an active hand card.");
            if (preflight(new ModRightClickContext(ArrivalEquipmentProbe.NewPlayer(), card, trigger)))
                throw new Exception("Another player's card passed local preflight.");
            card.RemoveFromCurrentPile(silent: true); PileType.Exhaust.GetPile(player).AddInternal(card);
            Expect(false);
            if (execute(synced)) throw new Exception("A card moved out of hand passed the execution guard.");
            GD.Print("PASS: actual right-click binding rejects hand/grid/upgrade selections before queuing, restores normal hand eligibility, and keeps local UI out of synchronized execution.");

            void Expect(bool expected)
            { if (preflight(local) != expected) throw new Exception("Wrong right-click preflight: " + _mode + "/" + _current + "/" + _inCardPlay); }
        }
        finally
        {
            h.UnpatchAll(h.Id); _hand = null; _room = null;
            hand.Free(); room.Free();
        }
    }

    public static bool Hand(ref NPlayerHand? __result) { __result = _hand; return false; }
    public static bool Room(ref NCombatRoom? __result) { __result = _room; return false; }
    public static bool Mode(ref NPlayerHand.Mode __result) { __result = _mode; return false; }
    public static bool InPlay(ref bool __result) { __result = _inCardPlay; return false; }
    public static bool Current(ref bool __result) { __result = _current; return false; }
    public static bool Eligible(ref bool __result) { __result = true; return false; }
}
