using MegaCrit.Sts2.Core.Entities.Cards;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Other;

// 衍生/机制卡基类：不由玩家获取，只在特定机制的流程内部出现（如「是」「否」选择卡）。
// 堵住所有随机获取路径：
//   - 图鉴隐藏（ShouldShowInCardLibrary=false）
//   - 战斗内随机生成（发现/药水等）过滤（CanBeGeneratedInCombat=false）
//   - 诅咒/修饰器池过滤（CanBeGeneratedByModifiers=false）
//   - Token 稀有度：变形卡路径天然排除
//   - 异解供货袋（YiJieExileBag）：按 Token 稀有度 + ShouldShowInCardLibrary 双判据排除
// 奖励/商店/事件走显式职业卡池，mod 卡池不在其中，无需处理。
public abstract class DerivedTokenCard : SelectSpecialBaseCard
{
    protected DerivedTokenCard(CardType type, TargetType target)
        : base(0, type, CardRarity.Token, target, shouldShowInCardLibrary: false)
    {
    }

    public override bool CanBeGeneratedInCombat => false;

    public override bool CanBeGeneratedByModifiers => false;
}
