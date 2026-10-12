using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 魔轰神鲁力
//   打出：造成2点伤害
//   送墓（= 本卡因效果被送入弃牌堆时触发）：将这张卡加入手牌，在这个回合可以免费打出
public class MoHongShenLuLiCard : SelectSpecialBaseCard, ISongMuCard
{
    public MoHongShenLuLiCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(2m, ValueProp.Move),
    ];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.GuangEMo,
        SelectTags.SongMu,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.GuangEMo),
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成2点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    // 送墓：将这张卡加入手牌，在这个回合可以免费打出
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        await CardPileCmd.Add(this, PileType.Hand);
        EnergyCost.SetThisTurn(0);
    }
}
