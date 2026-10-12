using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「强欲而贪欲之壶」：向除外堆加入10张随机卡，抽2张牌。
public class QiangYuErTanYuZhiHuCard : SelectSpecialBaseCard
{
    private const int _randomCards = 10;
    private const int _drawCount = 2;

    public QiangYuErTanYuZhiHuCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 向除外堆加入10张随机卡（不限定卡池/标签）
        await SelectToolCmd.AddRandomCardsToPile(Owner, Entry.ChuWaiPile, _randomCards);

        // 抽2张牌
        await CardPileCmd.Draw(choiceContext, _drawCount, Owner);
    }
}