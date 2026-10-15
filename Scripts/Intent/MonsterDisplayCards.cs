using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Interop.AutoRegistration;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Intent;

// 怪物卡：怪物意图卡牌化后显示的卡面，统一收录在 MonsterCardPool（图鉴可见）。
// 这些卡不可被玩家获得，只在怪物头上展示、描述怪物即将做出的动作——
// 效果本身仍由怪物招式（move）执行，卡面与效果严格对应（攻击份数 = 攻击次数）。
//
// 双面卡：被「强夺」抢进手牌的副本可由玩家打出（OnPlay）——
// 怪物用它是对玩家做动作，玩家用它则反向生效。打出后消耗（ExhaustOnNextPlay）。
// 非攻击意图不携带数值（原版意图也不显示），玩家版效果按 1 费标准定值：
// 格挡 5 / 力量 1 / 虚弱 1 / 回复 5。
public interface IMonsterDisplayCard
{
}

// 公共基类：动态文本由 mapper 在 CreateCard 返回的克隆体上注入（CreateCard 是
// "取注册单例 → ToMutable 克隆"，构造函数只在注册时执行一次，静态传参无效），
// 经 MonsterDisplayCardDescriptionPatch 渲染进描述。注入为空时保留卡面静态描述（图鉴展示用）。
//
// 注意：必须显式声明本池的注册特性。RitsuLib 的注册槽按「特性类型」互斥：
// MonsterDisplayCard 自己声明的 [RegisterCard] 会顶掉从 SelectSpecialBaseCard 继承来的
// [RegisterCard(typeof(SelectSpecialCardPool))]，否则怪物卡会被同时注册进特殊卡池（图鉴串池）。
[RegisterCard(typeof(MonsterCardPool), Inherit = true)]
public abstract class MonsterDisplayCard : SelectSpecialBaseCard, IMonsterDisplayCard
{
    // 注入的动作文本（取自游戏意图的现成本地化文案，含数值变量）
    public string IntentText { get; set; } = "";

    // 占位废牌标记（塞状态/诅咒卡塞进怪物意图槽的废牌）：
    // 无任何效果，只占用怪物的一个行动位；不可被「强夺」抢走
    public bool IsPlaceholder { get; set; }

    // 废牌持续性：打出后是否保留。
    //   状态牌 = 一次性（打出后移除）；诅咒牌 = 保留（打出后回到牌堆，隔圈再来）。
    // 做成属性而不是写死类型，方便以后按卡细化不同效果。
    public bool JunkPersists { get; set; }

    // 「知识的诅咒」这类废牌：怪物打出时自己掉血，而且**每打一次越疼**。
    // 当前伤害 = 基础值 + 每次递增 × 已经打过的次数（用同一个卡实例累计，所以会一路涨）。
    public int JunkBaseDamage { get; set; }
    public int JunkDamageStep { get; set; }
    public int JunkPlayCount { get; set; }
    public int JunkSelfDamage => JunkBaseDamage + JunkDamageStep * JunkPlayCount;

    // 动态文本来源：非空时每次取描述都重算（用于伤害随力量/虚弱实时变化）。
    // 为空时退回 IntentText / 卡面静态描述。
    public Func<string>? LiveText { get; set; }

    protected MonsterDisplayCard(CardType type, CardRarity rarity, TargetType target)
        : base(0, type, rarity, target, shouldShowInCardLibrary: true)
    {
    }

    // 无专属卡图：留空，引擎自动用 RitsuLib 内嵌占位图
    public override CardAssetProfile AssetProfile => CardAssetProfile.Empty;

