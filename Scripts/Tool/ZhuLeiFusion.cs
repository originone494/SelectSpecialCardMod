using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Cards.Special;

namespace SelectSpecialCardMod.Scripts.Tool;

// 珠泪融合：由触发送墓的珠泪卡调用
// 流程：根据弃牌堆与场上堆内容展示候选融合怪兽（水仙/卡雷多哈特/露莎卡人鱼）→ 玩家选1 → 按选择选素材 → 触发卡+素材返回抽牌堆 → 融合怪兽入手牌
// 说明：触发送墓的卡在弃牌堆中（被弃后触发送墓），作为素材之一被筛出返回抽牌堆
public static class ZhuLeiFusion
{
    // 受「同名卡一场战斗只能触发一次」限制的卡 = 带「珠泪融合」机制的卡（塞壬 / 小美人鱼 / 梅洛）。
    // 判据走标签（卡面已不注入「珠泪融合」关键字）。
    // 「一场战斗一次」的记录统一走 PerCombatOnce（按战斗 ID 自动过期），scope 用 "ZhuLeiRongHe"，
    // 与同一张卡身上其它效果（例如雷诺哈特送墓的 "SongMu"）的同名限制互不干扰。
    private static bool IsRongHeLimited(CardModel card) => ZhuLeiFilter.HasFusionLimit(card);

    private static string OnceKey(CardModel card) => "ZhuLeiRongHe:" + card.Id.Entry;

    // 本战斗这张卡名的珠泪融合是否已成功触发过（供卡自身的送墓判断「触发过珠泪融合的情况」用）
    public static bool HasFusedThisCombat(CardModel card) => IsRongHeLimited(card) && PerCombatOnce.IsUsed(OnceKey(card));

    private static bool HasActivatedThisCombat(Player player, CardModel card) => HasFusedThisCombat(card);

    private static void MarkActivated(Player player, CardModel card)
    {
        if (!IsRongHeLimited(card)) return;
        PerCombatOnce.Mark(OnceKey(card));
    }

