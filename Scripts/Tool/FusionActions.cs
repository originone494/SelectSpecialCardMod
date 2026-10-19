using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Power;

namespace SelectSpecialCardMod.Scripts.Tool;

public enum FusionActionKind
{
    Damage, AllDamage, Block, Draw, Mill, RandomExile, Heal, LoseHp, Energy,
    Strength, Dexterity, Artifact, Weak, Vulnerable, Poison, TempStrengthDown, RemoveEnemyStrength,
    Discard, ExhaustHand, RecoverDiscard, RecoverExhaust, RecoverExhaustFree, RecoverExile, Retrieve, RetrieveFree, RetrieveRandom,
    ExhaustDiscard, DamageVsBlock,
}

public enum FusionCardFilter
{
    Any, Attack, Skill, Power, ZhuLei, ZhuLeiMonster, ZhuLeiTrap, YiJie, YiJieMonster,
    KeMo, KeMoMonster, GuangEMo, YiXiangMing, YiXiangMingMonster, YiXiangMingSpell,
    TianBeiLong, Dragon, HorusMonster, YaoShengMonster, KeMoSpell,
}

// 配方只包含明确允许的动作，不持有原卡实例、委托或反射调用。
public sealed record FusionAction(FusionActionKind Kind, int Amount, FusionCardFilter Filter = FusionCardFilter.Any)
{
    public bool RequiresEnemy => Kind is FusionActionKind.Damage or FusionActionKind.Weak
        or FusionActionKind.Vulnerable or FusionActionKind.Poison or FusionActionKind.TempStrengthDown or FusionActionKind.RemoveEnemyStrength
        or FusionActionKind.DamageVsBlock;

    public DynamicVar CreateVar(string key) => Kind switch
    {
        FusionActionKind.Damage or FusionActionKind.AllDamage => new DamageVar(key, Amount, ValueProp.Move),
        FusionActionKind.DamageVsBlock => new FusionBlockDamageVar(key, Amount),
        FusionActionKind.Block => new BlockVar(key, Amount, ValueProp.Move),
        FusionActionKind.Weak => new PowerVar<WeakPower>(key, Amount),
        FusionActionKind.Vulnerable => new PowerVar<VulnerablePower>(key, Amount),
        FusionActionKind.Poison => new PowerVar<PoisonPower>(key, Amount),
        _ => new DynamicVar(key, Amount),
    };

    public string Describe(string key, CardModel? source = null)
    {
        var text = new LocString("card_selection", "SELECT_SPECIAL_CARD_MOD_MYTH_ACTION_" + Kind.ToString().ToUpperInvariant());
        text.Add("Value", (source != null ? source.DynamicVars[key] : CreateVar(key)).ToHighlightedString(false));
        text.Add("Kind", new LocString("card_selection", "SELECT_SPECIAL_CARD_MOD_MYTH_FILTER_" + Filter.ToString().ToUpperInvariant()).GetFormattedText());
        return text.GetFormattedText();
    }

    public IEnumerable<IHoverTip> HoverTips() => Kind switch
    {
        FusionActionKind.Strength or FusionActionKind.TempStrengthDown or FusionActionKind.RemoveEnemyStrength => [HoverTipFactory.FromPower<StrengthPower>()],
        FusionActionKind.Dexterity => [HoverTipFactory.FromPower<DexterityPower>()],
        FusionActionKind.Artifact => [HoverTipFactory.FromPower<ArtifactPower>()],
        FusionActionKind.Weak => [HoverTipFactory.FromPower<WeakPower>()],
        FusionActionKind.Vulnerable => [HoverTipFactory.FromPower<VulnerablePower>()],
        FusionActionKind.Poison => [HoverTipFactory.FromPower<PoisonPower>()],
        FusionActionKind.Retrieve or FusionActionKind.RetrieveFree or FusionActionKind.RetrieveRandom => [HoverTipFactory.FromKeyword(SelectKeywords.Retriever)],
        _ => [],
    };

