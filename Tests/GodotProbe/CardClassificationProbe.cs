using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

public static class CardClassificationProbe
{
    public static void Check()
    {
        // 本次迁移前的16张额外怪兽必须全部保留分类，其他已注册卡不能因机制标签被误分类。
        Type[] expected =
        [
            typeof(CanHuanShengLongShuangChaTianLongCard), typeof(CanHuanChaoLongSanJiTianLongCard),
            typeof(KeMoZhenHunGuanCard), typeof(KeMoDaShengGuanCard), typeof(KeMoShenShengGuanCard), typeof(TianZhiYueCard),
            typeof(KeMoHeHeJunWangCard), typeof(KeMoLuoLeiRiZiCard), typeof(KeMoZhenNuRiZiCard),
            typeof(ZhuLeiShuiXianCard), typeof(ZhuLeiKaLeiDuoHaTeCard), typeof(ZhuLeiLuShaKaCard),
            typeof(YiXiangMingYiShenGuangYaoTianShiCard), typeof(YiXiangMingShenYiFengBaoEMoCard),
            typeof(SiHuaLiaoLuanZhiLingShiCard), typeof(No41NiShuiMoShouCard),
        ];
        var actual = ModelDb.AllCards.Where(c => c is SelectSpecialBaseCard && CardClassification.IsExtraMonster(c))
            .Select(c => c.GetType()).ToHashSet();
        if (!actual.SetEquals(expected)) throw new Exception("Extra-monster classification changed its card inventory.");

        foreach (var (card, rating) in new (CardModel, int)[]
        {
            (ModelDb.Card<KeMoZhenHunGuanCard>(), 1), (ModelDb.Card<TianZhiYueCard>(), 2),
            (ModelDb.Card<KeMoShenShengGuanCard>(), 3), (ModelDb.Card<SiHuaLiaoLuanZhiLingShiCard>(), 4),
        })
            if (CardClassification.LinkRating(card) != rating || KeMoEquip.LinkRating(card) != rating)
                throw new Exception("Shared link rating no longer matches material counting.");

        var xyz = ModelDb.Card<No41NiShuiMoShouCard>();
        if (!xyz.Tags.Contains(SelectTags.ChaoLiangGuaiShou) || !xyz.Tags.Contains(SelectTags.ChaoLiang)
            || xyz.StarLevel != 0 || xyz.XyzRank != 4)
            throw new Exception("No.41 lost its type, summon mechanism or independent rank.");
        foreach (var card in new CardModel[] { ModelDb.Card<CanHuanShengLongShuangChaTianLongCard>(), ModelDb.Card<CanHuanChaoLongSanJiTianLongCard>() })
            if (!card.Tags.Contains(SelectTags.TongBuGuaiShou) || !card.Tags.Contains(SelectTags.TongBu))
                throw new Exception("A synchro dragon lost its monster type or existing mechanism tag.");

        Type[] mainDeck = [typeof(ZhuLeiLeiNuoHaTeCard), typeof(ZhuLeiSaiRenCard), typeof(ZhuLeiXiaoMeiCard),
            typeof(ZhuLeiMeiLuoCard), typeof(ZhuLeiJuSheCard), typeof(YiXiangMingTianShiZhiShengCard),
            typeof(YiXiangMingEMoZhiShengCard), typeof(YaoJingChuanJiBaiXueCard)];
        foreach (var type in mainDeck)
            if (CardClassification.IsExtraMonster(ModelDb.GetById<CardModel>(ModelDb.GetId(type))))
                throw new Exception("A main-deck or pendulum monster was incorrectly excluded from retrieval.");
        var pearlMonsters = ModelDb.AllCards.Where(ZhuLeiFilter.IsMonster).ToList();
        if (pearlMonsters.Count != 8 || pearlMonsters.Count(c => !CardClassification.IsExtraMonster(c)) != 5)
            throw new Exception("Pearl-monster retrieval no longer has five main-deck candidates.");

        var frame = AccessTools.Method(typeof(ExtraDeckSummonHook), "CardFrameGroup");
        foreach (var (card, group) in new (CardModel, int)[]
        {
            (ModelDb.Card<ZhuLeiShuiXianCard>(), 0), (ModelDb.Card<CanHuanShengLongShuangChaTianLongCard>(), 1),
            (xyz, 2), (ModelDb.Card<SiHuaLiaoLuanZhiLingShiCard>(), 3), (ModelDb.Card<YiXiangMingTianShiZhiShengCard>(), 4),
        })
            if (!Equals(frame.Invoke(null, [card]), group)) throw new Exception("Extra-deck face grouping changed.");
        GD.Print("PASS: 16 extra monsters retain classification; five main-deck Pearl monsters remain retrievable; type tags, Link1-4 and face groups agree.");
    }
}
