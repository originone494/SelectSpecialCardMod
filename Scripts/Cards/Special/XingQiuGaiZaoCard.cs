using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 星球改造：「检索」1张能力牌。
public class XingQiuGaiZaoCard : SelectSpecialBaseCard
{
    public XingQiuGaiZaoCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 「检索」1张能力牌；poolFilter 传 null 即不限卡池，候选为 ModelDb.AllCards（含所有职业的卡池）
        await SelectToolCmd.RetrieverCard(
            choiceContext,
            Owner,
            c => c.Type == CardType.Power,
            null,
            1, source: this);
    }
}