    // 塞废牌共用（状态/诅咒两卡）：把一张废牌放进目标怪物的废牌池。
    // 废牌不直接顶替动作——它进入抽牌池，随怪物换手时参与抽牌；
    // 抽中才顶替本回合的一个动作（IntentCardTheft.AddBlocked），未抽中的下回合继续参与。
    private protected static void SealPlaceholder(Creature? target, IntentCardTheft.JunkKind kind)
    {
        if (target == null) return;
        // 路线 A：废牌直接进该怪抽牌堆（牌堆里的一张普通牌），下次被抽到时顶掉一个出牌机会
        if (MonsterDeck.IsBuilt(target))
        {
            MonsterDeck.AddJunk(target, kind);
            return;
        }
        IntentCardTheft.AddJunk(target, kind);
        // 立刻重抽：废牌（若被抽中）马上显示在怪物头顶
        IntentCardHook.FindOverlay(target)?.RequestRebuild();
    }

    // 废牌卡面：由 overlay 在抽中后显示。无任何效果，只占怪物的一个行动位。
    internal static CardModel CreateJunkCard(Player owner, IntentCardTheft.JunkKind kind)
    {
        var combat = owner.Creature.CombatState!;
        MonsterDisplayCard junk = kind == IntentCardTheft.JunkKind.Status
            ? combat.CreateCard<MonsterStatusCard>(owner)
            : combat.CreateCard<MonsterCardDebuffCard>(owner);
        junk.IsPlaceholder = true;
        junk.JunkPersists = kind != IntentCardTheft.JunkKind.Status;   // 状态牌一次性，其余保留
        if (kind == IntentCardTheft.JunkKind.KnowledgeCurse)
        {
            // 「知识的诅咒」：抽到就掉血，而且每抽到一次伤害递增（7、11、15…）
            junk.JunkBaseDamage = KnowledgeCurseBaseDamage;
            junk.JunkDamageStep = KnowledgeCurseDamageStep;
            junk.LiveText = () => MonsterText.Of(MonsterText.PlaceholderDamage,
                ("Damage", junk.JunkSelfDamage.ToString()));
        }
        else
        {
            junk.IntentText = new LocString("cards", "SELECT_SPECIAL_CARD_MOD_CARD_MONSTER_PLACEHOLDER.description")
                .GetFormattedText();
        }
        return junk;
    }

    // 「知识的诅咒」的伤害：第一次 7 点，之后每抽到一次 +4（7 / 11 / 15 / …）
    internal const int KnowledgeCurseBaseDamage = 7;
    internal const int KnowledgeCurseDamageStep = 4;
}

// 攻击：一张卡表示一次攻击（"造成X点伤害[N次]"）。伤害由 mapper 注入——
// 意图数值经 GetSingleDamage 取得，已含力量修正，与怪物意图图标显示一致。
// IntentDamage 默认 6：图鉴里显示示例数值，避免"造成0点伤害"。
public class MonsterAttackCard : MonsterDisplayCard
{
    public int IntentDamage { get; set; } = 6;

    // 段数：>1 时描述切换为多段模板（"造成x点伤害x次"），由 mapper 设置
    public int Repeats { get; set; } = 1;

    public MonsterAttackCard() : base(CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    // 玩家打出：对目标造成卡面数值的伤害，多段就打多次
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        // Unpowered：卡面数值已经含意图修正，避免再叠一次玩家侧加成（否则与卡面文字不一致）
        for (int i = 0; i < Repeats; i++)
        {
            await CreatureCmd.Damage(choiceContext, cardPlay.Target, IntentDamage, ValueProp.Move | ValueProp.Unpowered,
                Owner.Creature, this, cardPlay);
        }
    }
}

// 防御：获得格挡（格挡数值原版意图对玩家也不可见，卡面忠实显示语义）
public class MonsterDefendCard : MonsterDisplayCard
{
    public MonsterDefendCard() : base(CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    // 玩家打出：获得 5 点格挡（1 费标准）
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, 5m, ValueProp.Move, cardPlay);
    }
}

// 强化：获得能力（力量等，具体层数原版意图不显示）
public class MonsterBuffCard : MonsterDisplayCard
{
    public MonsterBuffCard() : base(CardType.Power, CardRarity.Common, TargetType.Self)
    {
    }

    // 玩家打出：获得 1 点力量（1 费标准）
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<StrengthPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }
}

