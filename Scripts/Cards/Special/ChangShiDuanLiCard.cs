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
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 常世断离：两段分别选择最多5张，均可不选；第一段为空不影响第二段。
public class ChangShiDuanLiCard : SelectSpecialBaseCard
{
    public ChangShiDuanLiCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    private const int Count = 5;

    private static LocString ChuWaiPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_CHANG_SHI_DUAN_LI_CHU_WAI");
    private static LocString ConsumePrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_CHANG_SHI_DUAN_LI_XIAO_HAO");

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
        HoverTipFactory.FromKeyword(SelectKeywords.ChuWai),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;

        // ① 从消耗堆选择最多5张卡除外。
        var exhaustPile = PileType.Exhaust.GetPile(Owner);
        if (exhaustPile.Cards.Count > 0)
        {
            int n = Math.Min(Count, exhaustPile.Cards.Count);
            var exiled = (await CardSelectCmd.FromCombatPile(
                choiceContext,
                exhaustPile,
                Owner,
                new CardSelectorPrefs(ChuWaiPrompt, 0, n) { RequireManualConfirmation = true })).ToList();
            foreach (var card in exiled)
                await CardPileCmd.Add(card, Entry.ChuWaiPile);
        }

        // ② 从除外堆选择最多5张卡送入消耗堆。
        var chuWaiPile = Entry.ChuWaiPile.GetPile(Owner);
        if (chuWaiPile == null || chuWaiPile.Cards.Count == 0) return;

        int m = Math.Min(Count, chuWaiPile.Cards.Count);
        var consumed = (await CardSelectCmd.FromCombatPile(
            choiceContext,
            chuWaiPile,
            Owner,
            new CardSelectorPrefs(ConsumePrompt, 0, m) { RequireManualConfirmation = true })).ToList();

        foreach (var card in consumed)
        {
            await CardPileCmd.Add(card, PileType.Exhaust);
        }
    }
}
