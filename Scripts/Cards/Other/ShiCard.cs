using MegaCrit.Sts2.Core.Entities.Cards;

namespace SelectSpecialCardMod.Scripts.Cards.Other;

// 辅助选择卡「是」：灿幻·开门 的效果选择用（DerivedTokenCard：不可被玩家获取）
public class ShiCard : DerivedTokenCard
{
    public ShiCard() : base(CardType.Skill, TargetType.Self)
    {
    }
}
