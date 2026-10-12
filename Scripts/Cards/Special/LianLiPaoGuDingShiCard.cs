using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 连栗炮固定式：消耗抽牌堆的2张费用为A的卡，然后消耗抽牌堆1张费用为B的卡，
// 若2A+B等于手牌数+敌人数、且A+B等于手牌数，则对所有敌人造成66点伤害，
// 并将消耗堆中1张费用为A的卡和1张费用为B的卡加入抽牌堆。
public class LianLiPaoGuDingShiCard : SelectSpecialBaseCard
{
    public LianLiPaoGuDingShiCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    // 伤害使用动态变量：满足条件时对所有敌人造成 66 点伤害
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(66m, ValueProp.Move),
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    private static LocString ChoiceAFirstPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_LIAN_LI_PAO_CHOICE_A_FIRST");
    private static LocString ChoiceAPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_LIAN_LI_PAO_CHOICE_A");
    private static LocString ChoiceBPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_LIAN_LI_PAO_CHOICE_B");
    private static LocString ChoiceABackPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_LIAN_LI_PAO_CHOICE_A_BACK");
    private static LocString ChoiceBBackPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_LIAN_LI_PAO_CHOICE_B_BACK");

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var drawPile = PileType.Draw.GetPile(Owner);
        if (drawPile.Cards.Count == 0) return;

        // ① 选择抽牌堆1张费用为A的卡并消耗
        var cardA1 = (await CardSelectCmd.FromSimpleGrid(
            choiceContext, drawPile.Cards.ToList(), Owner, CardPrefs(this, ChoiceAFirstPrompt, 1, 1))).FirstOrDefault();
        if (cardA1 == null) return;
        int costA = cardA1.EnergyCost.GetWithModifiers(CostModifiers.None);
        await CardCmd.Exhaust(choiceContext, cardA1);

        // ② 筛选抽牌堆中费用为A的卡，选择1张并消耗
        var costACandidates = drawPile.Cards
            .Where(c => c != cardA1 && c.EnergyCost.GetWithModifiers(CostModifiers.None) == costA)
            .ToList();
        if (costACandidates.Count == 0) return;
        var cardA2 = (await CardSelectCmd.FromSimpleGrid(
            choiceContext, costACandidates, Owner, CardPrefs(this, ChoiceAPrompt, 1, 1))).FirstOrDefault();
        if (cardA2 == null) return;
        await CardCmd.Exhaust(choiceContext, cardA2);

        // ③ 选择抽牌堆1张费用为B的卡并消耗（B与A可以相同，仅为区分）
        var costBCandidates = drawPile.Cards
            .Where(c => c != cardA1 && c != cardA2)
            .ToList();
        if (costBCandidates.Count == 0) return;
        var cardB = (await CardSelectCmd.FromSimpleGrid(
            choiceContext, costBCandidates, Owner, CardPrefs(this, ChoiceBPrompt, 1, 1))).FirstOrDefault();
        if (cardB == null) return;
        int costB = cardB.EnergyCost.GetWithModifiers(CostModifiers.None);
        await CardCmd.Exhaust(choiceContext, cardB);

        // ④ 判断条件：2A+B == 手牌数+敌人数 且 A+B == 手牌数（+1 是打出的这张本身）
        int handCount = PileType.Hand.GetPile(Owner).Cards.Count + 1;
        int enemyCount = CombatState!.HittableEnemies.Count;
        if (2 * costA + costB != handCount + enemyCount || costA + costB != handCount) return;

        // ⑤ 对所有敌人造成66点伤害
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .TargetingAllOpponents(CombatState!)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);

        // ⑥ 将消耗堆中1张费用为A的卡和1张费用为B的卡加入抽牌堆
        var exhaustPile = PileType.Exhaust.GetPile(Owner);
        var exhaustA = exhaustPile.Cards.Where(c => c.EnergyCost.GetWithModifiers(CostModifiers.None) == costA).ToList();
        var backCardA = (await CardSelectCmd.FromSimpleGrid(
            choiceContext, exhaustA, Owner, CardPrefs(this, ChoiceABackPrompt, 1, 1))).FirstOrDefault();
        if (backCardA != null) await CardPileCmd.Add(backCardA, PileType.Draw);

        var exhaustB = exhaustPile.Cards.Where(c => c.EnergyCost.GetWithModifiers(CostModifiers.None) == costB).ToList();
        var backCardB = (await CardSelectCmd.FromSimpleGrid(
            choiceContext, exhaustB, Owner, CardPrefs(this, ChoiceBBackPrompt, 1, 1))).FirstOrDefault();
        if (backCardB != null) await CardPileCmd.Add(backCardB, PileType.Draw);
    }
}