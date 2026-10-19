using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
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
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 珠泪·露莎卡人鱼（融合怪兽）
//   打出：造成15点伤害
//   驻场：回合结束时，若没有人工制品，获得1层人工制品（本卡在手上时）
//   应对：同名卡一回合一次。受到敌人攻击前，若手牌存在「珠泪」卡，可以选择丢弃1张「珠泪」卡，
//         使那次攻击造成的伤害为0。
//   送墓：每个实例仅一次。打出这张卡，之后，将这张卡加入手牌。
public class ZhuLeiLuShaKaCard : SelectSpecialBaseCard, ISongMuCard
{
    private bool _songMuUsed;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        // 新副本是独立实例，不继承来源实例的送墓次数。
        _songMuUsed = false;
    }

    public ZhuLeiLuShaKaCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    // 伤害使用动态变量：造成 15 点伤害
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(15m, ValueProp.Move),
    };

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ZhuLei,
        SelectTags.RongHeGuaiShou,
        SelectTags.ZhuLeiGuaiShou,
        SelectTags.YingDui,
        SelectTags.SongMu,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
        CardKeyword.Retain,
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Retain),
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.RongHeGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.YingDui),
    ];

    private static LocString YingDuiChoicePrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_ZHU_LEI_LU_SHA_KA_YING_DUI");

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成15点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await CreatureCmd.Damage(choiceContext, cardPlay.Target, DynamicVars.Damage.BaseValue, ValueProp.Move, Owner.Creature, this, cardPlay);
    }

    // 驻场：回合结束时（用 BeforeSideTurnEnd：它在"清空手牌"之前），
    //       若没有人工制品，获得1层人工制品（本卡在手上时）
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player) return;
        if (CombatState == null) return;
        if (!PileType.Hand.GetPile(Owner).Cards.Contains(this)) return;
        if (Owner.Creature.HasPower<ArtifactPower>()) return;

        if (!await ZhuChangEffectVfx.Play(this, this)) return;
        if (CombatState == null || Pile?.Type != PileType.Hand
            || Owner.Creature.HasPower<ArtifactPower>()) return;
        await PowerCmd.Apply<ArtifactPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }

    // 应对：受到敌人攻击前（参考小美人鱼 / 天下独步的大义贼的 BeforeDamageReceived 写法）
    public override async Task BeforeDamageReceived(PlayerChoiceContext choiceContext, Creature target, decimal amount,
        ValueProp props, Creature? dealer, CardModel? cardSource)
    {

        if (FeatureStatus.Checked && !FeatureStatus.AttackProtection) return;        if (target != Owner.Creature) return;
        if (ResponseDamageNegate.IsWholeAttackMarked(target)) return;
        if (CombatState == null) return;
        if (!PileType.Hand.GetPile(Owner).Cards.Contains(this)) return;
        if (cardSource != null) return;                      // 只要"受到敌人攻击"，排除卡牌/能力造成的伤害
        if (dealer == null || !dealer.IsMonster) return;

        var player = Owner;
        if (player is null) return;

        // 同名卡一回合一次
        string onceKey = PerTurnOnce.Key("YingDui", Id.ToString());
        if (PerTurnOnce.IsUsed(player, onceKey)) return;

        // 条件：手牌存在「珠泪」卡（本卡自己也是珠泪卡，所以在手牌时就已满足）
        if (!PileType.Hand.GetPile(player).Cards.Any(ZhuLeiFilter.IsCard)) return;

        // ①发动应对；②不发动，照常受到伤害。
        if (!await SelectToolCmd.AskYesNo(choiceContext, player, YingDuiChoicePrompt, source: this)) return;

        PerTurnOnce.Mark(player, onceKey);

        // 选择丢弃1张「珠泪」卡
        var selected = (await CardSelectCmd.FromHandForDiscard(
            prefs: CardPrefs(this, SelectionScreenPrompt, 1, 1),
            context: choiceContext,
            player: player,
            filter: ZhuLeiFilter.IsCard,
            source: null!)).ToList();
        if (selected.Count == 0) return;

        await CardCmd.Discard(choiceContext, selected[0]);

        // 保护同一次攻击的全部伤害段，连格挡也不消耗。
        ResponseDamageNegate.MarkWholeAttack(target);
    }

    // 送墓：每个实例仅一次，换回合不恢复，同名的不同实例互不占用。
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        if (_songMuUsed || Owner != player || Pile?.Type != PileType.Discard
            || CombatState == null || CombatManager.Instance.IsOverOrEnding) return;
        // 在自动打出前记账，防止本实例在连锁中再次送墓时重入。
        _songMuUsed = true;

        // ① 打出这张卡（随机敌人）
        Creature? enemy = player.RunState.Rng.CombatTargets.NextItem(player.Creature.CombatState!.HittableEnemies);
        if (enemy != null)
        {
            await SongMuHook.AutoPlayFromDiscard(ctx, this, enemy);
        }

        // ② 之后：将这张卡加入手牌
        await CardPileCmd.Add(this, PileType.Hand);
    }
}
