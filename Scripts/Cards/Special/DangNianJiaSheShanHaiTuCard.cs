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
using SelectSpecialCardMod.Scripts.Intent;

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

        // 「这场游戏中见过的卡」= mod 自己记录的「本局渲染过的卡」（RunSeenCards），
        // 并入原生 DiscoveredCards 兜底；排除怪物的意图卡（IMonsterDisplayCard）——
        // 那些是展示用卡，不该被变化获得。
        List<CardModel> seenCards = RunSeenCards.Ids
            .Concat(Owner.DiscoveredCards)
            .Distinct()
            .Select(id => ModelDb.GetByIdOrNull<CardModel>(id))
            .Where(card => card != null && card is not IMonsterDisplayCard)
            .Select(card => CombatState!.CreateCard(card!, Owner))
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
