using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace SelectSpecialCardMod.Scripts.Tool;

// 给 CardPileCmd.Draw 打一个"抽牌窗口"括号，用于把「抽牌」和「因效果加入手牌」区分开。
//
// 为什么需要：抽牌内部就是 `await Add(card, hand)`，和"效果把卡加进手里"
// 走的是同一条入堆路径，AfterCardChangedPiles 里无法分辨；而钩子又是 await 的，
// 在 AfterCardChangedPiles 里等不到紧随其后的 AfterCardDrawn。
//
// 所有抽牌都汇到这一个重载（单张 Draw、多张 Draw、DrawWithoutBlockingOnOtherPlayers 都调它）。
// 计数按玩家分开存，避免多人同时抽牌时互相干扰；用 Finalizer 保证原方法抛异常时也能把计数还原。
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw),
    new[] { typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool) })]
public static class DrawWindowPatch
{
    private static readonly Dictionary<Player, int> Depth = new();

    public static bool IsInsideDraw(Player? player)
        => player != null && Depth.TryGetValue(player, out int depth) && depth > 0;

    [HarmonyPrefix]
    static void Prefix(Player player)
    {
        Depth[player] = Depth.TryGetValue(player, out int depth) ? depth + 1 : 1;
    }

    // 用 Finalizer 而不是 Postfix：原方法抛异常时也要把计数还原，否则这个玩家会被永久当成"正在抽牌"
    [HarmonyFinalizer]
    static void Finalizer(Player player)
    {
        if (!Depth.TryGetValue(player, out int depth)) return;
        if (depth <= 1) Depth.Remove(player);
        else Depth[player] = depth - 1;
    }
}
