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
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「天盃龍ファドラ」（Tenpai Dragon Fadra）：
//   打出：造成8点伤害
//   登场：从消耗堆将1张「天杯龙」加入手牌
//   一回合只能打出一次，打出后返回手牌，之后可以进行一次「同调」
public class TianBeiLongLvFaCard : SelectSpecialBaseCard, IDengChangCard
{
    public TianBeiLongLvFaCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(8m, ValueProp.Move),
    ];

    public override int StarLevel => 3;

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.TianBeiLong,
        SelectTags.LongZu,
        ..StarTags,
        SelectTags.DengChang,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.TianBeiLong),
        HoverTipFactory.FromKeyword(SelectKeywords.LongZu),
        ..StarHoverTips,
        HoverTipFactory.FromKeyword(SelectKeywords.DengChang),
        HoverTipFactory.FromKeyword(SelectKeywords.TongDiao),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");

        // 造成8点伤害
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    // 登场选卡提示
    private static LocString DengChangPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_TIAN_BEI_LONG_LV_FA_DENG_CHANG");

    // 登场：从消耗堆将1张「天杯龙」加入手牌
    public async Task DengChangSpecial(PlayerChoiceContext ctx, Player player)
    {
        var exhaustPile = PileType.Exhaust.GetPile(player);
        if (!exhaustPile.Cards.Any(c => c.Tags.Contains(SelectTags.TianBeiLong))) return;

        var picked = (await CardSelectCmd.FromCombatPile(
            ctx,
            exhaustPile,
            player,
            CardPrefs(this, DengChangPrompt, 1, 1),
            filter: c => c.Tags.Contains(SelectTags.TianBeiLong))).FirstOrDefault();

        if (picked != null)
        {
            await CardPileCmd.Add(picked, PileType.Hand);
        }
    }

    // 一回合只能打出一次：按**这张卡自己**记（文本写的是「一回合一次」，不是「同名卡一回合一次」）
    private (CombatId? Combat, int Turn)? _playedThisTurn;

    protected override bool IsPlayable => Owner == null || _playedThisTurn != PerTurnOnce.CurrentKey(Owner);

    // 打出此卡后，返回手牌，之后可以进行一次「同调」（一回合一次）
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;
        ShiJiTracker.ReportAttackPlayed(Owner, cardPlay);

        if (cardPlay.Card != this) return;

        var now = PerTurnOnce.CurrentKey(Owner);
        if (_playedThisTurn == now) return;
        _playedThisTurn = now;

        // 打出后返回手牌
        await CardPileCmd.Add(this, PileType.Hand);

        // 可以进行一次「同调」
        await TongDiao.TryTongDiao(choiceContext, Owner, this);
    }
}