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
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Intent;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 刻魔·震怒之日（额外怪兽 · 融合怪兽）
//   融合素材：「刻魔·魔镌客」+光恶魔×2
//   打出：造成28点伤害
//   特殊效果（右键手牌发动）：随机击晕 N 只敌人（N = 这张卡当前装备数）；装备数 0 时不能发动
//   消耗（这张卡被消耗时）：消耗堆里必须有"除此卡以外的「光恶魔」"，否则整条不发动；
//         先把它返回抽牌堆·额外堆，再消耗一名敌人的 1 张意图卡牌（玩家从所有敌人的意图卡里选）
public class KeMoZhenNuRiZiCard : SelectSpecialBaseCard, IKeMoFusionMonster, ISpecialEffectCard
{
    public KeMoZhenNuRiZiCard() : base(2, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar(28m, ValueProp.Move),
    ];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ExtraMonster,
        SelectTags.GuangEMo,
        SelectTags.KeMoGuaiShou,
        SelectTags.KeMo,
        SelectTags.RongHeGuaiShou,
        SelectTags.SpecialEffect,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.ExtraMonster),
        HoverTipFactory.FromKeyword(SelectKeywords.GuangEMo),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMoGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMo),
        HoverTipFactory.FromKeyword(SelectKeywords.RongHeGuaiShou),
        HoverTipFactory.FromKeyword(SelectKeywords.SpecialEffect),
    ];

    // 融合素材：「刻魔·魔镌客」×1 + 光恶魔×2
    public IReadOnlyList<KeMoFusionMaterial> FusionMaterials =>
    [
        KeMoFusionMaterial.Card<KeMoMoJianKeCard>(1),
        KeMoFusionMaterial.Tag(SelectTags.GuangEMo, 2),
    ];

    private static bool IsGuangEMo(CardModel c) => c.Tags.Contains(SelectTags.GuangEMo);

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 造成28点伤害
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay)
            .Targeting(cardPlay.Target)
            .Execute(choiceContext);
    }

    // 特殊效果：随机击晕 N 只敌人（N = 这张卡的装备数）。装备数 0 → 不能发动（不亮、右键不响应）
    public bool CanActivateSpecialEffect(Player player) => KeMoEquip.EquipCount(this) >= 1;

    public async Task ActivateSpecialEffect(PlayerChoiceContext ctx, Player player)
    {
        int count = KeMoEquip.EquipCount(this);
        if (count <= 0) return;
        if (CombatState == null) return;

        // 从当前可命中的敌人里"不重复地随机"取最多 count 只，逐个击晕（原版 CreatureCmd.Stun）
        var pool = CombatState.HittableEnemies.ToList();
        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            var enemy = player.RunState.Rng.CombatTargets.NextItem(pool);
            if (enemy == null) break;
            pool.Remove(enemy);
            await CreatureCmd.Stun(enemy);
        }
    }

    // 「消耗」：这张卡被消耗时触发
    private static LocString ConsumeGuangEMoPrompt { get; } =
        new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_LUO_LEI_RI_ZI_XIAO_HAO");
    private static LocString ConsumeIntentPrompt { get; } =
        new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_INTENT_CONSUME");

    public override async Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
    {
        await base.AfterCardExhausted(choiceContext, card, causedByEthereal);
        if (card != this) return;
        if (Owner == null || CombatState == null) return;
        if (CombatManager.Instance.IsOverOrEnding) return;

        // ① 消耗堆里没有"除此卡以外的「光恶魔」" → 整条不发动
        var exhaustPile = PileType.Exhaust.GetPile(Owner);
        if (!exhaustPile.Cards.Any(c => c != this && IsGuangEMo(c))) return;

        var picked = (await CardSelectCmd.FromCombatPile(
            choiceContext,
            exhaustPile,
            Owner,
            CardPrefs(this, ConsumeGuangEMoPrompt, 1, 1),
            filter: c => c != this && IsGuangEMo(c))).FirstOrDefault();
        if (picked == null) return;

        // 额外怪兽 → 额外堆；其他的卡 → 抽牌堆
        if (picked.Tags.Contains(SelectTags.ExtraMonster))
            await ExtraDeckSeries.ReturnExtraMonster(Owner, picked);    // 额外怪兽：持久卡还给额外堆，副本退场
        else
            await SelectToolCmd.ReturnCardToPile(picked, PileType.Draw);

        // ② 消耗一名敌人的 1 张意图卡牌：把"所有敌人当前头顶的意图卡"列出来让玩家选
        await ConsumeEnemyIntentCard(choiceContext, Owner);
    }

    // 把敌人头顶的意图卡列成一个选卡界面，选中的那张就是被"消耗"的意图卡；
    // 拿走的方式复用「强夺」的零件 overlay.StealCard（只是不再复制一张回手牌）。
    private async Task ConsumeEnemyIntentCard(PlayerChoiceContext ctx, Player player)
    {
        if (CombatState == null) return;

        var enemies = CombatState.HittableEnemies.ToList();
        var candidates = new List<CardModel>();
        foreach (var enemy in enemies)
        {
            var overlay = IntentCardHook.FindOverlay(enemy);
            if (overlay == null) continue;
            candidates.AddRange(overlay.StealableModels);
        }
        if (candidates.Count == 0) return;

        var picked = (await CardSelectCmd.FromSimpleGrid(
            ctx,
            candidates,
            player,
            CardPrefs(this, ConsumeIntentPrompt, 1, 1))).FirstOrDefault();
        if (picked == null) return;

        foreach (var enemy in enemies)
        {
            var overlay = IntentCardHook.FindOverlay(enemy);
            if (overlay == null) continue;
            // ConsumeCard 只对"拥有这张意图卡"的那只怪成功（不是它的卡会直接返回 false）；
            // 演出用原版「消耗」特效（卡面侵蚀消散），不再是强夺的"飞向手牌区"
            if (!overlay.ConsumeCard(picked)) continue;

            // 路线 A：MonsterDeck 驱动的怪，StealCard 内部已经让它这一手空过；
            // 没被 MonsterDeck 跟踪的怪，则记一份"通用占位"，顶替它下一个动作（与强夺同一套）
            if (!MonsterDeck.IsTracked(enemy)) IntentCardTheft.Record(enemy, IntentCardTheft.TheftKind.Blocked);
            break;
        }
    }
}
