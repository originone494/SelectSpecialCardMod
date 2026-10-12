using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
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
// 前提条件：必须持有「死亡讯息-红」遗物（本 mod 的特殊卡入场券，开局发放，见 DeadEndRedGrant）。
// 没有这枚遗物时，击败精英不会出现特殊卡奖励。
public static class EliteSpecialReward
{
    /// <summary>一次奖励提供的选项数量（原版卡牌奖励是 3 选 1）。</summary>
    private const int OptionCount = 3;

    public static void Register()
    {
        RitsuLibFramework.SubscribeLifecycle<CombatVictoryEvent>(evt =>
        {
            _ = HandleEliteVictory(evt);
        });
    }

    private static async Task HandleEliteVictory(CombatVictoryEvent evt)
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

        // 只处理本地玩家（单人局即唯一玩家）
        Player? me = LocalContext.GetMe(evt.RunState.Players);
        if (me == null)
        {
            return;
        }

        // 前提：持有「死亡讯息-红」。没有这枚遗物就拿不到特殊卡（本 mod 唯一的特殊卡获取途径）。
        if (me.GetRelic<DeadEndRedRelic>() == null)
        {
            Log.Info($"[SelectSpecialCardMod] EliteSpecialReward: 玩家 {me.NetId} 没有「死亡讯息-红」，跳过特殊卡奖励。");
            return;
        }

        try
        {
            // 卡池模型必须从 ModelDb 取已注册的规范实例（直接 new 会触发 DuplicateModelException）。
            var pool = ModelDb.GetById<CardPoolModel>(ModelDb.GetId(typeof(SelectSpecialCardPool)));

            // 候选卡过滤规则与原版 CardCreationOptions.GetPossibleCards 一致（解锁状态 + 多人约束）。
            // 额外排除不可获取卡（Token 稀有度如「羊衍生物」、图鉴隐藏卡、怪物卡模板），
            // 判据与检索系统共用（SelectToolCmd.IsNonObtainable），避免衍生卡被当成奖励发出去。
            // 注意：该池同时经 [RegisterCard]（ModHelper 注入）与旧式 CardTypes 反射枚举收录卡牌，
            // 同一张卡会出现两次；这里按卡牌类型去重，保证一次奖励内的 3 张候选互不相同。
            List<CardModel> candidates = pool
                .GetUnlockedCards(me.UnlockState, me.RunState.CardMultiplayerConstraint)
                .Where(c => !SelectToolCmd.IsNonObtainable(c))
                // 「额外怪兽」只能从局外的额外卡组选择里带进来，局内不再从精英奖励获取
                .Where(c => !c.Tags.Contains(SelectTags.ExtraMonster))
                .GroupBy(c => c.GetType())
                .Select(g => g.First())
                .ToList();
            if (candidates.Count == 0)
            {
                Log.Warn($"[SelectSpecialCardMod] EliteSpecialReward: Special 卡池无可用候选卡（玩家 {me.NetId}），跳过特殊卡奖励。");
                return;
            }

            // 关键点：Special 池全部是 Ancient 稀有度，而原版 CardFactory 的任意稀有度抽取都产不出 Ancient——
            // Uniform 分支显式排除 Ancient；RegularEncounter/Elite/Boss 等分支掷出 Common/Uncommon/Rare 后
            // 沿 Common→Uncommon→Rare→Common 的循环向上爬，Ancient 直接映射为 None（死路），
            // 最终抛 "couldn't generate a valid rarity"。所以这里不走 CardCreationOptions + CardReward.Populate()，
            // 而是从候选池均匀随机抽 3 张互不相同的卡，走原版「指定卡牌」CardReward 构造器
            // （原版教程/事件同款路线）。序列化/读档由 RitsuLib 的 CardRewardToSerializablePatch 旁路数据兜底。
            var rng = me.PlayerRng.Rewards;
            List<CardModel> remaining = new(candidates);
            List<CardModel> offered = new(OptionCount);
            for (int i = 0; i < OptionCount && remaining.Count > 0; i++)
            {
                CardModel? canonical = rng.NextItem(remaining);
                if (canonical == null)
                {
                    break;
                }
                remaining.Remove(canonical);
                // 生成玩家拥有的可变卡实例，与原版 CardFactory.CreateForReward 的做法一致。
                CardModel mutableCard = me.RunState.CreateCard(canonical, me);
                // 特殊卡奖励统一以「已升级(+1)」形态发放。
                CardCmd.Upgrade(mutableCard);
                offered.Add(mutableCard);
            }
            if (offered.Count == 0)
            {
                Log.Warn($"[SelectSpecialCardMod] EliteSpecialReward: 未抽出任何特殊卡（玩家 {me.NetId}），跳过特殊卡奖励。");
                return;
            }

            // 「指定卡牌」构造器：3 张卡固定不变，不再参与任何稀有度抽取。
            // rerollOptions 仅用于 Driftwood 遗物触发的重掷（本奖励不支持重掷出 Ancient）；
            // 传入角色卡池的常规精英奖励选项作为兜底，避免重掷时走 CardFactory 抛异常。
            var reward = new CardReward(
                offered,
                CardCreationSource.Other,
                me,
                CardCreationOptions.ForRoom(me, RoomType.Elite));
            combatRoom.AddExtraReward(me, reward);

            Log.Info($"[SelectSpecialCardMod] EliteSpecialReward: 已为玩家 {me.NetId} 加入 {offered.Count} 选 1 特殊卡奖励：{string.Join(",", offered.Select(c => c.Id))}");
        }
        catch (Exception ex)
        {
            Log.Error($"[SelectSpecialCardMod] EliteSpecialReward: 为玩家 {me.NetId} 生成特殊卡奖励异常：{ex}");
        }

        await Task.CompletedTask;
    }
}
