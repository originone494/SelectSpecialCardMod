using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Combat.HandSize;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 异响鸣·天魔之声选姬。卡图继承基类 PortraitPath，文件名与本类大小写一致。
public class YiXiangMingTianMoZhiShengXuanJiCard : SelectSpecialBaseCard, ISpecialEffectCard, IShiJiCard
{
    public YiXiangMingTianMoZhiShengXuanJiCard()
        : base(1, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy) { }

    public override int StarLevel => 4;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(12m, ValueProp.Move)];
    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.YiXiangMing, SelectTags.YiXiangMingGuaiShou,
        SelectTags.SpecialEffect, SelectTags.ShiJi, ..StarTags,
    ];
    public override IEnumerable<CardKeyword> CanonicalKeywords => [];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMing),
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMingGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
        HoverTipFactory.FromKeyword(SelectKeywords.ShiJi),
        HoverTipFactory.FromKeyword(SelectKeywords.LingBaiQu),
        ..StarHoverTips,
    ];

    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay cardPlay)
    {
        if (cardPlay.Target == null) return;
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay).Targeting(cardPlay.Target).Execute(context);
    }

    public bool CanActivateSpecialEffect(Player player)
        => Owner == player && CombatState != null && player.Creature.IsAlive
           && !CombatManager.Instance.IsOverOrEnding && Pile?.Type == PileType.Hand;

    public async Task ActivateSpecialEffect(PlayerChoiceContext context, Player player)
        => await TryActivateSpecialEffect(context, player);

    public async Task<bool> TryActivateSpecialEffect(PlayerChoiceContext context, Player player)
    {
        if (!CanActivateSpecialEffect(player)) return false;
        var retrieved = await CardRetrieval.Retrieve(context, player,
            c => c.Tags.Contains(SelectTags.YiXiangMing) && c.Id != Id,
            new RetrievalOptions(Minimum: 1, Confirm: true),
            p => p is SelectSpecialCardPool, source: this);
        return retrieved.Count > 0;
    }

    [SavedProperty]
    public bool ShiJiUsed { get; set; }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        // 新副本独立计次；读档随后通过 SavedProperty 恢复原实例的次数。
        ShiJiUsed = false;
    }

    public IEnumerable<PileType> ShiJiPiles => [PileType.Exhaust];

    public bool CanActivateShiJi(Player player)
        => !ShiJiUsed && Owner == player && CombatState != null && player.Creature.IsAlive
           && !CombatManager.Instance.IsOverOrEnding && !HasBeenRemovedFromState
           && Pile?.Type == PileType.Exhaust
           && YiXiangMingChoiceCard.YiXiangMingInPendulumZone(player).Take(2).Count() >= 2
           && PileType.Hand.GetPile(player).Cards.Count < MaxHandSizeCalculator.Calculate(player);

    public async Task ActivateShiJi(PlayerChoiceContext context, Player player)
    {
        if (!CanActivateShiJi(player)) return;
        // 移牌可能触发连锁；先预占本实例的次数，防止同一实例重入。
        ShiJiUsed = true;
        var result = await CardPileCmd.Add(this, PileType.Hand);
        if (!result.success) ShiJiUsed = false;
    }
}
