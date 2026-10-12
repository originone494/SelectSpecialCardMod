using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 天使的施舍：抽3张牌，之后丢弃2张手牌。
public class TianShiDeShiSheCard : SelectSpecialBaseCard
{
    public TianShiDeShiSheCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 抽3张牌
        await CardPileCmd.Draw(choiceContext, 3, Owner);

        // 弃2张牌（玩家选择）
        var selected = (await CardSelectCmd.FromHand(
            prefs: CardPrefs(this, SelectionScreenPrompt, 2, 2),
            context: choiceContext,
            player: Owner,
            filter: null,
            source: this)).ToList();

        if (selected.Count > 0)
            await CardCmd.Discard(choiceContext, selected);
    }
}