    public static async Task TryFusion(PlayerChoiceContext choiceContext, Player player, CardModel triggerCard)
    {
        var combatState = player.Creature.CombatState;
        if (combatState == null) return;

        // 「同名卡一场战斗只能触发一次」：本战斗这个卡名的珠泪融合已经发动过 → 这次什么都不做
        //（卡本身照常留在弃牌堆里，只是不再弹融合界面）
        if (HasActivatedThisCombat(player, triggerCard)) return;

        // 素材池 = 手牌 + 弃牌堆 + 场上（Play）。
        // Play 堆必须纳入：触发送墓的卡常常是"某张珠泪怪兽结算时顺手丢掉的另一张"，
        // 而那张正在结算的卡（例如打出雷诺哈特 → 检索并丢弃塞壬 → 塞壬触发送墓）此刻还在 Play 堆里、
        // 并没有落进弃牌堆。若不算它，场上只有触发卡自己一张素材，任何融合都凑不齐，界面根本不会弹。
        // 安全性：CardModel.OnPlayWrapper 只在卡仍位于 Play 堆时才把它送去结果堆，
        // 所以被当作素材送回抽牌堆的那张卡不会被"打回"弃牌堆；且所有珠泪怪兽都是攻击牌，
        // 不存在能力牌常驻 Play 堆变成永久素材的问题。
        var availableCards = PileType.Hand.GetPile(player).Cards
            .Concat(PileType.Discard.GetPile(player).Cards)
            .Concat(PileType.Play.GetPile(player).Cards)
            .Distinct()
            .ToList();
        if (availableCards.Count == 0) return;

        // 触发卡必然是「珠泪怪兽」，它自己也占 1 张素材位。
        // 素材口径按融合配方的文案走：文案写「珠泪怪兽」，这里就是 IsMonster（含融合怪兽）。
        var monsters = availableCards.Where(ZhuLeiFilter.IsMonster).ToList();
        var otherMonsters = monsters.Where(c => c != triggerCard).ToList();
        int renoCount = availableCards.Count(c => c is ZhuLeiLeiNuoHaTeCard);
        bool hasShuiXian = availableCards.Any(c => c is ZhuLeiShuiXianCard);

        // 候选条件必须与实际素材判据一致，否则会出现"界面里选了却没有反应"（素材不足 → 静默 return）
        var candidates = new List<CardModel>();
        // 候选 = 额外堆里**已经存在**的融合怪兽（不再凭空造卡）
        var extraDeck = ExtraDeckSeries.Cards(player);
        // 水仙：2张「珠泪怪兽」= 触发卡 + 1张其它怪兽
        if (otherMonsters.Count >= 1
            && extraDeck.FirstOrDefault(c => c is ZhuLeiShuiXianCard) is { } shuiXian)
            candidates.Add(shuiXian);
        // 卡雷多哈特：1张「珠泪·雷诺哈特」+ 2张「珠泪怪兽」——三者必须是三张不同的卡，
        // 雷诺哈特本身不能顶替那 2 张「珠泪怪兽」。
        // 触发卡已经占掉一个素材位：
        //   触发卡是雷诺哈特 → 它就是那"1张雷诺哈特"，还需 2 张其它「珠泪怪兽」
        //   触发卡是其它珠泪怪兽 → 它算那 2 张怪兽之一，还需 1 张雷诺哈特 + 1 张其它「珠泪怪兽」
        bool hasRenoForFusion = triggerCard is ZhuLeiLeiNuoHaTeCard || renoCount >= 1;
        if (hasRenoForFusion && otherMonsters.Count >= 2
            && extraDeck.FirstOrDefault(c => c is ZhuLeiKaLeiDuoHaTeCard) is { } kaLeiDuoHaTe)
            candidates.Add(kaLeiDuoHaTe);
        // 露莎卡人鱼：1张「珠泪·水仙人鱼」+ 触发卡（水仙不可能是触发卡，其送墓是送墓5张）
        if (hasShuiXian
            && extraDeck.FirstOrDefault(c => c is ZhuLeiLuShaKaCard) is { } luShaKa)
            candidates.Add(luShaKa);

        if (candidates.Count == 0) return;

        // 玩家从候选融合怪兽中选1张（canSkip: true 允许放弃融合）
        CardModel? picked = await CardSelectCmd.FromChooseACardScreen(choiceContext, candidates, player, canSkip: true);
        if (picked == null) return;

        // 素材 = 触发送墓的卡（必含，已在弃牌堆里） + 按选择从手牌·弃牌堆选的卡
        var materials = new List<CardModel> { triggerCard };

        if (picked is ZhuLeiShuiXianCard)
        {
            // 选水仙：再从弃牌堆选1张「珠泪怪兽卡」
            var m = await SelectOne(choiceContext, player, otherMonsters, ShuiXianMaterialPrompt, triggerCard);
            if (m == null)
            {
                Log.Warn("[SelectSpecialCardMod] 珠泪融合：水仙素材为空，取消本次融合");
                return;
            }
            materials.Add(m);
        }
        else if (picked is ZhuLeiKaLeiDuoHaTeCard)
        {
            if (triggerCard is ZhuLeiLeiNuoHaTeCard)
            {
                // 触发卡本身就是「雷诺哈特」，已满足"1张雷诺哈特" → 再选 2 张其它「珠泪怪兽」
                var first = await SelectOne(choiceContext, player, otherMonsters, KaLeiDuoHaTeMonsterPrompt, triggerCard);
                if (first == null)
                {
                    Log.Warn("[SelectSpecialCardMod] 珠泪融合：卡雷多哈特缺少「珠泪怪兽」素材，取消本次融合");
                    return;
                }
                materials.Add(first);

                var rest = otherMonsters.Where(c => c != first).ToList();
                var second = await SelectOne(choiceContext, player, rest, KaLeiDuoHaTeMonsterPrompt, triggerCard);
                if (second == null)
                {
                    Log.Warn("[SelectSpecialCardMod] 珠泪融合：卡雷多哈特缺少第2张「珠泪怪兽」素材，取消本次融合");
                    return;
                }
                materials.Add(second);
            }
            else
            {
                // 触发卡是其它珠泪怪兽，它已算那 2 张「珠泪怪兽」之一 → 再选 1 张「雷诺哈特」+ 1 张其它「珠泪怪兽」
                var leiNuoOptions = availableCards.OfType<ZhuLeiLeiNuoHaTeCard>().Cast<CardModel>()
                    .Where(c => c != triggerCard).ToList();
                var m1 = await SelectOne(choiceContext, player, leiNuoOptions, KaLeiDuoHaTeRenoPrompt, triggerCard);
                if (m1 == null)
                {
                    Log.Warn("[SelectSpecialCardMod] 珠泪融合：卡雷多哈特缺少「雷诺哈特」素材，取消本次融合");
                    return;
                }
                materials.Add(m1);

                var monsterOptions = monsters.Where(c => c != triggerCard && c != m1).ToList();
                var m2 = await SelectOne(choiceContext, player, monsterOptions, KaLeiDuoHaTeMonsterPrompt, triggerCard);
                if (m2 == null)
                {
                    Log.Warn("[SelectSpecialCardMod] 珠泪融合：卡雷多哈特缺少「珠泪怪兽」素材，取消本次融合");
                    return;
                }
                materials.Add(m2);
            }
        }
        else if (picked is ZhuLeiLuShaKaCard)
        {
            // 选露莎卡人鱼：再从弃牌堆选1张「珠泪·水仙」
            var shuiXianOptions = availableCards.OfType<ZhuLeiShuiXianCard>().Cast<CardModel>().ToList();
            var m = await SelectOne(choiceContext, player, shuiXianOptions, LuShaKaMaterialPrompt, triggerCard);
            if (m == null)
            {
                Log.Warn("[SelectSpecialCardMod] 珠泪融合：露莎卡人鱼缺少「水仙人鱼」素材，取消本次融合");
                return;
            }
            materials.Add(m);
        }

        // 触发卡 + 素材卡返回抽牌堆
        foreach (var mat in materials.Distinct())
        {
            await CardPileCmd.Add(mat, PileType.Draw);
        }

        // 战斗内消耗：把额外堆里那张副本移出（进「额外堆·已用」，战斗结束时按规则补齐回来），
        // 产物则按模板造一张**战斗副本**加入手牌（战斗结束消失，不会进卡组）。
        // （CreateCard 只接受模板：直接传额外堆里的可变实例会抛 MutableModelException）
        await ExtraDeckSeries.Consume(player, picked);
        var template = ModelDb.AllCards.FirstOrDefault(c => c.GetType() == picked.GetType());
        if (template != null)
        {
            var copy = combatState.CreateCard(template, player);
            await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Hand, player);
        }

