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
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「幻禄の天盃龍」（Tenpai Dragon Genroku）：
//   打出：造成1点伤害
//   特殊效果（右键手牌发动）：消耗这张卡，「检索」除自身以外的1张「天杯龙」，那张卡在这个回合可以免费打出
//   登场：这张卡可以免费打出，变为「调整」，等级上升1星（移除「三星」获得「四星」）
//   一回合只能打出一次，打出后返回手牌
//
// 登场的星级上升同时更新关键字与标签（同调效果的星级判定读的是标签）；
// 标签底层的 HashSet 直接变更（游戏注释说明标签默认不可变，这里是刻意为之的实例级变化）。
public class TianBeiLongHuanLuCard : SelectSpecialBaseCard, IDengChangCard, ISpecialEffectCard
{
    public TianBeiLongHuanLuCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    // 登场后可免费打出
    private bool _canFreePlay;

    // 登场后已完成星级上升
    private bool _leveledUp;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(1m, ValueProp.Move),
    ];

    public override int StarLevel => _leveledUp ? 4 : 3;

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.TianBeiLong,
        SelectTags.LongZu,
        ..StarTags,
        SelectTags.DengChang,
        SelectTags.SpecialEffect,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
    ];

    // 悬停提示随登场状态变化：三星 ↔（调整 +）四星
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => _leveledUp
        ? [
            HoverTipFactory.FromKeyword(SelectKeywords.TianBeiLong),
            HoverTipFactory.FromKeyword(SelectKeywords.LongZu),
            ..StarHoverTips,
            HoverTipFactory.FromKeyword(SelectKeywords.TiaoZheng),
            HoverTipFactory.FromKeyword(SelectKeywords.DengChang),
            HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
        ]
        : [
            HoverTipFactory.FromKeyword(SelectKeywords.TianBeiLong),
            HoverTipFactory.FromKeyword(SelectKeywords.LongZu),
            ..StarHoverTips,
            HoverTipFactory.FromKeyword(SelectKeywords.DengChang),
            HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
            HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
        ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");

        // 造成1点伤害
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    // 特殊效果：消耗这张卡，「检索」除自身以外的1张「天杯龙」，那张卡在这个回合可以免费打出
    public bool CanActivateSpecialEffect(Player player) => true;

    public async Task ActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    { await TryActivateSpecialEffect(choiceContext, player); }

    public async Task<bool> TryActivateSpecialEffect(PlayerChoiceContext choiceContext, Player player)
    {
        SelectSpecialCardMod.Scripts.Rules.EffectActivation.PaymentStarted();
        var paid = await CardCmd.Exhaust(choiceContext, this);
        if (paid?.success != true) return false;

        var retrieved = await SelectToolCmd.RetrieverCard(
            choiceContext,
            player,
            c => c.Tags.Contains(SelectTags.TianBeiLong) && c.Id != Id,
            p => p is SelectSpecialCardPool,
            1, source: this);
        foreach (var card in retrieved)
        {
            card.EnergyCost.SetThisTurn(0);   // 那张卡在这个回合可以免费打出
        }
        return true;
    }

    // 登场：这张卡可以免费打出，变为「调整」，等级上升1星
    public async Task DengChangSpecial(PlayerChoiceContext ctx, Player player)
    {
        _canFreePlay = true;

        AddKeyword(SelectKeywords.TiaoZheng);   // 变为「调整」
        RemoveKeyword(StarKeyword(3));          // 等级上升1星：3星 → 4星
        AddKeyword(StarKeyword(4));

        if (Tags is HashSet<CardTag> tags)
        {
            tags.Remove(StarTag(3));
            tags.Add(StarTag(4));
            tags.Remove(SelectTags.TiaoZheng);
            tags.Add(SelectTags.TiaoZheng);
        }

        _leveledUp = true;

        await Task.CompletedTask;
    }

    // 登场后免费打出
    public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (card != this || !_canFreePlay) return false;

        modifiedCost = 0m;
        return true;
    }

    // 一回合只能打出一次：按**这张卡自己**记
    private (CombatId? Combat, int Turn)? _playedThisTurn;

    protected override bool IsPlayable => Owner == null || _playedThisTurn != PerTurnOnce.CurrentKey(Owner);

    // 打出此卡后，返回手牌（一回合一次）
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;
        ShiJiTracker.ReportAttackPlayed(Owner, cardPlay);

        if (cardPlay.Card != this) return;

        var now = PerTurnOnce.CurrentKey(Owner);
        if (_playedThisTurn == now) return;
        _playedThisTurn = now;

        await CardPileCmd.Add(this, PileType.Hand);
    }
}
