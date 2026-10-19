using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Cards;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Rules;
using SelectSpecialCardMod.Scripts.Tool;

// 实际调度器及杯满OnPlay，替换检索/弃牌选择、命令演出和后注效果边界。
public static class CardEffectQueueProbe
{
    private static readonly List<string> Events = [];
    private static CardModel? _discard, _arrival;
    private static bool _discardArrival, _throwDiscard;

    public static async Task Check()
    {
        await CheckScheduler();
        var h = new Harmony("scm.card-effect-queue.order-probe");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "CurrentCombatId"), prefix: new HarmonyMethod(typeof(CardEffectQueueProbe), nameof(Combat)));
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsOverOrEnding"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(ArrivalEquipmentProbe.LiveCombat)));
            h.Patch(AccessTools.Method(typeof(RunState), "IterateHookListeners", [typeof(ICombatState)]), prefix: new HarmonyMethod(typeof(EquipmentHookProbe), nameof(EquipmentHookProbe.Prefix)));
            h.Patch(AccessTools.Method(typeof(CombatState), "IterateHookListeners", Type.EmptyTypes), prefix: new HarmonyMethod(typeof(EquipmentHookProbe), nameof(EquipmentHookProbe.Prefix)));
            EquipmentHookProbe.Models = [];
            h.Patch(AccessTools.Method(typeof(SelectToolCmd), nameof(SelectToolCmd.RetrieverCard)), prefix: new HarmonyMethod(typeof(CardEffectQueueProbe), nameof(Retrieve)));
            h.Patch(AccessTools.Method(typeof(SelectToolCmd), nameof(SelectToolCmd.AskYesNo)), prefix: new HarmonyMethod(typeof(CardEffectQueueProbe), nameof(No)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHandForDiscard)), prefix: new HarmonyMethod(typeof(CardEffectQueueProbe), nameof(ChooseDiscard)));
            h.Patch(AccessTools.Method(typeof(CardCmd), nameof(CardCmd.Discard), [typeof(PlayerChoiceContext), typeof(CardModel)]), prefix: new HarmonyMethod(typeof(CardEffectQueueProbe), nameof(Discard)));
            h.Patch(AccessTools.Method(typeof(TianBeiLongLvFaCard), nameof(TianBeiLongLvFaCard.DengChangSpecial)), prefix: new HarmonyMethod(typeof(CardEffectQueueProbe), nameof(Arrival)));
            h.Patch(AccessTools.Method(typeof(ZhuLeiMeiLuoCard), nameof(ZhuLeiMeiLuoCard.SongMuSpecial)), prefix: new HarmonyMethod(typeof(CardEffectQueueProbe), nameof(Grave)));
            h.Patch(AccessTools.Method(typeof(BeiManDeCanHuanZhuangCard), nameof(BeiManDeCanHuanZhuangCard.AfterCardExhausted)), prefix: new HarmonyMethod(typeof(CardEffectQueueProbe), nameof(Exhausted)));
            var vfx = typeof(BeiManDeCanHuanZhuangCard).Assembly.GetType("SelectSpecialCardMod.Scripts.Tool.SpecialEffectVfx", true)!;
            h.Patch(AccessTools.Method(vfx, "Play"), prefix: new HarmonyMethod(typeof(CardEffectQueueProbe), nameof(Present)));

            var playEntry = AccessTools.Method(typeof(CardOnPlayHook), nameof(CardOnPlayHook.RunCardOnPlayHooks));
            if (Harmony.GetPatchInfo(playEntry)?.Postfixes
                    .Any(p => p.PatchMethod.DeclaringType == typeof(CardPlayEffectQueuePatch)) != true)
                throw new Exception("RitsuLib's actual OnPlay entry is missing the shared effect queue patch.");
            foreach (var scenario in new[] { "normal", "discard-arrival", "failure" })
            {
                Events.Clear();
                _discardArrival = scenario == "discard-arrival"; _throwDiscard = scenario == "failure";
                var player = ArrivalEquipmentProbe.NewPlayer();
                var cup = ArrivalEquipmentProbe.AddCard(typeof(BeiManDeCanHuanZhuangCard), player, PileType.Play);
                _discard = ArrivalEquipmentProbe.AddCard(typeof(ZhuLeiMeiLuoCard), player, PileType.Hand);
                var play = new CardPlay { Card = cup, Player = player, Target = null, ResultPile = PileType.Discard,
                    Resources = default, IsAutoPlay = false, PlayIndex = 0, PlayCount = 1 };
                var task = CardOnPlayHook.RunCardOnPlayHooks(cup, new BlockingPlayerChoiceContext(), play);
                try
                {
                    await task;
                    if (_throwDiscard) throw new Exception("Expected discard failure was swallowed.");
                }
                catch (InvalidOperationException ex) when (_throwDiscard && ex.Message == "probe discard failed") { }
                var expected = scenario switch
                {
                    "normal" => new[] { "retrieve", "retrieved", "choose-discard", "discard", "arrival", "grave" },
                    "discard-arrival" => new[] { "retrieve", "retrieved", "choose-discard", "discard" },
                    _ => new[] { "retrieve", "retrieved", "choose-discard", "discard" },
                };
                if (!Events.SequenceEqual(expected) || CardEffectQueue.IsResolving)
                    throw new Exception("Cup retrieval/discard/arrival order or stale/failed trigger cleanup is incorrect: " + scenario + " / " + string.Join(",", Events));
            }

            // 水仙的“检索后决定丢弃”也必须先完成，再结算梅洛送墓，避免融合插入水仙效果中途。
            Events.Clear(); _throwDiscard = _discardArrival = false;
            var waterOwner = ArrivalEquipmentProbe.NewPlayer();
            var water = (ZhuLeiShuiXianCard)ArrivalEquipmentProbe.AddCard(typeof(ZhuLeiShuiXianCard), waterOwner, PileType.Hand);
            await water.DengChangSpecial(new BlockingPlayerChoiceContext(), waterOwner);
            Expect("retrieve", "retrieved", "choose-destination", "discard", "grave");

            // 真实消耗回调必须等主体后再执行；重放不得再次排队成死循环。
            Events.Clear(); _throwDiscard = _discardArrival = false;
            var owner = ArrivalEquipmentProbe.NewPlayer();
            var exhausted = (BeiManDeCanHuanZhuangCard)ArrivalEquipmentProbe.AddCard(typeof(BeiManDeCanHuanZhuangCard), owner, PileType.Exhaust);
            await CardEffectQueue.Run(async () =>
            {
                Events.Add("main-start");
                await exhausted.AfterCardExhausted(new BlockingPlayerChoiceContext(), exhausted, false);
                Events.Add("main-end");
            });
            Expect("main-start", "main-end", "exhausted");

            // 抽牌标记在事件发生时检查，不能等到排队结算时再误认为因效果入手。
            Events.Clear();
            var drawn = ArrivalEquipmentProbe.AddCard(typeof(TianBeiLongLvFaCard), owner, PileType.Hand);
            var drawing = (HashSet<CardModel>)AccessTools.Field(typeof(DrawWindowPatch), "DrawingCards").GetValue(null)!;
            await CardEffectQueue.Run(async () =>
            {
                drawing.Add(drawn);
                try { await DengChangHook.OnCardAddedToHand(drawn); }
                finally { drawing.Remove(drawn); }
            });
            Expect();

            // 落泪之日：没有可返回目标不询问；拒绝发动不能进入后续选卡或移动目标。
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), nameof(CardSelectCmd.FromCombatPile),
                    [typeof(PlayerChoiceContext), typeof(CardPile), typeof(Player), typeof(MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs), typeof(Func<CardModel, bool>)]),
                prefix: new HarmonyMethod(typeof(CardEffectQueueProbe), nameof(RejectLacrimaSelection)));
            var lacrimaOwner = ArrivalEquipmentProbe.NewPlayer();
            var lacrima = (KeMoLuoLeiRiZiCard)ArrivalEquipmentProbe.AddCard(typeof(KeMoLuoLeiRiZiCard), lacrimaOwner, PileType.Exhaust);
            await lacrima.AfterCardExhausted(new BlockingPlayerChoiceContext(), lacrima, false);
            Expect();
            var fiend = ArrivalEquipmentProbe.AddCard(typeof(KeMoMoJianKeCard), lacrimaOwner, PileType.Exhaust);
            await lacrima.AfterCardExhausted(new BlockingPlayerChoiceContext(), lacrima, false);
            Expect("choose-destination"); // 本探针的AskYesNo边界固定选择不发动。
            if (fiend.Pile?.Type != PileType.Exhaust || lacrima.Pile?.Type != PileType.Exhaust)
                throw new Exception("Declining Lacrima's exhaust effect moved a card.");
            GD.Print("PASS: actual cup resolves retrieval and discard before arrival/grave effects; stale/failed triggers, exhaust replay, draw exclusion, FIFO descendants, async waits, nested batches and independent contexts are covered.");
        }
        finally
        {
            h.UnpatchAll(h.Id); Events.Clear(); _discard = _arrival = null;
            _discardArrival = _throwDiscard = false; EquipmentHookProbe.Models = [];
        }
    }

    private static async Task CheckScheduler()
    {
        Events.Clear();
        var gate = new TaskCompletionSource();
        var running = CardEffectQueue.Run(async () =>
        {
            Events.Add("main-start");
            await CardEffectQueue.Schedule(async () =>
            {
                Events.Add("B-start"); await gate.Task;
                await CardEffectQueue.Schedule(() => { Events.Add("D"); return Task.CompletedTask; });
                Events.Add("B-end");
            });
            await SongMuHook.DeferTriggers(async () =>
            {
                await CardEffectQueue.Schedule(() => { Events.Add("C"); return Task.CompletedTask; });
            });
            Events.Add("main-end");
        });
        Expect("main-start", "main-end", "B-start");
        if (running.IsCompleted || CardEffectQueue.IsResolving) throw new Exception("Async window ended early or leaked into the caller context.");
        gate.SetResult(); await running;
        Expect("main-start", "main-end", "B-start", "B-end", "C", "D");

        Events.Clear();
        gate = new TaskCompletionSource();
        var independent = CardEffectQueue.Run(async () =>
        {
            await CardEffectQueue.Schedule(() => { Events.Add("one"); return Task.CompletedTask; });
            await gate.Task;
        });
        await CardEffectQueue.Run(async () =>
        {
            await CardEffectQueue.Schedule(() => { Events.Add("two"); return Task.CompletedTask; });
        });
        Expect("two");
        gate.SetResult(); await independent; Expect("two", "one");

        Events.Clear();
        bool canceled = false;
        try
        {
            await CardEffectQueue.Run(async () =>
            {
                await CardEffectQueue.Schedule(() => { Events.Add("must-not-run"); return Task.CompletedTask; }, () => canceled = true);
                throw new InvalidOperationException("probe parent failed");
            });
        }
        catch (InvalidOperationException ex) when (ex.Message == "probe parent failed") { }
        await CardEffectQueue.Run(() => Task.CompletedTask);
        Expect();
        if (CardEffectQueue.IsResolving || !canceled) throw new Exception("A failed effect leaked its window or a pending activation flag.");

        Events.Clear(); canceled = false;
        await CardEffectQueue.Run(async () =>
        {
            await CardEffectQueue.Schedule(() => { Events.Add("keep"); return Task.CompletedTask; });
            try
            {
                await CardEffectQueue.Run(async () =>
                {
                    await CardEffectQueue.Schedule(() => { Events.Add("failed-child"); return Task.CompletedTask; }, () => canceled = true);
                    throw new InvalidOperationException("probe child failed");
                });
            }
            catch (InvalidOperationException ex) when (ex.Message == "probe child failed") { }
            await CardEffectQueue.Schedule(() => { Events.Add("after"); return Task.CompletedTask; });
        });
        Expect("keep", "after");
        if (!canceled) throw new Exception("A caught nested failure leaked its child effects or pending flag.");
    }
    private static void Expect(params string[] expected)
    { if (!Events.SequenceEqual(expected)) throw new Exception("Unexpected effect order: " + string.Join(",", Events)); }
    public static bool Combat(ref CombatId? __result) { __result = new CombatId(9281); return false; }
    public static bool Present(ref Task<bool> __result) { __result = Task.FromResult(true); return false; }
    public static bool Arrival(ref Task __result) { Events.Add("arrival"); __result = Task.CompletedTask; return false; }
    public static bool Grave(ref Task __result) { Events.Add("grave"); __result = Task.CompletedTask; return false; }
    public static bool Exhausted(ref Task __result) { Events.Add("exhausted"); __result = Task.CompletedTask; return false; }
    public static bool No(ref Task<bool> __result)
    { Events.Add("choose-destination"); __result = Task.FromResult(false); return false; }
    public static bool RejectLacrimaSelection()
        => throw new Exception("A skipped or unavailable Lacrima exhaust effect opened card selection.");
    public static bool Retrieve(Player player, CardModel? source, ref Task<List<CardModel>> __result)
    { __result = RetrieveCore(player, source); return false; }
    private static async Task<List<CardModel>> RetrieveCore(Player player, CardModel? source)
    {
        Events.Add("retrieve");
        _arrival = ArrivalEquipmentProbe.AddCard(source is ZhuLeiShuiXianCard ? typeof(ZhuLeiMeiLuoCard) : typeof(TianBeiLongLvFaCard), player, PileType.Hand);
        await DengChangHook.OnCardAddedToHand(_arrival);
        Events.Add("retrieved");
        return [_arrival];
    }
    public static bool ChooseDiscard(ref Task<IEnumerable<CardModel>> __result)
    {
        Events.Add("choose-discard");
        if (Events.Contains("arrival")) throw new Exception("Arrival interrupted the cup before its discard.");
        __result = Task.FromResult<IEnumerable<CardModel>>([(_discardArrival ? _arrival : _discard)!]); return false;
    }
    public static bool Discard(PlayerChoiceContext choiceContext, CardModel card, ref Task __result)
    { __result = DiscardCore(choiceContext, card); return false; }
    private static async Task DiscardCore(PlayerChoiceContext context, CardModel card)
    {
        Events.Add("discard");
        if (_throwDiscard) throw new InvalidOperationException("probe discard failed");
        card.RemoveFromCurrentPile(silent: true); PileType.Discard.GetPile(card.Owner).AddInternal(card);
        await SongMuHook.OnCardDiscarded(context, card);
    }
}
