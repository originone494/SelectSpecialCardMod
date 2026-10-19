using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib.Combat.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Cards.Special;

namespace SelectSpecialCardMod.Scripts.Power;

// 使用项目现有的临时力量模板，敌人回合结束时恢复实际降低的力量。
[RegisterPower]
public class YaoShengLuXiNaTempDownPower : ModTemporaryAppliedPowerTemplate<YaoShengHuaShiLuXiNaCard, StrengthPower>
{
    protected override bool IsPositive => false;
    // 图标留空：不再借用七星道的临时减益图标，交给框架的缺图占位处理。
    public override LocString Description => new("powers", "SELECT_SPECIAL_CARD_MOD_POWER_TEMP_STRENGTH_POWER_DOWN.description");
}
