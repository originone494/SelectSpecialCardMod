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

// 魂之解放：消耗弃牌堆最多5张卡（玩家选择，可选0张）。
public class HunZhiJieFangCard : SelectSpecialBaseCard
{
    public HunZhiJieFangCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 消耗弃牌堆最多5张卡（玩家选择，可选0张）
        var discardPile = PileType.Discard.GetPile(Owner);
        if (discardPile.Cards.Count == 0) return;

        var selected = (await CardSelectCmd.FromSimpleGrid(
            choiceContext,
            discardPile.Cards.ToList(),
            Owner,
            CardPrefs(this, SelectionScreenPrompt, 0, 5))).ToList();

        foreach (var card in selected)
        {
            await CardCmd.Exhaust(choiceContext, card);
        }
    }
}
