using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using STS2RitsuLib.Networking.Sidecar;
using STS2RitsuLib.RunData;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Intent;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Hook;

public sealed class RunSeenCardHistory
{
    public List<string> Cards { get; set; } = [];
}

// 记录属于看到卡面的本地玩家；按本局、玩家保存，不取卡牌拥有者或全局首次发现记录。
public static class RunSeenCards
{
    private sealed class PendingSync
    {
        public HashSet<string> Sent { get; } = [];
        public ulong LastAttempt { get; set; }
    }

    public sealed record SeenMessage(string Seed, string[] Cards);

    private static PlayerRunSavedData<RunSeenCardHistory> _history = null!;
    private static readonly ConditionalWeakTable<RunState, PendingSync> Pending = new();
    private static bool _initialized;
    private static readonly RitsuLibSidecarSyncMessageDescriptor<SeenMessage> SeenSync = new(
        ModuleId: Entry.ModId,
        MessageKey: "run_seen_cards_v1",
        Serialize: message => JsonSerializer.SerializeToUtf8Bytes(message),
        Deserialize: bytes => JsonSerializer.Deserialize<SeenMessage>(bytes)
            ?? throw new JsonException("Missing seen-card message."),
        Handle: Receive,
        ShouldBuffer: false,
        DispatchLocalOnBroadcast: false,
        ShouldBroadcast: true);

    public static void Init()
    {
        if (_initialized) return;
        _history = RunSavedDataStore.For(Entry.ModId)
            .RegisterPerPlayer<RunSeenCardHistory>("run_seen_cards");
        RitsuLibSidecarSyncMessages.Register(SeenSync);
        RitsuLibSidecarEvents.OnHandshakeCompleted(_ =>
        {
            var run = ActiveRun;
            if (run != null) Pending.Remove(run);
        });
        if (Engine.GetMainLoop() is SceneTree tree)
            tree.Root.CallDeferred(Node.MethodName.AddChild, new RunSeenSyncTicker { Name = "SelectSpecialSeenSync" });
        _initialized = true;
    }

    private static RunState? ActiveRun => RunManager.Instance?.DebugOnlyGetState();

    public static bool IsCandidate(CardModel? card)
        => card != null && card is not DerivedTokenCard && card is not IMonsterDisplayCard
            && (card.Rarity != CardRarity.Token || card.ShouldShowInCardLibrary || card.CanBeGeneratedInCombat);

    private static CardModel? Resolve(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try { return ModelDb.GetByIdOrNull<CardModel>(ModelId.Deserialize(text)); }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { return null; }
    }

    public static IReadOnlyList<ModelId> IdsFor(Player player)
        => _history.Get(player).Cards.Select(Resolve).Where(IsCandidate)
            .Where(c => CardAvailability.IsAllowed(c!, player.RunState)).Select(c => c!.Id)
            .Distinct().OrderBy(id => id).ToList();

    internal static bool Merge(RunState run, ulong viewer, IEnumerable<string> ids)
    {
        if (run.GetPlayer(viewer) == null) return false;
        var history = _history.Get(run, viewer);
        var known = history.Cards.ToHashSet(StringComparer.Ordinal);
        var additions = ids.Select(Resolve).Where(IsCandidate).Select(c => c!.Id.ToString())
            .Where(known.Add).ToList();
        if (additions.Count == 0) return false;
        history.Cards.AddRange(additions);
        _history.Set(run, viewer, history);
        return true;
    }

    // 已在当前客户端实际显示的卡面：商店、奖励、预览、队友牌面均归给观看者本人。
    internal static bool RecordVisible(CardModel? card)
    {
        var run = ActiveRun;
        var service = RunManager.Instance?.NetService;
        if (!_initialized || run == null || service == null || service.Type == NetGameType.Replay) return false;
        // 商店、奖励、选牌界面可以显示没有 Owner/RunState 的规范卡或预览副本。
        // 它们同样属于本局实际看到的卡；只有明确归属另一局的模型才排除。
        if (card == null || (card.RunState != null && !ReferenceEquals(card.RunState, run))) return false;
        var viewer = LocalContext.NetId == service.NetId ? run.GetPlayer(service.NetId) : null;
        if (viewer == null) return false;
        if (IsCandidate(card) && Merge(run, viewer.NetId, [card.Id.ToString()])) FlushPending(force: true);
        return true;
    }