    public bool Matches(CardModel card) => Filter switch
    {
        FusionCardFilter.Any => true,
        FusionCardFilter.Attack => card.Type == CardType.Attack,
        FusionCardFilter.Skill => card.Type == CardType.Skill,
        FusionCardFilter.Power => card.Type == CardType.Power,
        FusionCardFilter.ZhuLei => card.Tags.Contains(SelectTags.ZhuLei),
        FusionCardFilter.ZhuLeiMonster => card.Tags.Contains(SelectTags.ZhuLeiGuaiShou),
        FusionCardFilter.ZhuLeiTrap => card.Tags.Contains(SelectTags.ZhuLeiXianJing),
        FusionCardFilter.YiJie => card.Tags.Contains(SelectTags.YiJie),
        FusionCardFilter.YiJieMonster => card.Tags.Contains(SelectTags.YiJieGuaiShou),
        FusionCardFilter.KeMo => card.Tags.Contains(SelectTags.KeMo),
        FusionCardFilter.KeMoMonster => card.Tags.Contains(SelectTags.KeMoGuaiShou),
        FusionCardFilter.KeMoSpell => card.Tags.Contains(SelectTags.KeMoMoFa),
        FusionCardFilter.GuangEMo => card.Tags.Contains(SelectTags.GuangEMo),
        FusionCardFilter.YiXiangMing => card.Tags.Contains(SelectTags.YiXiangMing),
        FusionCardFilter.YiXiangMingMonster => card.Tags.Contains(SelectTags.YiXiangMingGuaiShou),
        FusionCardFilter.YiXiangMingSpell => card.Tags.Contains(SelectTags.YiXiangMingMoFa),
        FusionCardFilter.TianBeiLong => card.Tags.Contains(SelectTags.TianBeiLong),
        FusionCardFilter.Dragon => card.Tags.Contains(SelectTags.LongZu),
        FusionCardFilter.HorusMonster => card.Tags.Contains(SelectTags.HeLuSiGuaiShou),
        FusionCardFilter.YaoShengMonster => card.Tags.Contains(SelectTags.YaoShengGuaiShou),
        _ => false,
    };
}

// 范围只覆盖这一段伤害或这一项预览，避免给同一合体卡的其他伤害模块也翻倍。
// AsyncLocal隔离异步攻击与UI预览；来源和CardPlay必须一致，异常时由using恢复。
internal sealed class FusionBlockDamageScope : IDisposable
{
    private static readonly AsyncLocal<FusionBlockDamageScope?> Current = new();
    private readonly FusionBlockDamageScope? _previous;
    private readonly CardModel _source;
    private readonly CardPlay? _play;
    public FusionBlockDamageScope(CardModel source, CardPlay? play)
    {
        _previous = Current.Value; _source = source; _play = play; Current.Value = this;
    }
    public static decimal Multiplier(CardModel source, Creature? target, ValueProp props,
        Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => Current.Value is { } scope && scope._source == source && cardSource == source
           && ReferenceEquals(scope._play, cardPlay) && dealer == source.Owner?.Creature
           && props.IsPoweredAttack() && target?.Block > 0 ? 2m : 1m;
    public void Dispose() => Current.Value = _previous;
}

internal sealed class FusionBlockDamageVar(string key, decimal amount) : DamageVar(key, amount, ValueProp.Move)
{
    public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
    {
        using var scope = new FusionBlockDamageScope(card, null);
        base.UpdateCardPreview(card, previewMode, target, runGlobalHooks);
    }
}

// 一次合体卡打出只有一个上下文：目标死亡后重定向，之后的动作继续使用新的目标。
public sealed class FusionActionContext(CardModel source, PlayerChoiceContext choiceContext, CardPlay cardPlay)
{
    public CardModel Source { get; } = source;
    public PlayerChoiceContext ChoiceContext { get; } = choiceContext;
    public CardPlay CardPlay { get; } = cardPlay;
    public ICombatState? Combat { get; } = source.CombatState;
    private Creature? _target = cardPlay.Target;

    public bool CanContinue => Combat != null && Source.CombatState == Combat
        && Source.Owner?.Creature.IsAlive == true && !CombatManager.Instance.IsOverOrEnding;

