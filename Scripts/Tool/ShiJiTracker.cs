using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using STS2RitsuLib;
using SelectSpecialCardMod.Scripts.Hook;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「时机」条件的计数器：本回合打出的攻击卡数量。
// 各时机卡在 AfterCardPlayed 广播里上报（多张时机卡会对同一次打出重复上报，按 CardPlay 去重）。
public static class ShiJiTracker
{
    private static readonly Dictionary<Player, int> Attacks = new();
    private static readonly HashSet<CardPlay> Counted = new();

    public static void Init()
    {
        RitsuLibFramework.SubscribeLifecycle<PlayerTurnStartedEvent>(evt =>
        {
            Attacks.Remove(evt.Player);
            Counted.Clear();
            ShiJiHook.EnsureConnected(evt.Player);
        });
    }

    public static void ReportAttackPlayed(Player owner, CardPlay cardPlay)
    {
        if (!Counted.Add(cardPlay)) return;
        if (cardPlay.Card?.Type == CardType.Attack)
            Attacks[owner] = Attacks.GetValueOrDefault(owner) + 1;
    }

    public static int AttacksPlayedThisTurn(Player player)
        => Attacks.GetValueOrDefault(player);
}
