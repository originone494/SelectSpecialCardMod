using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Tool;

public sealed record FusionEffectDefinition
{
    public string Id { get; }
    public string Name { get; }
    public string Series { get; }
    public IReadOnlyList<CardTag> Tags { get; }
    public IReadOnlyList<FusionAction> Actions { get; }
    public Type ReferenceCard { get; }
    public FusionEffectDefinition(string id, string name, string series, IReadOnlyList<CardTag> tags,
        IReadOnlyList<FusionAction> actions, Type referenceCard)
    {
        Id = id; Name = name; Series = series; ReferenceCard = referenceCard;
        Tags = Array.AsReadOnly(tags.ToArray()); Actions = Array.AsReadOnly(actions.ToArray());
    }
    public string Describe(ShenHuaCompositeCard? source = null)
        => string.Join("", Actions.Select((a, i) => a.Describe(FusionEffectLibrary.VarKey(Id, i), source)));
}

// 独立配方：ReferenceCard仅供审核追溯，不从来源卡拷贝描述、标签、委托或执行逻辑。
// 模块ID是保存协议，发布后不要改名/复用；平衡改动只影响模块，不改变原卡。
public static class FusionEffectLibrary
{
    private static readonly IReadOnlyDictionary<CardTag, CardKeyword> AllowedTags = new Dictionary<CardTag, CardKeyword>
    {
        [SelectTags.ZhuLei] = SelectKeywords.ZhuLei,
        [SelectTags.ZhuLeiGuaiShou] = SelectKeywords.ZhuLeiGuaiShou,
        [SelectTags.ZhuLeiMoFa] = SelectKeywords.ZhuLeiMoFa,
        [SelectTags.ZhuLeiXianJing] = SelectKeywords.ZhuLeiXianJing,
        [SelectTags.YiJie] = SelectKeywords.YiJie,
        [SelectTags.YiJieGuaiShou] = SelectKeywords.YiJieGuaiShou,
        [SelectTags.YiJieMoXian] = SelectKeywords.YiJieMoXian,
        [SelectTags.KeMo] = SelectKeywords.KeMo,
        [SelectTags.KeMoGuaiShou] = SelectKeywords.KeMoGuaiShou,
        [SelectTags.KeMoMoFa] = SelectKeywords.KeMoMoFa,
        [SelectTags.KeMoXianJing] = SelectKeywords.KeMoXianJing,
        [SelectTags.GuangEMo] = SelectKeywords.GuangEMo,
        [SelectTags.YiXiangMing] = SelectKeywords.YiXiangMing,
        [SelectTags.YiXiangMingGuaiShou] = SelectKeywords.YiXiangMingGuaiShou,
        [SelectTags.YiXiangMingMoFa] = SelectKeywords.YiXiangMingMoFa,
        [SelectTags.YiXiangMingXianJing] = SelectKeywords.YiXiangMingXianJing,
        [SelectTags.HeLuSiGuaiShou] = SelectKeywords.HeLuSiGuaiShou,
        [SelectTags.TianBeiLong] = SelectKeywords.TianBeiLong,
        [SelectTags.LongZu] = SelectKeywords.LongZu,
        [SelectTags.CanHuanMoFa] = SelectKeywords.CanHuanMoFa,
        [SelectTags.YaoSheng] = SelectKeywords.YaoSheng,
        [SelectTags.YaoShengGuaiShou] = SelectKeywords.YaoShengGuaiShou,
        [SelectTags.YaoShengMoFa] = SelectKeywords.YaoShengMoFa,
        [SelectTags.YaoShengXianJing] = SelectKeywords.YaoShengXianJing,
    };
    public static IReadOnlyList<FusionEffectDefinition> All { get; } = Array.AsReadOnly<FusionEffectDefinition>(
    [
        new("melo", "梅洛", "zhu_lei", [SelectTags.ZhuLei, SelectTags.ZhuLeiGuaiShou], [new(FusionActionKind.Damage, 8), new(FusionActionKind.Mill, 3)], typeof(ZhuLeiMeiLuoCard)),
        new("siren", "塞壬", "zhu_lei", [SelectTags.ZhuLei, SelectTags.ZhuLeiGuaiShou], [new(FusionActionKind.Damage, 14), new(FusionActionKind.Discard, 1)], typeof(ZhuLeiSaiRenCard)),
        new("xiaomei", "小美", "zhu_lei", [SelectTags.ZhuLei, SelectTags.ZhuLeiGuaiShou], [new(FusionActionKind.Block, 10), new(FusionActionKind.Draw, 1)], typeof(ZhuLeiXiaoMeiCard)),
        new("renohart", "雷诺哈特", "zhu_lei", [SelectTags.ZhuLei, SelectTags.ZhuLeiGuaiShou], [new(FusionActionKind.Damage, 12), new(FusionActionKind.Retrieve, 1, FusionCardFilter.ZhuLeiMonster)], typeof(ZhuLeiLeiNuoHaTeCard)),
        new("jushe", "俱舍", "zhu_lei", [SelectTags.ZhuLei, SelectTags.ZhuLeiGuaiShou], [new(FusionActionKind.Damage, 20), new(FusionActionKind.ExhaustDiscard, 1)], typeof(ZhuLeiJuSheCard)),
        new("canxiang", "残响", "zhu_lei", [SelectTags.ZhuLei, SelectTags.ZhuLeiXianJing], [new(FusionActionKind.Damage, 6), new(FusionActionKind.Block, 7), new(FusionActionKind.Weak, 2)], typeof(ZhuLeiCanXiangCard)),
        new("shuixian", "水仙", "zhu_lei", [SelectTags.ZhuLei, SelectTags.ZhuLeiGuaiShou], [new(FusionActionKind.Block, 12), new(FusionActionKind.Mill, 5)], typeof(ZhuLeiShuiXianCard)),
        new("lusaka", "露莎卡", "zhu_lei", [SelectTags.ZhuLei, SelectTags.ZhuLeiGuaiShou], [new(FusionActionKind.Damage, 15), new(FusionActionKind.Artifact, 1)], typeof(ZhuLeiLuShaKaCard)),
        new("nailuo", "奈落", "yi_jie", [SelectTags.YiJie, SelectTags.YiJieGuaiShou], [new(FusionActionKind.Damage, 14), new(FusionActionKind.RandomExile, 4)], typeof(YiJieNaiLuoCard)),
        new("tuonela", "图奥内拉", "yi_jie", [SelectTags.YiJie, SelectTags.YiJieGuaiShou], [new(FusionActionKind.Block, 15), new(FusionActionKind.RandomExile, 3)], typeof(YiJieTuAoNeiLaCard)),
        new("huangquan", "黄泉国", "yi_jie", [SelectTags.YiJie, SelectTags.YiJieGuaiShou], [new(FusionActionKind.Block, 16), new(FusionActionKind.RandomExile, 5)], typeof(YiJieHuangQuanGuoCard)),
        new("xienengu", "欣嫩谷", "yi_jie", [SelectTags.YiJie, SelectTags.YiJieGuaiShou], [new(FusionActionKind.Block, 10), new(FusionActionKind.RandomExile, 2)], typeof(YiJieXinNenGuCard)),
        new("mingshen", "冥神府", "yi_jie", [SelectTags.YiJie, SelectTags.YiJieGuaiShou], [new(FusionActionKind.Damage, 18), new(FusionActionKind.RecoverExile, 1, FusionCardFilter.YiJie)], typeof(YiJieMingShenFuCard)),
        new("xiewuer", "谢屋尔", "yi_jie", [SelectTags.YiJie, SelectTags.YiJieGuaiShou], [new(FusionActionKind.Damage, 12), new(FusionActionKind.RandomExile, 3)], typeof(YiJieXieWuErCard)),
        new("zhanyundi", "战陨地", "yi_jie", [SelectTags.YiJie, SelectTags.YiJieGuaiShou], [new(FusionActionKind.AllDamage, 12), new(FusionActionKind.RandomExile, 2)], typeof(YiJieZhanYunDiCard)),
        new("fuyin", "福音", "yi_jie", [SelectTags.YiJie, SelectTags.YiJieMoXian], [new(FusionActionKind.RecoverExile, 1, FusionCardFilter.YiJieMonster), new(FusionActionKind.Draw, 2)], typeof(YiJieFuYinCard)),
        new("mojianke", "魔镌客", "ke_mo", [SelectTags.KeMo, SelectTags.KeMoGuaiShou, SelectTags.GuangEMo], [new(FusionActionKind.Damage, 18), new(FusionActionKind.Retrieve, 1, FusionCardFilter.KeMoSpell)], typeof(KeMoMoJianKeCard)),
        new("shensheng", "神圣棺", "ke_mo", [SelectTags.KeMo, SelectTags.KeMoGuaiShou, SelectTags.GuangEMo], [new(FusionActionKind.DamageVsBlock, 18)], typeof(KeMoShenShengGuanCard)),
        new("zhenhun", "镇魂棺", "ke_mo", [SelectTags.KeMo, SelectTags.KeMoGuaiShou, SelectTags.GuangEMo], [new(FusionActionKind.Damage, 6), new(FusionActionKind.RecoverExhaust, 1, FusionCardFilter.KeMoMonster)], typeof(KeMoZhenHunGuanCard)),
        new("dasheng", "大圣棺", "ke_mo", [SelectTags.KeMo, SelectTags.KeMoGuaiShou, SelectTags.GuangEMo], [new(FusionActionKind.Damage, 12), new(FusionActionKind.Block, 6)], typeof(KeMoDaShengGuanCard)),
        new("junwang", "赫赫君王", "ke_mo", [SelectTags.KeMo, SelectTags.KeMoGuaiShou, SelectTags.GuangEMo], [new(FusionActionKind.Damage, 30), new(FusionActionKind.Discard, 1)], typeof(KeMoHeHeJunWangCard)),
        new("luolei", "落泪之日", "ke_mo", [SelectTags.KeMo, SelectTags.KeMoGuaiShou, SelectTags.GuangEMo], [new(FusionActionKind.Damage, 24), new(FusionActionKind.TempStrengthDown, 2)], typeof(KeMoLuoLeiRiZiCard)),
        new("zhennu", "震怒之日", "ke_mo", [SelectTags.KeMo, SelectTags.KeMoGuaiShou, SelectTags.GuangEMo], [new(FusionActionKind.AllDamage, 14), new(FusionActionKind.Vulnerable, 1)], typeof(KeMoZhenNuRiZiCard)),
        new("yongsheng", "咏圣", "ke_mo", [SelectTags.KeMo, SelectTags.KeMoMoFa], [new(FusionActionKind.Retrieve, 1, FusionCardFilter.GuangEMo), new(FusionActionKind.Discard, 1)], typeof(KeMoYongShengCard)),
        new("jueze", "抉择", "yi_xiang_ming", [SelectTags.YiXiangMing, SelectTags.YiXiangMingMoFa], [new(FusionActionKind.Heal, 1), new(FusionActionKind.Draw, 2)], typeof(YiXiangMingJueZeCard)),
        new("qingzhu", "倾注", "yi_xiang_ming", [SelectTags.YiXiangMing, SelectTags.YiXiangMingMoFa], [new(FusionActionKind.Heal, 1), new(FusionActionKind.RetrieveRandom, 1, FusionCardFilter.YiXiangMing)], typeof(YiXiangMingQingZhuCard)),
        new("diaoyin", "调音", "yi_xiang_ming", [SelectTags.YiXiangMing, SelectTags.YiXiangMingMoFa], [new(FusionActionKind.Draw, 2)], typeof(YiXiangMingDiaoYinCard)),
        new("xingjie", "星界", "yi_xiang_ming", [SelectTags.YiXiangMing, SelectTags.YiXiangMingMoFa], [new(FusionActionKind.Draw, 1), new(FusionActionKind.Block, 8)], typeof(YiXiangMingDiaoYinCard)),
        new("angel", "天使之声", "yi_xiang_ming", [SelectTags.YiXiangMing, SelectTags.YiXiangMingGuaiShou], [new(FusionActionKind.Heal, 1), new(FusionActionKind.ExhaustHand, 1), new(FusionActionKind.Retrieve, 1, FusionCardFilter.YiXiangMing)], typeof(YiXiangMingTianShiZhiShengCard)),
        new("devil", "恶魔之声", "yi_xiang_ming", [SelectTags.YiXiangMing, SelectTags.YiXiangMingGuaiShou], [new(FusionActionKind.LoseHp, 1), new(FusionActionKind.Retrieve, 1, FusionCardFilter.YiXiangMing), new(FusionActionKind.Block, 4)], typeof(YiXiangMingEMoZhiShengCard)),
        new("yaoqing", "邀请", "yi_xiang_ming", [SelectTags.YiXiangMing, SelectTags.YiXiangMingMoFa], [new(FusionActionKind.RetrieveFree, 1, FusionCardFilter.YiXiangMingMonster)], typeof(YiXiangMingYaoQingCard)),
        new("storm", "风暴恶魔", "yi_xiang_ming", [SelectTags.YiXiangMing, SelectTags.YiXiangMingGuaiShou], [new(FusionActionKind.Block, 10), new(FusionActionKind.Energy, 1)], typeof(YiXiangMingShenYiFengBaoEMoCard)),
        new("habi", "先导哈比", "he_lu_si", [SelectTags.HeLuSiGuaiShou], [new(FusionActionKind.Block, 16), new(FusionActionKind.RecoverDiscard, 1)], typeof(HeLuSiXianDaoHaBiCard)),
        new("habi_soul", "哈比的引魂", "he_lu_si", [SelectTags.HeLuSiGuaiShou], [new(FusionActionKind.RecoverExhaust, 2), new(FusionActionKind.Block, 6)], typeof(HeLuSiXianDaoHaBiCard)),
        new("rongguang", "荣光", "he_lu_si", [SelectTags.HeLuSiGuaiShou], [new(FusionActionKind.Damage, 30)], typeof(HeLuSiRongGuangCard)),
        new("rongguang_flame", "荣光的余焰", "he_lu_si", [SelectTags.HeLuSiGuaiShou], [new(FusionActionKind.AllDamage, 10), new(FusionActionKind.Mill, 2)], typeof(HeLuSiRongGuangCard)),
        new("zhufu", "祝福", "he_lu_si", [SelectTags.HeLuSiGuaiShou], [new(FusionActionKind.Damage, 12), new(FusionActionKind.Strength, 1)], typeof(HeLuSiZhuFuCard)),
        new("zhufu_ward", "祝福的守护", "he_lu_si", [SelectTags.HeLuSiGuaiShou], [new(FusionActionKind.Block, 12), new(FusionActionKind.Dexterity, 1)], typeof(HeLuSiZhuFuCard)),
        new("jiahu", "加护", "he_lu_si", [SelectTags.HeLuSiGuaiShou], [new(FusionActionKind.Draw, 3), new(FusionActionKind.Discard, 1)], typeof(HeLuSiJiaHuCard)),
        new("wangguan", "王棺的庇护", "he_lu_si", [], [new(FusionActionKind.Retrieve, 1, FusionCardFilter.HorusMonster), new(FusionActionKind.Block, 8)], typeof(WangZhiGuanCard)),
        new("baiban", "白板", "tian_bei_long", [SelectTags.TianBeiLong, SelectTags.LongZu], [new(FusionActionKind.Damage, 9), new(FusionActionKind.Retrieve, 1, FusionCardFilter.TianBeiLong)], typeof(TianBeiLongBaiBanCard)),
        new("hongzhong", "红中", "tian_bei_long", [SelectTags.TianBeiLong, SelectTags.LongZu], [new(FusionActionKind.Damage, 7), new(FusionActionKind.RetrieveFree, 1, FusionCardFilter.Dragon)], typeof(TianBeiLongHongZhongCard)),
        new("lvfa", "绿发", "tian_bei_long", [SelectTags.TianBeiLong, SelectTags.LongZu], [new(FusionActionKind.Damage, 8), new(FusionActionKind.Draw, 1)], typeof(TianBeiLongLvFaCard)),
        new("huanlu", "幻禄", "tian_bei_long", [SelectTags.TianBeiLong, SelectTags.LongZu], [new(FusionActionKind.Damage, 1), new(FusionActionKind.Strength, 2)], typeof(TianBeiLongHuanLuCard)),
        new("kaimen", "开门", "tian_bei_long", [SelectTags.CanHuanMoFa], [new(FusionActionKind.Retrieve, 1, FusionCardFilter.Dragon), new(FusionActionKind.Energy, 1)], typeof(CanHuanKaiMenCard)),
        new("shuangcha", "双叉天龙", "tian_bei_long", [SelectTags.TianBeiLong, SelectTags.LongZu], [new(FusionActionKind.Damage, 20), new(FusionActionKind.Block, 5)], typeof(CanHuanShengLongShuangChaTianLongCard)),
        new("santian", "三极天龙", "tian_bei_long", [SelectTags.TianBeiLong, SelectTags.LongZu], [new(FusionActionKind.AllDamage, 15), new(FusionActionKind.Draw, 1)], typeof(CanHuanChaoLongSanJiTianLongCard)),
        new("beiman", "杯满", "tian_bei_long", [SelectTags.CanHuanMoFa], [new(FusionActionKind.Block, 8), new(FusionActionKind.Energy, 2)], typeof(BeiManDeCanHuanZhuangCard)),
        new("lucina", "露西娜", "yao_sheng", [SelectTags.YaoSheng, SelectTags.YaoShengGuaiShou], [new(FusionActionKind.Damage, 25), new(FusionActionKind.TempStrengthDown, 8)], typeof(YaoShengHuaShiLuXiNaCard)),
        new("dina", "蒂娜", "yao_sheng", [SelectTags.YaoSheng, SelectTags.YaoShengGuaiShou], [new(FusionActionKind.Damage, 14), new(FusionActionKind.Block, 10)], typeof(YaoShengBoShiDiNaCard)),
        new("fortuna", "福尔图娜", "yao_sheng", [SelectTags.YaoSheng, SelectTags.YaoShengGuaiShou], [new(FusionActionKind.Damage, 16), new(FusionActionKind.RemoveEnemyStrength, 0)], typeof(YaoShengYueShiFuErTuNaCard)),
        new("regina", "蕾绮娜", "yao_sheng", [SelectTags.YaoSheng, SelectTags.YaoShengGuaiShou], [new(FusionActionKind.Damage, 18), new(FusionActionKind.Draw, 3)], typeof(YaoShengFengShiLeiQiNaCard)),
        new("flower", "花诗的邀约", "yao_sheng", [SelectTags.YaoSheng, SelectTags.YaoShengGuaiShou], [new(FusionActionKind.Retrieve, 1, FusionCardFilter.YaoShengMonster), new(FusionActionKind.Block, 5)], typeof(YaoShengHuaShiLuXiNaCard)),
        new("tide", "波诗的屏障", "yao_sheng", [SelectTags.YaoSheng, SelectTags.YaoShengGuaiShou], [new(FusionActionKind.Block, 10), new(FusionActionKind.Dexterity, 2)], typeof(YaoShengBoShiDiNaCard)),
        new("moon", "月诗的静夜", "yao_sheng", [SelectTags.YaoSheng, SelectTags.YaoShengGuaiShou], [new(FusionActionKind.Weak, 2), new(FusionActionKind.Artifact, 1)], typeof(YaoShengYueShiFuErTuNaCard)),
        new("wind", "风诗的回旋", "yao_sheng", [SelectTags.YaoSheng, SelectTags.YaoShengGuaiShou], [new(FusionActionKind.Draw, 2), new(FusionActionKind.Energy, 1)], typeof(YaoShengFengShiLeiQiNaCard)),
        new("charity", "天使的施舍", "mo_fa", [], [new(FusionActionKind.Draw, 3), new(FusionActionKind.Discard, 2)], typeof(TianShiDeShiSheCard)),
        new("desire", "强欲的代价", "mo_fa", [], [new(FusionActionKind.RandomExile, 4), new(FusionActionKind.Draw, 2)], typeof(QiangYuErTanYuZhiHuCard)),
        new("grass", "邻家割草", "mo_fa", [], [new(FusionActionKind.Mill, 6)], typeof(LinJiaGeCaoCard)),
        new("pot", "壶中的馈赠", "mo_fa", [], [new(FusionActionKind.Draw, 3), new(FusionActionKind.LoseHp, 2)], typeof(QiangYuBingJinManZhiHuCard)),
        new("smallworld", "小世界", "mo_fa", [], [new(FusionActionKind.ExhaustHand, 1), new(FusionActionKind.Retrieve, 1, FusionCardFilter.Attack)], typeof(XiaoShiJieCard)),
        new("revive", "死者苏生", "mo_fa", [], [new(FusionActionKind.RecoverExhaustFree, 1, FusionCardFilter.Attack)], typeof(SiZheSuShengCard)),
        new("armistice", "一时休战", "mo_fa", [], [new(FusionActionKind.Draw, 1), new(FusionActionKind.Block, 12)], typeof(YiShiXiuZhanCard)),
        new("snow", "白雪", "mo_fa", [], [new(FusionActionKind.Damage, 18), new(FusionActionKind.Weak, 1)], typeof(YaoJingChuanJiBaiXueCard)),
    ]);
    private static readonly IReadOnlyDictionary<string, FusionEffectDefinition> ById
        = All.ToDictionary(m => m.Id, StringComparer.Ordinal);
    static FusionEffectLibrary() => Validate();
    public static FusionEffectDefinition? Find(string id) => ById.GetValueOrDefault(id);
    public static bool IsAllowedTag(CardTag tag) => AllowedTags.ContainsKey(tag);
    public static string VarKey(string id, int actionIndex) => $"Myth_{id}_{actionIndex}";
    public static IEnumerable<IHoverTip> TagHoverTips(IEnumerable<CardTag> tags)
        => tags.Distinct().Where(IsAllowedTag).Select(t => HoverTipFactory.FromKeyword(AllowedTags[t]));

