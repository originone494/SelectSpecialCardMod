using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「左腕的代价」（Left Arm Offering）：手牌数在 2 及以上时能够打出。
// 消耗所有手牌，「检索」1 张技能牌。
public class ZuoWanDeDaiJiaCard : SelectSpecialBaseCard
{
    // 打出后本卡已离开手牌，因此"手牌数≥2"意味着至少还有 1 张其他手牌可供消耗
    protected override bool IsPlayable
        => Owner != null
           && CombatManager.Instance.IsInProgress
           && PileType.Hand.GetPile(Owner).Cards.Count >= 2;

    public ZuoWanDeDaiJiaCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 消耗所有手牌（先消耗，后检索，保证检索到的技能牌不会被消耗掉）
        var handCards = PileType.Hand.GetPile(Owner).Cards.ToList();
        foreach (var card in handCards)
        {
            await CardCmd.Exhaust(choiceContext, card);
        }

        // 「检索」1 张技能牌加入手牌
        await SelectToolCmd.RetrieverCard(
            choiceContext,
            Owner,
            c => c.Type == CardType.Skill,
            null,
            1, source: this);
    }
}