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
//   打出后玩家遁走，战斗立即按"通过"结算（标准胜利流程），但奖励全部置空
//   （金币/卡牌/药水一概没有——无奖励路径走游戏原生的 EmptyForRoom）。
//
// 为什么不把玩家节点从战斗移除：战斗结束流程（EndCombatInternal）还会遍历
// Players 做 revive/收尾，移除节点/实体有死锁风险——只做视觉淡出。
// 为什么不逐个放跑敌人：那是"敌人逃跑"的演出，玩家遁走不需要动敌人。
public static class EscapeState
{
    // 逃跑标记：置位后战斗结算时奖励置空。下一场战斗开始（CombatBegan）时重置——
    // 多人下每个玩家的奖励生成都会经过 patch（标记保持不清，全员空奖励）。
    internal static bool PlayerEscaped { get; private set; }

    public static void Init()
    {
        CombatManager.Instance.CombatBegan += _ => PlayerEscaped = false;
    }

    // 玩家遁走：淡出演出 → 反射调 internal EndCombatInternal()（无参重载）
    // 立即按胜利结算战斗——节点正常通过，只是奖励被 patch 置空。
    public static async Task FleeCombat(Player player)
    {
        PlayerEscaped = true;

        // 玩家节点淡出遁走（不移除节点）
        var node = NCombatRoom.Instance?.GetCreatureNode(player.Creature);
        if (node != null)
        {
            var tween = node.CreateTween();
            tween.TweenProperty(node, "modulate", Colors.Transparent, 0.4f);
            // Tween.Finished 是 C# 事件不能直接 await，用 TaskCompletionSource 桥接
            var finished = new TaskCompletionSource();
            tween.Finished += () => finished.SetResult();
            await finished.Task;
        }

        // EndCombatInternal 有两个重载（无参 internal / 带参 private），必须按无参精确取
        var method = AccessTools.Method(typeof(CombatManager), "EndCombatInternal", System.Type.EmptyTypes);
        if (method?.Invoke(CombatManager.Instance, null) is Task task) await task;
    }
}

// 逃跑后无奖励：战斗结算生成奖励时（WithRewardsFromRoom），逃跑标记置位则
// 换成 EmptyForRoom（游戏原生的空奖励路径）——金币/卡牌/药水一概不生成，
// 奖励界面显示为空（可跳过）。
[HarmonyPatch(typeof(RewardsSet), nameof(RewardsSet.WithRewardsFromRoom))]
public static class EscapeNoRewardPatch
{
    [HarmonyPrefix]
    static bool Prefix(RewardsSet __instance, AbstractRoom room, ref RewardsSet __result)
    {
        if (!EscapeState.PlayerEscaped) return true;
        __result = __instance.EmptyForRoom(room);
        return false;
    }
}
