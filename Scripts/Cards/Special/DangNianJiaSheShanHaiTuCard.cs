using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Hook;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

public class DangNianJiaSheShanHaiTuCard : SelectSpecialBaseCard
{
    public DangNianJiaSheShanHaiTuCard() : base(1, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 先选择一张可变化的手牌。
        CardModel? selected = (await CardSelectCmd.FromHand(
            prefs: CardPrefs(this, SelectionScreenPrompt, 1, 1),
            context: choiceContext,
            player: Owner,
            filter: card => card.IsTransformable,
            source: this)).FirstOrDefault();

        if (selected == null) return;

        // 本人视野，含商店、奖励与特殊牌；同步快照后才创建相同顺序的副本。
        var candidates = await RunSeenCards.SynchronizeCandidates(choiceContext, Owner);
        List<CardModel> seenCards = candidates
            .Select(card => CombatState!.CreateCard(card, Owner))
            .ToList();

        if (seenCards.Count == 0) return;

        // 候选数量任意，必须用 FromSimpleGrid（网格界面）；
        // FromChooseACardScreen 只支持不超过3张，超过会直接抛异常。
        CardModel? replacement = (await CardSelectCmd.FromSimpleGrid(
            choiceContext,
            seenCards,
            Owner,
            CardPrefs(this, SelectionScreenPrompt, 1, 1))).FirstOrDefault();

        if (replacement == null) return;

        await CardCmd.Transform(selected, replacement);
    }
}
