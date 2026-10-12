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

// 常世断离：从消耗堆选择5张卡除外（移入除外堆），从除外堆选择5张卡消耗（移入消耗堆）。
// 不足 5 张时全选；两段选择都需要玩家手动确认。
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

        // ① 从消耗堆选择5张卡除外（不足5张则全选）
        var exhaustPile = PileType.Exhaust.GetPile(Owner);
        if (exhaustPile == null || exhaustPile.Cards.Count == 0) return;

        int n = Math.Min(Count, exhaustPile.Cards.Count);
        var exiled = (await CardSelectCmd.FromCombatPile(
            choiceContext,
            exhaustPile,
            Owner,
            new CardSelectorPrefs(ChuWaiPrompt, n, n) { RequireManualConfirmation = true })).ToList();
        if (exiled.Count == 0) return;

        foreach (var card in exiled)
        {
            await CardPileCmd.Add(card, Entry.ChuWaiPile);
        }

        // ② 从除外堆选择5张卡消耗（不足5张则全选）
        var chuWaiPile = Entry.ChuWaiPile.GetPile(Owner);
        if (chuWaiPile == null || chuWaiPile.Cards.Count == 0) return;

        int m = Math.Min(Count, chuWaiPile.Cards.Count);
        var consumed = (await CardSelectCmd.FromCombatPile(
            choiceContext,
            chuWaiPile,
            Owner,
            new CardSelectorPrefs(ConsumePrompt, m, m) { RequireManualConfirmation = true })).ToList();

        foreach (var card in consumed)
        {
            await CardPileCmd.Add(card, PileType.Exhaust);
        }
    }
}