    public Creature? Target()
    {
        if (!CanContinue) return null;
        var enemies = Combat!.HittableEnemies.Where(e => e.IsAlive && e.CombatState == Combat).ToList();
        if (_target != null && enemies.Contains(_target)) return _target;
        if (enemies.Count == 0) return null;
        _target = Source.Owner.RunState.Rng.CombatTargets.NextItem(enemies);
        return _target;
    }
}

public static class FusionActions
{
    public static async Task Execute(FusionAction action, string key, FusionActionContext ctx)
    {
        if (!ctx.CanContinue) return;
        var source = ctx.Source;
        var owner = source.Owner;
        var context = ctx.ChoiceContext;
        var value = source.DynamicVars[key];
        Creature? target;
        switch (action.Kind)
        {
            case FusionActionKind.Damage:
                target = ctx.Target();
                if (target != null) await DamageCmd.Attack(value.BaseValue).FromCard(source, ctx.CardPlay).Targeting(target).Execute(context);
                break;
            case FusionActionKind.DamageVsBlock:
                target = ctx.Target();
                if (target != null)
                {
                    using var scope = new FusionBlockDamageScope(source, ctx.CardPlay);
                    await DamageCmd.Attack(value.BaseValue).FromCard(source, ctx.CardPlay).Targeting(target).Execute(context);
                }
                break;
            case FusionActionKind.AllDamage:
                if (ctx.Combat!.HittableEnemies.Any(e => e.IsAlive))
                    await DamageCmd.Attack(value.BaseValue).FromCard(source, ctx.CardPlay).TargetingAllOpponents(ctx.Combat).Execute(context);
                break;
            case FusionActionKind.Block:
                await CreatureCmd.GainBlock(owner.Creature, (BlockVar)value, ctx.CardPlay);
                break;
            case FusionActionKind.Draw: await CardPileCmd.Draw(context, value.BaseValue, owner); break;
            case FusionActionKind.Mill: await SelectToolCmd.DuiMu(context, owner, value.IntValue); break;
            case FusionActionKind.RandomExile: await YiJieExileBag.DrawToChuWai(owner, value.IntValue); break;
            case FusionActionKind.Heal: await CreatureCmd.Heal(owner.Creature, value.BaseValue); break;
            case FusionActionKind.LoseHp:
                await CreatureCmd.Damage(context, owner.Creature, value.BaseValue,
                    ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, source, ctx.CardPlay);
                break;
            case FusionActionKind.Energy: await PlayerCmd.GainEnergy(value.BaseValue, owner); break;
            case FusionActionKind.Strength: await PowerCmd.Apply<StrengthPower>(context, owner.Creature, value.BaseValue, owner.Creature, source); break;
            case FusionActionKind.Dexterity: await PowerCmd.Apply<DexterityPower>(context, owner.Creature, value.BaseValue, owner.Creature, source); break;
            case FusionActionKind.Artifact: await PowerCmd.Apply<ArtifactPower>(context, owner.Creature, value.BaseValue, owner.Creature, source); break;
            case FusionActionKind.Weak:
                target = ctx.Target();
                if (target != null) await PowerCmd.Apply<WeakPower>(context, target, value.BaseValue, owner.Creature, source);
                break;
            case FusionActionKind.Vulnerable:
                target = ctx.Target();
                if (target != null) await PowerCmd.Apply<VulnerablePower>(context, target, value.BaseValue, owner.Creature, source);
                break;
            case FusionActionKind.Poison:
                target = ctx.Target();
                if (target != null) await PowerCmd.Apply<PoisonPower>(context, target, value.BaseValue, owner.Creature, source);
                break;
            case FusionActionKind.TempStrengthDown:
                target = ctx.Target();
                if (target != null) await PowerCmd.Apply<YaoShengLuXiNaTempDownPower>(context, target, value.BaseValue, owner.Creature, source);
                break;
            case FusionActionKind.RemoveEnemyStrength:
                target = ctx.Target();
                var strength = target?.GetPower<StrengthPower>();
                if (strength?.Amount > 0) await PowerCmd.Remove(strength);
                break;
            case FusionActionKind.Discard:
            case FusionActionKind.ExhaustHand:
                await SelectHand(action, value.IntValue, ctx);
                break;
            case FusionActionKind.ExhaustDiscard: await ExhaustDiscard(action, value.IntValue, ctx); break;
            case FusionActionKind.RecoverDiscard: await Recover(action, value.IntValue, PileType.Discard, ctx); break;
            case FusionActionKind.RecoverExhaust: await Recover(action, value.IntValue, PileType.Exhaust, ctx); break;
            case FusionActionKind.RecoverExhaustFree: await Recover(action, value.IntValue, PileType.Exhaust, ctx); break;
            case FusionActionKind.RecoverExile: await Recover(action, value.IntValue, Entry.ChuWaiPile, ctx); break;
            case FusionActionKind.Retrieve:
            case FusionActionKind.RetrieveFree:
            case FusionActionKind.RetrieveRandom:
                var cards = await SelectToolCmd.RetrieverCard(context, owner, c => c != source && action.Matches(c),
                    amount: value.IntValue, minAmount: value.IntValue, source: source, requireManualConfirmation: true,
                    isRandom: action.Kind == FusionActionKind.RetrieveRandom);
                if (action.Kind == FusionActionKind.RetrieveFree && ctx.CanContinue)
                    foreach (var card in cards.Where(c => c.Owner == owner && c.Pile?.Type == PileType.Hand))
                    {
                        card.EnergyCost.SetThisTurn(0); card.SetStarCostThisTurn(0); card.InvokeEnergyCostChanged();
                    }
                break;
            default: throw new ArgumentOutOfRangeException(nameof(action.Kind));
        }
    }

