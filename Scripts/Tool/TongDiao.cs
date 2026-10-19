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
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Rules;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「同调」：规则文本见 card_keywords.json 的 SELECT_SPECIAL_CARD_MOD_KEYWORD_TONG_DIAO。
// 分支一：触发卡为非「调整」→ 选手牌 1 张「调整」卡；调整 4 星 → 升龙，7 星 → 超龙。
// 分支二：触发卡为「调整」→ 选手牌 1 张拥有星级的卡；3 星 → 升龙。
// 节奏：弹选择界面之前先等一刀（等前面的打出/伤害动画落地）。
// 产物判定：**先算产物再动卡**——星级组合不成立时什么都不处理，不移动任何卡。
// 关键点：触发卡与所选卡都走 CardCmd.Exhaust 进消耗堆（不进弃牌堆），不经过 AfterCardDiscarded。
public static class TongDiao
{
    // 分支一提示：从手牌选 1 张「调整」卡
    private static LocString TunerChoicePrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_TONG_DIAO_CHOICE_TUNER");

    // 分支二提示：从手牌选 1 张拥有星级的卡
    private static LocString StarChoicePrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_TONG_DIAO_CHOICE_STAR");

    private static readonly CardTag[] StarTags =
    [
        SelectTags.Xing3,
        SelectTags.Xing4,
        SelectTags.Xing7,
        SelectTags.Xing10,
    ];

    // 「同调」前的等待：等前面的打出/伤害动画走完再弹选择界面。
    private const float WaitFastSeconds = 0.35f;
    private const float WaitStandardSeconds = 0.8f;

    // 等待只是节奏，绝不能因为它让「同调」本身失效，所以整段包起来。
    private static async Task Pace()
    {
        try
        {
            await Cmd.CustomScaledWait(WaitFastSeconds, WaitStandardSeconds);
        }
        catch (Exception e)
        {
            MegaCrit.Sts2.Core.Logging.Log.Warn($"[SelectSpecialCardMod] 同调等待被跳过：{e.GetType().Name}");
        }
    }

    // 「同调」此时能不能发动：纯判据，不弹选牌、不消耗任何卡。
    // 特殊效果（右键）用它决定"能不能点"——没有可用产物时卡片不发光、右键也不响应。
    public static bool CanTongDiao(Player player, CardModel source)
    {
        var combat = player.Creature.CombatState;
        if (!IsActiveCombat(player, combat) || !IsPayable(player, source, combat, allowPlay: true)) return false;

        bool sourceIsTuner = source.Tags.Contains(SelectTags.TiaoZheng);
        var hand = PileType.Hand.GetPile(player);
        return hand.Cards.Any(c => c != source && IsPayable(player, c, combat)
            && MatchesStar(sourceIsTuner, c) && HasProduct(player, sourceIsTuner, c));
    }

