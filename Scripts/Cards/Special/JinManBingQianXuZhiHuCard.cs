using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「金満で謙虚な壺」（Pot of Prosperity）：向除外堆加入 6 张随机「额外怪兽」卡。查看抽牌堆顶的 6 张卡，选择 1 张加入手牌。
public class JinManBingQianXuZhiHuCard : SelectSpecialBaseCard
{
    private const int _exileCards = 6;
    private const int _foreseeAmount = 6;

    public JinManBingQianXuZhiHuCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 向除外堆加入 6 张随机「额外怪兽」卡
        await SelectToolCmd.AddRandomCardsToPile(
            Owner,
            Entry.ChuWaiPile,
            _exileCards,
            filter: c => CardClassification.IsExtraMonster(c));

        // 查看抽牌堆顶的6张卡，选择1张加入手牌（其余留在抽牌堆顶）
        await SelectToolCmd.Foresee(choiceContext, Owner, _foreseeAmount, source: this, remainingToDiscard: false);
    }
}
