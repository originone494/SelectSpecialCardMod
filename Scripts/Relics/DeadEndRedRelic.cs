using MegaCrit.Sts2.Core.Entities.Relics;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using SelectSpecialCardMod.Scripts.Pool;

namespace SelectSpecialCardMod.Scripts.Relics;

// 「死亡讯息-红」：从 yuno mod 搬过来的遗物，本 mod 的「特殊卡入场券」。
// 效果不挂在遗物上，它只是一个开关：持有它，击败每层第一个精英后才会出现特殊卡 3 选 1 奖励
// （判定见 Tool/EliteSpecialReward.cs），获得方式见 Tool/DeadEndRedGrant.cs（开局发放）。
// 卡图路径约定与卡牌一致：文件名 = C# 类名（PascalCase）+ .png。
[RegisterRelic(typeof(SelectSpecialRelicPool), Inherit = true)]
public class DeadEndRedRelic : ModRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Starter;

    public override RelicAssetProfile AssetProfile => new(
        // 小图标（原版 85x85）
        IconPath: $"res://SelectSpecialCardMod/images/relics/{GetType().Name}.png",
        // 轮廓图标（原版 85x85）
        IconOutlinePath: $"res://SelectSpecialCardMod/images/relics/{GetType().Name}.png",
        // 大图标（原版 256x256）
        BigIconPath: $"res://SelectSpecialCardMod/images/relics/{GetType().Name}.png");
}
