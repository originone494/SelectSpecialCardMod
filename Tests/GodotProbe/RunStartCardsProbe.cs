using System.Runtime.CompilerServices;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Unlocks;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Intent;
using SelectSpecialCardMod.Scripts.Myth;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;
using STS2RitsuLib.RunData;

// 保留实际开局发放流程、候选、RNG及逐玩家保存；替换设置同步、选牌/奖励界面和入组命令边界。
public static class RunStartCardsProbe
{
    private static int _flags, _syncs, _choices, _rewards, _mythRewards;
    private static bool _reversePool, _skip, _failBeforeReward, _failAfterAdd, _emptyChoice, _single, _emptyPool, _failBeforeAdd, _blockMyth;
    private static string _midMythAddState = "";
    private static string _lastReward = "";
    private static readonly List<CardModel> ChoiceCopies = [];
    private static PlayerRunSavedData<RunStartCardData> Saved
        => (PlayerRunSavedData<RunStartCardData>)AccessTools.Field(typeof(RunStartCards), "_saved").GetValue(null)!;
    private static RunSavedData<CardContentRunPolicy> Policy
        => (RunSavedData<CardContentRunPolicy>)AccessTools.Field(typeof(CardAvailability), "_runPolicy").GetValue(null)!;
    private static Task Grant(Player player) => (Task)AccessTools.Method(typeof(RunStartCards), "GrantForPlayer").Invoke(null, [player])!;

