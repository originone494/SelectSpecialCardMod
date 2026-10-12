using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace SelectSpecialCardMod.Scripts.Pool;

// 共享遗物池：本 mod 没有人物角色，遗物必须挂在一个共享池里才会进入 ModelDb.AllRelicPools
// （与卡牌的 [RegisterSharedCardPool] 同理，否则遗物解析/领取会失败）。
[RegisterSharedRelicPool]
public class SelectSpecialRelicPool : TypeListRelicPoolModel
{
    // 本 mod 没有人物角色，使用原版的无色能量图标
    public override string EnergyColorName => "Colorless";
}
