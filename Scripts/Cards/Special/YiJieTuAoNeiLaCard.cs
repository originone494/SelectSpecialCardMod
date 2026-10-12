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

// 异解·图奥内拉：获得格挡，向除外堆加入3张随机卡，之后可从除外堆打出1张异解怪兽卡
public class YiJieTuAoNeiLaCard : SelectSpecialBaseCard
{
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new BlockVar(15m, ValueProp.Move),
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.YiJie),
        HoverTipFactory.FromKeyword(SelectKeywords.YiJieGuaiShou),
    ];

    public YiJieTuAoNeiLaCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override HashSet<CardTag> CanonicalTags => [SelectTags.YiJie, SelectTags.YiJieGuaiShou];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 1. 获得格挡
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);

        // 2. 向除外堆加入3张随机卡（全卡池完全随机，各抽各的）
        await YiJieExileBag.DrawToChuWai(Owner, 3);

        // 3. 从除外堆选择1张「异解怪兽」卡打出（可以不选）
        var prefs = CardPrefs(this, SelectionScreenPrompt, 0, 1);
        var picked = (await CardSelectCmd.FromCombatPile(
            choiceContext,
            Entry.ChuWaiPile.GetPile(Owner),
            Owner,
            prefs,
            filter: c => c.Tags.Contains(SelectTags.YiJieGuaiShou))).FirstOrDefault();

        if (picked != null)
        {
            await CardCmd.AutoPlay(choiceContext, picked, null);
        }
    }

    // 敌人数量大于1的情况下，这张卡可以免费打出
    public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (card != this || originalCost <= 0m) return false;
        if ((card.Owner.Creature.CombatState?.HittableEnemies.Count ?? 0) <= 1) return false;

        modifiedCost = 0m;
        return true;
    }
}