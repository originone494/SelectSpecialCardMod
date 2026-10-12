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
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 异解·谢屋尔：造成伤害，向除外堆加入2张随机卡，之后可从除外堆取回最多2张异解魔陷卡
public class YiJieXieWuErCard : SelectSpecialBaseCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(12m, ValueProp.Move),
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.YiJie),
        HoverTipFactory.FromKeyword(SelectKeywords.YiJieGuaiShou),
    ];

    public YiJieXieWuErCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override HashSet<CardTag> CanonicalTags => [SelectTags.YiJie, SelectTags.YiJieGuaiShou];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");

        // 1. 造成伤害
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, cardPlay).Targeting(cardPlay.Target)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);

        // 2. 向除外堆加入2张随机卡（全卡池完全随机，各抽各的）
        await YiJieExileBag.DrawToChuWai(Owner, 2);

        // 3. 从除外堆将最多2张「异解魔陷」卡加入手牌（可以不选）
        var prefs = CardPrefs(this, SelectionScreenPrompt, 0, 2);
        var selected = await CardSelectCmd.FromCombatPile(
            choiceContext,
            Entry.ChuWaiPile.GetPile(Owner),
            Owner,
            prefs,
            filter: c => c.Tags.Contains(SelectTags.YiJieMoXian));

        foreach (var card in selected)
        {
            await CardPileCmd.Add(card, PileType.Hand);
        }
    }

    // 手牌存在其他攻击牌的情况下，这张卡可以免费打出
    public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (card != this || originalCost <= 0m) return false;
        if (PileType.Hand.GetPile(card.Owner).Cards.Count(c => c != card && c.Type == CardType.Attack) == 0) return false;

        modifiedCost = 0m;
        return true;
    }
}