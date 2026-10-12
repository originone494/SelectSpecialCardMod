using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Combat.CardTargeting;
using STS2RitsuLib.Keywords;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Power;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 禁忌的圣冠：这张卡也可以对玩家使用（CustomTargetType.Anyone，可指定任意友方或敌方）。
// 目标在敌人回合结束为止，受到的伤害为0，造成的伤害也为0。
// 多人游戏：目标解析（GetTargets）与效果命令均由 RitsuLib/游戏同步，无本地状态。
public class JinJiDeShengGuanCard : SelectSpecialBaseCard
{
    public JinJiDeShengGuanCard() : base(0, CardType.Skill, CardRarity.Ancient, CustomTargetType.Anyone)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 目标可以是任意存活的友方或敌方（CustomTargetType.Anyone）
        foreach (var target in this.GetTargets(cardPlay.Target))
        {
            await PowerCmd.Apply<JinJiDeShengGuanPower>(choiceContext, target, 1, Owner.Creature, this);
        }
    }
}
