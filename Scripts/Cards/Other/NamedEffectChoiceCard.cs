namespace SelectSpecialCardMod.Scripts.Cards.Other;

// 通用具名机制选项；沿用效果选项卡的卡框和来源卡图，不进入玩家卡池。
public class NamedEffectChoiceCard : EffectChoiceCard
{
    public string ChoiceTitle { get; set; } = "";
    public override string Title => string.IsNullOrEmpty(ChoiceTitle) ? base.Title : ChoiceTitle;
}
