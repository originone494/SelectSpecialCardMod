using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 刻魔·大圣棺（额外怪兽）
//   召唤条件：消耗包含「光恶魔」的攻击卡×2（手牌满足时，右键额外堆可以手动召唤）
//   打出：造成12点伤害
//   特殊效果（右键手牌发动）：从额外堆选1只素材凑得齐的「刻魔融合怪兽」→ 一张一张从消耗堆选素材
//             （可以中途取消）→ 素材返回抽牌堆·额外堆，那只融合怪兽加入手牌
public class KeMoDaShengGuanCard : SelectSpecialBaseCard, IExtraDeckSummonCard, ISpecialEffectCard, IShiJiCard
{
    public KeMoDaShengGuanCard() : base(0, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(12m, ValueProp.Move),
    ];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ExtraMonster,
        SelectTags.GuangEMo,
        SelectTags.Link2,
        SelectTags.KeMoGuaiShou,
        SelectTags.KeMo,
        SelectTags.SpecialEffect,
        SelectTags.ShiJi,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.ExtraMonster),
        HoverTipFactory.FromKeyword(SelectKeywords.GuangEMo),
        HoverTipFactory.FromKeyword(SelectKeywords.Link2),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMoGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMo),
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
        HoverTipFactory.FromKeyword(SelectKeywords.ShiJi),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成12点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    // 召唤条件：消耗包含「光恶魔」的攻击卡×2（Link2 = 连接值凑满 2、至少 2 张，且至少 1 张是「光恶魔」）
    // 素材选择用「连接召唤」那套「逐张选 + 动态筛选」（与大圣棺的特殊效果、神圣棺的 Link3 同源）
    private static LocString SummonCostPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_DA_SHENG_GUAN_SUMMON_COST");

    private static List<CardModel> LinkMaterials(Player player)
        => PileType.Hand.GetPile(player).Cards.Where(c => c.Type == CardType.Attack).ToList();

    public bool CanSummonFromExtraDeck(Player player)
        => KeMoLinkSummon.CanPay(LinkMaterials(player), 2, minCards: 2, requireGuangEMo: true);

    public async Task<bool> PaySummonCost(PlayerChoiceContext ctx, Player player)
    {
        var chosen = await KeMoLinkSummon.SelectMaterial(
            ctx, player, LinkMaterials(player), 2, minCards: 2, requireGuangEMo: true,
            SummonCostPrompt, this);
        if (chosen == null) return false;

        await SelectToolCmd.ExhaustCards(ctx, chosen);
        return true;
    }

    // 特殊效果：额外堆里没有"素材凑得齐"的融合怪兽 → 不发动、也不亮
    private static LocString PickMonsterPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_FUSION_PICK_MONSTER");
    private static LocString PickMaterialPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_FUSION_PICK_MATERIAL");

    public bool CanActivateSpecialEffect(Player player)
        => KeMoFusion.HasCandidate(player, KeMoFusion.PilePool(PileType.Exhaust));

    public async Task ActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    {
        await KeMoFusion.TryFusion(choiceContext, player, this, PickMonsterPrompt, PickMaterialPrompt,
            KeMoFusion.PilePool(PileType.Exhaust), KeMoFusionDestination.Deck);
    }

    // 时机：只能从消耗堆发动。把手牌 1 张「光恶魔（连接怪兽除外）」装上这件装备（伤害 +12）
    public IEnumerable<PileType> ShiJiPiles => [PileType.Exhaust];

    public bool CanActivateShiJi(Player player)
        => PileType.Hand.GetPile(player).Cards.Any(KeMoEquip.CanEquipTo);

    public async Task ActivateShiJi(PlayerChoiceContext ctx, Player player)
    {
        await KeMoEquip.Equip(ctx, player, this, 2, this);   // Link2 → +2×6 = +12
    }
}
