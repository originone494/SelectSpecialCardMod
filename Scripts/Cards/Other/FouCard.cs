using MegaCrit.Sts2.Core.Entities.Cards;

namespace SelectSpecialCardMod.Scripts.Cards.Other;

// 辅助选择卡「否」：灿幻·开门 的效果选择用（DerivedTokenCard：不可被玩家获取）
public class FouCard : DerivedTokenCard
{
    public FouCard() : base(CardType.Skill, TargetType.Self)
    {
    }
}
