using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 幽世断离：从消耗堆选择1张卡加入手牌。选择1张手牌除外的情况下，可以从除外堆选择1张卡送入消耗堆。
public class YouShiDuanLiCard : SelectSpecialBaseCard
{
    public YouShiDuanLiCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // ① 从消耗堆选择1张卡加入手牌（消耗堆为空或未选择则停止）
        var exhaustPile = PileType.Exhaust.GetPile(Owner);
        if (exhaustPile.Cards.Count == 0) return;

        var retrieved = (await CardSelectCmd.FromSimpleGrid(
            choiceContext, exhaustPile.Cards.ToList(), Owner, CardPrefs(this, SelectionScreenPrompt, 1, 1))).ToList();
        if (retrieved.Count == 0) return;

        await CardPileCmd.Add(retrieved[0], PileType.Hand);

        // ② 选择1张手牌除外（可选，未选择则结束）
        var exiled = (await CardSelectCmd.FromHand(
            prefs: CardPrefs(this, SelectionScreenPrompt, 0, 1),
            context: choiceContext,
            player: Owner,
            filter: null,
            source: this)).ToList();
        if (exiled.Count == 0) return;

        await CardPileCmd.Add(exiled[0], Entry.ChuWaiPile);

        // ③ 从除外堆选择1张卡送入消耗堆（除外堆为空则跳过）
        var chuWaiPile = Entry.ChuWaiPile.GetPile(Owner);
        if (chuWaiPile.Cards.Count == 0) return;

        var moved = (await CardSelectCmd.FromCombatPile(
            choiceContext,
            chuWaiPile,
            Owner,
            CardPrefs(this, SelectionScreenPrompt, 1, 1))).ToList();
        if (moved.Count == 0) return;

        await CardPileCmd.Add(moved[0], PileType.Exhaust);
    }
}
