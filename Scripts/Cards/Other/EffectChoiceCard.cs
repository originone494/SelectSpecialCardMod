using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using STS2RitsuLib.Scaffolding.Content;

namespace SelectSpecialCardMod.Scripts.Cards.Other;

// 选项只是选择界面的机制卡，不进入手牌、检索、奖励或随机生成。
public abstract class EffectChoiceCard : DerivedTokenCard
{
    protected EffectChoiceCard() : base(CardType.Skill, TargetType.Self) { }

    public string EffectText { get; set; } = "";
    public string PortraitSourceClassName { get; set; } = "YiXiangMingJueZeCard";

    // 使用发动卡的卡图，避免为通用的①/②选项绑定某一种具体效果。
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://SelectSpecialCardMod/images/cards/{PortraitSourceClassName}.png");

    protected override void AddExtraArgsToDescription(LocString description)
    {
        base.AddExtraArgsToDescription(description);
        description.Add("Effect", EffectText);
    }
}
