using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using SelectSpecialCardMod.Scripts.Pool;

namespace SelectSpecialCardMod.Scripts.Relics;

// 仅保留旧 ModelId 的读档兼容；DuelDskGrant 在局内准备时替换为决斗盘。
// 不再发放，不再作为特殊卡奖励资格，也不参与新的遗物抽取。
// 卡图路径约定与卡牌一致：文件名 = C# 类名（PascalCase）+ .png。
[RegisterRelic(typeof(SelectSpecialRelicPool), Inherit = true)]
public class DeadEndRedRelic : ModRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Starter;

    public override bool IsAllowed(IRunState runState) => false;
    public override bool IsAllowedInShops => false;

    public override RelicAssetProfile AssetProfile => new(
        // 小图标（原版 85x85）
        IconPath: $"res://SelectSpecialCardMod/images/relics/{GetType().Name}.png",
        // 轮廓图标（原版 85x85）
        IconOutlinePath: $"res://SelectSpecialCardMod/images/relics/{GetType().Name}.png",
        // 大图标（原版 256x256）
        BigIconPath: $"res://SelectSpecialCardMod/images/relics/{GetType().Name}.png");
}
