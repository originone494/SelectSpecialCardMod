using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「珠泪」家族的口径集中在这里，避免每张卡各写一遍 Tags.Contains(...) 导致同一个词有多种含义。
//
// 层级（子集向上兼容：挂了子标签的卡一定也挂父标签）：
//   珠泪（所有珠泪卡）
//     ├─ 珠泪怪兽        ← 所有怪兽，「含」融合怪兽
//     │    └─ 珠泪融合怪兽   （子集）
//     ├─ 珠泪魔法
//     └─ 珠泪陷阱
//
// 另有跨类机制「珠泪融合」（塞壬/小美人鱼/梅洛身上的发动式机制），它不是卡种类，
// 用 HasFusionLimit 判断（卡面上不再注入「珠泪融合」关键字，判据走标签）。
//
// 改口径只改这个文件，所有卡与效果自动跟上。
public static class ZhuLeiFilter
{
    /// <summary>珠泪卡（一切，含魔法/陷阱/融合怪兽）。</summary>
    public static bool IsCard(CardModel c) => c.Tags.Contains(SelectTags.ZhuLei);

    /// <summary>珠泪怪兽（含珠泪融合怪兽）。</summary>
    public static bool IsMonster(CardModel c) => c.Tags.Contains(SelectTags.ZhuLeiGuaiShou);

    /// <summary>珠泪融合怪兽 = 珠泪怪兽 ＋ 融合怪兽。</summary>
    public static bool IsFusionMonster(CardModel c)
        => IsMonster(c) && c.Tags.Contains(SelectTags.RongHeGuaiShou);

    /// <summary>珠泪魔法。</summary>
    public static bool IsSpell(CardModel c) => c.Tags.Contains(SelectTags.ZhuLeiMoFa);

    /// <summary>珠泪陷阱。</summary>
    public static bool IsTrap(CardModel c) => c.Tags.Contains(SelectTags.ZhuLeiXianJing);

    /// <summary>除珠泪融合怪兽以外的珠泪卡（"检索1张珠泪卡，融合怪兽除外"这种口径）。</summary>
    public static bool IsCardExceptFusionMonster(CardModel c) => IsCard(c) && !IsFusionMonster(c);

    /// <summary>是否带「珠泪融合」这个发动式机制（不是卡种类，是机制；判据走标签，卡面不注入关键字）。</summary>
    public static bool HasFusionLimit(CardModel c) => c.Tags.Contains(SelectTags.ZhuLeiRongHe);
}
