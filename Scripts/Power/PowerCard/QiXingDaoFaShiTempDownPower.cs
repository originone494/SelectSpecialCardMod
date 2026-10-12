using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib.Combat.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using SelectSpecialCardMod.Scripts.Cards.Special;

namespace SelectSpecialCardMod.Scripts.Power;

// 七星道法师「本回合降低力量」的临时能力包装（照抄 yuno 的 YunoTempStrengthPower<T> 用法）：
// 底层临时应用 StrengthPower，IsPositive=false 表示降低；描述与正面版共用一条 powers.json 文本。
[RegisterPower]
public class QiXingDaoFaShiTempDownPower : ModTemporaryAppliedPowerTemplate<QiXingDaoFaShiCard, StrengthPower>
{
    protected override bool IsPositive => false;

    // 自定义图标路径。1:1即可。原版游戏大图256x256，小图64x64。
    public override PowerAssetProfile AssetProfile => new(
        IconPath: $"res://SelectSpecialCardMod/images/powers/{GetType().Name}.png",
        BigIconPath: $"res://SelectSpecialCardMod/images/powers/{GetType().Name}.png"
    );

    // 多个临时能力共用一条文本（与 yuno 的 TEMP_STRENGTH_POWER_DOWN 一致）
    public override LocString Description => new("powers", "SELECT_SPECIAL_CARD_MOD_POWER_TEMP_STRENGTH_POWER_DOWN.description");
}