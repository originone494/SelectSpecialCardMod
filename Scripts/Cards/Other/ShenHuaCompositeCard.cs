using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Scaffolding.Content;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Other;

// 一个真实的技能实例；费用与消耗在生成时固定，所有模块动作直接以本卡为来源执行。
public class ShenHuaCompositeCard : DerivedTokenCard
{
    private string _moduleIdsJson = "[]";
    private IReadOnlyList<FusionEffectDefinition> _modules = Array.Empty<FusionEffectDefinition>();
    private IReadOnlyList<CardTag> _moduleTags = Array.Empty<CardTag>();
    private int _generatedTraits = -1;
    public ShenHuaCompositeCard() : base(CardType.Skill, TargetType.Self) { }

    // -1是旧档/尚未生成的默认值：保持原来的0费、无消耗；0～7低两位为费用，第3位为消耗。
    [SavedProperty]
    public int GeneratedTraits
    {
        get => _generatedTraits;
        set
        {
            AssertMutable();
            if (value < -1 || value > 7) throw new ArgumentOutOfRangeException(nameof(value));
            if (_generatedTraits == value) return;
            if (_generatedTraits >= 0) throw new InvalidOperationException("A generated myth card cannot reroll its traits.");
            _generatedTraits = value;
            EnergyCost.SetCustomBaseCost(GeneratedEnergyCost);
            // 创建/升级时原版可能已经缓存过关键词，因此同时更新实例自己的关键词集合。
            if (GeneratedExhaust) AddKeyword(CardKeyword.Exhaust);
            else RemoveKeyword(CardKeyword.Exhaust);
        }
    }

    public int GeneratedEnergyCost => _generatedTraits < 0 ? 0 : _generatedTraits & 3;
    public bool GeneratedExhaust => _generatedTraits >= 0 && (_generatedTraits & 4) != 0;

    public void RollGeneratedTraits()
    {
        AssertMutable();
        if (_generatedTraits >= 0) throw new InvalidOperationException("Myth traits have already been generated.");
        var rng = Owner.RunState.Rng.Niche;
        // 两次独立的局内随机：0/1/2/3各25%，消耗/不消耗各50%。只在新生成入口调用。
        GeneratedTraits = rng.NextInt(0, 4) | (rng.NextInt(0, 2) == 1 ? 4 : 0);
    }

    [SavedProperty]
    public string ModuleIdsJson
    {
        get => _moduleIdsJson;
        set
        {
            AssertMutable();
            if (_moduleIdsJson == value) return;
            if (_modules.Count > 0) throw new InvalidOperationException("A generated myth card cannot reroll its modules.");
            var ids = JsonSerializer.Deserialize<string[]>(value) ?? throw new JsonException("Missing myth modules.");
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
                throw new JsonException("Myth modules must be unique.");
            var modules = ids.Select(id => FusionEffectLibrary.Find(id)
                ?? throw new JsonException("Unknown myth module: " + id)).ToArray();
            _moduleIdsJson = JsonSerializer.Serialize(ids);
            _modules = Array.AsReadOnly(modules);
            _moduleTags = Array.AsReadOnly(modules.SelectMany(m => m.Tags)
                .Where(FusionEffectLibrary.IsAllowedTag).Distinct().OrderBy(t => t).ToArray());
        }
    }

    public IReadOnlyList<FusionEffectDefinition> Modules => _modules;
    // 规范模板没有随机配方，不能作为本体打出；已绑定的旧实例仍沿用原效果。
    protected override bool IsPlayable => Modules.Count > 0;
    public void BindModules(IEnumerable<FusionEffectDefinition> modules)
        => ModuleIdsJson = JsonSerializer.Serialize(modules.Select(m => m.Id).ToArray());
    public override IEnumerable<CardTag> Tags => _moduleTags;
    public override IEnumerable<CardKeyword> CanonicalKeywords => GeneratedExhaust ? [CardKeyword.Exhaust] : [];
    public override TargetType TargetType => Modules.Any(m => m.Actions.Any(a => a.RequiresEnemy)) ? TargetType.AnyEnemy : TargetType.Self;
    public override bool GainsBlock => Modules.Any(m => m.Actions.Any(a => a.Kind == FusionActionKind.Block));
    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => FusionBlockDamageScope.Multiplier(this, target, props, dealer, cardSource, cardPlay);
    // Token仍沿用本体的整卡插画布局，不向普通窄插画窗塞先古卡图。
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: "res://SelectSpecialCardMod/images/cards/SuiYuanShenHuaHeTiCard.png", VisualStyle: CardVisualStyle.Ancient);

    public override string Title
    {
        get
        {
            if (Modules.Count == 0) return base.Title;
            var title = new LocString("cards", Id.Entry + (Modules.Count == 1 ? ".name_one" : Modules.Count == 2 ? ".name_two" : ".name_many"));
            title.Add("A", Modules[0].Name);
            title.Add("B", Modules.Count > 1 ? Modules[1].Name : "");
            title.Add("Count", Modules.Count);
            return title.GetFormattedText() + (IsUpgraded ? "+" : "");
        }
    }

    // 按模块/动作预声明独立变量，避免生成后修改规范模板或重置复制/附魔后的数值。
    // 各伤害与格挡数值使用原版预览，力量、虚弱等修正可显示到卡面。
    protected override IEnumerable<DynamicVar> CanonicalVars => FusionEffectLibrary.All
        .SelectMany(m => m.Actions.Select((a, i) => a.CreateVar(FusionEffectLibrary.VarKey(m.Id, i))));

    protected override IEnumerable<IHoverTip> AdditionalHoverTips
        => FusionEffectLibrary.TagHoverTips(Tags).Concat(Modules.SelectMany(m => m.Actions).SelectMany(a => a.HoverTips()));

    protected override void AddExtraArgsToDescription(LocString description)
    {
        base.AddExtraArgsToDescription(description);
        description.Add("EffectText", Modules.Count == 0
            ? new LocString("static_hover_tips", "SELECT_SPECIAL_CARD_MOD_MYTH_UNBOUND_LEGACY.description").GetFormattedText()
            : string.Join("\n", Modules.Select((m, i) => $"{i + 1}.[gold]{m.Name}[/gold]：{m.Describe(this)}")));
    }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        => FusionEffectRunner.Execute(this, choiceContext, cardPlay);
}
