using MegaCrit.Sts2.Core.Entities.Cards;
using STS2RitsuLib.CardTags;
using STS2RitsuLib.Content;
using STS2RitsuLib.Interop.AutoRegistration;

namespace SelectSpecialCardMod.Scripts.Custom;

// —— 天杯龙系列 ——（层级：天杯龙 ⊃ 天杯龙怪兽／灿幻魔法；龙族为种族标记；同调为同调召唤标记）
[RegisterOwnedCardTag(nameof(TianBeiLong))]
[RegisterOwnedCardTag(nameof(CanHuanMoFa))]
[RegisterOwnedCardTag(nameof(LongZu))]
[RegisterOwnedCardTag(nameof(TongDiao))]
[RegisterOwnedCardTag(nameof(Xing3))]
[RegisterOwnedCardTag(nameof(Xing4))]
// 「驻场」：卡位于手牌时，适用驻场的效果
[RegisterOwnedCardTag(nameof(ZhuChang))]
// 「时机」：根据对应的时机，能够发动时机的效果
[RegisterOwnedCardTag(nameof(ShiJi))]
// 「登场」：一场战斗一次，卡通过非抽牌的方式加入手牌时触发
[RegisterOwnedCardTag(nameof(DengChang))]
// 「特殊效果」：右键手牌中拥有特殊效果的卡以发动其特殊效果
[RegisterOwnedCardTag(nameof(SpecialEffect))]
// 「荷鲁斯怪兽」：荷鲁斯系列怪兽卡
[RegisterOwnedCardTag(nameof(HeLuSiGuaiShou))]
// 「异解」系列（层级：异解 ⊃ 异解怪兽／异解魔陷）
[RegisterOwnedCardTag(nameof(YiJie))]
[RegisterOwnedCardTag(nameof(YiJieGuaiShou))]
[RegisterOwnedCardTag(nameof(YiJieMoXian))]
// 「送墓」：拥有送墓效果的卡（原「灵活」，效果不变）
[RegisterOwnedCardTag(nameof(SongMu))]
// 「应对」：受到敌人攻击前触发效果的卡（仅作代码标记，卡面用纯文本写"应对：…"）
[RegisterOwnedCardTag(nameof(YingDui))]
// 「额外怪兽」：来自游戏王、且属于额外卡组（融合/同调/超量/连接）的怪兽卡（描述留空）
[RegisterOwnedCardTag(nameof(ExtraMonster))]
[RegisterOwnedCardTag(nameof(ChaoLiang))]
// 「光恶魔」：天之月的种族/属性标记（描述留空）
[RegisterOwnedCardTag(nameof(GuangEMo))]
// 「Link2」：连接怪兽（LINK-2）标记（描述留空）
[RegisterOwnedCardTag(nameof(Link2))]
// 「Link1」：连接怪兽（LINK-1）标记（描述留空）
[RegisterOwnedCardTag(nameof(Link1))]
// 「Link3」：连接怪兽（LINK-3）标记（描述留空）
[RegisterOwnedCardTag(nameof(Link3))]
// 「融合怪兽」：融合怪兽的统一标记（「珠泪融合怪兽」＝珠泪怪兽＋融合怪兽，以此类推）
[RegisterOwnedCardTag(nameof(RongHeGuaiShou))]
// 刻魔系列（层级：刻魔 ⊃ 刻魔怪兽／刻魔魔法／刻魔陷阱）
[RegisterOwnedCardTag(nameof(KeMo))]
[RegisterOwnedCardTag(nameof(KeMoGuaiShou))]
[RegisterOwnedCardTag(nameof(KeMoMoFa))]
[RegisterOwnedCardTag(nameof(KeMoXianJing))]
// 珠泪系列（层级：珠泪 ⊃ 珠泪怪兽／珠泪魔法／珠泪陷阱；珠泪怪兽 ⊃ 珠泪融合怪兽／珠泪下级怪兽）
[RegisterOwnedCardTag(nameof(ZhuLei))]
[RegisterOwnedCardTag(nameof(ZhuLeiMoFa))]
[RegisterOwnedCardTag(nameof(ZhuLeiXianJing))]
[RegisterOwnedCardTag(nameof(ZhuLeiGuaiShou))]
[RegisterOwnedCardTag(nameof(ZhuLeiXiaJiGuaiShou))]
[RegisterOwnedCardTag(nameof(ZhuLeiRongHe))]
[RegisterOwnedCardTag(nameof(JuShe))]
[RegisterOwnedCardTag(nameof(YiXiangMing))]
[RegisterOwnedCardTag(nameof(YiXiangMingGuaiShou))]
[RegisterOwnedCardTag(nameof(YiXiangMingMoFa))]
[RegisterOwnedCardTag(nameof(YiXiangMingXianJing))]
[RegisterOwnedCardTag(nameof(LingBai))]
[RegisterOwnedCardTag(nameof(LingBaiEffect))]
[RegisterOwnedCardTag(nameof(KeDu3))]
[RegisterOwnedCardTag(nameof(KeDu5))]
public class SelectTags
{
    public static readonly CardTag TianBeiLong = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(TianBeiLong)).GetModCardTag();
    public static readonly CardTag CanHuanMoFa = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(CanHuanMoFa)).GetModCardTag();
    public static readonly CardTag LongZu = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(LongZu)).GetModCardTag();
    public static readonly CardTag TongDiao = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(TongDiao)).GetModCardTag();
    public static readonly CardTag ZhuChang = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(ZhuChang)).GetModCardTag();
    public static readonly CardTag ShiJi = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(ShiJi)).GetModCardTag();
    public static readonly CardTag DengChang = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(DengChang)).GetModCardTag();
    public static readonly CardTag CanHuanGuaiShou = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(CanHuanGuaiShou)).GetModCardTag();
    public static readonly CardTag Xing10 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(Xing10)).GetModCardTag();
    public static readonly CardTag Xing1 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(Xing1)).GetModCardTag();
    public static readonly CardTag Xing2 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(Xing2)).GetModCardTag();
    public static readonly CardTag Xing5 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(Xing5)).GetModCardTag();
    public static readonly CardTag Xing6 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(Xing6)).GetModCardTag();
    public static readonly CardTag Xing8 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(Xing8)).GetModCardTag();
    public static readonly CardTag Xing9 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(Xing9)).GetModCardTag();
    public static readonly CardTag Xing11 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(Xing11)).GetModCardTag();
    public static readonly CardTag Xing12 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(Xing12)).GetModCardTag();
    public static readonly CardTag TongBu = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(TongBu)).GetModCardTag();
    public static readonly CardTag Xing7 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(Xing7)).GetModCardTag();
    public static readonly CardTag TiaoZheng = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(TiaoZheng)).GetModCardTag();
    // 「三星 / 四星」：星级标记（同调效果的星级判定读的就是这些标签）
    public static readonly CardTag Xing3 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(Xing3)).GetModCardTag();
    public static readonly CardTag Xing4 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(Xing4)).GetModCardTag();
    // 「额外怪兽」：来自游戏王、且属于额外卡组（融合/同调/超量/连接）的怪兽卡
    public static readonly CardTag ExtraMonster = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(ExtraMonster)).GetModCardTag();
    public static readonly CardTag ChaoLiang = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(ChaoLiang)).GetModCardTag();
    public static readonly CardTag GuangEMo = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(GuangEMo)).GetModCardTag();
    public static readonly CardTag Link2 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(Link2)).GetModCardTag();
    // 「Link1」：连接怪兽（LINK-1）标记
    public static readonly CardTag Link1 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(Link1)).GetModCardTag();
    // 「Link3」：连接怪兽（LINK-3）标记
    public static readonly CardTag Link3 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(Link3)).GetModCardTag();
    // 「融合怪兽」：融合怪兽的统一标记（配合系列怪兽标签判断"某系列的融合怪兽"）
    public static readonly CardTag RongHeGuaiShou = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(RongHeGuaiShou)).GetModCardTag();
    // —— 刻魔系列 ——
    public static readonly CardTag KeMo = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(KeMo)).GetModCardTag();
    public static readonly CardTag KeMoGuaiShou = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(KeMoGuaiShou)).GetModCardTag();
    public static readonly CardTag KeMoMoFa = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(KeMoMoFa)).GetModCardTag();
    public static readonly CardTag KeMoXianJing = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(KeMoXianJing)).GetModCardTag();
    // 「特殊效果」：拥有可右键发动的特殊效果
    public static readonly CardTag SpecialEffect = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(SpecialEffect)).GetModCardTag();
    // 「荷鲁斯怪兽」：荷鲁斯系列怪兽卡
    public static readonly CardTag HeLuSiGuaiShou = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(HeLuSiGuaiShou)).GetModCardTag();
    // 「异解」：异解卡，包含异解怪兽卡和异解魔陷卡
    public static readonly CardTag YiJie = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(YiJie)).GetModCardTag();
    // 「异解怪兽」：异解卡中的怪兽卡
    public static readonly CardTag YiJieGuaiShou = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(YiJieGuaiShou)).GetModCardTag();
    // 「异解魔陷」：异解卡中的魔法·陷阱卡
    public static readonly CardTag YiJieMoXian = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(YiJieMoXian)).GetModCardTag();

    // 「送墓」：拥有可送墓触发的效果（原「灵活」）
    public static readonly CardTag SongMu = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(SongMu)).GetModCardTag();
    // 「应对」：受到敌人攻击前触发效果的卡（仅作代码标记，没有卡面文本）
    public static readonly CardTag YingDui = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(YingDui)).GetModCardTag();
    // —— 珠泪系列 ——
    public static readonly CardTag ZhuLei = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(ZhuLei)).GetModCardTag();
    public static readonly CardTag ZhuLeiMoFa = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(ZhuLeiMoFa)).GetModCardTag();
    public static readonly CardTag ZhuLeiXianJing = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(ZhuLeiXianJing)).GetModCardTag();
    public static readonly CardTag ZhuLeiGuaiShou = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(ZhuLeiGuaiShou)).GetModCardTag();
    // 「珠泪下级怪兽」= 珠泪怪兽里非融合的那部分（融合素材/检索对象用的就是它）
    public static readonly CardTag ZhuLeiXiaJiGuaiShou = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(ZhuLeiXiaJiGuaiShou)).GetModCardTag();
    // 「珠泪融合」：发动式关键字（塞壬／小美人鱼／梅洛）
    public static readonly CardTag ZhuLeiRongHe = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(ZhuLeiRongHe)).GetModCardTag();
    public static readonly CardTag JuShe = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(JuShe)).GetModCardTag();
    public static readonly CardTag YiXiangMing = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(YiXiangMing)).GetModCardTag();
    public static readonly CardTag YiXiangMingGuaiShou = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(YiXiangMingGuaiShou)).GetModCardTag();
    public static readonly CardTag YiXiangMingMoFa = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(YiXiangMingMoFa)).GetModCardTag();
    public static readonly CardTag YiXiangMingXianJing = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(YiXiangMingXianJing)).GetModCardTag();
    // 灵摆怪兽沿用原注册 ID，旧存档和旧引用仍识别为同一标签。
    public static readonly CardTag LingBaiGuaiShou = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(LingBai)).GetModCardTag();
    public static readonly CardTag LingBai = LingBaiGuaiShou;
    public static readonly CardTag LingBaiEffect = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(LingBaiEffect)).GetModCardTag();
    // 灵摆刻度与怪兽星级独立，供刻度条件判断使用。
    public static readonly CardTag KeDu3 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(KeDu3)).GetModCardTag();
    public static readonly CardTag KeDu5 = ModContentRegistry.GetQualifiedCardTagId(Entry.ModId, nameof(KeDu5)).GetModCardTag();
}
