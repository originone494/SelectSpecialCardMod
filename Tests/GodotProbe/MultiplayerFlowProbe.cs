using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Unlocks;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Relics;
using SelectSpecialCardMod.Scripts.Tool;
using STS2RitsuLib;
using STS2RitsuLib.Networking.ManagedActions;

// 两份独立状态执行实际托管行动和奖励构造；不启动真实网络或完整战斗 UI。
public static class MultiplayerFlowProbe
{
    private static Player? _local;
    private static bool _participating = true, _acceptRequest = true, _cancel, _invalidateSelection, _reversePool;
    private static int _requests, _activations, _choices;
    private static byte[]? _payload;
    private static ulong? _requestOwner;
    private static TaskCompletionSource<IEnumerable<CardModel>>? _choice;
    private static readonly FieldInfo Pending = AccessTools.Field(typeof(ShiJiHook), "_pendingRequest");
    private static readonly FieldInfo TurnPlayer = AccessTools.Field(typeof(ShiJiHook), "_turnPlayer");
    private static RitsuLibManagedNetActionDescriptor<ShiJiHook.ShiJiRequest> Descriptor
        => (RitsuLibManagedNetActionDescriptor<ShiJiHook.ShiJiRequest>)AccessTools.Field(typeof(ShiJiHook), "ShiJiAction").GetValue(null)!;

