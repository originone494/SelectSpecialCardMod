using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Intent;

namespace SelectSpecialCardMod.Scripts.Tool;

// 非攻击意图效果（强化/虚弱/格挡/治疗）的飞卡同步：怪物执行这些效果时，
// 让它头顶的下一张意图卡飞出——与攻击拆分（MonsterAttackSplitPatch）一起，
// 实现"怪物每做一个动作，飞一张对应卡"的逐张对应。
//
// 为什么在效果命令入口同步：move 的效果是 move 自定义 lambda 顺序执行的，
// 没有按意图的分发点；但每个效果最终都落到 PowerCmd.Apply / CreatureCmd.GainBlock /
// CreatureCmd.Heal 这些命令入口，以"施加者/获得者是否为怪物"判定来源。
//
// 只做即时触发，不插入等待——效果自身的演出动画提供了节奏间隔。
//
// 「强夺」联动：怪物意图执行期内，被抢走的非攻击效果在这里消费——
// 对应命令被跳过（return false），怪物真的不做那个动作，也不再飞卡（卡已易主）。
// 计数按"怪物意图执行期 + 该怪物"双重限定，玩家的同类命令不受影响。
//
// ⚠ 跳过 async 原方法时必须设置 __result：Harmony 对 return false 且未赋值
// 的 __result 留 default（null Task），下游 patch（如 BaseLib 包装返回 Task）
// 一 await 就是 NullReferenceException，怪物回合循环当场死亡。
[HarmonyPatch]
public static class IntentEffectFlyPatch
{
    // 怪物施加能力（给自己强化、给玩家上虚弱等）：
    //   占位（塞状态/诅咒卡）→ 该动作被废牌顶替，废牌飞出；
    //   强夺 → 对应动作跳过，不飞卡（卡已易主）；
    //   否则飞下一张卡。
    // 强化=给自己上正面能力，削弱=给玩家上能力——按目标区分计数。
    // 占位/强夺计数只在怪物意图执行期内消费（门卫），被动触发不误消费。
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PowerCmd), nameof(PowerCmd.Apply),
        new[] { typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature), typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool) })]
    static bool PowerApply(Creature? applier, Creature target, PowerModel power, decimal amount, ref Task __result)
    {
        if (applier is not { IsMonster: true }) return true;
        if (IntentCardFlySync.InPerformScope && IntentCardTheft.ConsumeBlocked(applier))
        {
            IntentCardFlySync.NotifyHit(applier);
            __result = Task.CompletedTask;
            return false;
        }
        bool consumed = ReferenceEquals(target, applier)
            ? IntentCardTheft.ConsumeBuff(applier)
            : IntentCardTheft.ConsumeDebuff(applier);
        if (consumed)
        {
            __result = Task.CompletedTask;
            return false;
        }
        // 观测：这一招真的发出了这条能力 —— 直接读命令参数记下来（不受反击/触发等影响）
        MonsterEffectRecord.NotePower(power.GetType().Name, amount, target);
        IntentCardFlySync.NotifyHit(applier);
        return true;
    }

    // 怪物获得格挡：占位 → 废牌顶替并飞出；强夺 → 跳过；否则飞下一张卡（返回实际格挡量，被占时 0）
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainBlock),
        new[] { typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(CardPlay), typeof(bool) })]
    static bool GainBlock(Creature creature, decimal amount, ref Task<decimal> __result)
    {
        if (!creature.IsMonster) return true;
        if (IntentCardFlySync.InPerformScope && IntentCardTheft.ConsumeBlocked(creature))
        {
            IntentCardFlySync.NotifyHit(creature);
            __result = Task.FromResult(0m);
            return false;
        }
        if (IntentCardTheft.ConsumeBlock(creature))
        {
            __result = Task.FromResult(0m);
            return false;
        }
        // 观测：这一招真的获得了格挡 —— 记的是命令声明的值（不会被过程中受到的伤害抵消）
        MonsterEffectRecord.NoteBlock(amount, creature);
        IntentCardFlySync.NotifyHit(creature);
        return true;
    }

    // 怪物回复生命：占位 → 废牌顶替并飞出；强夺 → 跳过；否则飞下一张卡
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Heal),
        new[] { typeof(Creature), typeof(decimal), typeof(bool) })]
    static bool Heal(Creature creature, decimal amount, ref Task __result)
    {
        if (!creature.IsMonster) return true;
        if (IntentCardFlySync.InPerformScope && IntentCardTheft.ConsumeBlocked(creature))
        {
            IntentCardFlySync.NotifyHit(creature);
            __result = Task.CompletedTask;
            return false;
        }
        if (IntentCardTheft.ConsumeHeal(creature))
        {
            __result = Task.CompletedTask;
            return false;
        }
        // 观测：这一招真的回了血 —— 记的是命令声明的值
        MonsterEffectRecord.NoteHeal(amount, creature);
        IntentCardFlySync.NotifyHit(creature);
        return true;
    }

    // 怪物召唤（CreatureCmd.Add 是三条召唤重载的汇聚点）→ 被抢的召唤意图在这里消费：
    // 新怪不入场，怪物真的不再召唤。玩家召唤（Side.Player）与战斗外布置不受影响——
    // 只有"怪物意图执行期间 + 怪物本体 + 敌方侧"才拦截。
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Add), new[] { typeof(Creature) })]
    static bool AddCreature(Creature creature, ref Task __result)
    {
        if (!creature.IsMonster || creature.Side != CombatSide.Enemy) return true;
        var actor = IntentCardTheft.CurrentActor;
        if (actor == null) return true;
        // 占位：召唤动作被废牌顶替（新怪不入场，废牌飞出）；强夺：召唤被抢
        if (IntentCardFlySync.InPerformScope && IntentCardTheft.ConsumeBlocked(actor))
        {
            IntentCardFlySync.NotifyHit(actor);
            __result = Task.CompletedTask;
            return false;
        }
        if (!IntentCardTheft.ConsumeSummon(actor)) return true;
        __result = Task.CompletedTask;
        return false;
    }

    // 塞状态牌/诅咒牌拦截。原本打在 AddToCombatAndPreview（泛型 async 方法）上，
    // 游戏升级后其编译器生成的泛型状态机局部变量让 MonoMod 导入崩溃
    // （"Specified method is not supported"），整个补丁类应用失败。
    // 改打非泛型的入堆底层 AddGeneratedCardToCombat：每张状态/诅咒卡入堆前检查。
    // 语义对齐"整招不发生"：第一条记录（占位顶替/强夺）消费后，本次行动作用域内
    // 后续入堆全部跳过（塞 3 张 = 3 张全不塞）。返回 success=false 的结果，
    // 外层预览只预览 success==true 的条目（PreviewCardPileAdd），跳过安全。
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.AddGeneratedCardToCombat),
        new[] { typeof(CardModel), typeof(PileType), typeof(Player), typeof(CardPilePosition) })]
    static bool AddGeneratedCard(CardModel card, ref Task<CardPileAddResult> __result)
    {
        var actor = IntentCardTheft.CurrentActor;
        if (actor == null) return true;
        // 只看塞进玩家牌堆的卡（怪物塞牌：卡 owner = 目标玩家，非怪物）
        if (card.Owner?.Creature is not { IsMonster: false }) return true;
        if (SkipSealAdds)
        {
            __result = Task.FromResult(default(CardPileAddResult));
            return false;
        }
        // 占位：塞牌动作被废牌顶替（一张不塞，废牌飞出）；强夺：塞牌被抢
        if (IntentCardFlySync.InPerformScope && IntentCardTheft.ConsumeBlocked(actor))
        {
            IntentCardFlySync.NotifyHit(actor);
            SkipSealAdds = true;
            __result = Task.FromResult(default(CardPileAddResult));
            return false;
        }
        if (!IntentCardTheft.ConsumeStatus(actor) && !IntentCardTheft.ConsumeDebuff(actor)) return true;
        SkipSealAdds = true;
        __result = Task.FromResult(default(CardPileAddResult));
        return false;
    }

    // 塞牌拦截标记：同一次行动作用域内，塞牌被抢/顶替后，后续入堆一律跳过。
    // 由 MonsterActorScopePatch 在每次行动开始时复位。
    internal static bool SkipSealAdds;

    // 处决的死亡结算（处决怪如 GasBomb/WaterfallGiant：攻击玩家后自爆 Kill 自己；
    // 若有处决玩家的场景则目标为玩家）：DeathBlow 卡被抢后攻击部分由拆分拦截取消，
    // 这里消费处决计数跳过死亡——整招不发生，怪物空过。
    // 怪物互杀（清理自己的召唤物等）不受影响。
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Kill), new[] { typeof(Creature), typeof(bool) })]
    static bool Kill(Creature creature, ref Task __result)
    {
        var actor = IntentCardTheft.CurrentActor;
        if (actor == null || (creature != actor && creature.IsMonster)) return true;
        // 占位：处决自爆被废牌顶替（怪物不死，废牌飞出）；强夺：处决被抢
        if (IntentCardFlySync.InPerformScope && IntentCardTheft.ConsumeBlocked(actor))
        {
            IntentCardFlySync.NotifyHit(actor);
            __result = Task.CompletedTask;
            return false;
        }
        if (!IntentCardTheft.ConsumeDeathBlow(actor)) return true;
        __result = Task.CompletedTask;
        return false;
    }

    // 逃跑（ThievingHopper/FatGremlin 等）：被抢的逃跑意图在这里消费——怪物不再离场。
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Escape), new[] { typeof(Creature), typeof(bool) })]
    static bool Escape(Creature creature, ref Task __result)
    {
        var actor = IntentCardTheft.CurrentActor;
        if (actor == null || creature != actor) return true;
        if (!IntentCardTheft.ConsumeEscape(actor)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

// 记录"当前行动的怪物"：召唤拦截（CreatureCmd.Add）需要知道召唤者是谁——
// Add 的参数只有被召唤的新怪。MoveState 拿不到怪物引用，改从
// MonsterModel.PerformMove 的实例上取（__instance.Creature）。
// Postfix 包装返回的 Task 关闭——async 方法 Postfix 会在首个 await 前就返回，
// 与 MonsterIntentScopePatch 同一套时序处理。
[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.PerformMove))]
public static class MonsterActorScopePatch
{
    [HarmonyPrefix]
    static void Enter(MonsterModel __instance)
    {
        IntentEffectFlyPatch.SkipSealAdds = false;   // 塞牌拦截标记按行动复位
        IntentCardTheft.BeginActorScope(__instance.Creature);
    }

    [HarmonyPostfix]
    static void Wrap(ref Task __result)
    {
        __result = Wrapped(__result);

        static async Task Wrapped(Task inner)
        {
            try
            {
                await inner;
            }
            finally
            {
                IntentCardTheft.EndActorScope();
            }
        }
    }
}
