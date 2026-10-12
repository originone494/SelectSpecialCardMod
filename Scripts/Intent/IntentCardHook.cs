using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2RitsuLib;

namespace SelectSpecialCardMod.Scripts.Intent;

// 意图卡牌悬浮层生命周期：战斗一开始（CombatBegan）就挂载，玩家回合开始时再刷新一次。
// 怪物意图在开场 RollMove 后就出现了，若只订阅 PlayerTurnStartedEvent，
// 进战斗到首个玩家回合之间会有一段什么都不显示的空窗。
public static class IntentCardHook
{
    public static Player? LocalPlayer { get; private set; }

    public static void Init()
    {
        CombatManager.Instance.CombatBegan += OnCombatBegan;
        RitsuLibFramework.SubscribeLifecycle<PlayerTurnStartedEvent>(evt =>
        {
            LocalPlayer = evt.Player;
            EnsureAttached();
        });
    }

    private static void OnCombatBegan(CombatState state)
    {
        // 多人：必须取"本机玩家"而非列表第一个（FirstOrDefault 是房主），
        // 否则非房主客户端的伤害数值计算/卡 owner 归属全部指向别人
        LocalPlayer = MegaCrit.Sts2.Core.Context.LocalContext.GetMe(state.Players)
                      ?? state.Players.FirstOrDefault();
        // 路线 A：新战斗开始，清空上一场残留的怪物牌堆
        MonsterDeck.ClearAll();
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
        if (node is NCreature creature && !creature.HasNode("IntentCardOverlay"))
        {
            var overlay = new IntentCardOverlayNode { Name = "IntentCardOverlay" };
            creature.AddChild(overlay);
        }

        foreach (Node child in node.GetChildren())
        {
            AttachRecursive(child);
        }
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
