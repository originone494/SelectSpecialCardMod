using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 异响鸣·邀请：技能、先古、0费；卡图继承基类 PortraitPath。
public class YiXiangMingYaoQingCard : SelectSpecialBaseCard
{
    public YiXiangMingYaoQingCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self) { }
    protected override HashSet<CardTag> CanonicalTags => [SelectTags.YiXiangMing, SelectTags.YiXiangMingMoFa];
    public override IEnumerable<CardKeyword> CanonicalKeywords => [];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMing),
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMingMoFa),
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMingGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.LingBaiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
    ];

    public static bool IsYiXiangMingMonster(CardModel card)
        => card.Type == CardType.Attack && card.Tags.Contains(SelectTags.YiXiangMingGuaiShou);

    public static bool IsYiXiangMingPendulumMonster(CardModel card)
        => IsYiXiangMingMonster(card) && card.Tags.Contains(SelectTags.LingBaiGuaiShou);

    public bool HasNonPendulumMonsterInHand()
        => PileType.Hand.GetPile(Owner).Cards.Any(c => c.Owner == Owner && !c.HasBeenRemovedFromState
            && IsYiXiangMingMonster(c) && !c.Tags.Contains(SelectTags.LingBaiGuaiShou));

    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay cardPlay)
    {
        var combat = CombatState;
        if (combat == null || !CanContinue()) return;
        var one = combat.CreateCard<EffectOneCard>(Owner);
        var two = combat.CreateCard<EffectTwoCard>(Owner);
        one.EffectText = new LocString("cards", Id.Entry + ".option1").GetFormattedText();
        two.EffectText = new LocString("cards", Id.Entry + ".option2").GetFormattedText();
        one.PortraitSourceClassName = two.PortraitSourceClassName = GetType().Name;
        // 邀请不沿用其他三张魔法的生命变化、无灵摆区时随机适用规则。
        var selected = await CardSelectCmd.FromChooseACardScreen(context, [one, two], Owner, canSkip: false);
        if (!CanContinue()) return;
        if (selected == one)
        {
            var cards = await CardRetrieval.Retrieve(context, Owner, IsYiXiangMingMonster,
                new RetrievalOptions(Minimum: 1, Confirm: true), p => p is SelectSpecialCardPool, source: this);
            foreach (var card in cards.Where(c => c.Owner == Owner && !c.HasBeenRemovedFromState))
            {
                card.EnergyCost.SetThisTurn(0);
                card.SetStarCostThisTurn(0);
            }
        }
        else if (selected == two && HasNonPendulumMonsterInHand())
        {
            var templates = ModelDb.AllCards.Where(IsYiXiangMingPendulumMonster)
                .Where(c => !SelectToolCmd.IsNonObtainable(c) && !c.Tags.Contains(SelectTags.ExtraMonster))
                .GroupBy(c => c.Id).Select(g => g.First()).OrderBy(c => c.Id).ToList();
            if (templates.Count < 2) return;
            var candidates = templates.Select(c => combat.CreateCard(c, Owner)).ToList();
            var first = await Pick(candidates, "HAND");
            if (first == null || !CanContinue() || !HasNonPendulumMonsterInHand()) return;
            var remaining = candidates.Where(c => c.Id != first.Id).ToList();
            var second = await Pick(remaining, "EXTRA");
            if (second == null || first.Id == second.Id || !CanContinue() || !HasNonPendulumMonsterInHand()) return;
            // 两张都选好后才结算，检索先后顺序不依赖网格对多选结果的排序。
            await CardPileCmd.AddGeneratedCardToCombat(first, PileType.Hand, Owner);
            if (CanContinue())
                await PendulumExtraDeck.AddTemporary(Owner, templates.Single(c => c.Id == second.Id));
        }

        bool CanContinue() => CombatState == combat && Owner.Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding;
        async Task<CardModel?> Pick(List<CardModel> cards, string destination)
        {
            var prompt = new LocString("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_YAO_QING_" + destination);
            prompt.Add("CardName", Title);
            var picked = (await CardSelectCmd.FromSimpleGrid(context, cards, Owner,
                new CardSelectorPrefs(prompt, 1, 1) { RequireManualConfirmation = true })).FirstOrDefault();
            return picked != null && cards.Contains(picked) ? picked : null;
        }
    }
}
