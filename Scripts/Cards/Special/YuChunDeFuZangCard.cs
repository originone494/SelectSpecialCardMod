using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「愚蠢的副葬」（Foolish Burial Goods）：「检索」并丢弃 1 张技能牌。
// 与愚蠢的埋葬同款，复用 RetrieverCard 的 isDiscard 形态，限定技能牌。
public class YuChunDeFuZangCard : SelectSpecialBaseCard
{
    public YuChunDeFuZangCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 「检索」（全卡池）并丢弃 1 张技能牌
        await SelectToolCmd.RetrieverCard(
            choiceContext,
            Owner,
            c => c.Type == CardType.Skill,
            null,
            1,
            isDiscard: true, source: this);
    }
}