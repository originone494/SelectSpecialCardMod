using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 神之密告：消耗1张手牌，之后，消耗手牌·抽牌堆·弃牌堆中的同名卡。
public class ShenZhiMiGaoCard : SelectSpecialBaseCard
{
    private static LocString ExhaustPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_EXHAUST");

    public ShenZhiMiGaoCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // ① 消耗1张手牌（强制选1张，手牌为空则结束）
        if (PileType.Hand.GetPile(Owner).Cards.Count == 0) return;

        CardModel? selected = (await CardSelectCmd.FromHand(
            prefs: CardPrefs(this, ExhaustPrompt, 1, 1),
            context: choiceContext,
            player: Owner,
            filter: null,
            source: this)).FirstOrDefault();

        if (selected == null) return;

        // 先记下卡名（同名判定依据），再消耗该牌
        var targetId = selected.Id;
        await CardCmd.Exhaust(choiceContext, selected);

        // ② 之后，消耗手牌·抽牌堆·弃牌堆中的同名卡
        var sameNameCards = new[] { PileType.Hand, PileType.Draw, PileType.Discard }
            .SelectMany(pileType => pileType.GetPile(Owner).Cards)
            .Where(card => card.Id == targetId)
            .ToList();

        foreach (var card in sameNameCards)
        {
            await CardCmd.Exhaust(choiceContext, card);
        }
    }
}