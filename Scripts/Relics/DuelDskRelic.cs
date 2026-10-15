using MegaCrit.Sts2.Core.Entities.Relics;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using SelectSpecialCardMod.Scripts.Pool;

namespace SelectSpecialCardMod.Scripts.Relics;

// 「决斗盘」：开局发放的特殊卡奖励资格，接替「死亡讯息-红」。
// 击败每层第一个精英时的特殊卡3选1由 EliteSpecialReward 结算。
// 卡图路径约定与卡牌一致：文件名 = C# 类名（PascalCase）+ .png，
// 对应 res://SelectSpecialCardMod/images/relics/DuelDskRelic.png。
[RegisterRelic(typeof(SelectSpecialRelicPool), Inherit = true)]
public class DuelDskRelic : ModRelicTemplate
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
