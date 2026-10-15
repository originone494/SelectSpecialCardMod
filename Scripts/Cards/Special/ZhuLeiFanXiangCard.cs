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
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 珠泪·反响
//   打出：是 →「检索」1张「珠泪下级怪兽」卡；否 → 从弃牌堆选择1张「珠泪怪兽」加入手牌；
//         之后，丢弃1张与其费用相同的「珠泪怪兽」卡
//   送墓：从消耗堆将1张「珠泪陷阱」卡加入手牌
public class ZhuLeiFanXiangCard : SelectSpecialBaseCard, ISongMuCard
{
    public ZhuLeiFanXiangCard() : base(1, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ZhuLei,
        SelectTags.SongMu,
        SelectTags.ZhuLeiMoFa,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiXiaJiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiXianJing),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiMoFa),
    ];

    // 是/否：是否「检索」1张「珠泪下级怪兽」卡？
    private static LocString RetrieveChoicePrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_ZHU_LEI_FAN_XIANG_RETRIEVE");

    // 否 → 从弃牌堆选1张「珠泪怪兽」
    private static LocString FromDiscardPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_ZHU_LEI_FAN_XIANG_FROM_DISCARD");

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // ① 是 →「检索」1张「珠泪下级怪兽」卡；否 → 从弃牌堆选择1张「珠泪怪兽」加入手牌
        var discardPile = PileType.Discard.GetPile(Owner);
        // 没有可回收的珠泪怪兽时直接检索，不询问无法执行的回收分支。
        bool retrieve = !discardPile.Cards.Any(ZhuLeiFilter.IsMonster)
            || await SelectToolCmd.AskYesNo(choiceContext, Owner, RetrieveChoicePrompt, source: this);

        CardModel? added;
        if (retrieve)
        {
            added = (await SelectToolCmd.RetrieverCard(
                choiceContext,
                Owner,
                ZhuLeiFilter.IsLowerMonster,
                p => p is SelectSpecialCardPool,
                1, source: this)).FirstOrDefault();
        }
        else
        {
            added = discardPile.Cards.Any(ZhuLeiFilter.IsMonster)
                ? (await CardSelectCmd.FromCombatPile(
                    choiceContext,
                    discardPile,
                    Owner,
                    CardPrefs(this, FromDiscardPrompt, 1, 1),
                    filter: ZhuLeiFilter.IsMonster)).FirstOrDefault()
                : null;
            if (added != null) await CardPileCmd.Add(added, PileType.Hand);
        }

        if (added?.Pile?.Type != PileType.Hand) return;

        // ② 之后：丢弃1张与其费用相同的「珠泪怪兽」卡（允许就是刚加入手牌的这一张）
        int cost = added.EnergyCost.GetWithModifiers(CostModifiers.None);
        bool hasSameCostMonster = PileType.Hand.GetPile(Owner).Cards
            .Any(c => ZhuLeiFilter.IsMonster(c) && c.EnergyCost.GetWithModifiers(CostModifiers.None) == cost);
        if (!hasSameCostMonster) return;

        CardModel? toDiscard = (await CardSelectCmd.FromHandForDiscard(
            prefs: CardPrefs(this, DiscardNamedPrompt, 1, 1),
            context: choiceContext,
            player: Owner,
            filter: c => ZhuLeiFilter.IsMonster(c) && c.EnergyCost.GetWithModifiers(CostModifiers.None) == cost,
            source: this)).FirstOrDefault();

        if (toDiscard == null) return;

        await CardCmd.Discard(choiceContext, toDiscard);
    }

    // 送墓：从消耗堆将1张「珠泪陷阱」卡加入手牌
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        var exhaustPile = PileType.Exhaust.GetPile(player);
        if (!exhaustPile.Cards.Any(ZhuLeiFilter.IsTrap)) return;

        var picked = (await CardSelectCmd.FromCombatPile(
            ctx,
            exhaustPile,
            player,
            CardPrefs(this, SelectionScreenPrompt, 1, 1),
            filter: ZhuLeiFilter.IsTrap)).FirstOrDefault();

        if (picked != null)
        {
            await CardPileCmd.Add(picked, PileType.Hand);
        }
    }
}