    public static async Task Check()
    {
        var h = new Harmony("scm.multiplayer-flows.godot-framework");
        var previousPending = Pending.GetValue(null);
        var previousPlayer = TurnPlayer.GetValue(null);
        try
        {
            h.Patch(AccessTools.Method(typeof(LocalContext), "IsMe", [typeof(Player)]),
                prefix: new HarmonyMethod(typeof(MultiplayerFlowProbe), nameof(IsMe)));
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsInProgress"),
                prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(ArrivalEquipmentProbe.InProgress)));
            foreach (var name in new[] { "IsEnding", "IsOverOrEnding" })
                h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), name),
                    prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(ArrivalEquipmentProbe.LiveCombat)));
            h.Patch(AccessTools.Method(typeof(CombatManager), "IsPartOfPlayerTurn"),
                prefix: new HarmonyMethod(typeof(MultiplayerFlowProbe), nameof(Participating)));
            h.Patch(AccessTools.Method(typeof(CombatManager), "IsPlayerReadyToEndTurn"),
                prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), nameof(ArrivalEquipmentProbe.LiveCombat)));
            var request = typeof(RitsuLibManagedNetActions).GetMethods().Single(m => m.Name == "Request" && m.IsGenericMethodDefinition)
                .MakeGenericMethod(typeof(ShiJiHook.ShiJiRequest));
            h.Patch(request, prefix: new HarmonyMethod(typeof(MultiplayerFlowProbe), nameof(Send)));
            h.Patch(AccessTools.Method(typeof(CardSelectCmd), "FromCombatPile",
                [typeof(PlayerChoiceContext), typeof(CardPile), typeof(Player), typeof(CardSelectorPrefs), typeof(Func<CardModel, bool>)]),
                prefix: new HarmonyMethod(typeof(MultiplayerFlowProbe), nameof(Select)));
            h.Patch(AccessTools.Method(typeof(FuSuDeTianKongShenCard), "ActivateShiJi"),
                prefix: new HarmonyMethod(typeof(MultiplayerFlowProbe), nameof(Activate)));
            h.Patch(AccessTools.Method(typeof(CardPoolModel), "GetUnlockedCards"),
                prefix: new HarmonyMethod(typeof(MultiplayerFlowProbe), nameof(Pool)));
            ShiJiHook.Register(); ShiJiHook.Register();
            await CheckTiming();
            CheckRewards();
            GD.Print("PASS: timing managed-action payload/owner, local UI isolation, two independent peer executions, cancel/stale/rejected requests, completion notification; all-player deterministic elite rewards and RNG state.");
        }
        finally
        {
            h.UnpatchAll(h.Id);
            Pending.SetValue(null, previousPending); TurnPlayer.SetValue(null, previousPlayer);
            _local = null; _payload = null; _choice = null;
            _participating = _acceptRequest = true; _cancel = _invalidateSelection = _reversePool = false;
        }
    }

    private static Player TimingPlayer(ulong id)
    {
        var player = ArrivalEquipmentProbe.NewPlayer();
        AccessTools.Field(typeof(Player), "<NetId>k__BackingField").SetValue(player, id);
        AccessTools.Property(typeof(CombatState), "CurrentSide").SetValue(player.Creature.CombatState, CombatSide.Player);
        var card = player.Creature.CombatState!.CreateCard(ModelDb.Card<FuSuDeTianKongShenCard>(), player);
        PileType.Exhaust.GetPile(player).AddInternal(card);
        return player;
    }

    private static bool Request(Player player) => (bool)AccessTools.Method(typeof(ShiJiHook), "Request")
        .Invoke(null, [player, PileType.Exhaust])!;

    private static Task Execute(Player player, ShiJiHook.ShiJiRequest message)
    {
        // 使用实际框架行动执行器，从序列化数据恢复消息和 GameActionPlayerChoiceContext。
        var bytes = Descriptor.Serialize(message);
        var action = new RitsuLibManagedGameAction(player, RitsuLibManagedNetActions.Register(Descriptor), Descriptor.ActionType, bytes);
        return (Task)AccessTools.Method(typeof(RitsuLibManagedGameAction), "ExecuteAction").Invoke(action, null)!;
    }

    private static async Task CheckTiming()
    {
        var owner = TimingPlayer(11);
        var other = TimingPlayer(22);
        _local = owner; Pending.SetValue(null, null);
        ShiJiHook.EnsureConnected(owner); ShiJiHook.EnsureConnected(other);
        if (ShiJiHook.CurrentTurnPlayer != owner) throw new Exception("Remote turn notification replaced the local timing player.");
        _requests = _activations = _choices = 0;
        if (Request(other) || !Request(owner) || Request(owner) || _requests != 1 || _requestOwner != owner.NetId)
            throw new Exception("Timing requests are not owner-only or duplicate clicks were sent.");
        var message = Descriptor.Deserialize(_payload!);
        if (message.Pile != PileType.Exhaust || message.Turn != owner.PlayerCombatState!.TurnNumber
            || message.Version != 1 || Descriptor.ActionType != MegaCrit.Sts2.Core.Entities.Multiplayer.GameActionType.CombatPlayPhaseOnly)
            throw new Exception("Timing payload lost its pile/turn/version or play-phase queue restriction.");
        int finished = 0;
        var card = PileType.Exhaust.GetPile(owner).Cards.Single();
        card.ExecutionFinished += _ => finished++;
        _choice = new TaskCompletionSource<IEnumerable<CardModel>>();
        var running = Execute(owner, message);
        if (running.IsCompleted || Request(owner)) throw new Exception("Timing lock ended before the selection completed.");
        _choice.SetResult([card]); await running; _choice = null;
        if (_activations != 1 || finished != 1 || card.Pile?.Type != PileType.Hand || Pending.GetValue(null) != null)
            throw new Exception("Timing did not complete exactly once or failed to restore the selected card lifecycle.");

        // 另一客户端的本机玩家是22，但行动拥有者仍是11。
        var remoteOwner = TimingPlayer(11);
        _local = other; ShiJiHook.EnsureConnected(other);
        await Execute(remoteOwner, message);
        if (_activations != 2 || PileType.Hand.GetPile(remoteOwner).Cards.Count != 1 || ShiJiHook.CurrentTurnPlayer != other)
            throw new Exception("Remote timing execution used local UI ownership or produced a different result.");

        var candidate = TimingPlayer(11);
        int before = _choices;
        await Execute(candidate, message with { Version = 99 });
        await Execute(candidate, message with { Turn = message.Turn + 1 });
        await Execute(candidate, message with { Pile = PileType.Hand });
        _participating = false; await Execute(candidate, message); _participating = true;
        if (_choices != before) throw new Exception("Invalid, stale or nonparticipant requests opened a timing choice.");
        _cancel = true; await Execute(candidate, message); _cancel = false;
        _invalidateSelection = true; await Execute(candidate, message); _invalidateSelection = false;
        if (_activations != 2) throw new Exception("Canceled or moved timing cards still activated.");

        var retry = TimingPlayer(44);
        _local = retry; Pending.SetValue(null, null); _acceptRequest = false;
        int sentBefore = _requests;
        if (Request(retry) || Pending.GetValue(null) != null || _requests != sentBefore + 1)
            throw new Exception("Rejected request retained the local lock or never reached the transport boundary.");
        _acceptRequest = true;
    }

    private static void CheckRewards()
    {
        var first = RewardPeer(false);
        _reversePool = true;
        var second = RewardPeer(true);
        if (first != second) throw new Exception("Peers produced different rewards or player reward RNG state.");
    }

    private static string RewardPeer(bool reversePlayers)
    {
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        AccessTools.Field(typeof(RunState), "_allCards").SetValue(run, new List<CardModel>());
        var players = new[] { RewardPlayer(run, 11, true), RewardPlayer(run, 22, true), RewardPlayer(run, 33, false) };
        AccessTools.Field(typeof(RunState), "_players").SetValue(run, (reversePlayers ? players.Reverse() : players).ToList());
        var point = new MapPointHistoryEntry { Rooms = [new MapPointRoomHistoryEntry { RoomType = RoomType.Elite }] };
        AccessTools.Field(typeof(RunState), "_mapPointHistory").SetValue(run, new List<List<MapPointHistoryEntry>> { new() { point } });
        var combat = new CombatState(ModelDb.AllEncounters.First(e => e.RoomType == RoomType.Elite).ToMutable(), run);
        var room = (CombatRoom)RuntimeHelpers.GetUninitializedObject(typeof(CombatRoom));
        AccessTools.Field(typeof(CombatRoom), "<CombatState>k__BackingField").SetValue(room, combat);
        AccessTools.Field(typeof(CombatRoom), "_extraRewards").SetValue(room, new Dictionary<Player, List<Reward>>());
        _local = reversePlayers ? players[1] : players[0];
        void Victory() => AccessTools.Method(typeof(EliteSpecialReward), "HandleEliteVictory").Invoke(null,
            [new CombatVictoryEvent(run, combat, room, DateTimeOffset.UtcNow)]);
        string RngState() => JsonSerializer.Serialize(players.Select(p => p.PlayerRng.ToSerializable()).ToList());
        string untouched = JsonSerializer.Serialize(players[2].PlayerRng.ToSerializable());
        Victory();
        if (room.ExtraRewards.Count != 2 || room.ExtraRewards.ContainsKey(players[2])
            || untouched != JsonSerializer.Serialize(players[2].PlayerRng.ToSerializable()))
            throw new Exception("Elite rewards were local-only or generated for a player without the Duel Disk.");
        var signature = new List<string>();
        foreach (var player in players.Take(2))
        {
            var reward = room.ExtraRewards[player].Single() as CardReward ?? throw new Exception("Missing card reward.");
            var cards = reward.Cards.ToList();
            if (cards.Count != 3 || cards.DistinctBy(c => c.Id).Count() != 3
                || cards.Any(c => c.Owner != player || !c.IsUpgraded || c.Rarity == CardRarity.Token
                    || !c.ShouldShowInCardLibrary || CardClassification.IsExtraMonster(c)))
                throw new Exception("Reward options lost uniqueness, ownership, filtering or upgrades.");
            signature.Add(player.NetId + ":" + string.Join(",", cards.Select(c => c.Id)));
        }
        var state = RngState();
        Victory();
        if (room.ExtraRewards.Values.Any(rewards => rewards.Count != 1) || state != RngState())
            throw new Exception("Repeated victory notification duplicated rewards or advanced RNG again.");
        return string.Join(";", signature) + state;
    }

    private static Player RewardPlayer(RunState run, ulong id, bool disk)
    {
        var player = ArrivalEquipmentProbe.NewPlayer();
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        AccessTools.Field(typeof(Player), "<NetId>k__BackingField").SetValue(player, id);
        AccessTools.Field(typeof(Player), "<Character>k__BackingField").SetValue(player, ModelDb.Character<Ironclad>());
        AccessTools.Field(typeof(Player), "<UnlockState>k__BackingField").SetValue(player, UnlockState.all);
        AccessTools.Field(typeof(Player), "_relics").SetValue(player, new List<RelicModel>());
        var rng = AccessTools.Property(typeof(Player), "PlayerRng");
        rng.SetValue(player, Activator.CreateInstance(rng.PropertyType, [12345UL + id]));
        if (disk) player.AddRelicInternal(ModelDb.Relic<DuelDskRelic>().ToMutable());
        return player;
    }

    public static bool IsMe(Player? player, ref bool __result) { __result = player == _local; return false; }
    public static bool Participating(ref bool __result) { __result = _participating; return false; }
    public static bool Send(RitsuLibManagedNetActionDescriptor<ShiJiHook.ShiJiRequest> descriptor,
        ShiJiHook.ShiJiRequest message, ulong? ownerNetId, ref bool __result)
    { _requests++; _payload = descriptor.Serialize(message); _requestOwner = ownerNetId; __result = _acceptRequest; return false; }
    public static bool Select(PlayerChoiceContext context, CardPile pile, Player player, CardSelectorPrefs prefs,
        Func<CardModel, bool> filter, ref Task<IEnumerable<CardModel>> __result)
    {
        _choices++;
        if (context is not GameActionPlayerChoiceContext || context.OwnerId != player.NetId
            || prefs.MinSelect != 0 || prefs.MaxSelect != 1 || !prefs.RequireManualConfirmation)
            throw new Exception("Timing selection lost its synchronized action context or cancellation settings.");
        var card = pile.Cards.Single(filter);
        if (_invalidateSelection) { card.RemoveFromCurrentPile(silent: true); PileType.Discard.GetPile(player).AddInternal(card); }
        __result = _choice?.Task ?? Task.FromResult<IEnumerable<CardModel>>(_cancel ? [] : [card]);
        return false;
    }
    public static bool Activate(FuSuDeTianKongShenCard __instance, Player player, ref Task __result)
    {
        if (__instance.Owner != player) throw new Exception("Timing activated somebody else's card.");
        _activations++; __instance.RemoveFromCurrentPile(silent: true); PileType.Hand.GetPile(player).AddInternal(__instance);
        __result = Task.CompletedTask; return false;
    }
    public static bool Pool(CardPoolModel __instance, ref IEnumerable<CardModel> __result)
    {
        if (__instance is not SelectSpecialCardPool) return true;
        CardModel[] cards = [ModelDb.Card<YiXiangMingJueZeCard>(), ModelDb.Card<YiXiangMingQingZhuCard>(),
            ModelDb.Card<YiXiangMingDiaoYinCard>(), ModelDb.Card<FuSuDeTianKongShenCard>(), ModelDb.Card<YiXiangMingJueZeCard>(),
            ModelDb.Card<EffectOneCard>(), ModelDb.Card<CanHuanShengLongShuangChaTianLongCard>()];
        __result = _reversePool ? cards.Reverse() : cards; return false;
    }
}
