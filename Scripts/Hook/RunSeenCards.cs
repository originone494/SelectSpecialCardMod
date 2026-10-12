using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Runs;

namespace SelectSpecialCardMod.Scripts.Hook;

// 本局「见过的卡」记录。
//
// 为什么不直接用 Player.DiscoveredCards：它的写入条件是
//   if (Progress.MarkCardAsSeen(card.Id) && ...) card.Owner.DiscoveredCards.Add(card.Id)
// 而 Progress 是**全局存档**，MarkCardAsSeen 只在「全局首次发现」时返回 true——
// 老存档里绝大多数卡在前几局就发现过，因此永远不会进入本局列表，
// 结果本局列表里只剩 mod 的新卡（表现为「候选只有一张阻击」）。
//
// 这里改为在卡牌被渲染进界面的那一刻（NCard.Model setter）自行记录，
// 得到真正意义上「本局见过的卡」。仅记录局内（有 Run/Combat 上下文）的卡。
public static class RunSeenCards
{
    private static readonly List<ModelId> _ids = new();
    private static readonly HashSet<ModelId> _set = new();

    public static IReadOnlyList<ModelId> Ids => _ids;

    public static void Init()
    {
        // 新一局开始 → 清空上一局的记录
        if (RunManager.Instance != null) RunManager.Instance.RunStarted += _ => Clear();
    }

    public static void Clear()
    {
        _ids.Clear();
        _set.Clear();
    }

    internal static void Record(CardModel? card)
    {
        if (card == null) return;
        // 只在局内记录：图鉴/统计等界面的卡没有 Run/Combat 上下文，不算「见过」
        if (card.RunState == null && card.CombatState == null) return;
        if (_set.Add(card.Id)) _ids.Add(card.Id);
    }
}

// 卡牌被渲染时登记（NCard.Model 的 setter 是「这张卡出现在屏幕上」的统一入口，
// 手牌/抽牌堆/弃牌堆/奖励/商店等所有展示都会经过这里）。
[HarmonyPatch(typeof(NCard), nameof(NCard.Model), MethodType.Setter)]
public static class RunSeenCardsPatch
{
    [HarmonyPostfix]
    static void After(NCard __instance)
    {
        RunSeenCards.Record(__instance.Model);
    }
}