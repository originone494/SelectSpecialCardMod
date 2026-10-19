using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace SelectSpecialCardMod.Scripts.Myth;

// 新机制独立升级规则；不继承特殊卡基类的“外观升级”策略，旧合体卡仍保留原类。
[RegisterCard(typeof(SelectSpecialCardPool))]
public class MythMutationCard : ModCardTemplate
{
    private string _snapshotJson = "";
    internal MythCardSnapshot? FixedSnapshot { get; private set; }
    internal bool PlayedFromHand { get; private set; }
    public MythMutationCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self, showInCardLibrary: false) { }
    [SavedProperty]
    public string SnapshotJson
    {
        get => _snapshotJson;
        set
        {
            AssertMutable();
            if (value == _snapshotJson) return;
            if (FixedSnapshot != null) throw new InvalidOperationException("Myth snapshot cannot reroll.");
            if (value.Length > 65536) throw new InvalidOperationException("Myth snapshot too large.");
            var parsed = MythJson.Read<MythCardSnapshot>(value);
            MythCardFactory.ValidateSnapshot(parsed);
            if (parsed.SeriesTags == null) parsed = parsed with { SeriesTags = MythSeriesTags.FromSource(parsed.Effect.SourceCard) };
            FixedSnapshot = parsed; _snapshotJson = MythJson.Write(parsed);
            EnergyCost.SetCustomBaseCost(parsed.EnergyCost);
            if (parsed.Exhaust) AddKeyword(CardKeyword.Exhaust); else RemoveKeyword(CardKeyword.Exhaust);
            ApplyValues(null);
        }
    }
    public MythCardSnapshot CaptureSnapshot() => MythJson.Read<MythCardSnapshot>(_snapshotJson);
    public void Bind(MythCardSnapshot snapshot) => SnapshotJson = MythJson.Write(snapshot);
    public override CardType Type => FixedSnapshot == null ? CardType.Skill : Enum.Parse<CardType>(FixedSnapshot.CardType);
    public override TargetType TargetType => FixedSnapshot?.Effect.Target switch
    { "enemy" => TargetType.AnyEnemy, "all_enemies" => TargetType.AllEnemies,
        "any_creature" => STS2RitsuLib.Combat.CardTargeting.CustomTargetType.Anyone, _ => TargetType.Self };
    public override bool GainsBlock => FixedSnapshot?.Actions.Any(MythEffectExecutor.IsBlock) == true;
    public override IEnumerable<CardTag> Tags => MythSeriesTags.Resolve(FixedSnapshot?.SeriesTags);
    public override IEnumerable<CardKeyword> CanonicalKeywords => FixedSnapshot?.Exhaust == true ? [CardKeyword.Exhaust] : [];
    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    protected override bool IsPlayable => FixedSnapshot != null && (Owner == null || MythConditionEvaluator.CanPlay(this));
    public override string Title => FixedSnapshot == null ? base.Title : MythCardText.Title(FixedSnapshot) + (IsUpgraded ? "+" : "");
    public override CardAssetProfile AssetProfile => new(PortraitPath: FixedSnapshot?.Art.Path ?? MythArtManifest.Fallback,
        BetaPortraitPath: FixedSnapshot?.Art.Path ?? MythArtManifest.Fallback, VisualStyle: CardVisualStyle.Ancient);
    protected override IEnumerable<DynamicVar> CanonicalVars => MythEffectExecutor.Variables();
    protected override IEnumerable<IHoverTip> AdditionalHoverTips
        => FixedSnapshot == null ? [] : MythEffectExecutor.HoverTips(FixedSnapshot.Actions).Concat(MythSeriesTags.HoverTips(FixedSnapshot.SeriesTags));
    protected override void AddExtraArgsToDescription(LocString description)
    { base.AddExtraArgsToDescription(description); description.Add("EffectText", MythCardText.Description(this)); }
    protected override Task OnPlay(PlayerChoiceContext context, CardPlay cardPlay) => MythEffectExecutor.Execute(this, context, cardPlay);
    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        await base.AfterCardChangedPiles(card, oldPileType, clonedBy);
        if (card == this) PlayedFromHand = Pile?.Type == PileType.Play && oldPileType == PileType.Hand;
    }
    protected override void AfterCloned() { base.AfterCloned(); PlayedFromHand = false; ReturnUsedTurn = -1; }
    [SavedProperty] public int ReturnUsedTurn { get; set; } = -1;
    internal bool CanReturnThisTurn()
    {
        int turn = Owner.PlayerCombatState?.TurnNumber ?? -1;
        if (turn < 0 || ReturnUsedTurn == turn) return false;
        ReturnUsedTurn = turn; return true;
    }
    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? source, CardPlay? play)
        => FusionBlockDamageScope.Multiplier(this, target, props, dealer, source, play);
    private void ApplyValues(MythCardSnapshot? before)
    {
        var s = FixedSnapshot!;
        for (int i = 0; i < s.Actions.Count; i++)
        {
            var a = s.Actions[i];
            var amount = DynamicVars[MythEffectExecutor.Key(i, a.Op)];
            var repeat = DynamicVars[MythEffectExecutor.RepeatKey(i)];
            if (before == null) { amount.BaseValue = a.Amount; repeat.BaseValue = a.Repeat; }
            else
            {
                int delta = a.Amount - before.Actions[i].Amount, repeatDelta = a.Repeat - before.Actions[i].Repeat;
                if (delta != 0) amount.UpgradeValueBy(delta);
                if (repeatDelta != 0) repeat.UpgradeValueBy(repeatDelta);
            }
            if (a.Op.StartsWith("lib_"))
            {
                var args = MythLibraryEffects.Args(a);
                var oldArgs = before == null ? null : MythLibraryEffects.Args(before.Actions[i]);
                for (int arg = 0; arg < args.Length; arg++)
                {
                    int number = int.TryParse(args[arg], out int value) ? value : 0;
                    var variable = DynamicVars[MythLibraryEffects.ArgKey(i, arg)];
                    if (oldArgs == null) variable.BaseValue = number;
                    else if (int.TryParse(oldArgs[arg], out int oldValue) && number != oldValue) variable.UpgradeValueBy(number - oldValue);
                }
            }
        }
    }
    protected override void OnUpgrade()
    {
        var old = FixedSnapshot;
        if (old == null || old.UpgradeLevel >= CurrentUpgradeLevel) return; // 读档按已保存等级恢复时不叠加。
        var values = new Dictionary<string, int>(old.EffectValues);
        foreach (var p in old.Effect.Upgrade) values[p.Key] += p.Value;
        var updated = old with { EffectValues = values, Actions = MythCardFactory.Resolve(old.Effect, values),
            EnergyCost = Math.Clamp(old.EnergyCost + old.Effect.UpgradeCostDelta, 0, 3), UpgradeLevel = CurrentUpgradeLevel };
        MythCardFactory.ValidateSnapshot(updated);
        FixedSnapshot = updated; _snapshotJson = MythJson.Write(updated); ApplyValues(old);
        if (updated.EnergyCost != old.EnergyCost) EnergyCost.UpgradeBy(updated.EnergyCost - old.EnergyCost);
    }
}
