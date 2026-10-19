using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 四花缭乱之灵使。PortraitPath 继承基类，文件名须与本类名大小写一致。
public class SiHuaLiaoLuanZhiLingShiCard : SelectSpecialBaseCard
{
    public SiHuaLiaoLuanZhiLingShiCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy) { }

    protected override HashSet<CardTag> CanonicalTags => [SelectTags.Link4];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
    [
        HoverTipFactory.FromKeyword(SelectKeywords.Link4),
    ];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new CalculationBaseVar(18m),
        new ExtraDamageVar(3m),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(static (card, _) =>
        {
            if (card?.Owner == null || card.CombatState == null) return 0m;
            // 打出后自身已不在手牌；预览也排除自身，避免预览与实际结算相差一种类型。
            return PileType.Hand.GetPile(card.Owner).Cards.Where(c => c != card).Select(c => c.Type).Distinct().Count();
        }),
    ];

    private static LocString FirstPrompt => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_SI_HUA_FIRST");
    private static LocString SecondPrompt => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_SI_HUA_SECOND");

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        var player = Owner;
        var combat = CombatState;
        bool CanContinue() => combat != null && player.Creature.CombatState == combat
            && player.Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding;

        await DamageCmd.Attack(DynamicVars.CalculatedDamage)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
        if (!CanContinue()) return;

        var discard = PileType.Discard.GetPile(player);
        bool IsAvailable(CardModel card) => card.Owner == player && card.CombatState == combat
            && card.Pile == discard && !card.HasBeenRemovedFromState;
        var firstOptions = discard.Cards.Where(a => IsAvailable(a)
            && discard.Cards.Any(b => IsAvailable(b) && CanRecoverTogether(a, b))).ToList();
        if (firstOptions.Count == 0) return;

        var first = (await CardSelectCmd.FromSimpleGrid(choiceContext, firstOptions, player,
            CardPrefs(this, FirstPrompt, 1, 1))).FirstOrDefault();
        if (!CanContinue() || first == null || !IsAvailable(first)) return;

        var secondOptions = discard.Cards.Where(c => IsAvailable(c) && CanRecoverTogether(first, c)).ToList();
        if (secondOptions.Count == 0) return;
        var second = (await CardSelectCmd.FromSimpleGrid(choiceContext, secondOptions, player,
            CardPrefs(this, SecondPrompt, 1, 1))).FirstOrDefault();
        // 两张均确认且仍然合法后才开始移动，避免联机或选牌期间牌堆变化导致错误回收。
        if (!CanContinue() || second == null || !IsAvailable(first) || !IsAvailable(second)
            || !CanRecoverTogether(first, second)) return;

        await CardPileCmd.Add(first, PileType.Hand);
        // 第一张入手可能触发其他效果，第二张须重新检查，不能从新位置强行取走。
        if (CanContinue() && IsAvailable(second)) await CardPileCmd.Add(second, PileType.Hand);
    }

    internal static bool CanRecoverTogether(CardModel first, CardModel second)
    {
        if (ReferenceEquals(first, second)) return false;
        // 与现有费用比较规则一致，忽略临时费用修正；X 费用单独成组，不视作 0 费。
        bool sameCost = first.EnergyCost.CostsX == second.EnergyCost.CostsX
            && (first.EnergyCost.CostsX || first.EnergyCost.GetWithModifiers(CostModifiers.None)
                == second.EnergyCost.GetWithModifiers(CostModifiers.None));
        bool sameType = first.Type == second.Type;
        return sameCost != sameType;
    }
}
