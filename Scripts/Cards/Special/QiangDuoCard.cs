using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Intent;
using SelectSpecialCardMod.Scripts.Pool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 强夺（游戏王·魔法卡）
//   0费·技能·先古·消耗
//   效果：从一名敌人的意图卡牌中选择1张加入手牌。
//
// 真抢语义：选中的那张意图卡从怪物头顶移除（飞向手牌区演出），怪物行动时
// 对应的动作不发生——攻击少打一份（MonsterAttackSplitPatch 消费）、
// 格挡/强化/削弱/治疗少执行一次（IntentEffectFlyPatch 消费）、
// 召唤不入场（CreatureCmd.Add）、塞牌一张不塞（CardPileCmd.AddToCombatAndPreview）、
// 处决不自爆（CreatureCmd.Kill）、逃跑不离场（CreatureCmd.Escape）。
// 抢来的卡复制自意图展示卡（双面卡：玩家打出反向生效），打出后消耗。
//
// 每张强夺只能发动一次（打出即消耗），天然限制抢卡次数。
public class QiangDuoCard : SelectSpecialBaseCard
{
    public QiangDuoCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    // 关键字「消耗」：打出后这张卡消耗
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips =>
        [HoverTipFactory.FromKeyword(CardKeyword.Exhaust)];

    // 选牌提示：从该怪物的意图卡牌中选择1张抢夺
    private static LocString StealPrompt { get; } =
        new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_QIANG_DUO");

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        Player player = Owner;
        Creature target = cardPlay.Target;

        // ① 该怪物头顶的意图卡列表（overlay 不存在 = 意图未卡牌化，效果空转）
        var overlay = IntentCardHook.FindOverlay(target);
        if (overlay == null) return;
        var candidates = overlay.StealableModels;
        if (candidates.Count == 0) return;

        // ② 弹选牌界面选 1 张
        var picked = (await CardSelectCmd.FromSimpleGrid(
            choiceContext,
            candidates,
            player,
            CardPrefs(this, StealPrompt, 1, 1))).FirstOrDefault();
        if (picked == null) return;

        // ③ 抢：头顶移除（飞向手牌区演出）+ 记录被抢份数（怪物行动时对应动作不发生）
        if (!overlay.StealCard(picked)) return;
        // 路线 A：怪物出牌由 MonsterDeck 驱动，抢走即从手牌移除、这一手不会打出它，
        // 无需旧的「计数抵扣」机制（原来的 Count 消费链只服务于原版意图执行）
        if (!MonsterDeck.IsTracked(target))
            foreach (var kind in Classify(picked)) IntentCardTheft.Record(target, kind);

        // ④ 复制一张进手牌（双面卡：玩家打出反向生效），打出后消耗
        var copy = IntentCardMapper.CreateCopy(player, picked);
        // 抢来的意图卡一律带「消耗」关键字（卡面可见，打出后消耗）
        copy.AddKeyword(CardKeyword.Exhaust);
        await CardPileCmd.Add(copy, PileType.Hand);
    }

    // 意图卡类型 → 被抢计数种类（可多个：处决卡同时记账攻击与处决——
    // 攻击部分由拆分拦截取消、自爆死亡由 Kill 拦截取消，整招不发生）。
    // 塞诅咒牌多数实际走 PowerCmd.Apply（对玩家上负面能力），复用削弱计数；
    // 塞状态牌走 CardPileCmd.AddToCombatAndPreview，逃跑走 CreatureCmd.Escape，均已拦截。
    // 未知/隐藏意图本来就没有效果动作（天然空过），不记账。
    private static IReadOnlyList<IntentCardTheft.TheftKind> Classify(CardModel model) => model switch
    {
        MonsterAttackCard => [IntentCardTheft.TheftKind.Attack],
        MonsterDeathBlowCard => [IntentCardTheft.TheftKind.Attack, IntentCardTheft.TheftKind.DeathBlow],
        MonsterDefendCard => [IntentCardTheft.TheftKind.Block],
        MonsterBuffCard => [IntentCardTheft.TheftKind.Buff],
        MonsterDebuffCard => [IntentCardTheft.TheftKind.Debuff],
        MonsterCardDebuffCard => [IntentCardTheft.TheftKind.Debuff],
        MonsterHealCard => [IntentCardTheft.TheftKind.Heal],
        MonsterStatusCard => [IntentCardTheft.TheftKind.Status],
        MonsterSummonCard => [IntentCardTheft.TheftKind.Summon],
        // 逃跑意图映射到未知兜底卡——按 SourceType 识别，抢走后怪物整招空过
        MonsterUnknownCard { SourceType: IntentType.Escape } => [IntentCardTheft.TheftKind.Escape],
        _ => [],
    };
}
