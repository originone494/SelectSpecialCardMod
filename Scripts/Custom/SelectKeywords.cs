using MegaCrit.Sts2.Core.Entities.Cards;
using STS2RitsuLib.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Keywords;

namespace SelectSpecialCardMod.Scripts.Custom;

[RegisterOwnedCardKeyword(nameof(Retriever), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(CanHuanMoFa), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(TianBeiLong), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(LongZu), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(ZhuChang), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(ShiJi), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(DengChang), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(CanHuanGuaiShou), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Xing10), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Xing1), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Xing2), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Xing5), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Xing6), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Xing8), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Xing9), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Xing11), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Xing12), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(TongBu), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(TongDiao), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Xing7), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Xing3), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Xing4), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(TiaoZheng), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(ChuWai), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(ExtraMonster), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(GuangEMo), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Link2), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Link1), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(Link3), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
// 「融合怪兽」：融合怪兽的统一标记（「珠泪融合怪兽」＝珠泪怪兽＋融合怪兽，以此类推）
[RegisterOwnedCardKeyword(nameof(RongHeGuaiShou), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
// 刻魔系列（层级：刻魔 ⊃ 刻魔怪兽／刻魔魔法／刻魔陷阱）
[RegisterOwnedCardKeyword(nameof(KeMo), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(KeMoGuaiShou), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(KeMoMoFa), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(KeMoXianJing), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(SpecialEffect), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(HeLuSiGuaiShou), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(YiJie), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(YiJieGuaiShou), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(YiJieMoXian), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
// 「送墓」：卡因效果被送入弃牌堆时触发（原「灵活」，效果不变；机制见 SongMuHook）
[RegisterOwnedCardKeyword(nameof(SongMu), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
// 「应对」：不进卡牌的关键字列表，只在悬停提示里展示（卡面靠"应对：…"纯文本 + YingDui 标签承担）
[RegisterOwnedCardKeyword(nameof(YingDui), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
// —— 珠泪系列 ——（层级：珠泪 ⊃ 珠泪怪兽／珠泪魔法／珠泪陷阱；珠泪怪兽 ⊃ 珠泪融合怪兽／珠泪下级怪兽）
[RegisterOwnedCardKeyword(nameof(ZhuLei), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(ZhuLeiMoFa), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(ZhuLeiXianJing), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(ZhuLeiGuaiShou), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(ZhuLeiXiaJiGuaiShou), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(ZhuLeiRongHe), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
[RegisterOwnedCardKeyword(nameof(JuShe), CardDescriptionPlacement = ModKeywordCardDescriptionPlacement.BeforeCardDescription)]
public class SelectKeywords
{
    // 「检索」：从某一卡池中将若干张牌加入手牌
    public static readonly CardKeyword Retriever = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Retriever)).GetModCardKeyword();

    // 「灿幻魔法」：天杯龙系列的魔法卡
    public static readonly CardKeyword CanHuanMoFa = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(CanHuanMoFa)).GetModCardKeyword();

    // 「天杯龙」：系列总类
    public static readonly CardKeyword TianBeiLong = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(TianBeiLong)).GetModCardKeyword();

    // 「龙族」：龙族的怪兽卡
    public static readonly CardKeyword LongZu = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(LongZu)).GetModCardKeyword();

    // 「驻场」：卡位于手牌时，适用驻场的效果
    public static readonly CardKeyword ZhuChang = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(ZhuChang)).GetModCardKeyword();

    // 「时机」：根据对应的时机，能够发动时机的效果
    public static readonly CardKeyword ShiJi = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(ShiJi)).GetModCardKeyword();

    // 「登场」：一场战斗一次，卡通过非抽牌的方式加入手牌时触发
    public static readonly CardKeyword DengChang = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(DengChang)).GetModCardKeyword();

    // 「灿幻怪兽」：天杯龙系列的怪兽卡
    public static readonly CardKeyword CanHuanGuaiShou = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(CanHuanGuaiShou)).GetModCardKeyword();

    // 「十星」：星级标记
    public static readonly CardKeyword Xing10 = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Xing10)).GetModCardKeyword();
    public static readonly CardKeyword Xing1 = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Xing1)).GetModCardKeyword();
    public static readonly CardKeyword Xing2 = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Xing2)).GetModCardKeyword();
    public static readonly CardKeyword Xing5 = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Xing5)).GetModCardKeyword();
    public static readonly CardKeyword Xing6 = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Xing6)).GetModCardKeyword();
    public static readonly CardKeyword Xing8 = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Xing8)).GetModCardKeyword();
    public static readonly CardKeyword Xing9 = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Xing9)).GetModCardKeyword();
    public static readonly CardKeyword Xing11 = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Xing11)).GetModCardKeyword();
    public static readonly CardKeyword Xing12 = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Xing12)).GetModCardKeyword();

    // 「同步」：同调召唤相关标记
    public static readonly CardKeyword TongBu = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(TongBu)).GetModCardKeyword();

    // 「同调」：同调召唤规则（消耗触发卡与所选卡，按星级组合产出升龙/超龙；机制见 Tool/TongDiao）
    public static readonly CardKeyword TongDiao = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(TongDiao)).GetModCardKeyword();

    // 「七星」：星级标记
    public static readonly CardKeyword Xing7 = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Xing7)).GetModCardKeyword();

    // 「三星」：星级标记
    public static readonly CardKeyword Xing3 = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Xing3)).GetModCardKeyword();

    // 「四星」：星级标记
    public static readonly CardKeyword Xing4 = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Xing4)).GetModCardKeyword();

    // 「调整」：调整怪兽
    public static readonly CardKeyword TiaoZheng = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(TiaoZheng)).GetModCardKeyword();

    // 「除外」：移出战斗（参考原版「消耗」的描述风格）
    public static readonly CardKeyword ChuWai = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(ChuWai)).GetModCardKeyword();

    // 「额外怪兽」：描述留空
    public static readonly CardKeyword ExtraMonster = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(ExtraMonster)).GetModCardKeyword();

    public static readonly CardKeyword GuangEMo = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(GuangEMo)).GetModCardKeyword();

    public static readonly CardKeyword Link2 = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Link2)).GetModCardKeyword();

    public static readonly CardKeyword Link1 = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Link1)).GetModCardKeyword();

    public static readonly CardKeyword Link3 = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(Link3)).GetModCardKeyword();

    // 「融合怪兽」：融合怪兽的统一标记（配合系列怪兽标签判断"某系列的融合怪兽"）
    public static readonly CardKeyword RongHeGuaiShou = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(RongHeGuaiShou)).GetModCardKeyword();

    // 「刻魔」：刻魔系列总类（含「刻魔怪兽」）
    public static readonly CardKeyword KeMo = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(KeMo)).GetModCardKeyword();

    // 「刻魔怪兽」：刻魔卡中的怪兽卡
    public static readonly CardKeyword KeMoGuaiShou = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(KeMoGuaiShou)).GetModCardKeyword();

    // 「刻魔魔法」：刻魔卡中的魔法卡
    public static readonly CardKeyword KeMoMoFa = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(KeMoMoFa)).GetModCardKeyword();

    // 「刻魔陷阱」：刻魔卡中的陷阱卡
    public static readonly CardKeyword KeMoXianJing = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(KeMoXianJing)).GetModCardKeyword();

    // 「特殊效果」：右键手牌中拥有特殊效果的卡以发动其特殊效果（机制见 SpecialEffectHook）
    public static readonly CardKeyword SpecialEffect = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(SpecialEffect)).GetModCardKeyword();

    // 「荷鲁斯怪兽」：荷鲁斯系列怪兽卡（王之棺的检索/回收/弃牌伤害对象）
    public static readonly CardKeyword HeLuSiGuaiShou = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(HeLuSiGuaiShou)).GetModCardKeyword();

    // 「异解」：异解卡，包含异解怪兽卡和异解魔陷卡
    public static readonly CardKeyword YiJie = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(YiJie)).GetModCardKeyword();

    // 「异解怪兽」：异解卡中的怪兽卡
    public static readonly CardKeyword YiJieGuaiShou = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(YiJieGuaiShou)).GetModCardKeyword();

    // 「异解魔陷」：异解卡中的魔法·陷阱卡
    public static readonly CardKeyword YiJieMoXian = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(YiJieMoXian)).GetModCardKeyword();

    // 「送墓」：拥有送墓字段的牌，因效果被送入弃牌堆时触发（机制见 SongMuHook）
    public static readonly CardKeyword SongMu = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(SongMu)).GetModCardKeyword();

    // 「应对」：受到敌人攻击前发动（只用于悬停说明，不挂进 CanonicalKeywords）
    public static readonly CardKeyword YingDui = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(YingDui)).GetModCardKeyword();

    // 「珠泪」：系列总类，包含珠泪怪兽、珠泪魔法、珠泪陷阱
    public static readonly CardKeyword ZhuLei = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(ZhuLei)).GetModCardKeyword();

    // 「珠泪魔法」：珠泪卡中的魔法卡
    public static readonly CardKeyword ZhuLeiMoFa = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(ZhuLeiMoFa)).GetModCardKeyword();

    // 「珠泪陷阱」：珠泪卡中的陷阱卡
    public static readonly CardKeyword ZhuLeiXianJing = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(ZhuLeiXianJing)).GetModCardKeyword();

    // 「珠泪怪兽」：珠泪卡中的怪兽卡（含融合怪兽）
    public static readonly CardKeyword ZhuLeiGuaiShou = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(ZhuLeiGuaiShou)).GetModCardKeyword();

    // 「珠泪下级怪兽」：珠泪怪兽里非融合的那部分（只用于悬停说明，不挂进 CanonicalKeywords）
    public static readonly CardKeyword ZhuLeiXiaJiGuaiShou = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(ZhuLeiXiaJiGuaiShou)).GetModCardKeyword();

    // 「珠泪融合」：发动式关键字（塞壬／小美人鱼／梅洛），机制见 Tool/ZhuLeiFusion
    public static readonly CardKeyword ZhuLeiRongHe = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(ZhuLeiRongHe)).GetModCardKeyword();

    // 「俱舍」：珠泪·俱舍 的代码标记
    public static readonly CardKeyword JuShe = ModContentRegistry.GetQualifiedKeywordId(Entry.ModId, nameof(JuShe)).GetModCardKeyword();
}