    // 各玩家的私人见卡记录分别复制到主机存档，不并入其他玩家的候选。
    // 发送失败保留待发送记录；重连后补发，接收端去重。
    internal static void FlushPending(bool force = false)
    {
        var run = ActiveRun;
        var service = RunManager.Instance?.NetService;
        if (!_initialized || run == null || service == null || service.Type == NetGameType.Replay) return;
        var viewer = LocalContext.NetId == service.NetId ? run.GetPlayer(service.NetId) : null;
        if (viewer == null) return;
        var pending = Pending.GetOrCreateValue(run);
        var now = Time.GetTicksMsec();
        if (!force && now - pending.LastAttempt < 1000) return;
        pending.LastAttempt = now;
        var ids = IdsFor(viewer).Select(id => id.ToString()).Where(id => !pending.Sent.Contains(id)).ToArray();
        foreach (var batch in ids.Chunk(64))
        {
            bool sent = service.Type == NetGameType.Singleplayer
                || RitsuLibSidecarSyncMessages.SendToHostAndBroadcast(service, SeenSync,
                    new SeenMessage(run.Rng.StringSeed, batch));
            if (!sent) break;
            pending.Sent.UnionWith(batch);
        }
    }

    private static Task Receive(RitsuLibSidecarSyncMessageContext<SeenMessage> context)
    {
        var run = ActiveRun;
        if (run != null && RunManager.Instance.NetService.Type != NetGameType.Replay
            && context.Message.Seed == run.Rng.StringSeed && context.Message.Cards != null)
            Merge(run, context.SenderNetId, context.Message.Cards);
        return Task.CompletedTask;
    }

    // 网格原版同步索引：先同步选择者的规范卡列表，再创建共同顺序的战斗副本。
    public static async Task<IReadOnlyList<CardModel>> SynchronizeCandidates(PlayerChoiceContext context, Player player)
    {
        if (CardSelectCmd.Selector != null) return IdsFor(player).Select(ModelDb.GetById<CardModel>).ToList();
        var synchronizer = RunManager.Instance.PlayerChoiceSynchronizer;
        uint choiceId = synchronizer.ReserveChoiceId(player);
        await context.SignalPlayerChoiceBegun(player, PlayerChoiceOptions.None);
        try
        {
            List<CardModel> cards;
            if (LocalContext.IsMe(player) && RunManager.Instance.NetService.Type != NetGameType.Replay)
            {
                cards = IdsFor(player).Select(ModelDb.GetById<CardModel>).ToList();
                synchronizer.SyncLocalChoice(player, choiceId, PlayerChoiceResult.FromCanonicalCards(cards));
            }
            else
            {
                cards = (await SelectSpecialCardMod.Scripts.Tool.RunLifetime.Wait(synchronizer.WaitForRemoteChoice(player, choiceId), (RunState)player.RunState)).AsCanonicalCards()
                    .Where(IsCandidate).Where(c => CardAvailability.IsAllowed(c, player.RunState))
                    .DistinctBy(c => c.Id).OrderBy(c => c.Id).ToList();
            }
            Merge((RunState)player.RunState, player.NetId, cards.Select(c => c.Id.ToString()));
            return cards;
        }
        finally { await context.SignalPlayerChoiceEnded(); }
    }
}

[HarmonyPatch(typeof(NCard), nameof(NCard._Ready))]
public static class RunSeenCardsPatch
{
    static void Postfix(NCard __instance) => __instance.AddChild(new RunSeenCardObserver());
}
