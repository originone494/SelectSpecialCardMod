using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「强欲而金满之壶」：向除外堆加入6张随机「额外怪兽」卡，抽2张牌。
public class QiangYuBingJinManZhiHuCard : SelectSpecialBaseCard
{
    private const int _exileCards = 6;
    private const int _drawCount = 2;

    public QiangYuBingJinManZhiHuCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 向除外堆加入6张随机「额外怪兽」卡
        await SelectToolCmd.AddRandomCardsToPile(
            Owner,
            Entry.ChuWaiPile,
            _exileCards,
            filter: c => c.Tags.Contains(SelectTags.ExtraMonster));

        // 抽2张牌
        await CardPileCmd.Draw(choiceContext, _drawCount, Owner);
    }
}