    public static async Task<bool> TryTongDiao(PlayerChoiceContext choiceContext, Player player, CardModel source, bool pace = true)
    {
        var combat = player.Creature.CombatState;
        var combatId = CombatManager.Instance.CurrentCombatId;
        var hand = PileType.Hand.GetPile(player);
        var sourcePile = source.Pile;
        bool IsCurrent() => IsActiveCombat(player, combat) && CombatManager.Instance.CurrentCombatId == combatId;
        bool CanPay(CardModel card) => IsPayable(player, card, combat, allowPlay: card == source)
            && (card != source || ReferenceEquals(card.Pile, sourcePile));
        if (!IsCurrent() || !CanPay(source) || !CanTongDiao(player, source)) return false;

        // 等前面的操作走完（打出动画 / 伤害结算），再进入同调的选择。
        // 特殊效果的统一演出已经播过，调用方会传 pace:false 跳过这段等待。
        if (pace)
        {
            await Pace();
            if (!IsCurrent() || !CanPay(source)) return false;
        }

        bool sourceIsTuner = source.Tags.Contains(SelectTags.TiaoZheng);

        // 候选 = 手牌中**除自身外**的「调整」卡（分支一）/ 拥有星级的卡（分支二），
        // 并且必须是"星级组合成立 + 产物已经在额外堆里"的卡。
        // 来源可在出牌区；搭档只从手牌选择，不能把来源再次当作自己的搭档。
        // 产物不在额外堆时该卡不会出现在候选里——不会出现"选完才发现没有对应同调怪兽"。
        Func<CardModel, bool> filter = c => c != source && CanPay(c)
            && MatchesStar(sourceIsTuner, c) && HasProduct(player, sourceIsTuner, c);
        if (!hand.Cards.Any(filter)) return false;

        // 「可以进行一次」：可选，取消即不同调
        var prefs = new CardSelectorPrefs(sourceIsTuner ? StarChoicePrompt : TunerChoicePrompt, 1, 1)
        { Cancelable = true, RequireManualConfirmation = true };
        CardModel? chosen = (await CardSelectCmd.FromHand(
            prefs: prefs,
            context: choiceContext,
            player: player,
            filter: filter,
            // 由 AfterCardPlayed 触发时，来源的 ExecutionFinished 已经发生。
            source: null!)).FirstOrDefault();
        if (!IsCurrent() || chosen == null || !filter(chosen) || !CanPay(source)) return false;

        // 先算产物：星级组合不成立时**什么都不处理**——既不同调，也不消耗任何卡。
        CardModel? dragon = ResolveDragon(sourceIsTuner, chosen);
        if (dragon == null) return false;

        // 同调产物必须已经在额外堆里；没有就不发动，素材也不消耗（和"先算产物再动卡"同一原则）
        CardModel? fromExtra = ExtraDeckSeries.FindByType(player, dragon.GetType());
        if (fromExtra == null)
        {
            MegaCrit.Sts2.Core.Logging.Log.Warn(
                $"[SelectSpecialCardMod] 同调：额外堆里没有「{dragon.Title}」，本次不同调");
            return false;
        }

        bool IsExtraAvailable() => fromExtra.Owner == player && !fromExtra.HasBeenRemovedFromState
            && ReferenceEquals(fromExtra.Pile, Entry.ExtraPile.GetPile(player));

        // 支付开始即锁定次数：由特殊效果发动时「同名卡一回合一次」立刻提交，
        // 之后即使支付失败也不再允许重试。自动触发的红中/绿发没有该作用域，这里是空操作。
        EffectActivation.PaymentStarted();

        // 消耗会触发连锁；逐张重查资格，并确认实际支付成功后才继续。
        foreach (var material in new[] { source, chosen })
        {
            if (!IsCurrent() || !IsExtraAvailable() || !CanPay(material)) return false;
            var paid = await CardCmd.Exhaust(choiceContext, material);
            if (paid?.success != true || !IsCurrent()) return false;
        }

        // 战斗内消耗：把额外堆里那张副本移出（进「额外堆·已用」，战斗结束时按规则补齐回来），
        // 产物则按模板造一张**战斗副本**加入手牌（战斗结束消失，不会进卡组）。
        if (!IsCurrent() || !IsExtraAvailable()) return false;
        await ExtraDeckSeries.Consume(player, fromExtra);
        if (!IsCurrent() || fromExtra.HasBeenRemovedFromState
            || fromExtra.Owner != player || fromExtra.Pile?.Type != Entry.ExtraUsedPile) return false;
        var copy = ExtraDeckSeries.CreateSummonedCopy(player, fromExtra);
        await CardPileCmd.Add(copy, PileType.Hand);
        return true;
    }

    // 同一场战斗仍然有效：未结束、玩家存活。
    private static bool IsActiveCombat(Player player, ICombatState? combat)
        => combat != null && ReferenceEquals(player.Creature.CombatState, combat)
           && !CombatManager.Instance.IsOverOrEnding && player.Creature.IsAlive;

    // 搭档必须在手牌；仅发动同调的来源卡允许留在出牌区，不能借此使用其他出牌区的卡。
    private static bool IsPayable(Player player, CardModel card, ICombatState? combat, bool allowPlay = false)
        => card.Owner == player && ReferenceEquals(card.CombatState, combat)
           && (ReferenceEquals(card.Pile, PileType.Hand.GetPile(player))
               || allowPlay && ReferenceEquals(card.Pile, PileType.Play.GetPile(player)))
           && MaterialPayment.CanUseAsMaterial(card);

    // 分支一（触发卡非「调整」）：搭档必须是「调整」卡。
    // 分支二（触发卡是「调整」）：搭档必须是拥有星级的卡。
    private static bool MatchesStar(bool sourceIsTuner, CardModel card)
        => sourceIsTuner ? StarTags.Any(card.Tags.Contains) : card.Tags.Contains(SelectTags.TiaoZheng);

    // 星级组合算出来的产物必须已经在额外堆里，否则这次同调无解。
    private static bool HasProduct(Player player, bool sourceIsTuner, CardModel material)
    {
        var product = ResolveDragon(sourceIsTuner, material);
        var extra = product == null ? null : ExtraDeckSeries.FindByType(player, product.GetType());
        return extra != null && extra.Owner == player && !extra.HasBeenRemovedFromState
            && ReferenceEquals(extra.Pile, Entry.ExtraPile.GetPile(player));
    }

    // 星级组合 → 同调产物；组合不成立返回 null（不做任何副作用）
    private static CardModel? ResolveDragon(bool sourceIsTuner, CardModel chosen) => (sourceIsTuner, chosen) switch
    {
        { sourceIsTuner: true } when chosen.Tags.Contains(SelectTags.Xing3)
            => ModelDb.Card<CanHuanShengLongShuangChaTianLongCard>(),
        { sourceIsTuner: false } when chosen.Tags.Contains(SelectTags.Xing4)
            => ModelDb.Card<CanHuanShengLongShuangChaTianLongCard>(),
        { sourceIsTuner: false } when chosen.Tags.Contains(SelectTags.Xing7)
            => ModelDb.Card<CanHuanChaoLongSanJiTianLongCard>(),
        _ => null,
    };
}
