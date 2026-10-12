using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using SelectSpecialCardMod.Scripts.Intent;

namespace SelectSpecialCardMod.Scripts.Pool;

// 怪物卡池：收录怪物意图卡牌化的所有卡面模板（IMonsterDisplayCard 实现）。
// 必须标 [RegisterSharedCardPool]：非人物卡池不会自动进 ModelDb.AllCardPools，
// 不加则卡牌的 card.Pool 解析会抛 "not in any card pool!" 异常，导致渲染失败。
// 图鉴可见性由 Entry.cs 的 RegisterCardLibraryCompendiumSharedPoolFilter 注册。
[RegisterSharedCardPool]
public class MonsterCardPool : TypeListCardPoolModel
{
    // 卡池的ID。必须唯一防撞车。
    public override string Title => "SelectSpecialCardModMonsterCardPool";

    // 卡牌改为经 [RegisterCard(typeof(MonsterCardPool), Inherit = true)]（标在 MonsterDisplayCard 上）
    // 由 ModHelper.AddModelToPool 注入。不要再重写旧式 CardTypes —— 官方注释明确说两者并用会在池里产生重复条目。

    // 新 mod 没有人物角色，使用原版的无色能量图标
    public override string EnergyColorName => "Colorless";

    // 卡池的主题色：暗红（怪物）
    public override Color DeckEntryCardColor => new(0.78f, 0.32f, 0.32f);

    // 卡池是否是无色。例如事件、状态等卡池就是无色的。
    public override bool IsColorless => false;
}
