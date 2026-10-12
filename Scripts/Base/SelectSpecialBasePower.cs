using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace SelectSpecialCardMod.Scripts.Base;

[RegisterPower(Inherit = true)]
public abstract class SelectSpecialBasePower : ModPowerTemplate
{
    // 自定义图标路径。1:1即可。原版游戏大图256x256，小图64x64。
    public override PowerAssetProfile AssetProfile => new(
        IconPath: $"res://SelectSpecialCardMod/images/powers/{GetType().Name}.png",
        BigIconPath: $"res://SelectSpecialCardMod/images/powers/{GetType().Name}.png"
    );
}
