using System.Runtime.CompilerServices;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Myth;
using STS2RitsuLib.RunData;
using SelectSpecialCardMod.Scripts.Cards.Special;

namespace SelectSpecialCardMod.Scripts.Tool;

public sealed class RunStartCardData
{
    public int Version { get; set; }
    public bool ChooseCard { get; set; }
    public bool GiveReward { get; set; }
    public bool ChoiceCompleted { get; set; }
    public bool RewardCompleted { get; set; }
    public bool GiveMythCard { get; set; }
    public bool MythCardCompleted { get; set; }
    public bool MythCardGrantStarted { get; set; }
    public int MythCardCountBefore { get; set; }
    public string SelectedCardId { get; set; } = "";
    public int SelectedCountBefore { get; set; }
    public List<string> RewardCardIds { get; set; } = [];
    public Dictionary<string, int> RewardCountsBefore { get; set; } = [];
    public List<string> FixedRewardCards { get; set; } = [];
    public bool GiveMythReward { get; set; }
    public bool MythRewardCompleted { get; set; }
    public bool MythRewardRerolled { get; set; }
    public List<string> FixedMythRewardCards { get; set; } = [];
    public Dictionary<string, int> MythRewardCountsBefore { get; set; } = [];
}

public static class RunStartCards
{
    private static PlayerRunSavedData<RunStartCardData>? _saved;
    private static readonly ConditionalWeakTable<RunState, SemaphoreSlim> Gates = new();

    public static void Register()
        => _saved ??= RunSavedDataStore.For(Entry.ModId).RegisterPerPlayer<RunStartCardData>("start_special_cards");

