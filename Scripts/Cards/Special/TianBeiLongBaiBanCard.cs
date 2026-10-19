using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Rules;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「天盃龍パイドラ」（Tenpai Dragon Paidra）：
//   打出：造成9点伤害，之后进行一次「同调」。
//   特殊效果：一场战斗一次，「检索」1张「灿幻魔法」卡（取消选择不消耗次数）。
public class TianBeiLongBaiBanCard : SelectSpecialBaseCard, ISpecialEffectCard
{
    public TianBeiLongBaiBanCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(9m, ValueProp.Move),
    ];

    public override int StarLevel => 3;

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.TianBeiLong,
        SelectTags.LongZu,
        ..StarTags,
        SelectTags.SpecialEffect,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.TianBeiLong),
        HoverTipFactory.FromKeyword(SelectKeywords.LongZu),
        ..StarHoverTips,
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
        HoverTipFactory.FromKeyword(SelectKeywords.TongDiao),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");

        // 造成9点伤害
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);

        await TongDiao.TryTongDiao(choiceContext, Owner, this);
    }

    // 沿用现有检索的战斗次数账本，改由特殊效果发动。
    private static string SearchKey(CardModel card) => PerCombatOnce.Key("BaiBanSearch", card.Id.ToString());

    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner != null) ShiJiTracker.ReportAttackPlayed(Owner, cardPlay);
        return base.AfterCardPlayed(choiceContext, cardPlay);
    }

    public bool CanActivateSpecialEffect(Player player)
        => Owner == player && !PerCombatOnce.IsUsed(player, SearchKey(this));

    public async Task ActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    { await TryActivateSpecialEffect(choiceContext, player); }

    public async Task<bool> TryActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    {
        if (!CanActivateSpecialEffect(player)) return false;
        var retrieved = await SelectToolCmd.RetrieverCard(
            choiceContext, player, c => c.Tags.Contains(SelectTags.CanHuanMoFa),
            p => p is SelectSpecialCardPool, 1, source: this);
        if (retrieved.Count == 0) return false;
        PerCombatOnce.Mark(player, SearchKey(this));
        return true;
    }
}
