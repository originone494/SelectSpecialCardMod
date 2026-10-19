using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 妖精传姬·白雪。PortraitPath 继承基类，使用本类准确名称作为 PNG 文件名。
public class YaoJingChuanJiBaiXueCard : SelectSpecialBaseCard, IShiJiCard
{
    private const int ExhaustCount = 7;
    private static LocString ExhaustPrompt => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_BAI_XUE_EXHAUST");

    public YaoJingChuanJiBaiXueCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy) { }

    public override int StarLevel => 4;

    protected override HashSet<CardTag> CanonicalTags => [SelectTags.ShiJi, ..StarTags];

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(18m, ValueProp.Move),
        new PowerVar<WeakPower>(1m),
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
    [
        HoverTipFactory.FromPower<WeakPower>(),
        HoverTipFactory.FromKeyword(SelectKeywords.ShiJi),
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
        ..StarHoverTips,
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        var combat = CombatState;
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
        if (combat != null && Owner.Creature.CombatState == combat
            && !CombatManager.Instance.IsOverOrEnding && cardPlay.Target.IsAlive)
            await PowerCmd.Apply<WeakPower>(choiceContext, cardPlay.Target, DynamicVars.Weak.IntValue, Owner.Creature, this);
    }

    public IEnumerable<PileType> ShiJiPiles => [PileType.Discard];

    private bool IsInDiscard(Player player, CardModel card)
        => card.Owner == player && card.CombatState == CombatState && !card.HasBeenRemovedFromState
           && card.Pile == PileType.Discard.GetPile(player);

    public bool CanActivateShiJi(Player player)
        => Owner == player && CombatState != null && player.Creature.CombatState == CombatState
           && player.Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding
           && IsInDiscard(player, this)
           && PileType.Discard.GetPile(player).Cards.Count(c => c != this && IsInDiscard(player, c)) >= ExhaustCount;

    public async Task ActivateShiJi(PlayerChoiceContext ctx, Player player)
    {
        if (!CanActivateShiJi(player)) return;
        var combat = CombatState;
        bool CanContinue() => combat != null && CombatState == combat && player.Creature.CombatState == combat
            && player.Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding && IsInDiscard(player, this);
        var discard = PileType.Discard.GetPile(player);
        var chosen = (await CardSelectCmd.FromCombatPile(ctx, discard, player,
            CardPrefsConfirm(this, ExhaustPrompt, ExhaustCount, ExhaustCount),
            filter: c => c != this && IsInDiscard(player, c))).ToList();

        // 确认足额且互不重复后才开始支付；取消或选牌期间状态变化不会消耗卡。
        if (!CanContinue() || chosen.Count != ExhaustCount || chosen.Distinct().Count() != ExhaustCount
            || chosen.Any(c => c == this || !IsInDiscard(player, c))) return;

        foreach (var card in chosen)
        {
            // 消耗会触发其他效果；不能从已改变的位置强行取走后续选牌。
            if (!CanContinue() || !IsInDiscard(player, card)) return;
            var result = await CardCmd.Exhaust(ctx, card);
            if (result?.success != true) return;
        }

        // 没有额外的回合/战斗次数限制，只有实际消耗全部7张后才回手。
        if (CanContinue()) await CardPileCmd.Add(this, PileType.Hand);
    }
}
