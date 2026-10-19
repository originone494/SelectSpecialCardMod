using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Relics;

namespace SelectSpecialCardMod.Scripts.Tool;

// 击败每层的第一个精英时：从 Special 卡池随机抽 3 张，以原版卡牌奖励界面 3 选 1 加入卡组。
// 移植自 yuno mod 的 EliteSpecialReward。
// 前提条件：必须持有「决斗盘」遗物（本 mod 的特殊卡入场券，开局发放，见 DuelDskGrant）。
// 没有这枚遗物时，击败精英不会出现特殊卡奖励。
public static class EliteSpecialReward
{
    private static bool _registered;
    private static readonly ConditionalWeakTable<CombatRoom, HashSet<ulong>> Granted = new();

    public static void Register()
    {
        if (_registered) return;
        // 奖励构造没有异步命令；在事件返回前完成，避免原版先生成奖励列表。
        RitsuLibFramework.SubscribeLifecycle<CombatVictoryEvent>(HandleEliteVictory);
        _registered = true;
    }

    private static void HandleEliteVictory(CombatVictoryEvent evt)
    {
        if (evt.Room is not CombatRoom combatRoom || combatRoom.RoomType != RoomType.Elite)
        {
            return;
        }
        if (evt.CombatState == null)
        {
            return;
        }

        // “每层的第一个精英”：地图历史按幕分组（MapPointHistory[actIndex] 为当前幕的全部记录），
        // 记录在进房时写入并随存档持久化。本次胜利时当前幕的精英房间数恰为 1，即为该层第一个被击败的精英；
        // 读档后依据历史重新判定，不会多发或漏发。
        int actIndex = evt.RunState.CurrentActIndex;
        if (actIndex < 0 || actIndex >= evt.RunState.MapPointHistory.Count)
        {
            return;
        }
        if (evt.RunState.MapPointHistory[actIndex].Count(entry => entry.HasRoomOfType(RoomType.Elite)) != 1)
        {
            return;
        }

        // 各端都生成所有玩家的奖励。原版同步的是奖励索引，不会补发缺失的奖励对象。
        foreach (var player in evt.RunState.Players.OrderBy(p => p.NetId))
            GrantForPlayer(combatRoom, player);
    }

    internal static void GrantForPlayer(CombatRoom combatRoom, Player player)
    {
        var granted = Granted.GetOrCreateValue(combatRoom);
        if (granted.Contains(player.NetId)) return;
        // 前提：持有「决斗盘」。旧存档遗物由 RunSetup 提前迁移。
        if (player.GetRelic<DuelDskRelic>() == null)
        {
            Log.Info($"[SelectSpecialCardMod] EliteSpecialReward: 玩家 {player.NetId} 没有「决斗盘」，跳过特殊卡奖励。");
            return;
        }

        try
        {
            var reward = SpecialCardRewards.Create(player);
            if (reward == null)
            {
                Log.Warn($"[SelectSpecialCardMod] EliteSpecialReward: Special 卡池无可用候选卡（玩家 {player.NetId}），跳过特殊卡奖励。");
                return;
            }
            combatRoom.AddExtraReward(player, reward);
            granted.Add(player.NetId);

            Log.Info($"[SelectSpecialCardMod] EliteSpecialReward: 已为玩家 {player.NetId} 加入 {reward.Cards.Count()} 选 1 特殊卡奖励：{string.Join(",", reward.Cards.Select(c => c.Id))}");
        }
        catch (Exception ex)
        {
            Log.Error($"[SelectSpecialCardMod] EliteSpecialReward: 为玩家 {player.NetId} 生成特殊卡奖励异常：{ex}");
        }
    }
}
