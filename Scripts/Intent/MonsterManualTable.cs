using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;

namespace SelectSpecialCardMod.Scripts.Intent;

// 「人工覆盖表」：数值读不出来、或你想固定某只怪某一招的**玩家版效果**时，在这里手写。
// 优先级最高：人工 > 观测 > 静态表 > 占位值（作者明确指定的内容不会被自动值覆盖）。
//
// 每条写：本地化键后缀（Id）、以及效果
// （Block 格挡 / Heal 治疗 / SelfPowers 自己的增益 / PlayerPowers 给敌人的减益）。
// 键 = "怪物种类|招式id"（怪物种类是全大写下划线的类名；招式 id 是状态机里的 id）。
// 卡名和卡面描述一律写在本地化文件里，代码里不出现任何文案字面量。
//
// 用法示例（对应正文写在 localization/zhs/cards.json）：
//   ["GUARDBOT|GUARD_MOVE"] = new Entry { Id = "GUARD_BOT_GUARD", Block = 15 },
//     → SELECT_SPECIAL_CARD_MOD_MANUAL_GUARD_BOT_GUARD.title / .description
//   ["QUEEN|BURN_BRIGHT_FOR_ME_MOVE"] = new Entry { Id = "QUEEN_BURN_BRIGHT", Block = 20 }
//       .Self(("StrengthPower", 1)),   // 描述留空则按效果自动拼
internal static class MonsterManualTable
{
    internal sealed class Entry
    {
        // 本地化键后缀：卡名/描述从 SELECT_SPECIAL_CARD_MOD_MANUAL_{Id}.title / .description 取
        //（正文在 localization/<语言>/cards.json，代码里不写文案；描述留空则按效果自动拼）。
        internal string Id = "";
        internal int Block { get; set; }
        internal int Heal { get; set; }
        internal string Seal = "";                       // 非空 = 改为给目标怪塞一张废牌（status/curse/knowledge）
        internal readonly List<PowerEffect> SelfPowers = new();
        internal readonly List<PowerEffect> PlayerPowers = new();
        internal readonly List<string> Summoned = new();  // 要召出来的怪物种类（玩家方宠物）

        // 链式写法：.Self(("StrengthPower", 1)).Players(("WeakPower", 2))
        internal Entry Self(params (string Power, int Amount)[] powers)
        {
            foreach (var (power, amount) in powers) SelfPowers.Add(new PowerEffect(power, amount));
            return this;
        }

        internal Entry Players(params (string Power, int Amount)[] powers)
        {
            foreach (var (power, amount) in powers) PlayerPowers.Add(new PowerEffect(power, amount));
            return this;
        }

        internal Entry Summons(params string[] monsterKinds)
        {
            foreach (var kind in monsterKinds) Summoned.Add(kind);
            return this;
        }
    }

    private static readonly Dictionary<string, Entry> Table = new()
    {
        // 守卫机器人「加固」：原版是给场上其它"制造者"加 15 格挡（目标是队友，读不到）→
        // 玩家版按"给自己"处理。
        ["GUARDBOT|GUARD_MOVE"] = new Entry
        {
            Id = "GUARD_BOT_GUARD",
            Block = 15,
        },

        // 女王「为我燃烧」：原版是给队友各加 1 层力量 + 自己 20 格挡（队友那部分读不到）→
        // 玩家版都给自己。
        ["QUEEN|BURN_BRIGHT_FOR_ME_MOVE"] = new Entry
        {
            Id = "QUEEN_BURN_BRIGHT",
            Block = 20,
        }.Self(("StrengthPower", 1)),

        // 知识恶魔「知识的诅咒」：原版是往你的牌堆塞诅咒卡（读不到数值，也不是上能力）→
        // 玩家版反过来：给目标怪塞一张「知识的诅咒」，它抽到时会掉血。
        ["KNOWLEDGE_DEMON|CURSE_OF_KNOWLEDGE_MOVE"] = new Entry
        {
            Id = "KNOWLEDGE_CURSE",
            Seal = "knowledge",
        },
    };

    internal static Entry? Lookup(string? monsterKind, string? moveId)
    {
        if (string.IsNullOrEmpty(monsterKind) || string.IsNullOrEmpty(moveId)) return null;
        return Table.TryGetValue(monsterKind + "|" + moveId, out var entry) ? entry : null;
    }

    // 转成通用的效果结构（人工表是整条覆盖，不做逐字段合并）
    internal static MonsterMoveEffects ToEffects(Entry entry)
    {
        var fx = new MonsterMoveEffects { Block = entry.Block, Heal = entry.Heal, SealJunk = entry.Seal };
        fx.SelfPowers.AddRange(entry.SelfPowers);
        fx.PlayerPowers.AddRange(entry.PlayerPowers);
        foreach (var kind in entry.Summoned) fx.SummonedMonsters.Add((kind, 0));
        return fx;
    }

    // 卡名 / 描述：只从本地化取（取不到就返回空，由调用方走自动拼接/兜底）
    internal static string TitleOf(Entry entry) => Localized(entry.Id, "title") ?? "";

    internal static string DescriptionOf(Entry entry) => Localized(entry.Id, "description") ?? "";

    private static string? Localized(string id, string field)
    {
        if (string.IsNullOrEmpty(id)) return null;
        try
        {
            var loc = new LocString(MonsterText.Table, $"SELECT_SPECIAL_CARD_MOD_MANUAL_{id}.{field}");
            return loc.Exists() ? loc.GetFormattedText() : null;
        }
        catch
        {
            return null;
        }
    }
}
