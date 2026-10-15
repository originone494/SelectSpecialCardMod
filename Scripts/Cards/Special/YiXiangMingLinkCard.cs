using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Rules;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 两张异响鸣 Link1 共用的召唤条件；各卡名的召唤次数相互独立。
public abstract class YiXiangMingLinkCard : SelectSpecialBaseCard, IExtraDeckSummonCard, IDengChangCard, ISpecialEffectCard
{
    protected YiXiangMingLinkCard() : base(0, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy) { }

    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(25m, ValueProp.Move)];
    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.YiXiangMing, SelectTags.YiXiangMingGuaiShou, SelectTags.Link1,
        SelectTags.ExtraMonster, SelectTags.DengChang, SelectTags.SpecialEffect,
    ];
    public override IEnumerable<CardKeyword> CanonicalKeywords => [];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMing),
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMingGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.Link1),
        HoverTipFactory.FromKeyword(SelectKeywords.ExtraMonster),
        HoverTipFactory.FromKeyword(SelectKeywords.DengChang),
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
        HoverTipFactory.FromPower<YiXiangMingZhiShiWuPower>(),
    ];

    private string SummonKey => "YiXiangMingLinkSummon:" + Id;
    protected static int Counters(Player player) => player.Creature.GetPower<YiXiangMingZhiShiWuPower>()?.Amount ?? 0;
    protected bool CanResolve(Player player) => Owner == player && player.Creature.IsAlive
        && player.Creature.CombatState != null && !CombatManager.Instance.IsOverOrEnding;
    protected bool CanUseSpecialEffect(Player player) => CanResolve(player)
        && CombatState == player.Creature.CombatState && Pile?.Type == PileType.Hand;

    private static bool IsMaterial(CardModel card, Player player)
        => card.Owner == player && card.Type == CardType.Attack && card.Pile?.Type == PileType.Hand
           && !card.HasBeenRemovedFromState;

    public bool CanSummonFromExtraDeck(Player player)
        => CanResolve(player) && Pile?.Type == Entry.ExtraPile && !HasBeenRemovedFromState
           && Counters(player) >= 3
           && !PlayerEffectState.For(player).IsUnavailable(SummonKey, PlayerEffectState.Combat())
           && PileType.Hand.GetPile(player).Cards.Any(c => IsMaterial(c, player));

    private static LocString SummonPrompt => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_YI_XIANG_MING_LINK_SUMMON");

    public async Task<bool> PaySummonCost(PlayerChoiceContext context, Player player)
    {
        if (!CanSummonFromExtraDeck(player)) return false;
        var combat = player.Creature.CombatState;
        var chosen = (await CardSelectCmd.FromHand(context, player,
            CardPrefsConfirm(this, SummonPrompt, 0, 1), c => IsMaterial(c, player), source: null!)).ToList();
        if (chosen.Count != 1 || !IsMaterial(chosen[0], player)
            || player.Creature.CombatState != combat || !CanSummonFromExtraDeck(player)) return false;

        using var reservation = PlayerEffectState.For(player).Reserve(SummonKey, PlayerEffectState.Combat());
        if (reservation == null) return false;
        try
        {
            var paid = await CardCmd.Exhaust(context, chosen[0]);
            if (paid?.success != true) return false;
            reservation.Commit();
            return true;
        }
        catch
        {
            // 异常前的消耗可能已触发连锁，不能允许不确定的重复支付。
            reservation.Commit();
            throw;
        }
    }

    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay cardPlay)
    {
        if (cardPlay.Target == null) return;
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay).Targeting(cardPlay.Target).Execute(context);
    }

    public abstract Task DengChangSpecial(PlayerChoiceContext context, Player player);
    public abstract bool CanActivateSpecialEffect(Player player);
    public async Task ActivateSpecialEffect(PlayerChoiceContext context, Player player)
        => await TryActivateSpecialEffect(context, player);
    public abstract Task<bool> TryActivateSpecialEffect(PlayerChoiceContext context, Player player);
}