    public static async Task Check()
    {
        CardAvailability.Register(); RunStartCards.Register(); RunStartCards.Register();
        var defaults = JsonSerializer.Deserialize<CardContentPreferences>("{}")!;
        if (defaults.ChooseSpecialCardAtStart || defaults.SpecialCardRewardAtStart || defaults.MythCardRewardAtStart || defaults.GrantMythCardAtStart || defaults.RandomizeAllCardRewards || defaults.AllCardRewardsSpecial || !defaults.GrantDuelDiskAtStart)
            throw new Exception("Start-card settings changed old defaults.");
        foreach (var method in new[] { nameof(NTransition.FadeIn), nameof(NTransition.RoomFadeIn) })
            if (Harmony.GetPatchInfo(AccessTools.Method(typeof(NTransition), method))?.Postfixes
                .Any(p => p.PatchMethod.DeclaringType == typeof(RunStartCardsReadyPatch)) != true)
                throw new Exception("The post-fade startup patch is missing: " + method);
        var h = new Harmony("scm.start-special-cards.godot-framework");
        try
        {
            h.Patch(AccessTools.Method(typeof(RunStartCards), "SynchronizePreferences"),
                prefix: new HarmonyMethod(typeof(RunStartCardsProbe), nameof(Settings)));
            h.Patch(AccessTools.Method(typeof(CardPoolModel), "GetUnlockedCards"),
                prefix: new HarmonyMethod(typeof(RunStartCardsProbe), nameof(Pool)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromSimpleGrid"),
                prefix: new HarmonyMethod(typeof(RunStartCardsProbe), nameof(Select)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromChooseACardScreen"),
                prefix: new HarmonyMethod(typeof(RunStartCardsProbe), nameof(ChooseReward)));
            h.Patch(AccessTools.Method(typeof(RewardsCmd), "OfferCustom"),
                prefix: new HarmonyMethod(typeof(RunStartCardsProbe), nameof(Offer)));
            h.Patch(AccessTools.Method(typeof(CardPileCmd), "Add", [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(RunStartCardsProbe), nameof(Add)));

            // 三个开关的八种组合；指定神话卡不受非游戏王过滤影响，其他奖励继续遵循过滤。
            foreach (int flags in Enumerable.Range(0, 8))
            {
                Reset(); _flags = flags;
                var player = NewPlayer();
                await Grant(player);
                _flags = 7 - flags; // 模拟之后改变本机配置，不能改变本局快照。
                await Grant(player);
                int expected = ((flags & 1) != 0 ? 1 : 0) + ((flags & 2) != 0 ? 1 : 0) + ((flags & 4) != 0 ? 1 : 0);
                var deck = PileType.Deck.GetPile(player).Cards;
                if (deck.Count != expected || _syncs != 1 || _choices != ((flags & 1) != 0 ? 1 : 0)
                    || _rewards != ((flags & 2) != 0 ? 1 : 0) || deck.OfType<SuiYuanShenHuaHeTiCard>().Count() != ((flags & 4) != 0 ? 1 : 0)
                    || deck.OfType<SuiYuanShenHuaHeTiCard>().Any(c => c.EnergyCost.CostsX || c.EnergyCost.Canonical != 0)
                    || deck.Any(c => !c.IsUpgraded || CardClassification.IsExtraMonster(c)
                        || c is YiCunJiaCard || c.Owner != player)
                    || ChoiceCopies.Any(c => c.Pile == null && !c.HasBeenRemovedFromState))
                    throw new Exception("Start-card stacking, filtering, upgrade, ownership, cleanup or once-per-run rules failed.");
                var restored = JsonSerializer.Deserialize<RunStartCardData>(JsonSerializer.Serialize(Saved.Get(player)))!;
                Saved.Set((RunState)player.RunState, player.NetId, restored);
                await Grant(player);
                if (deck.Count != expected || _syncs != 1) throw new Exception("Restoring completed state duplicated startup cards.");
            }

            // v8四位个人协议：新神话奖励与其他三个开局奖励独立叠加，读档及改设置不重复发放。
            foreach (int flags in Enumerable.Range(0, 16))
            {
                Reset(); _flags = flags;
                var p = NewPlayer(version: 8);
                await Grant(p);
                _flags = 15 - flags;
                Saved.Set((RunState)p.RunState, p.NetId,
                    JsonSerializer.Deserialize<RunStartCardData>(JsonSerializer.Serialize(Saved.Get(p)))!);
                await Grant(p);
                int expected = Enumerable.Range(0, 4).Count(bit => (flags & (1 << bit)) != 0);
                var deck = PileType.Deck.GetPile(p).Cards;
                if (deck.Count != expected || _syncs != 1 || _mythRewards != ((flags & 8) != 0 ? 1 : 0)
                    || _rewards != ((flags & 2) != 0 ? 1 : 0) + ((flags & 8) != 0 ? 1 : 0)
                    || deck.OfType<MythMutationCard>().Count() != ((flags & 8) != 0 ? 1 : 0)
                    || deck.Any(c => !c.IsUpgraded || c.Owner != p || c.CardScope != p.RunState)
                    || ChoiceCopies.Any(c => c.Pile == null && !c.HasBeenRemovedFromState))
                    throw new Exception("Opening myth reward failed stacking, ownership, permanent scope, cleanup or once-only recovery.");
            }

            Reset(); _flags = 8;
            var oldRewardSnapshot = NewPlayer(version: 8);
            Saved.Set((RunState)oldRewardSnapshot.RunState, oldRewardSnapshot.NetId,
                new RunStartCardData { Version = 2, ChoiceCompleted = true, RewardCompleted = true, MythCardCompleted = true });
            await Grant(oldRewardSnapshot);
            if (_syncs != 0 || _rewards != 0 || !Saved.Get(oldRewardSnapshot).MythRewardCompleted)
                throw new Exception("An initialized old snapshot retroactively received the new myth reward.");
            Reset(); _flags = 0;
            var oldProtocol = NewPlayer(version: 7);
            await Grant(oldProtocol);
            if (Saved.Get(oldProtocol).Version != 2 || CardAvailability.UsesMythStartRewardSetting(oldProtocol.RunState))
                throw new Exception("A v7 run gained the fourth preference bit.");

            Reset(); _flags = 10; _emptyPool = true;
            var emptySpecial = NewPlayer(version: 8);
            await Grant(emptySpecial);
            if (_rewards != 1 || _mythRewards != 1 || !Saved.Get(emptySpecial).RewardCompleted
                || PileType.Deck.GetPile(emptySpecial).Cards.Single() is not MythMutationCard)
                throw new Exception("An empty normal special pool suppressed the independent myth reward.");

            Reset(); _flags = 8; _skip = true;
            var skipMythReward = NewPlayer(version: 8);
            await Grant(skipMythReward); await Grant(skipMythReward);
            if (_mythRewards != 1 || PileType.Deck.GetPile(skipMythReward).Cards.Count != 0 || !Saved.Get(skipMythReward).MythRewardCompleted)
                throw new Exception("Skipping the myth reward granted a card or reopened the reward.");

            Reset(); _flags = 8; _failBeforeReward = true;
            var pendingMythReward = NewPlayer(version: 8);
            await ExpectFailure(Grant(pendingMythReward));
            string pendingMythOffer = _lastReward;
            string pendingMythRng = JsonSerializer.Serialize(pendingMythReward.PlayerRng.ToSerializable());
            var mythSave = JsonSerializer.Deserialize<RunStartCardData>(JsonSerializer.Serialize(Saved.Get(pendingMythReward)))!;
            if (mythSave.FixedMythRewardCards.Count != 3 || mythSave.MythRewardCompleted
                || ChoiceCopies.Any(c => !c.HasBeenRemovedFromState))
                throw new Exception("Interrupted myth reward did not persist complete snapshots or clean up previews.");
            Saved.Set((RunState)pendingMythReward.RunState, pendingMythReward.NetId, mythSave);
            var otherRecipe = MythCardRewards.Create(pendingMythReward, new MegaCrit.Sts2.Core.Random.Rng(98765));
            PileType.Deck.GetPile(pendingMythReward).AddInternal(otherRecipe);
            _failBeforeReward = false; await Grant(pendingMythReward);
            if (_lastReward != pendingMythOffer || pendingMythRng != JsonSerializer.Serialize(pendingMythReward.PlayerRng.ToSerializable())
                || _mythRewards != 2 || PileType.Deck.GetPile(pendingMythReward).Cards.Count != 2 || !Saved.Get(pendingMythReward).MythRewardCompleted)
                throw new Exception("Pending myth reward rerolled or matched a different recipe sharing its template ID.");

            Reset(); _flags = 8; _failAfterAdd = true;
            var interruptedMythReward = NewPlayer(version: 8);
            await ExpectFailure(Grant(interruptedMythReward));
            Saved.Set((RunState)interruptedMythReward.RunState, interruptedMythReward.NetId,
                JsonSerializer.Deserialize<RunStartCardData>(_midMythAddState)!);
            _failAfterAdd = false; await Grant(interruptedMythReward);
            if (_mythRewards != 1 || PileType.Deck.GetPile(interruptedMythReward).Cards.Count != 1 || !Saved.Get(interruptedMythReward).MythRewardCompleted)
                throw new Exception("A snapshot taken during myth reward acquisition duplicated the obtained card.");

            Reset(); _flags = 8;
            var mythRewardOwner = NewPlayer(version: 8, id: 101);
            var mythRewardTeammate = NewPlayer(version: 8, id: 202);
            AccessTools.Field(typeof(Player), "_runState").SetValue(mythRewardTeammate, mythRewardOwner.RunState);
            await Grant(mythRewardOwner); _flags = 0; await Grant(mythRewardTeammate);
            if (PileType.Deck.GetPile(mythRewardOwner).Cards.Single() is not MythMutationCard
                || PileType.Deck.GetPile(mythRewardTeammate).Cards.Count != 0 || Saved.Get(mythRewardTeammate).GiveMythReward)
                throw new Exception("The opening myth reward crossed personal preferences or player ownership.");

            Reset(); _flags = 8;
            var mythPeer1 = NewPlayer(version: 8); await Grant(mythPeer1);
            string mythSignature = _lastReward + JsonSerializer.Serialize(mythPeer1.PlayerRng.ToSerializable());
            Reset(); _flags = 8;
            var mythPeer2 = NewPlayer(version: 8); await Grant(mythPeer2);
            if (mythSignature != _lastReward + JsonSerializer.Serialize(mythPeer2.PlayerRng.ToSerializable()))
                throw new Exception("Opening myth rewards or reward RNG diverged between independent peers.");

            Reset(); _flags = 3;
            var legacy = NewPlayer(version: 3);
            await Grant(legacy);
            if (_syncs != 0 || _choices != 0 || _rewards != 0) throw new Exception("Old saves gained new choices/rewards.");

            Reset(); _flags = 3;
            var previous = NewPlayer(version: 4);
            await Grant(previous);
            _flags = 7; await Grant(previous);
            if (_syncs != 1 || Saved.Get(previous).GiveMythCard || PileType.Deck.GetPile(previous).Cards.Count != 2)
                throw new Exception("The v4 setting protocol gained a new startup card.");
            Reset(); _flags = 4;
            var oldSnapshot = NewPlayer();
            Saved.Set((RunState)oldSnapshot.RunState, oldSnapshot.NetId,
                new RunStartCardData { Version = 1, ChoiceCompleted = true, RewardCompleted = true });
            await Grant(oldSnapshot);
            if (_syncs != 0 || PileType.Deck.GetPile(oldSnapshot).Cards.Count != 0)
                throw new Exception("A previously initialized start snapshot received a retroactive myth card.");

            Reset(); _flags = 4;
            var existing = NewPlayer();
            var originalMyth = existing.RunState.CreateCard(ModelDb.Card<SuiYuanShenHuaHeTiCard>(), existing);
            PileType.Deck.GetPile(existing).AddInternal(originalMyth);
            string originalRng = JsonSerializer.Serialize(existing.PlayerRng.ToSerializable());
            await Grant(existing); await Grant(existing);
            if (PileType.Deck.GetPile(existing).Cards.OfType<SuiYuanShenHuaHeTiCard>().Count() != 2
                || Saved.Get(existing).MythCardCountBefore != 1 || originalRng != JsonSerializer.Serialize(existing.PlayerRng.ToSerializable()))
                throw new Exception("Fixed myth grant skipped a pre-existing copy or consumed reward RNG.");

            Reset(); _flags = 4; _failBeforeAdd = true;
            var beforeAdd = NewPlayer();
            await ExpectFailure(Grant(beforeAdd));
            if (Saved.Get(beforeAdd).MythCardCompleted || PileType.Deck.GetPile(beforeAdd).Cards.Count != 0)
                throw new Exception("A failed pre-add myth grant was marked completed.");
            _failBeforeAdd = false; await Grant(beforeAdd);
            if (PileType.Deck.GetPile(beforeAdd).Cards.Count != 1 || !Saved.Get(beforeAdd).MythCardCompleted)
                throw new Exception("Failed myth grant could not resume once.");

            Reset(); _flags = 4; _failAfterAdd = true;
            var duringAdd = NewPlayer();
            await ExpectFailure(Grant(duringAdd));
            Saved.Set((RunState)duringAdd.RunState, duringAdd.NetId, JsonSerializer.Deserialize<RunStartCardData>(_midMythAddState)!);
            _failAfterAdd = false; await Grant(duringAdd);
            if (PileType.Deck.GetPile(duringAdd).Cards.Count != 1 || !Saved.Get(duringAdd).MythCardCompleted || _syncs != 1)
                throw new Exception("Restoring a snapshot taken during myth addition duplicated the card.");

            Reset(); _flags = 4; _blockMyth = true;
            var blockedMyth = NewPlayer();
            await Grant(blockedMyth); await Grant(blockedMyth);
            if (PileType.Deck.GetPile(blockedMyth).Cards.Count != 0 || !Saved.Get(blockedMyth).MythCardCompleted || _syncs != 1)
                throw new Exception("A native blocked card addition was retried indefinitely.");

            Reset(); _flags = 4;
            var mythOwner = NewPlayer(id: 101); var teammate = NewPlayer(id: 202);
            AccessTools.Field(typeof(Player), "_runState").SetValue(teammate, mythOwner.RunState);
            await Grant(mythOwner); _flags = 0; await Grant(teammate);
            if (PileType.Deck.GetPile(mythOwner).Cards.Single() is not SuiYuanShenHuaHeTiCard
                || PileType.Deck.GetPile(teammate).Cards.Count != 0 || !Saved.Get(mythOwner).GiveMythCard || Saved.Get(teammate).GiveMythCard)
                throw new Exception("The fixed myth grant crossed personal settings or player ownership.");

            Reset(); _flags = 2; _skip = true;
            var skipped = NewPlayer();
            await Grant(skipped); await Grant(skipped);
            if (_rewards != 1 || PileType.Deck.GetPile(skipped).Cards.Count != 0 || !Saved.Get(skipped).RewardCompleted)
                throw new Exception("Skipping a start reward caused automatic acquisition or a repeat.");

            Reset(); _flags = 3; _emptyPool = true;
            var empty = NewPlayer();
            string emptyRng = JsonSerializer.Serialize(empty.PlayerRng.ToSerializable());
            await Grant(empty); await Grant(empty);
            if (_choices != 0 || _rewards != 0 || !Saved.Get(empty).ChoiceCompleted || !Saved.Get(empty).RewardCompleted
                || emptyRng != JsonSerializer.Serialize(empty.PlayerRng.ToSerializable()))
                throw new Exception("An empty pool opened a screen or advanced reward RNG.");

            Reset(); _flags = 3; _single = true;
            await Grant(NewPlayer());
            if (_choices != 1 || _rewards != 1) throw new Exception("A one-card pool bypassed the manual selection or reward.");

            Reset(); _flags = 1; _emptyChoice = true;
            var cancelled = NewPlayer();
            await ExpectFailure(Grant(cancelled));
            if (PileType.Deck.GetPile(cancelled).Cards.Count != 0 || Saved.Get(cancelled).ChoiceCompleted
                || ChoiceCopies.Any(c => !c.HasBeenRemovedFromState))
                throw new Exception("An empty choice granted a card or leaked temporary options.");

            // 失败前已经随机的奖励选项必须保存，恢复状态后不能重新抽取。
            Reset(); _flags = 3; _failBeforeReward = true;
            var pending = NewPlayer();
            await ExpectFailure(Grant(pending));
            string offers = _lastReward, rng = JsonSerializer.Serialize(pending.PlayerRng.ToSerializable());
            Saved.Set((RunState)pending.RunState, pending.NetId,
                JsonSerializer.Deserialize<RunStartCardData>(JsonSerializer.Serialize(Saved.Get(pending)))!);
            _failBeforeReward = false;
            await Grant(pending);
            if (_lastReward != offers || rng != JsonSerializer.Serialize(pending.PlayerRng.ToSerializable())
                || _choices != 1 || PileType.Deck.GetPile(pending).Cards.Count != 2)
                throw new Exception("Pending reward recovery rerolled or repeated the full-pool choice.");

            Reset(); _flags = 1; _failAfterAdd = true;
            var interrupted = NewPlayer();
            await ExpectFailure(Grant(interrupted));
            _failAfterAdd = false;
            await Grant(interrupted);
            if (_choices != 1 || PileType.Deck.GetPile(interrupted).Cards.Count != 1 || !Saved.Get(interrupted).ChoiceCompleted)
                throw new Exception("An interrupted post-add hook duplicated the start card.");

            // 同局两名玩家的快照相反；设置/获得不串到队友。
            Reset(); _flags = 1;
            var first = NewPlayer(id: 101);
            var second = NewPlayer(id: 202);
            AccessTools.Field(typeof(Player), "_runState").SetValue(second, first.RunState);
            await Grant(first);
            _flags = 2;
            await Grant(second);
            if (!Saved.Get(first).ChooseCard || Saved.Get(first).GiveReward || Saved.Get(second).ChooseCard || !Saved.Get(second).GiveReward
                || _syncs != 2 || PileType.Deck.GetPile(first).Cards.Count != 1 || PileType.Deck.GetPile(second).Cards.Count != 1)
                throw new Exception("Start-card preferences or cards crossed player ownership.");

            // 两份独立状态、不同候选枚举顺序，奖励与 RNG 必须一致。
            Reset(); _flags = 2;
            var peer1 = NewPlayer();
            await Grant(peer1);
            string signature = _lastReward + JsonSerializer.Serialize(peer1.PlayerRng.ToSerializable());
            Reset(); _flags = 2; _reversePool = true;
            var peer2 = NewPlayer();
            await Grant(peer2);
            if (_lastReward + JsonSerializer.Serialize(peer2.PlayerRng.ToSerializable()) != signature)
                throw new Exception("Start rewards diverged between peers.");

            Reset(); _flags = 2; _failBeforeReward = true;
            var randomPending = NewPlayer(version: 6);
            Policy.Set((RunState)randomPending.RunState, new() { Version = 6, OnlyYuGiOh = true, RandomizeAllCardRewards = true });
            await ExpectFailure(Grant(randomPending));
            string fixedOffer = _lastReward, fixedRng = randomPending.PlayerRng.Rewards.ToSerializable().ToString();
            if (Saved.Get(randomPending).FixedRewardCards.Count != 3)
                throw new Exception("Pending special start reward did not save complete options.");
            // 模拟旧版本尚未领取、但已经错误保存为神话卡的选项。
            var oldOptions = Enumerable.Range(0, 3).Select(i => MythCardRewards.Create(randomPending, new MegaCrit.Sts2.Core.Random.Rng((ulong)(3456 + i)))).ToList();
            var pendingData = Saved.Get(randomPending);
            pendingData.FixedRewardCards = MythCardRewards.CaptureCards(oldOptions);
            pendingData.RewardCountsBefore = oldOptions.Select(c => (string)AccessTools.Method(typeof(RunStartCards), "RewardKey").Invoke(null, [c])!)
                .Distinct().ToDictionary(key => key, _ => 0);
            Saved.Set((RunState)randomPending.RunState, randomPending.NetId, pendingData);
            foreach (var card in oldOptions) card.RemoveFromState();
            Saved.Set((RunState)randomPending.RunState, randomPending.NetId,
                JsonSerializer.Deserialize<RunStartCardData>(JsonSerializer.Serialize(Saved.Get(randomPending)))!);
            // 同模板的其他配方已经在卡组，不能冒充本次奖励的领取记录。
            var unrelated = MythCardRewards.Create(randomPending, new MegaCrit.Sts2.Core.Random.Rng(98765));
            PileType.Deck.GetPile(randomPending).AddInternal(unrelated);
            _failBeforeReward = false;
            await Grant(randomPending);
            if (_lastReward != fixedOffer || fixedRng != randomPending.PlayerRng.Rewards.ToSerializable().ToString()
                || _rewards != 2 || PileType.Deck.GetPile(randomPending).Cards.Count != 2 || !Saved.Get(randomPending).RewardCompleted)
                throw new Exception("Opening special reward recovery rerolled options or matched an unrelated myth card.");
        }
        finally { h.UnpatchAll(h.Id); Reset(); }
        GD.Print("PASS: start-card defaults, all toggle combinations, player isolation, main-deck filtering, cosmetic upgrades, cleanup, skipping, legacy saves, once-only recovery and deterministic reward options.");
    }

    private static Player NewPlayer(int version = 5, ulong id = 101)
    {
        var player = ArrivalEquipmentProbe.NewPlayer();
        var run = (RunState)player.RunState;
        AccessTools.Field(typeof(RunState), "_allCards").SetValue(run, new List<CardModel>());
        AccessTools.Field(typeof(RunState), "_players").SetValue(run, new List<Player> { player });
        AccessTools.Field(typeof(RunState), "_mapPointHistory").SetValue(run, new List<List<MapPointHistoryEntry>>());
        AccessTools.Field(typeof(Player), "<NetId>k__BackingField").SetValue(player, id);
        AccessTools.Field(typeof(Player), "<Character>k__BackingField").SetValue(player, ModelDb.Character<Ironclad>());
        AccessTools.Field(typeof(Player), "<UnlockState>k__BackingField").SetValue(player, UnlockState.all);
        AccessTools.Field(typeof(Player), "_relics").SetValue(player, new List<RelicModel>());
        AccessTools.Field(typeof(Player), "_runPiles").SetValue(player, new CardPile[] { new(PileType.Deck) });
        var rng = AccessTools.Property(typeof(Player), "PlayerRng");
        rng.SetValue(player, Activator.CreateInstance(rng.PropertyType, [12345UL + id]));
        Policy.Set(run, new() { Version = version, OnlyYuGiOh = true });
        return player;
    }

    private static async Task ExpectFailure(Task task)
    {
        try { await task; }
        catch (InvalidOperationException) { return; }
        throw new Exception("The expected selection/add/reward interruption did not occur.");
    }

    private static void Reset()
    {
        _flags = _syncs = _choices = _rewards = _mythRewards = 0;
        _reversePool = _skip = _failBeforeReward = _failAfterAdd = _emptyChoice = _single = _emptyPool = _failBeforeAdd = _blockMyth = false;
        _lastReward = _midMythAddState = ""; ChoiceCopies.Clear();
    }

    public static bool Settings(ref Task<int> __result) { _syncs++; __result = Task.FromResult(_flags); return false; }
    public static bool Pool(CardPoolModel __instance, ref IEnumerable<CardModel> __result)
    {
        if (__instance is not SelectSpecialCardPool) return true;
        CardModel[] cards = [ModelDb.Card<YiXiangMingJueZeCard>(), ModelDb.Card<YiXiangMingQingZhuCard>(),
            ModelDb.Card<YiXiangMingDiaoYinCard>(), ModelDb.Card<YiCunJiaCard>(), ModelDb.Card<No41NiShuiMoShouCard>(),
            ModelDb.Card<EffectOneCard>(), ModelDb.Card<MonsterMoveCard>(), ModelDb.Card<YiXiangMingJueZeCard>()];
        __result = _emptyPool ? [] : _single ? cards.Take(1) : _reversePool ? cards.Reverse() : cards;
        return false;
    }
    public static bool Select(IReadOnlyList<CardModel> cardsIn, Player player, CardSelectorPrefs prefs, ref Task<IEnumerable<CardModel>> __result)
    {
        _choices++;
        ChoiceCopies.AddRange(cardsIn);
        if (!prefs.RequireManualConfirmation || prefs.MinSelect != 1 || prefs.MaxSelect != 1
            || cardsIn.Count != (_single ? 1 : 3) || cardsIn.Any(c => c.Owner != player || !c.IsUpgraded))
            throw new Exception("Full-pool start choice used invalid candidates or skipped manual confirmation.");
        __result = Task.FromResult<IEnumerable<CardModel>>(_emptyChoice ? [] : cardsIn.Take(1));
        return false;
    }
    public static bool Offer(Player player, List<Reward> rewards, ref Task __result)
    {
        _rewards++;
        var cards = ((CardReward)rewards.Single()).Cards.ToList();
        ChoiceCopies.AddRange(cards);
        _lastReward = string.Join(",", cards.Select(c => c is MythMutationCard m ? m.SnapshotJson : c.Id.ToString()));
        bool myth = cards.Count > 0 && cards.All(c => c is MythMutationCard m && m.SnapshotJson.Length > 0);
        if (myth) _mythRewards++;
        if (cards.Count != (myth ? 3 : _single ? 1 : 3) || (!myth && cards.DistinctBy(c => c.Id).Count() != cards.Count)
            || (!myth && cards.Any(c => c is MythMutationCard))
            || cards.Any(c => !c.IsUpgraded || c.Owner != player || c is YiCunJiaCard || CardClassification.IsExtraMonster(c)))
            throw new Exception("Start rewards differ from special elite reward rules.");
        __result = _failBeforeReward ? Task.FromException(new InvalidOperationException("fixture reward interruption"))
            : _skip ? SkipReward() : AddReward();
        return false;
        async Task AddReward()
        {
            // 无历史的空地图也应真实进入兼容奖励的选择/入组逻辑，而非直接塞卡。
            if (!(await (Task<bool>)AccessTools.Method(rewards[0].GetType(), "OnSelect").Invoke(rewards[0], null)!))
                throw new Exception("The initial map reward could not be selected without room history.");
        }
        Task SkipReward() { rewards[0].OnSkipped(); return Task.CompletedTask; }
    }
    public static bool ChooseReward(IReadOnlyList<CardModel> cards, ref Task<CardModel?> __result)
    { __result = Task.FromResult<CardModel?>(cards.First()); return false; }
    public static bool Add(CardModel card, PileType newPileType, ref Task<CardPileAddResult> __result)
    {
        if (_failBeforeAdd) { __result = Task.FromException<CardPileAddResult>(new InvalidOperationException("fixture pre-add hook")); return false; }
        if (_blockMyth && card is SuiYuanShenHuaHeTiCard)
        { __result = Task.FromResult(new CardPileAddResult { cardAdded = card, success = false, targetPile = newPileType }); return false; }
        card.RemoveFromCurrentPile();
        newPileType.GetPile(card.Owner).AddInternal(card);
        if (card is SuiYuanShenHuaHeTiCard or MythMutationCard) _midMythAddState = JsonSerializer.Serialize(Saved.Get(card.Owner));
        __result = _failAfterAdd ? Task.FromException<CardPileAddResult>(new InvalidOperationException("fixture post-add hook"))
            : Task.FromResult(new CardPileAddResult { cardAdded = card, success = true, targetPile = newPileType });
        return false;
    }
}
