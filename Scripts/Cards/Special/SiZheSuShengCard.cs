using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 死者苏生：从消耗堆将1张攻击卡加入手牌，那张卡在这个回合可以免费打出。
public class SiZheSuShengCard : SelectSpecialBaseCard
{
    public SiZheSuShengCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;

        // 从消耗堆将1张攻击卡加入手牌
        var exhaustPile = PileType.Exhaust.GetPile(Owner);
        if (exhaustPile == null || exhaustPile.Cards.Count == 0) return;

        var attackCards = exhaustPile.Cards.Where(c => c.Type == CardType.Attack).ToList();
        if (attackCards.Count == 0) return;

        var picked = (await CardSelectCmd.FromSimpleGrid(
            choiceContext,
            attackCards,
            Owner,
            CardPrefs(this, SelectionScreenPrompt, 1, 1))).FirstOrDefault();
        if (picked == null) return;

        await CardPileCmd.Add(picked, PileType.Hand);

        // 那张卡在这个回合可以免费打出
        picked.EnergyCost.SetThisTurn(0);
    }
}
