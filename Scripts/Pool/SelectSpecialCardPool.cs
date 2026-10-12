using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Intent;

namespace SelectSpecialCardMod.Scripts.Pool;

// 必须标 [RegisterSharedCardPool]：该池不是人物卡池（人物卡池会自动进入 ModelDb.AllCardPools），
// 若不加则它不会出现在 ModelDb.AllCardPools 里，卡牌的 card.Pool 解析会抛
// “not in any card pool!” 异常，导致奖励卡的渲染/领取失败。
[RegisterSharedCardPool]
public class SelectSpecialCardPool : TypeListCardPoolModel
{
    // 卡池的ID。必须唯一防撞车。
    public override string Title => "SelectSpecialCardModSpecialCardPool";

    // 卡牌改为经 [RegisterCard(typeof(SelectSpecialCardPool), Inherit = true)]（标在 SelectSpecialBaseCard 上）
    // 由 ModHelper.AddModelToPool 注入；怪物卡因为 MonsterDisplayCard 自己声明了 [RegisterCard(MonsterCardPool)]
    // 会顶掉这条继承注册，所以不会混进本池。
    // 不要再重写旧式 CardTypes —— 官方注释明确说两者并用会在池里产生重复条目。

    // 新 mod 没有人物角色，使用原版的无色能量图标
    public override string EnergyColorName => "Colorless";

    // 卡池的主题色
    public override Color DeckEntryCardColor => new(0.62f, 0.44f, 0.85f);

    // 卡池是否是无色。例如事件、状态等卡池就是无色的。
    public override bool IsColorless => false;
}
