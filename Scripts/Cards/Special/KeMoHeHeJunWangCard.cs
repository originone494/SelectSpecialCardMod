using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
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

// 刻魔·赫赫君王（额外怪兽 · 融合怪兽）
//   融合素材：「刻魔融合怪兽」+融合·连接怪兽
//   打出：造成30点伤害
//   登场：可以选择丢弃1张卡（是/否），「检索」并消耗1张「光恶魔」
//   消耗（这张卡被消耗时）：从消耗堆·除外堆将1张其他「刻魔」卡加入手牌
public class KeMoHeHeJunWangCard : SelectSpecialBaseCard, IDengChangCard, IKeMoFusionMonster
{
    public KeMoHeHeJunWangCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(30m, ValueProp.Move),
    ];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.KeMo,
        SelectTags.KeMoGuaiShou,
        SelectTags.RongHeGuaiShou,
        SelectTags.GuangEMo,
        SelectTags.DengChang,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.KeMo),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMoGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.RongHeGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.GuangEMo),
        HoverTipFactory.FromKeyword(SelectKeywords.DengChang),
    ];

    // 融合素材：「刻魔融合怪兽」×1（= 刻魔怪兽 ＋ 融合怪兽）+ 「融合·连接怪兽」×1（任意融合怪兽或连接怪兽）
    public IReadOnlyList<KeMoFusionMaterial> FusionMaterials =>
    [
        new(c => c.Tags.Contains(SelectTags.KeMoGuaiShou) && c.Tags.Contains(SelectTags.RongHeGuaiShou), 1),
        new(c => c.Tags.Contains(SelectTags.RongHeGuaiShou) || KeMoEquip.IsLinkMonster(c), 1),
    ];

    private static bool IsGuangEMo(CardModel c) => c.Tags.Contains(SelectTags.GuangEMo);

    private static bool IsKeMo(CardModel c) => c.Tags.Contains(SelectTags.KeMo);

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成30点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    // 登场：是/否 → 是则丢弃1张卡（参考珠泪·水仙人鱼的是/否）；之后「检索」并消耗1张「光恶魔」
    private static LocString DiscardChoicePrompt { get; } =
        new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_HE_HE_JUN_WANG_DISCARD");

    public async Task DengChangSpecial(PlayerChoiceContext ctx, Player player)
    {
        var hand = PileType.Hand.GetPile(player);
        if (hand != null && hand.Cards.Count > 0
            && await SelectToolCmd.AskYesNo(ctx, player, DiscardChoicePrompt, source: this))
        {
            var toDiscard = (await CardSelectCmd.FromHandForDiscard(
                prefs: CardPrefs(this, DiscardNamedPrompt, 1, 1),
                context: ctx,
                player: player,
                filter: _ => true,
                source: null!)).FirstOrDefault();
            if (toDiscard != null) await CardCmd.Discard(ctx, toDiscard);
        }

        // 「检索」并消耗1张「光恶魔」
        await SelectToolCmd.RetrieverCard(
            ctx,
            player,
            IsGuangEMo,
            p => p is SelectSpecialCardPool,
            1, source: this, isExhaust: true);
    }

    // 「消耗」：这张卡被消耗时触发 —— 从消耗堆·除外堆将1张其他「刻魔」卡加入手牌
    private static LocString ConsumePrompt { get; } =
        new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_HE_HE_JUN_WANG_CONSUME");
    public override async Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
    {
        await base.AfterCardExhausted(choiceContext, card, causedByEthereal);
        if (card != this) return;
        if (Owner == null) return;
        if (CombatManager.Instance.IsOverOrEnding) return;

        // 其他「刻魔」卡：带「刻魔」标签且不是这张卡
        static bool IsOtherKeMo(CardModel c, CardModel self)
            => c != self && c.Tags.Contains(SelectTags.KeMo);

        // 消耗堆·除外堆：只有一堆有卡就直接从那堆选，两堆都有才问"是否从消耗堆将卡加入手牌？"
        var selected = await SelectToolCmd.PickFromTwoPiles(
            choiceContext, Owner, c => IsOtherKeMo(c, this),
            PileType.Exhaust, Entry.ChuWaiPile,
            SelectToolCmd.ExhaustOrChuWaiChoicePrompt, ConsumePrompt, this);
        foreach (var picked in selected)
        {
            await CardPileCmd.Add(picked, PileType.Hand);
        }
    }
}