    public static void Validate()
    {
        if (All.Count == 0 || All.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != All.Count)
            throw new InvalidOperationException("Myth effect IDs must be nonempty and unique.");
        foreach (var module in All)
        {
            if (string.IsNullOrWhiteSpace(module.Id) || module.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')
                || string.IsNullOrWhiteSpace(module.Name) || string.IsNullOrWhiteSpace(module.Series)
                || module.Actions.Count == 0 || module.Tags.Any(t => !IsAllowedTag(t))
                || !typeof(SelectSpecialBaseCard).IsAssignableFrom(module.ReferenceCard)
                || module.ReferenceCard == typeof(SuiYuanShenHuaHeTiCard) || module.ReferenceCard == typeof(ShenHuaCompositeCard))
                throw new InvalidOperationException("Invalid myth module metadata: " + module.Id);
            foreach (var action in module.Actions)
                if (!Enum.IsDefined(action.Kind) || !Enum.IsDefined(action.Filter) || action.Amount < 0
                    || (action.Amount == 0 && action.Kind != FusionActionKind.RemoveEnemyStrength))
                    throw new InvalidOperationException("Invalid myth action: " + module.Id);
        }
    }

    public static IReadOnlyList<FusionEffectDefinition> Roll(Player player, int count)
    {
        if (count < 0 || count > All.Count) throw new ArgumentOutOfRangeException(nameof(count));
        var remaining = All.OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
        var result = new List<FusionEffectDefinition>(count);
        var usedSeries = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            // 每一轮先等概率选系列，再选该系列中的模块；尽量跨系列，模块始终不放回。
            var groups = remaining.GroupBy(m => m.Series).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
            var available = groups.Where(g => !usedSeries.Contains(g.Key)).ToList();
            if (available.Count == 0) { usedSeries.Clear(); available = groups; }
            var group = player.RunState.Rng.Niche.NextItem(available)
                ?? throw new InvalidOperationException("No myth effect series is available.");
            var module = player.RunState.Rng.Niche.NextItem(group.ToList())
                ?? throw new InvalidOperationException("No myth effect module is available.");
            result.Add(module); remaining.Remove(module); usedSeries.Add(module.Series);
        }
        return Array.AsReadOnly(result.ToArray());
    }
}
