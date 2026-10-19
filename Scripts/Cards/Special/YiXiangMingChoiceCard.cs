using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 三张异响鸣魔法共用的「①/②选择、无灵摆区卡时随机、生命变化后继续效果」流程。
public abstract class YiXiangMingChoiceCard : SelectSpecialBaseCard
{
    protected YiXiangMingChoiceCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self) { }

    protected override HashSet<CardTag> CanonicalTags => [SelectTags.YiXiangMing, SelectTags.YiXiangMingMoFa];
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMing),
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMingMoFa),
        HoverTipFactory.FromKeyword(SelectKeywords.YiXiangMingXianJing),
        HoverTipFactory.FromKeyword(SelectKeywords.LingBaiQu),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
    ];

    public static bool HasYiXiangMingInPendulumZone(Player player)
        => YiXiangMingInPendulumZone(player).Any();

    public static IEnumerable<YiXiangMingVoiceCard> YiXiangMingInPendulumZone(Player player)
        => PileType.Hand.GetPile(player).Cards.OfType<YiXiangMingVoiceCard>()
            .Where(c => c.Owner == player && c.Tags.Contains(SelectTags.YiXiangMing)
                && c.LockedPendulumSide != PendulumEdgeSide.None && c.CanApplyPendulumEffect);

    protected sealed override async Task OnPlay(PlayerChoiceContext context, CardPlay cardPlay)
    {
        var combat = CombatState;
        if (combat == null || !CanContinue(combat)) return;
        bool first;
        if (HasYiXiangMingInPendulumZone(Owner))
        {
            var one = combat.CreateCard<EffectOneCard>(Owner);
            var two = combat.CreateCard<EffectTwoCard>(Owner);
            one.UseAppearanceOf(this);
            two.UseAppearanceOf(this);
            one.EffectText = new LocString("cards", Id.Entry + ".option1").GetFormattedText();
            two.EffectText = new LocString("cards", Id.Entry + ".option2").GetFormattedText();
            // 与原版飞溅相同的双卡选择界面；必须选择其中一项。
            var selected = await CardSelectCmd.FromChooseACardScreen(context, [one, two], Owner, canSkip: false);
            if (selected != one && selected != two) return;
            first = selected == one;
        }
        else
        {
            // 使用局内同步随机流，不能使用系统 Random。
            first = Owner.RunState.Rng.Niche.NextInt(0, 2) == 0;
        }

        if (!CanContinue(combat)) return;
        if (first)
            await CreatureCmd.Heal(Owner.Creature, 1m);
        else
            // 与原版祭品的失去生命值走同一命令，绕过格挡并触发生命变化相关效果。
            await CreatureCmd.Damage(context, Owner.Creature, 1m,
                ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, this, cardPlay);

        // 指示物连锁可能已经结束战斗，或失去生命值导致死亡。
        if (CanContinue(combat)) await ResolveChoice(context, first);
    }

    private bool CanContinue(ICombatState combat)
        => CombatState == combat && Owner?.Creature.IsAlive == true && !CombatManager.Instance.IsOverOrEnding;

    protected abstract Task ResolveChoice(PlayerChoiceContext context, bool first);
}
