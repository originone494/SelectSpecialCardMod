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
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 刻魔·镇魂棺（额外怪兽）
//   召唤条件：消耗光恶魔怪兽×1。对这张卡的召唤一回合只能进行一次。
//             （手牌有「光恶魔」怪兽、且本回合还没召唤过时，右键额外堆可以手动召唤）
//   打出：造成6点伤害
//   特殊效果（右键手牌发动）：消耗这张卡，「检索」1张「刻魔怪兽」卡，那张卡在这个回合可以免费打出
public class KeMoZhenHunGuanCard : SelectSpecialBaseCard, ISpecialEffectCard, IExtraDeckSummonCard, IShiJiCard
{
    public KeMoZhenHunGuanCard() : base(0, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(6m, ValueProp.Move),
    ];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ExtraMonster,
        SelectTags.GuangEMo,
        SelectTags.Link1,
        SelectTags.KeMoGuaiShou,
        SelectTags.KeMo,
        SelectTags.SpecialEffect,
        SelectTags.ShiJi,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.ExtraMonster),
        HoverTipFactory.FromKeyword(SelectKeywords.GuangEMo),
        HoverTipFactory.FromKeyword(SelectKeywords.Link1),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMoGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMo),
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
        HoverTipFactory.FromKeyword(SelectKeywords.ShiJi),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成6点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    // 召唤条件：手牌的「光恶魔」怪兽至少 1 张；「对这张卡的召唤一回合只能进行一次」按同名卡记
    private static LocString SummonCostPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_ZHEN_HUN_GUAN_SUMMON_COST");

    private string SummonOnceKey => PerTurnOnce.Key("ExtraDeckSummon", Id.Entry);

    public bool CanSummonFromExtraDeck(Player player)
        => !PerTurnOnce.IsUsed(player, SummonOnceKey)
           && PileType.Hand.GetPile(player).Cards.Any(c => c.Tags.Contains(SelectTags.GuangEMo));

    public async Task<bool> PaySummonCost(PlayerChoiceContext ctx, Player player)
    {
        // 从手牌选择要消耗的光恶魔怪兽：范围 (0,1)——即使手牌只有 1 张，也必须玩家自己选；
        // 不选够 1 张点确认即等于放弃，什么都不发生（与天之月同一套写法）。
        var prompt = new LocString(SummonCostPrompt.LocTable, SummonCostPrompt.LocEntryKey);
        prompt.Add("CardName", Title);
        var chosen = (await CardSelectCmd.FromHand(
            prefs: new CardSelectorPrefs(prompt, 0, 1) { RequireManualConfirmation = true },
            context: ctx,
            player: player,
            filter: c => c.Tags.Contains(SelectTags.GuangEMo),
            source: this)).ToList();
        if (chosen.Count < 1) return false;

        await SelectToolCmd.ExhaustCards(ctx, chosen);

        // 对这张卡的召唤一回合只能进行一次（同名卡共享一次；换回合自动失效）
        PerTurnOnce.Mark(player, SummonOnceKey);
        return true;
    }

    // 特殊效果：消耗这张卡，「检索」1张「刻魔怪兽」卡，那张卡在这个回合可以免费打出
    public bool CanActivateSpecialEffect(Player player) => true;

    public async Task ActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    {
        await CardCmd.Exhaust(choiceContext, this);

        var retrieved = await SelectToolCmd.RetrieverCard(
            choiceContext,
            player,
            c => c.Tags.Contains(SelectTags.KeMoGuaiShou),
            p => p is SelectSpecialCardPool,
            1, source: this);
        foreach (var card in retrieved)
        {
            card.EnergyCost.SetThisTurn(0);
        }
    }

    // 时机：只能从消耗堆发动。把手牌 1 张「光恶魔（连接怪兽除外）」装上这件装备（伤害 +6）
    public IEnumerable<PileType> ShiJiPiles => [PileType.Exhaust];

    public bool CanActivateShiJi(Player player)
        => PileType.Hand.GetPile(player).Cards.Any(KeMoEquip.CanEquipTo);

    public async Task ActivateShiJi(PlayerChoiceContext ctx, Player player)
    {
        await KeMoEquip.Equip(ctx, player, this, 1, this);   // Link1 → +1×6 = +6
    }
}