// 削弱：对玩家施加负面状态（虚弱/易伤等）
public class MonsterDebuffCard : MonsterDisplayCard
{
    public MonsterDebuffCard() : base(CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    // 玩家打出：对目标怪施加 1 层虚弱（1 费标准）
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await PowerCmd.Apply<WeakPower>(choiceContext, cardPlay.Target, 1, Owner.Creature, this);
    }
}

// 治疗：回复生命
public class MonsterHealCard : MonsterDisplayCard
{
    public MonsterHealCard() : base(CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    // 玩家打出：回复 5 点生命（1 费标准）
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.Heal(Owner.Creature, 5m);
    }
}

// 召唤：呼唤同伴。
// 双面卡：怪物用它=呼唤同伴入场；玩家打出（强夺副本）=召唤选中的敌人作为己方宠物——
// 复制所选敌人的同款怪物到玩家侧（站玩家身边、无意图、不被敌人攻击），
// 生命对齐所选敌人；之后每回合开始帮玩家打（PetAlly），
// 伤害=召唤时该敌人意图里的攻击伤害快照（无攻击意图则宠物 0 攻纯占位）。
public class MonsterSummonCard : MonsterDisplayCard
{
    public MonsterSummonCard() : base(CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        await MonsterPet.Summon(choiceContext, Owner, cardPlay.Target);
    }
}

// 塞状态牌：双面卡。怪物用它=把状态牌加入玩家牌堆；玩家打出（强夺副本）=
// 向敌人的意图卡槽塞入一张状态废牌——废牌无任何效果，只占用怪物的一个行动位。
public class MonsterStatusCard : MonsterDisplayCard
{
    public MonsterStatusCard() : base(CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        SealPlaceholder(cardPlay.Target, IntentCardTheft.JunkKind.Status);
        return Task.CompletedTask;
    }
}

// 塞诅咒牌：双面卡。怪物用它=把诅咒牌加入玩家牌堆；玩家打出（强夺副本）=
// 向敌人的意图卡槽塞入一张诅咒废牌——与塞状态牌同理，只占用怪物的一个行动位。
public class MonsterCardDebuffCard : MonsterDisplayCard
{
    public MonsterCardDebuffCard() : base(CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        SealPlaceholder(cardPlay.Target, IntentCardTheft.JunkKind.Curse);
        return Task.CompletedTask;
    }
}

// 处决：满足条件时直接击杀（死亡一击）。
// 双面卡：玩家打出（强夺副本）=对目标造成卡面数值伤害（镜像处决怪的攻击部分），
// 打出后消耗（对应"自爆"的一次性语义，ExhaustOnNextPlay 由强夺统一设置）。
// IntentDamage 默认 10：图鉴里显示示例数值。
public class MonsterDeathBlowCard : MonsterDisplayCard
{
    public int IntentDamage { get; set; } = 10;

    public MonsterDeathBlowCard() : base(CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        // Unpowered：按卡面数值原样结算，避免叠加玩家侧加成
        await CreatureCmd.Damage(choiceContext, cardPlay.Target, IntentDamage, ValueProp.Move | ValueProp.Unpowered,
            Owner.Creature, this, cardPlay);
    }
}

// 未知：逃跑/击晕/隐藏/其他无法归类的意图兜底。
// SourceType 记录原始意图类型：「强夺」据此区分——逃跑（Escape）被抢后怪物整招
// 空过（Classify 记账 + move 入口跳过），其余未知类本就没有效果动作，抢走只是演出。
// 双面卡：玩家打出逃跑副本=自身逃离战斗（烟雾弹）：玩家遁走、战斗立即按通过结算、无任何奖励。
public class MonsterUnknownCard : MonsterDisplayCard
{
    public IntentType SourceType { get; set; } = IntentType.Unknown;

    public MonsterUnknownCard() : base(CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (SourceType != IntentType.Escape) return;
        await EscapeState.FleeCombat(Owner);
    }
}

