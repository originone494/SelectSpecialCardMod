using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「强欲而谦虚之壶」：查看抽牌堆顶的3张卡，选择1张加入手牌，其余返回抽牌堆顶。
public class QiangYuBingQianXuZhiHuCard : SelectSpecialBaseCard
{
    private const int _foreseeAmount = 3;

    public QiangYuBingQianXuZhiHuCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 查看抽牌堆顶的3张卡，选择1张加入手牌（其余返回抽牌堆顶）
        await SelectToolCmd.Foresee(choiceContext, Owner, _foreseeAmount, source: this, remainingToDiscard: false);
    }
}