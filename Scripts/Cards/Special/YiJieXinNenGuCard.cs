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

// 异解·欣嫩谷：获得格挡，向除外堆加入1张随机卡，之后可从除外堆取回最多2张异解怪兽卡
public class YiJieXinNenGuCard : SelectSpecialBaseCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new BlockVar(10m, ValueProp.Move),
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.YiJie),
        HoverTipFactory.FromKeyword(SelectKeywords.YiJieGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.YiJieMoXian),
    ];

    public YiJieXinNenGuCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override HashSet<CardTag> CanonicalTags => [SelectTags.YiJie, SelectTags.YiJieGuaiShou, SelectTags.YiJieMoXian];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 1. 获得格挡
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

        // 2. 向除外堆加入1张随机卡（全卡池完全随机）
        await YiJieExileBag.DrawToChuWai(Owner, 1);

        // 3. 从除外堆将最多2张「异解怪兽」卡加入手牌（可以不选）
        var prefs = CardPrefs(this, SelectionScreenPrompt, 0, 2);
        var selected = await CardSelectCmd.FromCombatPile(
            choiceContext,
            Entry.ChuWaiPile.GetPile(Owner),
            Owner,
            prefs,
            filter: c => c.Tags.Contains(SelectTags.YiJieGuaiShou));

        foreach (var card in selected)
        {
            await CardPileCmd.Add(card, PileType.Hand);
        }
    }

    // 手牌没有其他攻击牌的情况下，这张卡可以免费打出
    public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (card != this || originalCost <= 0m) return false;
        if (PileType.Hand.GetPile(card.Owner).Cards.Count(c => c != card && c.Type == CardType.Attack) > 0) return false;

        modifiedCost = 0m;
        return true;
    }
}