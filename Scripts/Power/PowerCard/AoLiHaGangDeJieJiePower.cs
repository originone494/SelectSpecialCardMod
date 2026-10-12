using MegaCrit.Sts2.Core.Entities.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Power;

// 奥利哈刚的结界：没有任何效果，只作为「奥利哈刚第二结界」「奥利哈刚第三结界」
// 的条件判定目标存在。能力栏正常显示，描述文本留空。
[RegisterPower]
public class AoLiHaGangDeJieJiePower : SelectSpecialBasePower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
}