    public static async Task Prepare(RunState run)
    {
        if (!CardAvailability.UsesStartCardSettings(run) || run.CurrentRoom == null
            || run.CurrentRoom is CombatRoom) return;
        var token = RunLifetime.Token(run);
        var gate = Gates.GetValue(run, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            if (!ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), run)) return;
            await RunSetup.Prepare(run);
            foreach (var player in run.Players.OrderBy(p => p.NetId))
            {
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), run)) return;
                await GrantForPlayer(player);
                await ExtraDeckSeries.EnsureFull(player);
            }
        }
        finally { gate.Release(); }
    }

    internal static async Task GrantForPlayer(Player player)
    {
        Register();
        var run = (RunState)player.RunState;
        if (!CardAvailability.UsesStartCardSettings(run)) return;
        var data = _saved!.Get(player);
        bool supportsMythCard = CardAvailability.UsesMythStartCardSetting(run);
        bool supportsMythReward = CardAvailability.UsesMythStartRewardSetting(run);
        if (data.Version == 0)
        {
            // 正常在开局事件房间执行；已有进度且没有开局快照时不迟发奖励。
            if (run.CurrentActIndex > 0 || run.TotalFloor > 1)
            {
                data.Version = supportsMythReward ? 3 : supportsMythCard ? 2 : 1;
                data.ChoiceCompleted = data.RewardCompleted = data.MythCardCompleted = data.MythRewardCompleted = true;
                Save();
                return;
            }
            int flags = await SynchronizePreferences(player);
            if (flags < 0 || flags > (supportsMythReward ? 15 : supportsMythCard ? 7 : 3)) throw new InvalidOperationException("Invalid personal start card settings.");
            data.ChooseCard = (flags & 1) != 0;
            data.GiveReward = (flags & 2) != 0;
            data.ChoiceCompleted = !data.ChooseCard;
            data.RewardCompleted = !data.GiveReward;
            data.GiveMythCard = supportsMythCard && (flags & 4) != 0;
            data.MythCardCompleted = !data.GiveMythCard;
            data.GiveMythReward = supportsMythReward && (flags & 8) != 0;
            data.MythRewardCompleted = !data.GiveMythReward;
            data.Version = supportsMythReward ? 3 : supportsMythCard ? 2 : 1;
            Save();
            Entry.Logger.Info($"[StartCards] Player {player.NetId} settings: choose={data.ChooseCard}; reward={data.GiveReward}; myth={data.GiveMythCard}; mythReward={data.GiveMythReward}.");
        }

        RunLifetime.Token(run).ThrowIfCancellationRequested();
        // 保存发生在入组钩子/奖励演出等待期间时，已进入卡组的卡不能再次发放。
        if (!data.ChoiceCompleted && data.SelectedCardId.Length > 0
            && Count(data.SelectedCardId) > data.SelectedCountBefore) data.ChoiceCompleted = true;
        if (!data.RewardCompleted && RewardWasAdded()) data.RewardCompleted = true;
        if (!supportsMythReward || data.Version < 3 || !data.GiveMythReward || MythRewardWasAdded())
            data.MythRewardCompleted = true;
        // 已有开局快照的旧局不补发；保存发生在入组钩子期间时，用既有卡组确认已发放。
        if (!supportsMythCard || data.Version < 2 || !data.GiveMythCard) data.MythCardCompleted = true;
        string mythId = ModelDb.Card<SuiYuanShenHuaHeTiCard>().Id.ToString();
        if (!data.MythCardCompleted && data.MythCardGrantStarted && Count(mythId) > data.MythCardCountBefore)
            data.MythCardCompleted = true;
        Save();

        if (!data.MythCardCompleted)
        {
            if (!data.MythCardGrantStarted)
            {
                data.MythCardCountBefore = Count(mythId);
                data.MythCardGrantStarted = true;
                Save();
            }
            RunLifetime.Token(run).ThrowIfCancellationRequested();
            // 用户明确指定此开关优先于非游戏王卡过滤；不依赖随机奖励候选或决斗盘。
            var card = run.CreateCard(ModelDb.Card<SuiYuanShenHuaHeTiCard>(), player);
            try
            {
                var result = await CardPileCmd.Add(card, PileType.Deck);
                data.MythCardCompleted = true;
                Save();
                Entry.Logger.Info($"[StartCards] Player {player.NetId} received 随缘神话合体, added={result.success}.");
            }
            finally
            {
                if (Count(mythId) > data.MythCardCountBefore) { data.MythCardCompleted = true; Save(); }
                CleanUp([card]);
            }
        }

        if (!data.ChoiceCompleted)
        {
            var candidates = SpecialCardRewards.Candidates(player);
            if (candidates.Count == 0)
            {
                data.ChoiceCompleted = true;
                Save();
                Entry.Logger.Info($"[StartCards] No selectable special cards for player {player.NetId}.");
            }
            else
            {
                var copies = new List<CardModel>();
                try
                {
                    CardModel? picked;
                    if (data.SelectedCardId.Length > 0)
                    {
                        var canonical = candidates.FirstOrDefault(c => c.Id.ToString() == data.SelectedCardId)
                            ?? throw new InvalidOperationException("The pending start card is no longer available.");
                        picked = run.CreateCard(canonical, player);
                        copies.Add(picked);
                    }
                    else
                    {
                        foreach (var canonical in candidates) copies.Add(run.CreateCard(canonical, player));
                        var prefs = new CardSelectorPrefs(new LocString("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_START_CARD"), 1, 1)
                        { RequireManualConfirmation = true };
                        var selected = (await CardSelectCmd.FromSimpleGrid(new BlockingPlayerChoiceContext(), copies, player, prefs)
                            .WaitAsync(RunLifetime.Token(run))).ToList();
                        if (selected.Count != 1 || !copies.Contains(selected[0]))
                            throw new InvalidOperationException("Start card selection must contain exactly one offered card.");
                        picked = selected[0];
                        data.SelectedCardId = picked.Id.ToString();
                        data.SelectedCountBefore = Count(data.SelectedCardId);
                        Save();
                    }
                    RunLifetime.Token(run).ThrowIfCancellationRequested();
                    var result = await CardPileCmd.Add(picked, PileType.Deck);
                    // 原版遗物可能阻止或替换入组；本次领取已结算，不在后续房间反复补发。
                    data.ChoiceCompleted = true;
                    Save();
                    Entry.Logger.Info($"[StartCards] Player {player.NetId} selected {picked.Id}, added={result.success}.");
                }
                finally
                {
                    if (data.SelectedCardId.Length > 0 && Count(data.SelectedCardId) > data.SelectedCountBefore)
                    { data.ChoiceCompleted = true; Save(); }
                    CleanUp(copies);
                }
            }
        }

        if (!data.RewardCompleted) await GrantSpecialReward();

        if (!data.MythRewardCompleted)
        {
            var copies = new List<CardModel>();
            try
            {
                if (data.FixedMythRewardCards.Count == 0)
                {
                    for (int i = 0; i < 3; i++) copies.Add(MythCardRewards.Create(player, player.PlayerRng.Rewards));
                    data.FixedMythRewardCards = MythCardRewards.CaptureCards(copies);
                    data.MythRewardCountsBefore = copies.Select(RewardKey).Distinct().ToDictionary(key => key, RewardCount);
                    Save(); // 展示前固定完整配方，读档不重抽，也不将同模板的其他神话卡误判为已领取。
                }
                else copies.AddRange(MythCardRewards.RestoreCards(player, data.FixedMythRewardCards));
                var reward = SpecialCardRewards.CreateOpeningMythReward(copies, player,
                    CardCreationOptions.ForRoom(player, RoomType.Elite));
                SpecialCardRewards.TrackOpeningMythReward(reward, data.MythRewardRerolled, (updated, rerolled) =>
                {
                    var options = updated.Cards.ToList();
                    copies.AddRange(options.Where(c => !copies.Contains(c)));
                    data.FixedMythRewardCards = MythCardRewards.CaptureCards(options);
                    data.MythRewardCountsBefore = options.Select(RewardKey).Distinct().ToDictionary(key => key, RewardCount);
                    data.MythRewardRerolled = rerolled;
                    Save(); // 遗物修改或重掷后也保存最终选项，恢复时不能回到初始配方或恢复重掷次数。
                });
                await RewardsCmd.OfferCustom(player, [reward]).WaitAsync(RunLifetime.Token(run));
                data.MythRewardCompleted = true;
                Save();
                Entry.Logger.Info($"[StartCards] Player {player.NetId} completed the opening myth reward.");
            }
            finally
            {
                if (MythRewardWasAdded()) { data.MythRewardCompleted = true; Save(); }
                CleanUp(copies);
            }
        }

        async Task GrantSpecialReward()
        {
            // 兼容旧版本尚未领取、但被全局开关误转换为神话卡的开局奖励。
            if (MythCardRewards.ContainsMythCards(data.FixedRewardCards))
            {
                data.FixedRewardCards = [];
                data.RewardCountsBefore = data.RewardCardIds.ToDictionary(id => id, Count);
                Save();
            }
            if (data.RewardCardIds.Count == 0)
            {
                var rolled = SpecialCardRewards.RollOptions(player);
                data.RewardCardIds = rolled.Select(c => c.Id.ToString()).ToList();
                data.RewardCountsBefore = data.RewardCardIds.ToDictionary(id => id, Count);
                Save(); // 先保存选项，重进房间/继续游戏不重新抽奖励。
            }
            var available = SpecialCardRewards.Candidates(player).ToDictionary(c => c.Id.ToString());
            var options = data.RewardCardIds.Select(id => available.GetValueOrDefault(id)).OfType<CardModel>().ToList();
            var reward = SpecialCardRewards.Create(player, options, forRunStart: true,
                fixedCards: data.FixedRewardCards.Count > 0 ? data.FixedRewardCards : null);
            if (reward == null)
            {
                data.RewardCompleted = true;
                Save();
                return;
            }
            // 所有客户端都构造、等待所有玩家的奖励；原版只向本人展示，并同步领取/跳过。
            var copies = reward.Cards.ToList();
            if (data.FixedRewardCards.Count == 0 && (CardAvailability.ReplacesCardRewards(run) || copies.Any(c => c is MythMutationCard)))
            {
                data.FixedRewardCards = MythCardRewards.CaptureCards(copies);
                data.RewardCountsBefore = copies.Select(RewardKey).Distinct().ToDictionary(key => key, RewardCount);
                Save(); // 固定完整随机效果与领取前数量，恢复时不依赖同一个模板ID。
            }
            try
            {
                await RewardsCmd.OfferCustom(player, [reward]).WaitAsync(RunLifetime.Token(run));
                data.RewardCompleted = true;
                Save();
            }
            finally
            {
                if (RewardWasAdded()) { data.RewardCompleted = true; Save(); }
                CleanUp(copies);
            }
        }

        int Count(string id) => PileType.Deck.GetPile(player).Cards.Count(c => c.Id.ToString() == id);
        int RewardCount(string key) => PileType.Deck.GetPile(player).Cards.Count(c => RewardKey(c) == key);
        bool RewardWasAdded() => data.RewardCountsBefore.Any(pair => RewardCount(pair.Key) > pair.Value);
        bool MythRewardWasAdded() => data.MythRewardCountsBefore.Any(pair => RewardCount(pair.Key) > pair.Value);
        void Save() => _saved.Set(run, player.NetId, data);
    }

    private static string RewardKey(CardModel card) => card is MythMutationCard myth
        ? "myth:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(myth.SnapshotJson)))
        : card.Id.ToString();

    private static void CleanUp(IEnumerable<CardModel> cards)
    {
        foreach (var card in cards)
            if (!card.HasBeenRemovedFromState && card.Pile == null) card.RemoveFromState();
    }

    private static async Task<int> SynchronizePreferences(Player player)
    {
        var run = (RunState)player.RunState;
        var sync = RunManager.Instance.PlayerChoiceSynchronizer;
        uint choice = sync.ReserveChoiceId(player);
        var context = new BlockingPlayerChoiceContext();
        await context.SignalPlayerChoiceBegun(player, PlayerChoiceOptions.None);
        try
        {
            int flags;
            if (LocalContext.IsMe(player) && RunManager.Instance.NetService.Type != NetGameType.Replay)
            {
                flags = (CardAvailability.StartCardPreference.Read() ? 1 : 0)
                    | (CardAvailability.StartRewardPreference.Read() ? 2 : 0)
                    | (CardAvailability.UsesMythStartCardSetting(run) && CardAvailability.MythCardPreference.Read() ? 4 : 0)
                    | (CardAvailability.UsesMythStartRewardSetting(run) && CardAvailability.StartMythRewardPreference.Read() ? 8 : 0);
                sync.SyncLocalChoice(player, choice, PlayerChoiceResult.FromIndex(flags));
            }
            else flags = (await RunLifetime.Wait(sync.WaitForRemoteChoice(player, choice), run)).AsIndex();
            RunLifetime.Token(run).ThrowIfCancellationRequested();
            return flags;
        }
        finally { await context.SignalPlayerChoiceEnded(); }
    }
}

// 房间进入钩子处于联机位置更新和屏幕淡入之前，不能在其内等待玩家选择。
// 新开局经过 RoomFadeIn，继续游戏经过 FadeIn；等待实际淡入完成，再展示开局选项。
[HarmonyPatch]
public static class RunStartCardsReadyPatch
{
    static IEnumerable<MethodBase> TargetMethods()
        => [AccessTools.Method(typeof(NTransition), nameof(NTransition.FadeIn)),
            AccessTools.Method(typeof(NTransition), nameof(NTransition.RoomFadeIn))];

    static void Postfix(ref Task __result)
    {
        if (NRun.Instance != null && RunManager.Instance?.DebugOnlyGetState() is { } run)
            __result = Complete(__result, run);
    }
    static async Task Complete(Task task, RunState run)
    {
        await task;
        if (NRun.Instance == null || !ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), run)) return;
        await RunStartCards.Prepare(run);
    }
}