        // 到这里才算真正"发动"成功（玩家从界面选了融合怪兽、素材也齐了），
        // 记下"本战斗这张卡名的珠泪融合已发动"；中途放弃/素材不足取消的都不消耗这次机会。
        MarkActivated(player, triggerCard);
    }

    private static async Task<CardModel?> SelectOne(PlayerChoiceContext choiceContext, Player player, List<CardModel> options, LocString prompt, CardModel trigger)
    {
        if (options.Count == 0) return null;
        // 统一用简单网格选素材，并显示对应的融合提示文字
        //（克隆 LocString 再注入卡名，避免向共享静态实例反复追加变量）
        var p = new LocString(prompt.LocTable, prompt.LocEntryKey);
        p.Add("CardName", trigger.Title);
        var prefs = new CardSelectorPrefs(p, 1, 1);
        return (await CardSelectCmd.FromSimpleGrid(choiceContext, options, player, prefs)).FirstOrDefault();
    }

    private static LocString ShuiXianMaterialPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_TO_FUSION_SHUI_XIAN");
    private static LocString KaLeiDuoHaTeRenoPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_TO_FUSION_KA_LEI_DUO_HA_TE_RENO");
    private static LocString KaLeiDuoHaTeMonsterPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_TO_FUSION_KA_LEI_DUO_HA_TE_MONSTER");
    private static LocString LuShaKaMaterialPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_TO_FUSION_LU_SHA_KA");
}
