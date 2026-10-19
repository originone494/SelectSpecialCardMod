using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
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

// 刻魔·落泪之日（额外怪兽 · 融合怪兽）
//   融合素材：光恶魔×2（卡面文案，实际靠别的卡的效果融合）
//   打出：造成24点伤害
//   登场：从消耗堆·除外堆将1张「光恶魔」加入手牌（是/否，与珠泪·水仙人鱼同款）
//   驻场：这张卡位于手牌时，所有敌人失去6点力量（真的挂「力量 −6」，血条上方能看到图标）
//   消耗：可以从消耗堆将1张除此卡以外的「光恶魔」返回抽牌堆·额外堆（额外怪兽→额外堆），对所有敌人造成12点伤害
public class KeMoLuoLeiRiZiCard : SelectSpecialBaseCard, IDengChangCard, IKeMoFusionMonster
{
    public KeMoLuoLeiRiZiCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(24m, ValueProp.Move),
    ];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.GuangEMo,
        SelectTags.KeMoGuaiShou,
        SelectTags.KeMo,
        SelectTags.RongHeGuaiShou,
        SelectTags.DengChang,
        SelectTags.ZhuChang,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.GuangEMo),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMoGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMo),
        HoverTipFactory.FromKeyword(SelectKeywords.RongHeGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.DengChang),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuChang),
    ];

    private static bool IsGuangEMo(CardModel c) => c.Tags.Contains(SelectTags.GuangEMo);

    // 融合素材：光恶魔×2
    public IReadOnlyList<KeMoFusionMaterial> FusionMaterials => [KeMoFusionMaterial.Tag(SelectTags.GuangEMo, 2)];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成24点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    // 驻场：这张卡位于手牌时，所有敌人失去6点力量（真的给敌人挂上「力量 −6」，血条上方能看到图标）
    private const int ZhuChangStrengthDown = 6;

    // 记录**实际被扣了力量**的敌人。离开手牌时只把记录里的敌人恢复，
    // 而不是"给当时还活着的所有敌人 +6"——后者在"某只敌人这次没被扣（例如被人工制品挡下）"
    // 或"新敌人是在其它时点入场"的情况下会把力量错误地加上去。
    private Dictionary<Creature, int> _zhuChangReduced = [];
    private HashSet<Creature> _zhuChangAttempted = [];
    private CombatId? _zhuChangCombat;

    private void EnsureZhuChangCombat()
    {
        var current = CombatManager.Instance.CurrentCombatId;
        if (_zhuChangCombat == current) return;
        _zhuChangReduced.Clear();
        _zhuChangAttempted.Clear();
        _zhuChangCombat = current;
    }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        // CardModel 使用浅克隆，驻场账本必须属于新实例。
        _zhuChangReduced = [];
        _zhuChangAttempted = [];
        _zhuChangCombat = null;
    }

    // 这张卡进出「手牌」时：进手牌 → 给所有敌人 -6 力量；离开手牌 → 把记录里的敌人恢复
    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        await base.AfterCardChangedPiles(card, oldPileType, clonedBy);
        if (card != this) return;
        if (CombatState == null) return;
        EnsureZhuChangCombat();

        bool nowHand = Pile?.Type == PileType.Hand;
        bool wasHand = oldPileType == PileType.Hand;
        if (nowHand == wasHand) return;   // 与手牌无关的移动，不处理

        var ctx = new BlockingPlayerChoiceContext();
        if (nowHand)
        {
            var targets = CombatState.HittableEnemies.Where(e => !_zhuChangAttempted.Contains(e)).ToList();
            if (targets.Count == 0) return;
            if (!await ZhuChangEffectVfx.Play(this, card)) return;
            if (CombatState == null || Pile?.Type != PileType.Hand) return;
            foreach (var enemy in targets.Where(e => e.IsAlive && CombatState.HittableEnemies.Contains(e)))
            {
                await ReduceStrength(ctx, enemy);
            }
        }
        else
        {
            await RestoreStrength(ctx);
        }
    }

    // 驻场期间新入场的敌人也要失去力量
    public override async Task AfterCreatureAddedToCombat(Creature creature)
    {
        await base.AfterCreatureAddedToCombat(creature);
        if (Pile?.Type != PileType.Hand) return;
        if (CombatState == null) return;
        EnsureZhuChangCombat();
        if (!creature.IsMonster) return;

        if (_zhuChangAttempted.Contains(creature) || !CombatState.HittableEnemies.Contains(creature)) return;
        if (!await ZhuChangEffectVfx.Play(this, creature)) return;
        if (CombatState == null || Pile?.Type != PileType.Hand
            || !CombatState.HittableEnemies.Contains(creature)) return;
        await ReduceStrength(new BlockingPlayerChoiceContext(), creature);
    }

    private async Task ReduceStrength(PlayerChoiceContext ctx, Creature enemy)
    {
        if (!_zhuChangAttempted.Add(enemy)) return;
        int before = enemy.GetPower<StrengthPower>()?.Amount ?? 0;
        try { await PowerCmd.Apply<StrengthPower>(ctx, enemy, -ZhuChangStrengthDown, Owner?.Creature, this); }
        catch { _zhuChangAttempted.Remove(enemy); throw; }
        int reduced = before - (enemy.GetPower<StrengthPower>()?.Amount ?? 0);
        // 返回值为空也可能是力量恰好降到 0；按实际变化记账。
        if (reduced > 0) _zhuChangReduced[enemy] = reduced;
    }

    private async Task RestoreStrength(PlayerChoiceContext ctx)
    {
        var reductions = _zhuChangReduced.ToList();
        _zhuChangReduced.Clear();
        _zhuChangAttempted.Clear();
        foreach (var (enemy, amount) in reductions)
        {
            if (enemy.IsAlive)
                await PowerCmd.Apply<StrengthPower>(ctx, enemy, amount, Owner?.Creature, this);
        }
    }

    // 登场：从消耗堆·除外堆将1张「光恶魔」加入手牌（是/否）
    private static LocString DengChangPickPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_LUO_LEI_RI_ZI_DENG_CHANG_PICK");

    public async Task DengChangSpecial(PlayerChoiceContext ctx, Player player)
    {
        // 消耗堆·除外堆：只有一堆有卡就直接从那堆选，两堆都有才问"是否从消耗堆将卡加入手牌？"
        var selected = await SelectToolCmd.PickFromTwoPiles(
            ctx, player, IsGuangEMo,
            PileType.Exhaust, Entry.ChuWaiPile,
            SelectToolCmd.ExhaustOrChuWaiChoicePrompt, DengChangPickPrompt, this);
        foreach (var card in selected)
        {
            await CardPileCmd.Add(card, PileType.Hand);
        }
    }

    // 消耗：这张卡被消耗时发动
    private static LocString XiaoHaoChoicePrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_LUO_LEI_RI_ZI_XIAO_HAO_CHOICE");
    private static LocString XiaoHaoPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_LUO_LEI_RI_ZI_XIAO_HAO");

    public override async Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
    {
        await base.AfterCardExhausted(choiceContext, card, causedByEthereal);
        if (card != this) return;
        if (Owner == null) return;
        if (CombatManager.Instance.IsOverOrEnding) return;
        if (CombatState == null) return;

        var exhaustPile = PileType.Exhaust.GetPile(Owner);
        if (!exhaustPile.Cards.Any(c => c != this && IsGuangEMo(c))) return;
        // 无候选不询问；放弃本次效果时，不返回卡牌，也不造成伤害。
        if (!await SelectToolCmd.AskYesNo(choiceContext, Owner, XiaoHaoChoicePrompt, source: this)) return;
        var picked = (await CardSelectCmd.FromCombatPile(
            choiceContext, exhaustPile, Owner, CardPrefs(this, XiaoHaoPrompt, 1, 1),
            filter: c => c != this && IsGuangEMo(c))).FirstOrDefault();
        if (picked == null || picked.Pile?.Type != PileType.Exhaust) return;

        // 必须成功返回一张光恶魔，才继续造成群体伤害。
        if (CardClassification.IsExtraMonster(picked))
        {
            if (!await ExtraDeckSeries.ReturnExtraMonster(Owner, picked)) return;
        }
        else
        {
            await SelectToolCmd.ReturnCardToPile(picked, PileType.Draw);
            if (picked.Pile?.Type != PileType.Draw) return;
        }
        if (CombatManager.Instance.IsOverOrEnding || CombatState == null) return;

        // 对所有敌人造成12点伤害
        var enemies = CombatState.HittableEnemies.ToList();
        if (enemies.Count > 0)
        {
            await CreatureCmd.Damage(choiceContext, enemies, 12m, ValueProp.Move, Owner.Creature);
        }
    }
}
