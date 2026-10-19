using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Myth;

// 只读取效果来源的分类元数据，不继承原卡星级、额外怪兽身份或效果能力。
public static class MythSeriesTags
{
    private static readonly IReadOnlyDictionary<string, (CardTag Tag, CardKeyword Keyword)> Allowed = new Dictionary<string, (CardTag Tag, CardKeyword Keyword)>(StringComparer.Ordinal)
    {
        [nameof(SelectTags.JuShe)] = (SelectTags.JuShe, SelectKeywords.JuShe),
        [nameof(SelectTags.CanHuanGuaiShou)] = (SelectTags.CanHuanGuaiShou, SelectKeywords.CanHuanGuaiShou),
        [nameof(SelectTags.ZhuLei)] = (SelectTags.ZhuLei, SelectKeywords.ZhuLei),
        [nameof(SelectTags.ZhuLeiGuaiShou)] = (SelectTags.ZhuLeiGuaiShou, SelectKeywords.ZhuLeiGuaiShou),
        [nameof(SelectTags.ZhuLeiMoFa)] = (SelectTags.ZhuLeiMoFa, SelectKeywords.ZhuLeiMoFa),
        [nameof(SelectTags.ZhuLeiXianJing)] = (SelectTags.ZhuLeiXianJing, SelectKeywords.ZhuLeiXianJing),
        [nameof(SelectTags.YiJie)] = (SelectTags.YiJie, SelectKeywords.YiJie),
        [nameof(SelectTags.YiJieGuaiShou)] = (SelectTags.YiJieGuaiShou, SelectKeywords.YiJieGuaiShou),
        [nameof(SelectTags.YiJieMoXian)] = (SelectTags.YiJieMoXian, SelectKeywords.YiJieMoXian),
        [nameof(SelectTags.KeMo)] = (SelectTags.KeMo, SelectKeywords.KeMo),
        [nameof(SelectTags.KeMoGuaiShou)] = (SelectTags.KeMoGuaiShou, SelectKeywords.KeMoGuaiShou),
        [nameof(SelectTags.KeMoMoFa)] = (SelectTags.KeMoMoFa, SelectKeywords.KeMoMoFa),
        [nameof(SelectTags.KeMoXianJing)] = (SelectTags.KeMoXianJing, SelectKeywords.KeMoXianJing),
        [nameof(SelectTags.GuangEMo)] = (SelectTags.GuangEMo, SelectKeywords.GuangEMo),
        [nameof(SelectTags.YiXiangMing)] = (SelectTags.YiXiangMing, SelectKeywords.YiXiangMing),
        [nameof(SelectTags.YiXiangMingGuaiShou)] = (SelectTags.YiXiangMingGuaiShou, SelectKeywords.YiXiangMingGuaiShou),
        [nameof(SelectTags.YiXiangMingMoFa)] = (SelectTags.YiXiangMingMoFa, SelectKeywords.YiXiangMingMoFa),
        [nameof(SelectTags.YiXiangMingXianJing)] = (SelectTags.YiXiangMingXianJing, SelectKeywords.YiXiangMingXianJing),
        [nameof(SelectTags.HeLuSiGuaiShou)] = (SelectTags.HeLuSiGuaiShou, SelectKeywords.HeLuSiGuaiShou),
        [nameof(SelectTags.TianBeiLong)] = (SelectTags.TianBeiLong, SelectKeywords.TianBeiLong),
        [nameof(SelectTags.LongZu)] = (SelectTags.LongZu, SelectKeywords.LongZu),
        [nameof(SelectTags.CanHuanMoFa)] = (SelectTags.CanHuanMoFa, SelectKeywords.CanHuanMoFa),
        [nameof(SelectTags.YaoSheng)] = (SelectTags.YaoSheng, SelectKeywords.YaoSheng),
        [nameof(SelectTags.YaoShengGuaiShou)] = (SelectTags.YaoShengGuaiShou, SelectKeywords.YaoShengGuaiShou),
        [nameof(SelectTags.YaoShengMoFa)] = (SelectTags.YaoShengMoFa, SelectKeywords.YaoShengMoFa),
        [nameof(SelectTags.YaoShengXianJing)] = (SelectTags.YaoShengXianJing, SelectKeywords.YaoShengXianJing),
    };

    public static List<string> FromSource(string sourceClass)
    {
        var source = ModelDb.AllCards.Single(c => c.GetType().Assembly == typeof(Entry).Assembly && c.GetType().Name == sourceClass);
        var tags = source.Tags.ToHashSet();
        return Allowed.Where(p => tags.Contains(p.Value.Tag)).Select(p => p.Key).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    public static IEnumerable<CardTag> Resolve(IReadOnlyList<string>? names)
        => names == null ? [] : names.Select(n => Allowed[n].Tag);

    public static IEnumerable<IHoverTip> HoverTips(IReadOnlyList<string>? names)
        => names == null ? [] : names.Select(n => HoverTipFactory.FromKeyword(Allowed[n].Keyword));

    public static void Validate(IReadOnlyList<string>? names)
    {
        if (names != null && (names.Count > Allowed.Count || names.Distinct(StringComparer.Ordinal).Count() != names.Count
            || names.Any(n => n == null || !Allowed.ContainsKey(n))))
            throw new InvalidOperationException("Invalid myth series tags.");
    }
}
