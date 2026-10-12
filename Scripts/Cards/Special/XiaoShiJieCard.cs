using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 小世界：消耗1张手牌A。之后，消耗抽牌堆的1张卡B，
// 若B的费用·类型·稀罕度与A只有一个相同，
// 则从随机角色卡池「检索」1张费用·类型·稀罕度只有一个是和B相同的卡。
public class XiaoShiJieCard : SelectSpecialBaseCard
{
    private static LocString ExhaustPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_EXHAUST");

    public XiaoShiJieCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // ① 消耗1张手牌A（强制选1张，选不了则结束）
        var handCards = PileType.Hand.GetPile(Owner).Cards.ToList();
        if (handCards.Count == 0) return;

        CardModel? selectedA = (await CardSelectCmd.FromHand(prefs: CardPrefs(this, ExhaustPrompt, 1, 1), context: choiceContext, player: Owner, filter: null, source: this)).FirstOrDefault();

        if (selectedA == null) return;
        var cardA = selectedA;
        await CardCmd.Exhaust(choiceContext, cardA);

        // ② 消耗抽牌堆的1张卡B（强制选1张，抽牌堆为空则停止处理）
        var drawPile = PileType.Draw.GetPile(Owner);
        if (drawPile.Cards.Count == 0) return;
        var selectedB = (await CardSelectCmd.FromSimpleGrid(
            choiceContext, drawPile.Cards.ToList(), Owner, CardPrefs(this, SelectionScreenPrompt, 1, 1))).ToList();
        if (selectedB.Count == 0) return;
        var cardB = selectedB[0];
        await CardCmd.Exhaust(choiceContext, cardB);

        // ③ 判断 A 与 B 的费用·类型·稀有度是否恰好1个相同
        if (CountSameProperties(cardA, cardB) != 1) return;

        // ④ 从随机角色卡池检索1张与 B 恰好1个属性相同的卡
        List<CardPoolModel> pools = Owner.UnlockState.CharacterCardPools.ToList();
        if (pools.Count == 0) return;
        var randomPool = Owner.RunState.Rng.Niche.NextItem(pools)!;

        await SelectToolCmd.RetrieverCard(
            choiceContext,
            Owner,
            c => CountSameProperties(c, cardB) == 1,
            p => p.Title == randomPool.Title,
            1, source: this);
    }

    // 统计两张卡在 费用 / 类型 / 稀有度 上相同的个数
    private static int CountSameProperties(CardModel a, CardModel b)
    {
        int same = 0;
        if (a.EnergyCost.GetWithModifiers(CostModifiers.None) == b.EnergyCost.GetWithModifiers(CostModifiers.None)) same++;
        if (a.Type == b.Type) same++;
        if (a.Rarity == b.Rarity) same++;
        return same;
    }
}