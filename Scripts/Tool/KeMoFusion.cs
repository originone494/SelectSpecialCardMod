using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「刻魔融合」：由「刻魔·大圣棺」的特殊效果调用。
// 一只「刻魔融合怪兽」自己声明融合素材，流程代码不认具体是哪只卡，新增融合怪兽只加一条声明即可。
//
// 流程（与用户敲定的口径一致）：
//   ① 从额外堆选 1 只素材凑得齐的「刻魔融合怪兽」——**必须选，不能跳过**；
//   ② 按它的素材需求，从消耗堆**一张一张**选素材；每一步只展示"选它之后剩下的槽位仍然凑得齐"的卡，
//      已被选过的卡不再出现；
//   ③ 中途**可以取消**（取消 = 整次特殊效果作废，什么都不消耗）；
//   ④ 选满后：素材返回抽牌堆（按怪兽分类判定为额外怪兽的返回额外堆），额外堆那只融合怪兽移出、战斗副本加入手牌。
public sealed record KeMoFusionMaterial(Func<CardModel, bool> Filter, int Count)
{
    /// <summary>按标签筛选（例如：光恶魔 ×2）。</summary>
    public static KeMoFusionMaterial Tag(CardTag tag, int count) => new(c => c.Tags.Contains(tag), count);

    /// <summary>按具体卡筛选（例如：「刻魔·魔镌客」×1）。</summary>
    public static KeMoFusionMaterial Card<T>(int count) where T : CardModel => new(c => c is T, count);
}

// 「刻魔融合怪兽」声明自己的融合素材（顺序就是槽位顺序；一张卡只占一个槽位）
public interface IKeMoFusionMonster
{
    IReadOnlyList<KeMoFusionMaterial> FusionMaterials { get; }
}

// 素材用过之后去哪：Deck = 返回抽牌堆（额外怪兽→额外堆）；Exhaust = 直接消耗掉
public enum KeMoFusionDestination
{
    Deck,
    Exhaust,
}

public static class KeMoFusion
{
    private static List<CardModel> MaterialPool(Player player, Func<Player, List<CardModel>> poolProvider)
        => poolProvider(player).Where(MaterialPayment.CanUseAsMaterial).Distinct().ToList();

    /// <summary>素材池就是某个牌堆（大圣棺＝消耗堆，咏圣＝手牌）。</summary>
    public static Func<Player, List<CardModel>> PilePool(PileType pile)
        => player => pile.GetPile(player)?.Cards.ToList() ?? [];

    /// <summary>额外堆里有没有"素材凑得齐"的刻魔融合怪兽（特殊效果能否发动/发亮的判据）。</summary>
    public static bool HasCandidate(Player player, Func<Player, List<CardModel>> poolProvider)
        => Candidates(player, poolProvider).Count > 0;

    private static List<CardModel> Candidates(Player player, Func<Player, List<CardModel>> poolProvider)
    {
        var pool = MaterialPool(player, poolProvider);
        return ExtraDeckSeries.Cards(player)
            .Where(c => c is IKeMoFusionMonster)
            .Where(c => Feasible(SlotsOf(((IKeMoFusionMonster)c).FusionMaterials), pool))
            .ToList();
    }

