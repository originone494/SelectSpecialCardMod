using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using STS2RitsuLib;
using SelectSpecialCardMod.Scripts.Hook;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「时机」条件的计数器：本回合打出的攻击卡数量。
// 各时机卡在 AfterCardPlayed 广播里上报（多张时机卡会对同一次打出重复上报，按 CardPlay 去重）。
public static class ShiJiTracker
{
    private sealed class TurnRecord((CombatId? Combat, int Turn) key)
    {
        public (CombatId? Combat, int Turn) Key { get; } = key;
        public HashSet<CardPlay> Counted { get; } = new();
        public int Attacks;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Player, TurnRecord> Records = new();

    private static TurnRecord GetRecord(Player player)
    {
        var key = PerTurnOnce.CurrentKey(player);
        if (!Records.TryGetValue(player, out var record) || record.Key != key)
        {
            Records.Remove(player);
            Records.Add(player, record = new TurnRecord(key));
        }
        return record;
    }

    public static void Init()
    {
        RitsuLibFramework.SubscribeLifecycle<PlayerTurnStartedEvent>(evt =>
        {
            Records.Remove(evt.Player);
            ShiJiHook.EnsureConnected(evt.Player);
        });
    }

    public static void ReportAttackPlayed(Player owner, CardPlay cardPlay)
    {
        if (cardPlay.Card?.Type != CardType.Attack) return;
        var player = cardPlay.Player;
        var record = GetRecord(player);
        if (record.Counted.Add(cardPlay)) record.Attacks++;
    }

    public static int AttacksPlayedThisTurn(Player player)
        => GetRecord(player).Attacks;
}
