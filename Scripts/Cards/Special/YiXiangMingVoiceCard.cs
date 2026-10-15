using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Rules;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

public enum PendulumEdgeSide { None, Left, Right }

// 两张「异响鸣·声音」共用的怪兽、特殊效果和灵摆规则。
public abstract class YiXiangMingVoiceCard : SelectSpecialBaseCard, ISpecialEffectCard, IPendulumCard
{
    private static LocString ExhaustPrompt => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_YI_XIANG_MING_EXHAUST");
    private static LocString CostExhaustPrompt => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_YI_XIANG_MING_COST_EXHAUST");

    private bool _placedAtEdgeByEffect;
    private bool _placementGrantedRetain;
    private bool _placementGrantedUnplayable;
    private PendulumEdgeSide _lockedPendulumSide;

    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public PendulumEdgeSide LockedPendulumSide
    {
        get => _lockedPendulumSide;
        set
        {
            _lockedPendulumSide = value;
            if (IsMutable && PlacedAtEdgeByEffect && Pile is { } pile)
                YiXiangMingHandPlacement.Enforce(pile);
        }
    }

    // 恢复放置资格时重建保留和无法打出；离手时只移除本次放置新增的关键字。
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public bool PlacedAtEdgeByEffect
    {
        get => _placedAtEdgeByEffect;
        set
        {
            _placedAtEdgeByEffect = value;
            if (!value) _lockedPendulumSide = PendulumEdgeSide.None;
            if (!IsMutable) return;
            if (value && !GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Retain))
            {
                AddKeyword(CardKeyword.Retain);
                _placementGrantedRetain = true;
            }
            else if (!value && _placementGrantedRetain)
            {
                RemoveKeyword(CardKeyword.Retain);
                _placementGrantedRetain = false;
            }
            if (value && !GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Unplayable))
            {
                AddKeyword(CardKeyword.Unplayable);
                _placementGrantedUnplayable = true;
            }
            else if (!value && _placementGrantedUnplayable)
            {
                RemoveKeyword(CardKeyword.Unplayable);
                _placementGrantedUnplayable = false;
            }
            if (value && Pile is { } pile) YiXiangMingHandPlacement.Enforce(pile);
        }
    }

    public bool CanApplyPendulumEffect
        => PlacedAtEdgeByEffect && YiXiangMingHandPlacement.IsAtEdge(this)
           && CombatState != null && Owner?.Creature.IsAlive == true
           && !CombatManager.Instance.IsOverOrEnding;

    // 只有主动发动式灵摆效果覆写此条件；两张声音卡的被动效果使用默认 false。
    public virtual bool CanActivatePendulumEffect(Player player) => false;

    // 两侧卡默认蓝边；只有可主动发动的灵摆效果当前能发动时才显示金边。
    public Color? PendulumOutlineColor
        => PlacedAtEdgeByEffect && Pile?.Type == PileType.Hand
            ? CanApplyPendulumEffect && CanActivatePendulumEffect(Owner) ? NCardHighlight.gold : Colors.DodgerBlue
            : null;

    protected YiXiangMingVoiceCard() : base(1, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy) { }

    protected abstract Type CounterpartType { get; }
    protected abstract bool CounterOnHealing { get; }
    public abstract int PendulumScale { get; }
    public override int StarLevel => 4;

    public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
        => !YiXiangMingHandPlacement.IsPlayLocked(card) || card != this;

    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(12m, ValueProp.Move)];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.YiXiangMing, SelectTags.YiXiangMingGuaiShou,
        SelectTags.LingBaiGuaiShou, SelectTags.LingBaiEffect, SelectTags.SpecialEffect,
        ..StarTags,
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMing),
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMingGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.LingBaiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.LingBaiEffect),
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
        ..StarHoverTips,
    ];

    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay cardPlay)
    {
        if (cardPlay.Target == null) return;
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay).Targeting(cardPlay.Target).Execute(context);

        if (CombatManager.Instance.IsOverOrEnding) return;
        var exhaust = PileType.Exhaust.GetPile(Owner);
        if (!exhaust.Cards.Any(IsVoiceSpellOrTrap)) return;

        var picked = (await CardSelectCmd.FromCombatPile(context, exhaust, Owner,
            CardPrefs(this, ExhaustPrompt, 1, 1), filter: IsVoiceSpellOrTrap)).FirstOrDefault();
        if (picked?.Pile?.Type != PileType.Exhaust || picked.Owner != Owner) return;
        if (!MegaCrit.Sts2.Core.Hooks.Hook.ShouldPlay(Owner.Creature.CombatState!, picked, out _, AutoPlayType.Default)) return;

        await CardPileCmd.Add(picked, PileType.Play);
        if (picked.Pile?.Type != PileType.Play) return;
        await CardCmd.AutoPlay(context, picked, null);
        if (!CombatManager.Instance.IsOverOrEnding && !picked.HasBeenRemovedFromState && picked.Pile != null)
            await CardPileCmd.Add(picked, Entry.ChuWaiPile);
    }

    private static bool IsVoiceSpellOrTrap(CardModel card)
        => card.Tags.Contains(SelectTags.YiXiangMingMoFa)
           || card.Tags.Contains(SelectTags.YiXiangMingXianJing);

    public bool CanActivateSpecialEffect(Player player)
        => (!FeatureStatus.Checked || FeatureStatus.PendulumPlacement)
           && Owner == player && CombatState != null && Pile?.Type == PileType.Hand
           && PileType.Hand.GetPile(player).Cards.Any(c => c != this && c.Owner == player)
           && YiXiangMingHandPlacement.CanPlacePair(player)
           && ModelDb.AllCards.Any(c => c.GetType() == CounterpartType);

    public async Task ActivateSpecialEffect(PlayerChoiceContext context, Player player)
        => await TryActivateSpecialEffect(context, player);

    public async Task<bool> TryActivateSpecialEffect(PlayerChoiceContext context, Player player)
    {
        if (!CanActivateSpecialEffect(player)) return false;
        var combat = CombatState;
        // 独立右键效果不经过 OnPlay；空来源让选牌结束后立即恢复手牌显示。
        var cost = (await CardSelectCmd.FromHand(
            prefs: CardPrefs(this, CostExhaustPrompt, 1, 1), context: context, player: player,
            filter: c => c != this && c.Owner == player, source: null!)).FirstOrDefault();
        if (cost == null || cost == this || cost.Owner != player || cost.Pile?.Type != PileType.Hand
            || CombatState != combat || Pile?.Type != PileType.Hand
            || !YiXiangMingHandPlacement.CanPlacePair(player)) return false;

        bool paid = false;
        var history = CombatManager.Instance.History;
        void OnHistoryChanged()
        {
            if (!paid && history.Entries.LastOrDefault() is CardExhaustedEntry entry
                && ReferenceEquals(entry.Card, cost) && cost.Pile?.Type == PileType.Exhaust)
            {
                paid = true;
                EffectActivation.PaymentStarted();
            }
        }
        history.Changed += OnHistoryChanged;
        bool exhausted;
        try { exhausted = (await CardCmd.Exhaust(context, cost))?.success == true; }
        finally { history.Changed -= OnHistoryChanged; }
        if (!exhausted) return false;
        // 入堆触发的连锁可能已再次移动代价卡；原版命令成功仍视为已支付。
        if (!paid) EffectActivation.PaymentStarted();

        if (CombatManager.Instance.IsOverOrEnding || CombatState != combat || Pile?.Type != PileType.Hand
            || !YiXiangMingHandPlacement.CanPlacePair(player))
            return true;
        var template = ModelDb.AllCards.FirstOrDefault(c => c.GetType() == CounterpartType);
        if (template == null) return true;
        var counterpart = player.Creature.CombatState!.CreateCard(template, player);
        var added = await CardPileCmd.AddGeneratedCardToCombat(counterpart, PileType.Hand, player);
        if (!added.success || counterpart.Pile?.Type != PileType.Hand) return true;

        // 发动者靠左、检索到的另一张靠右；逻辑顺序与可见顺序一起移动。
        await YiXiangMingHandPlacement.PutAtEdges(player, this, counterpart);
        return true;
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        await base.AfterCardChangedPiles(card, oldPileType, clonedBy);
        if (ReferenceEquals(card, this) && Pile?.Type != PileType.Hand)
            PlacedAtEdgeByEffect = false;
    }

    public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (!CanApplyPendulumEffect || creature != Owner?.Creature)
            return;
        if (CounterOnHealing ? delta <= 0m : delta >= 0m) return;
        await PowerCmd.Apply<YiXiangMingZhiShiWuPower>(
            new ThrowingPlayerChoiceContext(), creature, 1m, creature, this);
    }
}
