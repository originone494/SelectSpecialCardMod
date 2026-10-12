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

// 刻魔·神圣棺（额外怪兽 · Link3）
//   打出：造成18点伤害
//   特殊效果（右键手牌发动）：从消耗堆选择1张连接怪兽以外的「光恶魔」怪兽加入手牌，
//         这张卡作为装备给那张卡装备——① 伤害上升「给卡装备的连接怪兽标记合计×6」
//         （神圣棺自己是 Link3，所以单独装备就是 +18），② 攻击拥有格挡的敌人时造成双倍伤害
public class KeMoShenShengGuanCard : SelectSpecialBaseCard, ISpecialEffectCard, IExtraDeckSummonCard
{
    public KeMoShenShengGuanCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(18m, ValueProp.Move),
    ];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ExtraMonster,
        SelectTags.Link3,
        SelectTags.KeMo,
        SelectTags.KeMoGuaiShou,
        SelectTags.GuangEMo,
        SelectTags.SpecialEffect,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.ExtraMonster),
        HoverTipFactory.FromKeyword(SelectKeywords.Link3),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMo),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMoGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.GuangEMo),
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成18点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    // 特殊效果：消耗堆里有"连接怪兽以外的「光恶魔」怪兽"才能发动
    private static LocString PickPrompt { get; } =
        new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_SHEN_SHENG_GUAN_PICK");

    public bool CanActivateSpecialEffect(Player player)
        => PileType.Exhaust.GetPile(player).Cards.Any(KeMoEquip.CanEquipTo);

    public async Task ActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    {
        var exhaustPile = PileType.Exhaust.GetPile(player);
        if (!exhaustPile.Cards.Any(KeMoEquip.CanEquipTo)) return;

        // CardPrefsConfirm：只有 1 张候选也不能替玩家自动选
        var picked = (await CardSelectCmd.FromCombatPile(
            choiceContext,
            exhaustPile,
            player,
            CardPrefsConfirm(this, PickPrompt, 1, 1),
            filter: KeMoEquip.CanEquipTo)).FirstOrDefault();
        if (picked == null) return;

        // 加入手牌
        await CardPileCmd.Add(picked, PileType.Hand);

        // 这张卡（神圣棺）作为装备给那张卡装备：Link3 → 伤害 +3×6 = +18，
        // 并获得「攻击拥有格挡的敌人时造成双倍伤害」
        await KeMoEquip.EquipTo(this, picked, 3, doubleVsBlock: true);
    }

    // 召唤条件：消耗包含「光恶魔」的攻击卡 2 张以上（Link3 = 3 点：3 张普通，或 1 张 Link2 + 1 张普通）
    // 「包含光恶魔」= 选中的素材里至少 1 张是「光恶魔」，其余可以是任意攻击卡
    private static LocString SummonCostPrompt { get; } =
        new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_SHEN_SHENG_GUAN_SUMMON_COST");

    private static List<CardModel> LinkMaterials(Player player)
        => PileType.Hand.GetPile(player).Cards
            .Where(c => c.Type == CardType.Attack)
            .ToList();

    public bool CanSummonFromExtraDeck(Player player)
        => KeMoLinkSummon.CanPay(LinkMaterials(player), 3, minCards: 2, requireGuangEMo: true);

    public async Task<bool> PaySummonCost(PlayerChoiceContext ctx, Player player)
    {
        var chosen = await KeMoLinkSummon.SelectMaterial(
            ctx, player, LinkMaterials(player), 3, minCards: 2, requireGuangEMo: true,
            SummonCostPrompt, this);
        if (chosen == null) return false;

        await SelectToolCmd.ExhaustCards(ctx, chosen);
        return true;
    }
}