// 招式卡（路线 A）：1 个 MoveState = 1 张卡，招式内的全部意图按原版顺序依次结算，**不拆分**。
// 这样原版招式的原子性得以保留（如"攻击8 + 强化"仍是一手完整的动作）。
// 卡池 = 该怪所有招式各一张；手牌 = 抽到的招式，怪物行动时逐张打出。
public class MonsterMoveCard : MonsterDisplayCard
{
    // 该招式的全部意图（直接持有原版意图对象；数值在结算时实时计算）
    public IReadOnlyList<AbstractIntent> Intents { get; set; } = Array.Empty<AbstractIntent>();

    // 该招式对应的原版 MoveState：怪物出牌时直接调用它的原生 PerformMove，
    // 于是召唤目标 / 塞牌种类 / 逃跑演出等「硬编码在招式 lambda 里、意图对象读不到」的
    // 行为全部原样保留，不需要任何按怪物手写的映射表。
    public MoveState? SourceMove { get; set; }

    // 卡名：取自怪物招式的现成本地化名（如「重踏」）；空则退回卡面默认名「招式」。
    // 逐张不同，避免所有招式卡都叫「招式」。
    public string CardTitle { get; set; } = "";

    // 卡图路径：取该招式主意图的图标（游戏自带素材，不新增资源）；空则无卡图。
    public string ArtPath { get; set; } = "";

    // 该招式所属怪物的种类 id（如 "WATERFALL_GIANT"）：用作「观测记录」的键，
    // 使同种怪跨战斗共用同一份冻结数值（换阶段重建 / 换战斗都不会丢）。
    public string MonsterKind { get; set; } = "";

    // 该招式所属怪物本体：怪物自己那张卡的"实时怪物侧数值"要用它计算（副本不需要，留空）。
    public Creature? MonsterSource { get; set; }

    public override string Title => string.IsNullOrWhiteSpace(CardTitle) ? base.Title : CardTitle;

    public override string PortraitPath => string.IsNullOrWhiteSpace(ArtPath) ? base.PortraitPath : ArtPath;

    public override IEnumerable<string> AllPortraitPaths
        => string.IsNullOrWhiteSpace(ArtPath) ? base.AllPortraitPaths : new[] { ArtPath };

    // 目标按招式内容决定（覆写虚属性）：
    //   含 攻击/处决、削弱、塞状态/塞诅咒、召唤 → 需要选一个敌人；
    //   纯自我（强化/防御/治疗）→ 不需要目标，直接对自己生效。
    public override TargetType TargetType => NeedsEnemyTarget(Intents) ? TargetType.AnyEnemy : TargetType.Self;

    // 卡牌类型同样按招式内容决定（覆写虚属性）。
    // 构造参数给的是 Skill，只能当"没有意图时"的兜底；不覆写的话，抢过来的攻击招式
    // 会一直显示成技能卡（卡框/类型字样不对），而且打出去时不吃"攻击牌"相关的加成与判定
    //（力量、遗物、时机计数里按 Attack 分类的那些）。
    public override CardType Type => TypeOf(Intents);

    // 含攻击/处决 → 攻击；纯自我增益 → 能力；其余（防御/治疗/召唤/塞牌/逃跑/未知）→ 技能。
    private static CardType TypeOf(IReadOnlyList<AbstractIntent>? intents)
    {
        bool hasAttack = false, hasBuff = false, hasOther = false;
        foreach (var intent in intents ?? Array.Empty<AbstractIntent>())
        {
            switch (intent.IntentType)
            {
                case IntentType.Attack:
                case IntentType.DeathBlow:
                    hasAttack = true;
                    break;
                case IntentType.Buff:
                    hasBuff = true;
                    break;
                default:
                    hasOther = true;
                    break;
            }
        }
        if (hasAttack) return CardType.Attack;
        if (hasBuff && !hasOther) return CardType.Power;
        return CardType.Skill;
    }

