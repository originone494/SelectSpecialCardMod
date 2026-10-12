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
using SelectSpecialCardMod.Scripts.Cards.Other;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 珠泪·露莎卡人鱼（融合怪兽）
//   打出：造成15点伤害
//   驻场：回合结束时，若没有人工制品，获得1层人工制品（本卡在手上时）
//   应对：同名卡一回合一次。受到敌人攻击前，若手牌存在「珠泪」卡，可以选择丢弃1张「珠泪」卡，
//         使那次攻击造成的伤害为0。
//   送墓：同名卡一回合一次。打出这张卡，之后，将这张卡加入手牌。
public class ZhuLeiLuShaKaCard : SelectSpecialBaseCard, ISongMuCard
{
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
        SelectTags.ExtraMonster,
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

        await PowerCmd.Apply<ArtifactPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }

    // 应对：受到敌人攻击前（参考小美人鱼 / 天下独步的大义贼的 BeforeDamageReceived 写法）
    public override async Task BeforeDamageReceived(PlayerChoiceContext choiceContext, Creature target, decimal amount,
        ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner.Creature) return;
        if (CombatState == null) return;
        if (!PileType.Hand.GetPile(Owner).Cards.Contains(this)) return;
        if (cardSource != null) return;                      // 只要"受到敌人攻击"，排除卡牌/能力造成的伤害
        if (dealer == null || !dealer.IsMonster) return;

        var player = Owner;
        if (player is null) return;

        // 同名卡一回合一次
        string onceKey = PerTurnOnce.Key("YingDui", Id.Entry);
        if (PerTurnOnce.IsUsed(player, onceKey)) return;

        // 条件：手牌存在「珠泪」卡（本卡自己也是珠泪卡，所以在手牌时就已满足）
        if (!PileType.Hand.GetPile(player).Cards.Any(ZhuLeiFilter.IsCard)) return;

        // 是/否询问
        var shi = player.Creature.CombatState!.CreateCard<ShiCard>(player);
        var fou = player.Creature.CombatState!.CreateCard<FouCard>(player);
        CardModel? picked = (await CardSelectCmd.FromSimpleGrid(
            choiceContext,
            new List<CardModel> { shi, fou },
            player,
            CardPrefs(this, YingDuiChoicePrompt, 1, 1))).FirstOrDefault();
        if (picked is not ShiCard) return; // 否/取消 → 不发动，照常吃伤害

        PerTurnOnce.Mark(player, onceKey);

        // 选择丢弃1张「珠泪」卡
        var selected = (await CardSelectCmd.FromHandForDiscard(
            prefs: CardPrefs(this, SelectionScreenPrompt, 1, 1),
            context: choiceContext,
            player: player,
            filter: ZhuLeiFilter.IsCard,
            source: this)).ToList();
        if (selected.Count == 0) return;

        await CardCmd.Discard(choiceContext, selected[0]);

        // 举旗：让这次伤害变成真·0（连格挡都不掉，见 Tool/LuShaKaDamageNegate.cs）
        LuShaKaDamageNegate.Mark(Owner.Creature);
    }

    // 同步钩子：把这次伤害的 HP 损失清零（用 BeforeOsty 阶段，它一定收到"原本的目标"，
    // 不会被后面的 unblocked-damage 重定向换掉目标，所以旗子必定被收走）
    public override decimal ModifyHpLostBeforeOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (!LuShaKaDamageNegate.Consume(target)) return amount;

        return 0m;   // 只抵消这一次
    }

    // 送墓：同名卡一回合一次。打出这张卡，之后，将这张卡加入手牌。
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        string onceKey = PerTurnOnce.Key("SongMu", Id.Entry);
        if (PerTurnOnce.IsUsed(player, onceKey)) return;
        // 先记账：连锁里再丢掉一张同名卡时，它的送墓不会二次发动
        PerTurnOnce.Mark(player, onceKey);

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
