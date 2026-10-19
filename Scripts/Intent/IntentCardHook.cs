using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2RitsuLib;

namespace SelectSpecialCardMod.Scripts.Intent;

// 意图卡牌悬浮层生命周期：节点就绪时挂载，战斗开始和玩家回合开始时再补查。
// 怪物意图在开场 RollMove 后就出现了，若只订阅 PlayerTurnStartedEvent，
// 进战斗到首个玩家回合之间会有一段什么都不显示的空窗。
public static class IntentCardHook
{
    public static Player? LocalPlayer { get; private set; }

    public static void Init()
    {
        CombatManager.Instance.CombatBegan += OnCombatBegan;
        CombatManager.Instance.CombatEnded += _ => { LocalPlayer = null; MonsterDeck.ClearAll(); IntentCardTheft.Reset(); };
        RitsuLibFramework.SubscribeLifecycle<PlayerTurnStartedEvent>(evt =>
        {
            if (!MegaCrit.Sts2.Core.Context.LocalContext.IsMe(evt.Player)) return;
            LocalPlayer = evt.Player;
            EnsureAttached();
        });
    }

    private static void OnCombatBegan(CombatState state)
    {
        // 多人：必须取"本机玩家"而非列表第一个（FirstOrDefault 是房主），
        // 否则非房主客户端的伤害数值计算/卡 owner 归属全部指向别人
        LocalPlayer = MegaCrit.Sts2.Core.Context.LocalContext.GetMe(state.Players)
;
        // 路线 A：新战斗开始，清空上一场残留的怪物牌堆
        MonsterDeck.ClearAll();
        IntentCardTheft.Reset();
        _nextAttachCheckMsec = 0;
        EnsureAttached();
    }

    // 定期补挂（供 overlay 轮询调用）：战斗中途召唤的怪节点晚于事件入树，
    // 只靠事件挂载会漏。任一 overlay 每 2 秒触发一次全树扫描即可覆盖全部怪物。
    private static ulong _nextAttachCheckMsec;

    public static void TryAttachThrottled()
    {
        ulong now = Godot.Time.GetTicksMsec();
        if (now < _nextAttachCheckMsec) return;
        _nextAttachCheckMsec = now + 2000;
        EnsureAttached();
    }

    private static void EnsureAttached()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.CurrentScene == null) return;
        AttachRecursive(tree.CurrentScene);
    }

    private static void AttachRecursive(Node node)
    {
        if (node is NCreature creature) AttachCreature(creature);

        foreach (Node child in node.GetChildren())
        {
            AttachRecursive(child);
        }
    }

    internal static void AttachCreature(NCreature creature)
    {
        if (!GodotObject.IsInstanceValid(creature) || creature.IsQueuedForDeletion()
            || !creature.IsInsideTree() || !creature.IsNodeReady() || creature.HasNode("IntentCardOverlay")) return;
        creature.AddChild(new IntentCardOverlayNode { Name = "IntentCardOverlay" });
    }

    // 按怪物实体找它头顶的意图卡悬浮层（「强夺」用）：
    // overlay 挂在 NCreature（场景节点）下而非 Creature（实体）上，
    // 必须全树遍历做 Entity 引用比对——与 AttachRecursive 同一套寻址方式。
    public static IntentCardOverlayNode? FindOverlay(Creature creature)
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.CurrentScene == null) return null;
        return FindOverlayRecursive(tree.CurrentScene, creature);
    }

    private static IntentCardOverlayNode? FindOverlayRecursive(Node node, Creature creature)
    {
        if (node is NCreature n && ReferenceEquals(n.Entity, creature))
        {
            return n.GetNodeOrNull<IntentCardOverlayNode>("IntentCardOverlay");
        }
        foreach (Node child in node.GetChildren())
        {
            var found = FindOverlayRecursive(child, creature);
            if (found != null) return found;
        }
        return null;
    }
}

// 复活、召唤均会创建新NCreature；不依赖旧怪物仍然存活或下一个玩家回合才挂载。
[HarmonyPatch(typeof(NCreature), nameof(NCreature._Ready))]
public static class IntentCreatureReadyPatch
{
    [HarmonyPostfix]
    static void After(NCreature __instance)
    {
        // 等原版节点完成入树，避免在父节点仍设置子节点时修改子树。
        Callable.From(() => IntentCardHook.AttachCreature(__instance)).CallDeferred();
    }
}