    private static CardSelectorPrefs Prefs(FusionActionContext ctx, string key, int count)
    {
        var prompt = new LocString("card_selection", key);
        prompt.Add("CardName", ctx.Source.Title);
        prompt.Add("Count", count);
        return new CardSelectorPrefs(prompt, count, count) { RequireManualConfirmation = true };
    }

    private static async Task SelectHand(FusionAction action, int count, FusionActionContext ctx)
    {
        bool Eligible(CardModel card) => card != ctx.Source && card.Owner == ctx.Source.Owner
            && card.Pile?.Type == PileType.Hand && MaterialPayment.CanUseAsMaterial(card);
        count = Math.Min(count, PileType.Hand.GetPile(ctx.Source.Owner).Cards.Count(Eligible));
        if (count <= 0 || !ctx.CanContinue) return;
        var cards = (await CardSelectCmd.FromHand(ctx.ChoiceContext, ctx.Source.Owner,
            Prefs(ctx, action.Kind == FusionActionKind.Discard ? "SELECT_SPECIAL_CARD_MOD_MYTH_DISCARD" : "SELECT_SPECIAL_CARD_MOD_MYTH_EXHAUST", count),
            filter: Eligible, source: ctx.Source)).ToList();
        if (!ctx.CanContinue || cards.Count != count || cards.Distinct().Count() != count || cards.Any(c => !Eligible(c))) return;
        if (action.Kind == FusionActionKind.ExhaustHand)
            await MaterialPayment.Exhaust(ctx.ChoiceContext, ctx.Source.Owner, cards, count, PileType.Hand);
        else await CardCmd.Discard(ctx.ChoiceContext, cards);
    }

    private static async Task ExhaustDiscard(FusionAction action, int count, FusionActionContext ctx)
    {
        var owner = ctx.Source.Owner;
        bool Eligible(CardModel card) => card != ctx.Source && card.Owner == owner
            && card.Pile?.Type == PileType.Discard && MaterialPayment.CanUseAsMaterial(card) && action.Matches(card);
        var pile = PileType.Discard.GetPile(owner);
        count = Math.Min(count, pile.Cards.Count(Eligible));
        if (count <= 0 || !ctx.CanContinue) return;
        var cards = (await CardSelectCmd.FromCombatPile(ctx.ChoiceContext, pile, owner,
            Prefs(ctx, "SELECT_SPECIAL_CARD_MOD_MYTH_EXHAUST_DISCARD", count), Eligible)).ToList();
        if (!ctx.CanContinue || cards.Count != count || cards.Distinct().Count() != count || cards.Any(c => !Eligible(c))) return;
        await MaterialPayment.Exhaust(ctx.ChoiceContext, owner, cards, count, PileType.Discard);
    }

    private static async Task Recover(FusionAction action, int count, PileType pileType, FusionActionContext ctx)
    {
        var owner = ctx.Source.Owner;
        bool Eligible(CardModel card) => card != ctx.Source && card.Owner == owner && !card.HasBeenRemovedFromState
            && card.Pile?.Type == pileType && action.Matches(card);
        var pile = pileType.GetPile(owner);
        count = Math.Min(count, pile.Cards.Count(Eligible));
        if (count <= 0 || !ctx.CanContinue) return;
        var cards = (await CardSelectCmd.FromCombatPile(ctx.ChoiceContext, pile, owner,
            Prefs(ctx, "SELECT_SPECIAL_CARD_MOD_MYTH_RECOVER", count), Eligible)).ToList();
        if (!ctx.CanContinue || cards.Count != count || cards.Distinct().Count() != count || cards.Any(c => !Eligible(c))) return;
        foreach (var card in cards)
        {
            if (!ctx.CanContinue || !Eligible(card)) return;
            await CardPileCmd.Add(card, PileType.Hand);
            if (action.Kind == FusionActionKind.RecoverExhaustFree && ctx.CanContinue && card.Pile?.Type == PileType.Hand)
            { card.EnergyCost.SetThisTurn(0); card.SetStarCostThisTurn(0); card.InvokeEnergyCostChanged(); }
        }
    }
}
