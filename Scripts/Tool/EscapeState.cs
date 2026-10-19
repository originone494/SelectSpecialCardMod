using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;

namespace SelectSpecialCardMod.Scripts.Tool;

// 玩家逃跑（「强夺」逃跑卡副本）：玩家自身逃离战斗——类比杀戮尖塔1的烟雾弹。
//   打出后玩家遁走，战斗立即按"通过"结算（标准胜利流程），但该玩家的奖励全部置空
//   （金币/卡牌/药水一概没有——无奖励路径走游戏原生的 EmptyForRoom）。
//
// 为什么不把玩家节点从战斗移除：战斗结束流程（EndCombatInternal）还会遍历
// Players 做 revive/收尾，移除节点/实体有死锁风险——只做视觉淡出。
// 为什么不逐个放跑敌人：那是"敌人逃跑"的演出，玩家遁走不需要动敌人。
public static class EscapeState
{
    // 淡出动画的等待上限（动画本身 0.4 秒）。只是兜底，正常情况下 Finished 会先到。
    private static readonly TimeSpan FadeTimeout = TimeSpan.FromSeconds(1.5d);

    // 逃跑标记按玩家记录：置位后只有这名玩家的奖励结算走 EmptyForRoom。
    // 置位发生在「打出逃跑卡」这一步，而打出是所有客户端都会执行同一张卡的同步流程，
    // 所以各端得到的集合天然一致，不需要额外的网络状态。
    private static readonly HashSet<ulong> EscapedNetIds = [];

    public static bool HasEscaped(Player? player) => player != null && EscapedNetIds.Contains(player.NetId);

    public static void Init()
    {
        // 下一场战斗开始时清空；旧存档/回放进来是空集合，不会误触发空奖励。
        CombatManager.Instance.CombatBegan += _ => EscapedNetIds.Clear();
    }

    // 玩家遁走：淡出演出 → 反射调 internal EndCombatInternal()（无参重载）
    // 立即按胜利结算战斗——节点正常通过，只是该玩家的奖励被 patch 置空。
    public static async Task FleeCombat(Player player)
    {
        EscapedNetIds.Add(player.NetId);

        // 玩家节点淡出遁走（不移除节点）
        var node = NCombatRoom.Instance?.GetCreatureNode(player.Creature);
        if (node != null)
        {
            var tween = node.CreateTween();
            tween.TweenProperty(node, "modulate", Colors.Transparent, 0.4f);
            // Tween.Finished 是 C# 事件不能直接 await，用 TaskCompletionSource 桥接
            var finished = new TaskCompletionSource();
            tween.Finished += () => finished.SetResult();
            // 战斗结束/换房间会把 tween 一并销毁，此时 Finished 永远不会再触发；
            // 只等事件会让这次打出永远不返回（之后手牌全部点不动），所以必须有超时。
            await Task.WhenAny(finished.Task, Task.Delay(FadeTimeout));
        }

        // EndCombatInternal 有两个重载（无参 internal / 带参 private），必须按无参精确取
        var method = AccessTools.Method(typeof(CombatManager), "EndCombatInternal", System.Type.EmptyTypes);
        if (method?.Invoke(CombatManager.Instance, null) is Task task) await task;
    }
}

// 逃跑后无奖励：战斗结算生成奖励时（WithRewardsFromRoom），该玩家逃跑标记置位则
// 换成 EmptyForRoom（游戏原生的空奖励路径）——金币/卡牌/药水一概不生成，
// 奖励界面显示为空（可跳过）。按玩家判断，队友逃跑不会清空你的奖励。
[HarmonyPatch(typeof(RewardsSet), nameof(RewardsSet.WithRewardsFromRoom))]
public static class EscapeNoRewardPatch
{
    [HarmonyPrefix]
    static bool Prefix(RewardsSet __instance, AbstractRoom room, ref RewardsSet __result)
    {
        if (!EscapeState.HasEscaped(__instance.Player)) return true;
        __result = __instance.EmptyForRoom(room);
        return false;
    }
}
