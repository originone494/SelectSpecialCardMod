using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Rules;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 耀圣·月诗-福尔图娜。PortraitPath 继承基类，以准确类名作为 PNG 文件名。
public class YaoShengYueShiFuErTuNaCard : SelectSpecialBaseCard
{
    private bool _startedInCenter;
    private ICombatState? _startingCombat;

    public YaoShengYueShiFuErTuNaCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy) { }

    public override int StarLevel => 6;
    protected override HashSet<CardTag> CanonicalTags => [SelectTags.YaoSheng, SelectTags.YaoShengGuaiShou, ..StarTags];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(16m, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
    [
        HoverTipFactory.FromKeyword(SelectKeywords.YaoSheng),
        HoverTipFactory.FromKeyword(SelectKeywords.YaoShengGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.YaoShengXianJing),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
        new HoverTip(new LocString("static_hover_tips", "SELECT_SPECIAL_CARD_MOD_HAND_CENTER.title"),
            new LocString("static_hover_tips", "SELECT_SPECIAL_CARD_MOD_HAND_CENTER.description").GetFormattedText()),
        ..StarHoverTips,
    ];

    internal void CaptureStartingPosition()
    {
        _startingCombat = CombatState;
        _startedInCenter = YaoShengHuaShiLuXiNaCard.IsAtHandCenter(this);
        Entry.Logger.Info($"[Fortuna] play-start: center={_startedInCenter}, pile={Pile?.Type}, handCount={PileType.Hand.GetPile(Owner).Cards.Count}.");
    }

    // 按实际数值的增益/减益性质判断，避免把负力量等减益当作增益清除。
    internal static bool IsRemovableBuff(PowerModel power)
        => power.IsVisible && power.TypeForCurrentAmount == PowerType.Buff;

    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        var player = Owner;
        var combat = CombatState;
        bool fromCenter = _startedInCenter && combat != null && _startingCombat == combat;
        bool CanContinue() => combat != null && player.Creature.CombatState == combat
            && player.Creature.IsAlive && !CombatManager.Instance.IsOverOrEnding;

        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay).Targeting(cardPlay.Target).Execute(context);
        if (!fromCenter || !CanContinue())
        {
            Entry.Logger.Info($"[Fortuna] buff choice skipped: center={fromCenter}, canContinue={CanContinue()}.");
            return;
        }

        await RemoveChosenBuff(context, combat!, cardPlay.Target);
        if (!CanContinue()) return;

        // 清除增益每次从中央打出均可触发；只有后面的检索共享同名次数。
        string key = PerCombatOnce.Key("YaoShengFuErTuNaRetrieve", Id.ToString());
        using var reservation = PlayerEffectState.For(player).Reserve(key, PlayerEffectState.Combat());
        if (reservation == null) return;
        try
        {
            var retrieved = await SelectToolCmd.RetrieverCard(context, player,
                c => c.Id != Id && c.Tags.Contains(SelectTags.YaoShengXianJing),
                amount: 1, source: this, minAmount: 1, requireManualConfirmation: true);
            if (retrieved.Count > 0) reservation.Commit();
        }
        catch
        {
            reservation.Commit();
            throw;
        }
    }

    private async Task RemoveChosenBuff(PlayerChoiceContext context, ICombatState combat, Creature target)
    {
        bool CanContinue() => Owner.Creature.CombatState == combat && Owner.Creature.IsAlive
            && !CombatManager.Instance.IsOverOrEnding;
        bool IsEligible(Creature enemy) => enemy.IsAlive && enemy.CombatState == combat
            && combat.Enemies.Contains(enemy) && enemy.Powers.Any(IsRemovableBuff);
        // 只清除这次攻击目标的能力，不另选敌人，也不转移到其他仍有增益的敌人。
        // 清除不是再次攻击：目标因伤害变得不可命中时，只要仍在场存活，就继续选择增益。
        if (!CanContinue() || !IsEligible(target))
        {
            Entry.Logger.Info($"[Fortuna] target has no removable buff or is unavailable: target={target.LogName}.");
            return;
        }

        // 一种增益按 ModelId 分组；清除该种类全部层数及现有实例。
        var powers = target.Powers.Where(IsRemovableBuff).GroupBy(p => p.Id)
            .OrderBy(g => g.Key.ToString(), StringComparer.Ordinal).Select(g => g.First()).ToList();
        var powerOptions = powers.Select(power =>
        {
            var text = new LocString("card_selection", "SELECT_SPECIAL_CARD_MOD_FORTUNA_POWER_DETAIL");
            text.Add("EnemyName", target.Name);
            text.Add("PowerDescription", power.DumbHoverTip.Description);
            return (power, power.Title.GetFormattedText(), text.GetFormattedText());
        }).ToList();
        Entry.Logger.Info($"[Fortuna] opening buff choice: target={target.LogName}, options={powers.Count}.");
        var picked = await Choose(context, combat, powerOptions,
            new LocString("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_FORTUNA_POWER"));
        if (!CanContinue() || !IsEligible(target) || picked == null
            || !target.Powers.Contains(picked) || !IsRemovableBuff(picked)) return;

        foreach (var power in target.Powers.Where(p => p.Id == picked.Id && IsRemovableBuff(p)).ToList())
        {
            // 移除可能触发连锁，后续实例须重新确认，不能清除已转为减益的能力。
            if (!CanContinue() || !target.IsAlive || target.CombatState != combat
                || !combat.Enemies.Contains(target)) return;
            if (target.Powers.Contains(power) && IsRemovableBuff(power))
            {
                await PowerCmd.Remove(power);
                Entry.Logger.Info($"[Fortuna] buff removed: target={target.LogName}, power={power.Id}, removed={!target.Powers.Contains(power)}.");
            }
        }
    }

    private async Task<T?> Choose<T>(PlayerChoiceContext context, ICombatState combat,
        IReadOnlyList<(T Target, string Title, string Text)> choices, LocString prompt) where T : class
    {
        if (choices.Count == 0) return null;
        var options = new List<CardModel>();
        var targets = new Dictionary<CardModel, T>();
        try
        {
            foreach (var choice in choices)
            {
                var option = combat.CreateCard<NamedEffectChoiceCard>(Owner);
                option.UseAppearanceOf(this);
                option.ChoiceTitle = choice.Title;
                option.EffectText = choice.Text;
                options.Add(option);
                targets[option] = choice.Target;
            }
            // 网格支持任意数量选项，超过3种增益也能完整选择，并使用原版联机选择同步。
            var picked = (await CardSelectCmd.FromSimpleGrid(context, options, Owner,
                CardPrefsConfirm(this, prompt, 1, 1))).FirstOrDefault();
            return picked != null && targets.TryGetValue(picked, out var target) ? target : null;
        }
        finally
        {
            foreach (var option in options)
                if (!option.HasBeenRemovedFromState && option.Pile == null) option.RemoveFromState();
        }
    }
}