    private static bool NeedsEnemyTarget(IReadOnlyList<AbstractIntent> intents)
    {
        foreach (var intent in intents)
        {
            switch (intent.IntentType)
            {
                case IntentType.Attack:
                case IntentType.DeathBlow:
                case IntentType.Debuff:
                case IntentType.DebuffStrong:
                case IntentType.StatusCard:
                case IntentType.CardDebuff:
                case IntentType.Summon:
                    return true;
            }
        }
        return false;
    }

    private string _effectSnapshot = "";
    private MonsterMoveEffects? _frozenEffects;
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public string EffectSnapshot
    {
        get => _effectSnapshot;
        set
        {
            _frozenEffects = string.IsNullOrEmpty(value) ? null
                : (System.Text.Json.JsonSerializer.Deserialize<MonsterEffectSnapshot>(value)
                    ?? throw new System.Text.Json.JsonException("Missing effect snapshot.")).ToEffects();
            _effectSnapshot = value;
        }
    }
    internal MonsterMoveEffects? FrozenEffects => _frozenEffects;

    public MonsterMoveCard() : base(CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    // 费用 = 意图数（每个意图 1 费）：复合招式比单意图招式贵，卡面显示真实费用
    protected override int CanonicalEnergyCost => Math.Max(1, Intents.Count);

    // 赋值招式内容并同步卡面费用。
    // CanonicalEnergyCost 只在 CardEnergyCost 构造时读取一次（Canonical 是只读属性，
    // 构造后冻结），而 CreateCard 深克隆发生在 Intents 赋值之前 → 费用冻结为 1。
    // 必须赋值后用 SetCustomBaseCost 显式改写，卡面才会显示真实费用。
    public void InitMove(MoveState? source, IReadOnlyList<AbstractIntent> intents)
    {
        SourceMove = source;
        Intents = intents;
        EnergyCost.SetCustomBaseCost(Math.Max(1, intents.Count));
    }

    // 玩家打出（强夺副本）：反向生效——攻击/削弱打敌人，格挡/强化/治疗给自己。
    // 与怪物侧 MonsterCardEffects 的来源方向相反。
    // 数值逐字段取值：观测记录（最准，怪物侧冻结值）→ 静态表（兜底）→ 占位值。
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        string moveId = SourceMove?.Id ?? "";
        // 数值取值：人工覆盖表（作者指定）→ 观测（最准）→ 静态表；卡面描述用同一份（见 IntentCardMapper）
        var fx = IntentCardMapper.EffectsFor(this) ?? new MonsterMoveEffects();
        int block = fx.Block;
        int heal = fx.Heal;
        bool selfPowersApplied = false;
        bool playerPowersApplied = false;
        foreach (var intent in Intents)
        {
            switch (intent.IntentType)
            {
                case IntentType.Attack:
                case IntentType.DeathBlow:
                    if (intent is AttackIntent atk && cardPlay.Target != null)
                    {
                        // 像玩家自己的攻击牌：传「原始伤害」，由引擎按玩家侧加成（力量/遗物）结算一次，
                        // 目标侧的易伤也照常叠加。卡面数字用同一套算法实时显示，两边一致。
                        int damage = RawDamage(atk);
                        for (int i = 0; i < Math.Max(1, atk.Repeats); i++)
                        {
                            await CreatureCmd.Damage(choiceContext, cardPlay.Target, damage, ValueProp.Move,
                                Owner.Creature, this, cardPlay);
                        }
                    }
                    break;
                case IntentType.Defend:
                    await CreatureCmd.GainBlock(Owner.Creature, fx.BlockKnown ? block : 5m, ValueProp.Move, cardPlay);
                    break;
                case IntentType.Buff:
                    if (selfPowersApplied) break;   // 记录的是"整个招式"的效果，多个强化意图只结算一次
                    selfPowersApplied = true;
                    if (fx.SelfPowers.Count > 0)
                        foreach (var power in fx.SelfPowers)
                            await ApplyRecordedPower(choiceContext, power, Owner.Creature);
                    else if (!fx.SelfKnown)
                        await PowerCmd.Apply<StrengthPower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
                    break;
                case IntentType.Debuff:
                case IntentType.DebuffStrong:
                    if (cardPlay.Target == null) break;
                    // 人工表指定了"塞废牌"（如「知识的诅咒」）→ 改为塞牌，不上能力
                    if (!string.IsNullOrEmpty(fx.SealJunk))
                    {
                        SealPlaceholder(cardPlay.Target, SealKindOf(fx.SealJunk));
                        break;
                    }
                    if (playerPowersApplied) break;
                    playerPowersApplied = true;
                    if (fx.PlayerPowers.Count > 0)
                        foreach (var power in fx.PlayerPowers)
                            await ApplyRecordedPower(choiceContext, power, cardPlay.Target);
                    else if (!fx.PlayerKnown)
                        await PowerCmd.Apply<WeakPower>(choiceContext, cardPlay.Target, 1, Owner.Creature, this);
                    break;
                case IntentType.Heal:
                    await CreatureCmd.Heal(Owner.Creature, fx.HealKnown ? heal : 5m);
                    break;
                // 塞状态牌 / 塞诅咒牌：反手把对应废牌塞回目标怪物的牌堆
                // （含此意图的招式卡被「强夺」抢来后，玩家打出时生效）
                case IntentType.StatusCard:
                    if (cardPlay.Target != null)
                        SealPlaceholder(cardPlay.Target, IntentCardTheft.JunkKind.Status);
                    break;
                case IntentType.CardDebuff:
                    if (cardPlay.Target != null)
                        SealPlaceholder(cardPlay.Target, IntentCardTheft.JunkKind.Curse);
                    break;
                // 召唤：优先召出「这一招原本会召唤的怪」（观测到的，种类 + 生命照搬）；
                // 没有观测到时退回旧行为：把所选敌人复制成玩家方宠物。
                case IntentType.Summon:
                    if (fx.SummonedMonsters.Count > 0)
                        foreach (var (kind, hp) in fx.SummonedMonsters)
                            await MonsterPet.SummonKind(choiceContext, Owner, kind, hp);
                    else if (!fx.SummonsKnown && cardPlay.Target != null)
                        await MonsterPet.Summon(choiceContext, Owner, cardPlay.Target);
                    break;
                // 逃跑：玩家自己遁走（烟雾弹），战斗按通过结算、无奖励
                case IntentType.Escape:
                    await EscapeState.FleeCombat(Owner);
                    break;
                // 未知：无效果
            }
        }
    }

