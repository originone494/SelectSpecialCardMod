using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 天之月（额外怪兽）
//   打出：造成12点伤害
//   召唤条件：消耗攻击卡2张（手牌有2张攻击卡时，右键额外堆可以手动召唤）
public class TianZhiYueCard : SelectSpecialBaseCard, IExtraDeckSummonCard
{
    public TianZhiYueCard() : base(0, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(12m, ValueProp.Move),
    };

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.GuangEMo,
        SelectTags.Link2,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.GuangEMo),
        HoverTipFactory.FromKeyword(SelectKeywords.Link2),
    ];

    // 召唤条件：手牌的攻击卡至少 2 张
    private static LocString SummonCostPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_TIAN_ZHI_YUE_SUMMON_COST");

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await CreatureCmd.Damage(choiceContext, cardPlay.Target, DynamicVars.Damage.BaseValue, ValueProp.Move, Owner.Creature, this, cardPlay);
    }

    public bool CanSummonFromExtraDeck(Player player)
        => PileType.Hand.GetPile(player).Cards.Count(IsMaterial) >= 2;

    private static bool IsMaterial(CardModel card)
        => card.Type == CardType.Attack && MaterialPayment.CanUseAsMaterial(card);

    public async Task<bool> PaySummonCost(PlayerChoiceContext ctx, Player player)
    {
        // 从手牌选择要消耗的攻击卡：范围 (0,2)——即使手牌的攻击卡正好 2 张，也必须玩家自己选。
        // 用 RequireManualConfirmation（与「时机」「常世断离」同一套写法）：界面会出现确认按钮，
        // 不选够 2 张点确认即等于放弃；只有选够 2 张才会消耗并完成召唤。
        var prompt = new LocString(SummonCostPrompt.LocTable, SummonCostPrompt.LocEntryKey);
        prompt.Add("CardName", Title);
        var chosen = (await CardSelectCmd.FromHand(
            prefs: new CardSelectorPrefs(prompt, 0, 2) { RequireManualConfirmation = true },
            context: ctx,
            player: player,
            filter: IsMaterial,
            // 手动召唤不经过 CardModel 的打出流程，不会触发 ExecutionFinished。
            // 原版手牌选择支持空来源：确认后立即恢复选中卡的手牌节点，
            // 避免未选够素材而返回时，卡牌仍留在屏幕中央等待结束通知。
            source: null!)).ToList();
        if (chosen.Count < 2) return false;

        return await MaterialPayment.Exhaust(ctx, player, chosen, 2, PileType.Hand);
    }
}
