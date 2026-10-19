using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Scaffolding.Content;

namespace SelectSpecialCardMod.Scripts.Cards.Other;

// 选项只是选择界面的机制卡，不进入手牌、检索、奖励或随机生成。
public abstract class EffectChoiceCard : DerivedTokenCard
{
    protected EffectChoiceCard() : base(CardType.Skill, TargetType.Self) { }

    public string EffectText { get; set; } = "";

    // 尚未绑定发动卡时保持空白；空路径会被框架替换为缺图占位图。
    private const string BlankPortrait = "res://SelectSpecialCardMod/images/cards/EffectChoicePortrait.tres";
    private CardAssetProfile _appearance = new(
        PortraitPath: BlankPortrait, BetaPortraitPath: BlankPortrait, VisualStyle: CardVisualStyle.Standard);
    public override CardAssetProfile AssetProfile => _appearance;

    public void UseAppearanceOf(CardModel source)
    {
        var modCard = source as ModCardTemplate;
        var style = modCard?.CustomVisualStyle ?? CardVisualStyle.Default;
        if (style == CardVisualStyle.Default)
            style = source.Rarity == CardRarity.Ancient ? CardVisualStyle.Ancient : CardVisualStyle.Standard;
        var portrait = modCard?.CustomPortraitPath ?? source.PortraitPath;
        // 布局与卡图一起继承，避免先古整卡插画被塞入普通插画窗；机制稀有度仍为 Token。
        _appearance = new CardAssetProfile(
            PortraitPath: portrait,
            BetaPortraitPath: modCard != null ? modCard.CustomBetaPortraitPath ?? portrait : source.BetaPortraitPath,
            PortraitMaterialPath: modCard?.CustomPortraitMaterialPath,
            PortraitMaterial: modCard?.CustomPortraitMaterial,
            VisualStyle: style);
    }

    protected override void AddExtraArgsToDescription(LocString description)
    {
        base.AddExtraArgsToDescription(description);
        description.Add("Effect", EffectText);
    }
}