    /// <summary>
    /// 融合流程：选融合怪兽（必须选）→ 按它的素材需求逐张选素材（可取消）→ 收尾。
    /// poolProvider = 素材池怎么取（大圣棺＝消耗堆，咏圣＝手牌，怜歌＝手牌攻击卡＋作为装备的刻魔怪兽）；
    /// destination = 素材用完之后去哪（Deck＝返回抽牌堆·额外堆，Exhaust＝消耗掉）。
    /// </summary>
    public static async Task<bool> TryFusion(
        PlayerChoiceContext ctx, Player player, CardModel source,
        LocString pickMonsterPrompt, LocString pickMaterialPrompt,
        Func<Player, List<CardModel>> poolProvider, KeMoFusionDestination destination,
        Func<Task<bool>>? payActivationCost = null)
    {
        if (player.Creature.CombatState == null) return false;

        var candidates = Candidates(player, poolProvider);
        if (candidates.Count == 0) return false;

        // ① 选融合怪兽（必须选，不能跳过）
        // RequireManualConfirmation：候选只有 1 张时游戏会"自动帮玩家选"，这里必须关掉
        var chosenMonster = (await CardSelectCmd.FromSimpleGrid(
            ctx,
            candidates,
            player,
            Prefs(source, pickMonsterPrompt, 1, 1, confirm: true))).FirstOrDefault();
        if (chosenMonster is not IKeMoFusionMonster fusion) return false;

        // ② 逐张选素材
        var mats = fusion.FusionMaterials;
        var remaining = mats.Select(m => m.Count).ToArray();
        var pool = MaterialPool(player, poolProvider);
        var chosen = new List<CardModel>();

        // 注意：不能写成 `chosen.Count < remaining.Sum()`——选掉一张后 chosen 变大、remaining 变小，
        // 两边会同时逼近，第 1 张就判成"选够了"。这里按"还剩几个槽位"判断。
        while (remaining.Sum() > 0)
        {
            var available = pool.Where(c => MaterialPayment.CanUseAsMaterial(c) && !chosen.Contains(c)).ToList();

            // 候选 = "选它之后剩下的槽位仍然凑得齐"的卡；同时记住它占的是哪个槽位
            var allowed = new List<CardModel>();
            var slotFor = new Dictionary<CardModel, int>();
            foreach (var card in available)
            {
                for (int i = 0; i < mats.Count; i++)
                {
                    if (remaining[i] <= 0 || !mats[i].Filter(card)) continue;
                    remaining[i]--;
                    bool ok = Feasible(SlotsOf(mats, remaining), available.Where(x => x != card).ToList());
                    remaining[i]++;
                    if (ok)
                    {
                        allowed.Add(card);
                        slotFor[card] = i;
                        break;
                    }
                }
            }
            if (allowed.Count == 0) return false;

            // 可以取消（0~1 + 手动确认）：取消 → 整次效果作废
            var picked = (await CardSelectCmd.FromSimpleGrid(
                ctx,
                allowed,
                player,
                Prefs(source, pickMaterialPrompt, 0, 1, confirm: true))).FirstOrDefault();
            if (picked == null) return false;

            remaining[slotFor[picked]]--;
            chosen.Add(picked);
        }

        // ③ 选择全部完成后才支付；取消不会消耗来源、素材或发动次数。
        var template = ModelDb.AllCards.FirstOrDefault(c => c.GetType() == chosenMonster.GetType());
        var currentPool = MaterialPool(player, poolProvider);
        if (template == null || chosenMonster.Pile?.Type != Entry.ExtraPile
            || chosen.Any(c => c.Owner != player || !currentPool.Contains(c) || c.HasBeenRemovedFromState))
            return false;
        if (payActivationCost != null && !await payActivationCost()) return false;

        SelectSpecialCardMod.Scripts.Rules.EffectActivation.PaymentStarted();
        // 必须在任何宿主离手前统一卸下已选装备，避免它们被宿主提前消耗。
        if (destination == KeMoFusionDestination.Exhaust)
            foreach (var mat in chosen) KeMoEquip.DetachMaterialEquipment(mat);

        foreach (var mat in chosen)
        {
            if (CombatManager.Instance.IsOverOrEnding) return false;
            if (!MaterialPayment.CanUseAsMaterial(mat)) return false;
            if (destination == KeMoFusionDestination.Exhaust)
            {
                // 消耗素材：如果它正作为装备挂在别人身上，先卸掉那个组件
                if (!await KeMoEquip.ExhaustMaterial(ctx, mat)) return false;
                continue;
            }
            if (CardClassification.IsExtraMonster(mat))
            {
                if (!await ExtraDeckSeries.ReturnExtraMonster(player, mat)) return false;
            }
            else
            {
                await SelectToolCmd.ReturnCardToPile(mat, PileType.Draw);
                if (mat.Pile?.Type != PileType.Draw) return false;
            }
        }

        // 额外堆那只融合怪兽移出 → 战斗副本加入手牌（与珠泪融合同款）
        var combatState = player.Creature.CombatState;
        if (combatState == null || CombatManager.Instance.IsOverOrEnding
            || chosenMonster.Pile?.Type != Entry.ExtraPile) return false;
        await ExtraDeckSeries.Consume(player, chosenMonster);
        if (chosenMonster.Pile?.Type != Entry.ExtraUsedPile) return false;
        var copy = ExtraDeckSeries.CreateSummonedCopy(player, chosenMonster);
        var result = await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Hand, player);
        return result.success;
    }

    // 把「素材需求」摊平成一个个槽位（每个槽位一个筛选条件）
    private static List<Func<CardModel, bool>> SlotsOf(IReadOnlyList<KeMoFusionMaterial> mats, int[]? remaining = null)
    {
        var slots = new List<Func<CardModel, bool>>();
        for (int i = 0; i < mats.Count; i++)
        {
            int n = remaining == null ? mats[i].Count : remaining[i];
            for (int k = 0; k < n; k++) slots.Add(mats[i].Filter);
        }
        return slots;
    }

    // 槽位能不能用 pool 里的卡一一配上（匈牙利增广路；池子很小，够用且判定精确）
    private static bool Feasible(IReadOnlyList<Func<CardModel, bool>> slots, IReadOnlyList<CardModel> pool)
    {
        if (slots.Count == 0) return true;
        if (pool.Count < slots.Count) return false;

        var slotOfCard = new Dictionary<CardModel, int>();

        bool TryAssign(int slot, HashSet<CardModel> visited)
        {
            foreach (var card in pool)
            {
                if (visited.Contains(card) || !slots[slot](card)) continue;
                visited.Add(card);
                if (!slotOfCard.TryGetValue(card, out int other) || TryAssign(other, visited))
                {
                    slotOfCard[card] = slot;
                    return true;
                }
            }
            return false;
        }

        for (int s = 0; s < slots.Count; s++)
        {
            if (!TryAssign(s, [])) return false;
        }
        return true;
    }

    // 与 SelectSpecialBaseCard.CardPrefs 同款：克隆提示再注入卡名
    private static CardSelectorPrefs Prefs(CardModel source, LocString prompt, int min, int max, bool confirm = true)
    {
        var p = new LocString(prompt.LocTable, prompt.LocEntryKey);
        if (source != null) p.Add("CardName", source.Title);
        return new CardSelectorPrefs(p, min, max) { RequireManualConfirmation = confirm };
    }
}
