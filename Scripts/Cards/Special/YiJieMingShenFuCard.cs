using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 异解·冥神府：向除外堆加入7张随机卡，造成伤害，抽牌数随除外堆中的异解卡数量增长
public class YiJieMingShenFuCard : SelectSpecialBaseCard
{
    private const int _randomCards = 7;
    private const int _baseDraw = 2;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(18m, ValueProp.Move),
    ];

    public YiJieMingShenFuCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override HashSet<CardTag> CanonicalTags => [SelectTags.YiJie, SelectTags.YiJieGuaiShou];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");

        // 1. 向除外堆加入7张随机卡（全卡池完全随机，各抽各的）
        await YiJieExileBag.DrawToChuWai(Owner, _randomCards);

        // 2. 造成伤害
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, cardPlay).Targeting(cardPlay.Target)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);

        // 3. 抽2张牌；除外堆中每有1张「异解」卡，额外多抽1张（在7张随机卡入堆之后统计）
        int yiJieCount = Entry.ChuWaiPile.GetPile(Owner).Cards.Count(c => c.Tags.Contains(SelectTags.YiJie));
        await CardPileCmd.Draw(choiceContext, _baseDraw + yiJieCount, Owner);
    }
}