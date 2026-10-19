using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Tool;

// 替换伤害、抽牌和选择边界；执行真实OnPlay/检索入口，检查手动确认及选择范围。
public static class SelectionFlowProbe
{
    private static CardSelectorPrefs? _prefs;
    private static int _choices;

    public static async Task Check()
    {
        var h = new Harmony("scm.selection-boundaries.godot-framework");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"),
                prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(ArrivalEquipmentProbe.LiveCombat)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "Draw", [typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(SelectionFlowProbe), nameof(Draw)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromHand"),
                prefix: new HarmonyMethod(typeof(SelectionFlowProbe), nameof(Select)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromSimpleGrid"),
                prefix: new HarmonyMethod(typeof(SelectionFlowProbe), nameof(Select)));
            h.Patch(AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Damage),
                    [typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay)]),
                prefix: new HarmonyMethod(typeof(SelectionFlowProbe), nameof(Damage)));
            var ctx = new BlockingPlayerChoiceContext();
            foreach (int count in new[] { 0, 1, 2, 4 })
            {
                _prefs = null; _choices = 0;
                var player = ArrivalEquipmentProbe.NewPlayer();
                var combat = player.Creature.CombatState!;
                var source = combat.CreateCard(ModelDb.Card<TianShiDeShiSheCard>(), player);
                PileType.Play.GetPile(player).AddInternal(source);
                for (int i = 0; i < count; i++)
                    PileType.Hand.GetPile(player).AddInternal(combat.CreateCard(ModelDb.Card<TianShiDeShiSheCard>(), player));
                await (Task)AccessTools.Method(typeof(TianShiDeShiSheCard), "OnPlay").Invoke(source,
                    [ctx, new CardPlay { Card = source, Player = player, Target = null, IsAutoPlay = false, ResultPile = PileType.Exhaust,
                        Resources = default, PlayIndex = 0, PlayCount = 1 }])!;
                int expected = Math.Min(2, count);
                if (count == 0 ? _choices != 0 : _choices != 1 || _prefs == null
                    || _prefs.Value.MinSelect != expected || _prefs.Value.MaxSelect != expected || !_prefs.Value.RequireManualConfirmation)
                    throw new Exception("Graceful Charity requested an impossible selection with hand count " + count);
            }

            _prefs = null; _choices = 0;
            var retriever = ArrivalEquipmentProbe.NewPlayer();
            await SelectToolCmd.RetrieverCard(ctx, retriever, card => card is TianShiDeShiSheCard,
                amount: 2, minAmount: 2, requireManualConfirmation: true);
            if (_choices != 1 || _prefs == null || _prefs.Value.MinSelect != 1 || _prefs.Value.MaxSelect != 1 || !_prefs.Value.RequireManualConfirmation)
                throw new Exception("Retrieval did not clamp its mandatory count to the one available candidate.");

            // 默认及旧版显式false入口均不能关闭玩家确认；不改变原有0~1可跳过范围。
            foreach (bool legacyConfirm in new[] { true, false })
            {
                _prefs = null; _choices = 0;
                await SelectToolCmd.RetrieverCard(ctx, retriever, card => card is TianShiDeShiSheCard,
                    requireManualConfirmation: legacyConfirm);
                AssertManualRetrieval();
            }

            // 用户报告的实际入口：雷诺哈特检索并丢弃、水仙登场检索。
            var reno = retriever.Creature.CombatState!.CreateCard(ModelDb.Card<ZhuLeiLeiNuoHaTeCard>(), retriever);
            PileType.Play.GetPile(retriever).AddInternal(reno);
            _prefs = null; _choices = 0;
            await (Task)AccessTools.Method(typeof(ZhuLeiLeiNuoHaTeCard), "OnPlay").Invoke(reno,
                [ctx, new CardPlay { Card = reno, Player = retriever, Target = retriever.Creature, IsAutoPlay = false,
                    ResultPile = PileType.Discard, Resources = default, PlayIndex = 0, PlayCount = 1 }])!;
            AssertManualRetrieval();
            var water = (ZhuLeiShuiXianCard)ArrivalEquipmentProbe.AddCard(typeof(ZhuLeiShuiXianCard), retriever, PileType.Hand);
            _prefs = null; _choices = 0;
            await water.DengChangSpecial(ctx, retriever);
            AssertManualRetrieval();

            _prefs = null; _choices = 0;
            var witch = retriever.Creature.CombatState!.CreateCard(ModelDb.Card<QiXingDaoMoNvCard>(), retriever);
            PileType.Play.GetPile(retriever).AddInternal(witch);
            PileType.Hand.GetPile(retriever).AddInternal(retriever.Creature.CombatState.CreateCard(ModelDb.Card<TianShiDeShiSheCard>(), retriever));
            await (Task)AccessTools.Method(typeof(QiXingDaoMoNvCard), "OnPlay").Invoke(witch,
                [ctx, new CardPlay { Card = witch, Player = retriever, Target = retriever.Creature, IsAutoPlay = false,
                    ResultPile = PileType.Discard, Resources = default, PlayIndex = 0, PlayCount = 1 }])!;
            if (_choices != 1 || _prefs == null || _prefs.Value.MinSelect != 1 || _prefs.Value.MaxSelect != 1)
                throw new Exception("Seven-Star Witch allowed skipping its mandatory discard.");
            GD.Print("PASS: discard/retrieval keeps feasible ranges and manual confirmation, including actual Reinoheart and Kitkallos retrieval and legacy confirmation flags.");

            void AssertManualRetrieval()
            {
                if (_choices != 1 || _prefs == null || _prefs.Value.MinSelect != 0 || _prefs.Value.MaxSelect != 1
                    || !_prefs.Value.RequireManualConfirmation)
                    throw new Exception("Player retrieval bypassed manual confirmation or changed its optional range.");
            }
        }
        finally { h.UnpatchAll(h.Id); _prefs = null; }
    }

    public static bool Draw(ref Task<IEnumerable<CardModel>> __result)
    { __result = Task.FromResult<IEnumerable<CardModel>>([]); return false; }

    public static bool Damage(ref Task<IEnumerable<DamageResult>> __result)
    { __result = Task.FromResult<IEnumerable<DamageResult>>([]); return false; }

    public static bool Select(CardSelectorPrefs prefs, ref Task<IEnumerable<CardModel>> __result)
    {
        _prefs = prefs; _choices++;
        // 不执行后续弃牌/生成：本探针只捕获真实流程传出的选择范围。
        __result = Task.FromResult<IEnumerable<CardModel>>([]);
        return false;
    }
}
