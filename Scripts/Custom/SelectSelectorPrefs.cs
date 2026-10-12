using MegaCrit.Sts2.Core.Localization;

namespace SelectSpecialCardMod.Scripts.Custom;

// 选卡界面提示文本，键放在 card_selection.json / powers.json
public class SelectSelectorPrefs
{
    // 「检索」通用提示（source 为空时使用）
    public static LocString RetrieverSelectionPrompt = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_RETRIEVE");

    // 「预知」通用提示（source 为空时使用）
    public static LocString ForeseeSelectionPrompt = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_FORESEE");

    // 第二结界的“保留”选牌提示（沿用原版做法：键挂在 powers 表的 selectionScreenPrompt 上）
    public static LocString RetainSelectionPrompt = new(
        "powers", "SELECT_SPECIAL_CARD_MOD_POWER_AO_LI_HA_GANG_DI_ER_JIE_JIE_POWER.selectionScreenPrompt");

    // 第二结界“敌人攻击时”的可选丢弃提示
    public static LocString BarrierDiscardPrompt = new(
        "card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_BARRIER_DISCARD");
}