    // 原始伤害（不含任何修正）：交给引擎按玩家侧结算，避免与卡面双重叠加
    private static int RawDamage(AttackIntent atk)
    {
        try
        {
            return Math.Max(0, (int)(atk.DamageCalc?.Invoke() ?? 0m));
        }
        catch
        {
            return 0;
        }
    }

    // 人工表里 "塞废牌" 的取值 → 废牌种类
    private static IntentCardTheft.JunkKind SealKindOf(string name) => name switch
    {
        "curse" => IntentCardTheft.JunkKind.Curse,
        "knowledge" => IntentCardTheft.JunkKind.KnowledgeCurse,
        _ => IntentCardTheft.JunkKind.Status,
    };

    // 把记录到的能力【原样】施加到目标身上：种类照搬（不再固定成力量/虚弱），层数照搬
    private async Task ApplyRecordedPower(PlayerChoiceContext choiceContext, PowerEffect effect, Creature target)
    {
        if (effect.Amount <= 0 || string.IsNullOrEmpty(effect.Power)) return;
        var template = MonsterPowerLookup.FindCanonical(effect.Power);
        if (template == null)
        {
            GD.PushWarning($"[SelectSpecialCardMod] 找不到能力模板 {effect.Power}，跳过");
            return;
        }
        await PowerCmd.Apply(choiceContext, template.ToMutable(), target, effect.Amount, Owner.Creature, this);
    }
}
