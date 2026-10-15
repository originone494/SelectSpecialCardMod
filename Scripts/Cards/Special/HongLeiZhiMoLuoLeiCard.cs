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

// 红泪之魔 落泪（主卡组的「刻魔怪兽」）
//   打出：造成12点伤害，「检索」并消耗1张此卡以外的「刻魔」卡
//   时机（从消耗堆发动）：从消耗堆将1张「刻魔」连接怪兽加入手牌，那张卡在这个回合可以免费打出，
//         位于消耗堆的这张卡返回抽牌堆
public class HongLeiZhiMoLuoLeiCard : SelectSpecialBaseCard, IShiJiCard, ISpecialEffectCard
{
    public HongLeiZhiMoLuoLeiCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(12m, ValueProp.Move),
    ];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.KeMo,
        SelectTags.KeMoGuaiShou,
        SelectTags.GuangEMo,
        SelectTags.ShiJi,
        SelectTags.SpecialEffect,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.KeMo),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMoGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.GuangEMo),
        HoverTipFactory.FromKeyword(SelectKeywords.ShiJi),
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
    ];

    private static bool IsKeMo(CardModel c) => c.Tags.Contains(SelectTags.KeMo);

    // 「刻魔」连接怪兽 = 带「刻魔」且是连接怪兽（Link1 / Link2 / Link3 …）
    private static bool IsKeMoLinkMonster(CardModel c)
        => IsKeMo(c) && KeMoEquip.IsLinkMonster(c);

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成12点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);

    }

    // 特殊效果（右键手牌）：「检索」并消耗1张此卡以外的「刻魔」卡
    public bool CanActivateSpecialEffect(Player player) => true;

    public async Task ActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    { await TryActivateSpecialEffect(choiceContext, player); }

    public async Task<bool> TryActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    {
        var retrieved = await SelectToolCmd.RetrieverCard(
            choiceContext,
            player,
            c => IsKeMo(c) && c is not HongLeiZhiMoLuoLeiCard,
            p => p is SelectSpecialCardPool,
            1, source: this, isExhaust: true,
            minAmount: 1, requireManualConfirmation: true);   // 范围 (1,1)：必须选 1 张，只有 1 张候选也要自己点
        return retrieved.Count > 0;
    }

    // 时机：从消耗堆发动
    private static LocString ShiJiPrompt { get; } =
        new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_LUO_LEI_SHI_JI");

    public IEnumerable<PileType> ShiJiPiles => [PileType.Exhaust];

    public bool CanActivateShiJi(Player player)
        => PileType.Exhaust.GetPile(player).Cards.Any(IsKeMoLinkMonster);

    public async Task ActivateShiJi(PlayerChoiceContext ctx, Player player)
    {
        var exhaustPile = PileType.Exhaust.GetPile(player);
        if (!exhaustPile.Cards.Any(IsKeMoLinkMonster)) return;

        // CardPrefsConfirm：只有 1 张候选也不能替玩家自动选
        var picked = (await CardSelectCmd.FromCombatPile(
            ctx,
            exhaustPile,
            player,
            CardPrefsConfirm(this, ShiJiPrompt, 1, 1),
            filter: IsKeMoLinkMonster)).FirstOrDefault();
        if (picked == null) return;

        // 加入手牌，那张卡在这个回合可以免费打出
        await CardPileCmd.Add(picked, PileType.Hand);
        picked.EnergyCost.SetThisTurn(0);

        // 位于消耗堆的这张卡返回抽牌堆
        await SelectToolCmd.ReturnCardToPile(this, PileType.Draw);
    }
}
