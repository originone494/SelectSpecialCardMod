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

// 现世断离：消耗1张手牌，从消耗堆选择1张卡加入手牌。
public class XianShiDuanLiCard : SelectSpecialBaseCard
{
    public XianShiDuanLiCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // ① 消耗1张手牌（强制选择，手牌为空或未选择则停止）
        if (PileType.Hand.GetPile(Owner).Cards.Count == 0) return;

        var exhausted = (await CardSelectCmd.FromHand(
            prefs: CardPrefs(this, SelectionScreenPrompt, 1, 1),
            context: choiceContext,
            player: Owner,
            filter: null,
            source: this)).ToList();
        if (exhausted.Count == 0) return;

        await CardCmd.Exhaust(choiceContext, exhausted[0]);

        // ② 从消耗堆选择1张卡加入手牌（消耗堆为空则停止）
        var exhaustPile = PileType.Exhaust.GetPile(Owner);
        if (exhaustPile.Cards.Count == 0) return;

        var retrieved = (await CardSelectCmd.FromSimpleGrid(
            choiceContext, exhaustPile.Cards.ToList(), Owner, CardPrefs(this, SelectionScreenPrompt, 1, 1))).ToList();
        if (retrieved.Count == 0) return;

        await CardPileCmd.Add(retrieved[0], PileType.Hand);
    }
}
