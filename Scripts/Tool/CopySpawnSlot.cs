using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「复制敌人」类效果的落点分配（代罪阿呆 / 增值共用）。
//
// 背景：敌人站位由遭遇战场景里的槽位 Marker 决定——NCombatRoom.AddCreature 里
//   `if (creature.SlotName != null) nCreature.GlobalPosition = encounterSlots.GetNode<Marker2D>(SlotName).GlobalPosition;`
// SlotName 为 null 时引擎什么都不做 → 复制品停在默认点，多只全叠在一起。
//
// 两道保障：
//   ① PickSlot（入场前）：优先占一个**空闲槽位**；没有空闲槽位时仍返回一个**合法槽位名**
//      ——Exoskeleton / Myte / Wriggler / PhantasmalGardener 这类怪物的行动分支是按槽位名判断的，
//      而 ConditionalBranchState 没有匹配分支时会直接 `throw new InvalidOperationException(...)`，
//      所以名字必须合法。
//   ② EnsureClearOf（入场后**必须**调用）：以本体为基准，八方向（上/下/左/右/四个斜角）× 逐档外扩
//      找落点。约束分两档：
//        硬约束：① 必须留在屏幕内（超出就夹回可视范围）② **绝不能覆盖本体**
//        软约束：尽量也不压住其他敌人（找不到就退让，因为复制品之间允许互相重叠）
//      两轮都找不到时，取"屏幕内离本体最远"的候选点兜底。
//
// 安全性：位置只在"战斗开始"和"该生物入场瞬间"各写一次（NCombatRoom 里只有那两个调用点），
// 入场之后我们自己设的坐标不会被任何重排覆盖。
public static class CopySpawnSlot
{
    // 与原版自动排布相同的间距（NCombatRoom.PositionEnemies 里用的 70f）
    private const float MinGap = 70f;

    // 离屏幕边缘再留一点余量
    private const float ScreenMargin = 20f;

    // 逐档外扩最多几档（8 方向 × MaxSteps 个候选点）
    private const int MaxSteps = 6;

    // 候选方向（Godot 里 y 向下为正，所以 -1 = 上）。
    private static readonly Vector2[] Directions =
    [
        new(1, 0), new(-1, 0), new(0, -1), new(0, 1),
        new(1, -1), new(-1, -1), new(1, 1), new(-1, 1),
    ];

    // 入场前调用：挑一个槽位名（空闲优先；没有空闲就给最后一个槽位名当"合法名字"）
    public static string? PickSlot(ICombatState combatState)
    {
        var encounter = combatState.Encounter;

        string freeSlot = encounter?.Slots
            .FirstOrDefault(s => combatState.Enemies.All(c => c.SlotName != s)) ?? string.Empty;
        if (!string.IsNullOrEmpty(freeSlot)) return freeSlot;

        string? lastSlot = encounter?.Slots.LastOrDefault();
        return string.IsNullOrEmpty(lastSlot) ? null : lastSlot;
    }

    // 入场后调用：把复制品放到屏幕内、且不覆盖本体（尽量也不压其他敌人）
    public static void EnsureClearOf(Creature copy, Creature? reference)
    {
        if (reference?.GetCreatureNode() is not { } refNode) return;
        if (copy.GetCreatureNode() is not { } copyNode) return;

        Vector2 refPos = refNode.GlobalPosition;
        Vector2 size = copyNode.Visuals.Bounds.Size;
        Rect2 screen = UsableScreen(copyNode);

        // 与本体不重叠所需的水平/垂直距离
        float needX = HalfWidth(refNode) + size.X * 0.5f + MinGap;
        float needY = (refNode.Visuals.Bounds.Size.Y + size.Y) * 0.5f + MinGap;

        var others = copy.CombatState?.Enemies.Where(c => c != copy && c.IsAlive).ToList()
                     ?? new List<Creature>();

        // 第一轮：屏幕内 + 不压本体 + 尽量不压别人
        if (TryFind(copyNode, screen, refPos, needX, needY, others, avoidOthers: true, out Vector2 spot))
        {
            copyNode.GlobalPosition = spot;
            return;
        }

        // 第二轮：放宽到"屏幕内 + 不压本体"（复制品之间允许重叠）
        if (TryFind(copyNode, screen, refPos, needX, needY, others, avoidOthers: false, out spot))
        {
            copyNode.GlobalPosition = spot;
            return;
        }

        // 兜底：取屏幕内离本体最远的候选点（至少保证不跑出屏幕）
        copyNode.GlobalPosition = FarthestSpot(refNode, copyNode, screen, refPos, needX, needY);
    }

    private static bool TryFind(
        NCreature copyNode,
        Rect2 screen,
        Vector2 refPos,
        float needX,
        float needY,
        List<Creature> others,
        bool avoidOthers,
        out Vector2 spot)
    {
        for (int step = 1; step <= MaxSteps; step++)
        {
            foreach (Vector2 dir in Directions)
            {
                var raw = new Vector2(
                    refPos.X + dir.X * step * needX,
                    refPos.Y + dir.Y * step * needY);

                Vector2 candidate = ClampToScreen(raw, screen, copyNode);

                // 硬约束：不许覆盖本体
                if (Covers(candidate, copyNode, refPos, needX, needY)) continue;

                // 软约束：尽量不压其他敌人（复制品之间允许重叠）
                if (avoidOthers && !IsClear(copyNode, candidate, others)) continue;

                spot = candidate;
                return true;
            }
        }

        spot = default;
        return false;
    }

    private static Vector2 FarthestSpot(
        NCreature refNode, NCreature copyNode, Rect2 screen, Vector2 refPos, float needX, float needY)
    {
        Vector2 best = ClampToScreen(new Vector2(refPos.X + needX, refPos.Y), screen, copyNode);
        float bestDistance = best.DistanceTo(refPos);
        bool bestCovers = Covers(best, copyNode, refPos, needX, needY);

        for (int step = 1; step <= MaxSteps; step++)
        {
            foreach (Vector2 dir in Directions)
            {
                var raw = new Vector2(
                    refPos.X + dir.X * step * needX,
                    refPos.Y + dir.Y * step * needY);

                Vector2 candidate = ClampToScreen(raw, screen, copyNode);
                bool covers = Covers(candidate, copyNode, refPos, needX, needY);
                float distance = candidate.DistanceTo(refPos);

                // 不覆盖本体的优先；都覆盖时取更远的
                if (bestCovers && !covers)
                {
                    best = candidate; bestDistance = distance; bestCovers = false;
                    continue;
                }
                if (bestCovers == covers && distance > bestDistance)
                {
                    best = candidate; bestDistance = distance;
                }
            }
        }
        return best;
    }

    // 把落点夹进可视范围（按复制品的贴图尺寸留边）
    private static Vector2 ClampToScreen(Vector2 point, Rect2 screen, NCreature copyNode)
    {
        Vector2 half = copyNode.Visuals.Bounds.Size * 0.5f;
        float minX = screen.Position.X + half.X;
        float maxX = screen.End.X - half.X;
        float minY = screen.Position.Y + half.Y;
        float maxY = screen.End.Y - half.Y;

        return new Vector2(
            maxX >= minX ? Mathf.Clamp(point.X, minX, maxX) : screen.Position.X,
            maxY >= minY ? Mathf.Clamp(point.Y, minY, maxY) : screen.Position.Y);
    }

    // 当前可视区域。
    // 注意坐标系：生物节点的 GlobalPosition 是"画布坐标"，而视口可见矩形是"视口坐标"，
    // 两者在正常情况下一致；为稳妥起见取两者的交集，若交集小得不像屏幕（说明坐标系被变换过），
    // 就退回用战斗房间自己的矩形（它与生物 GlobalPosition 同在画布坐标系里）。
    private static Rect2 UsableScreen(NCreature node)
    {
        Rect2 viewport = node.GetViewport()?.GetVisibleRect() ?? new Rect2(Vector2.Zero, new Vector2(1920f, 1080f));
        Rect2 room = NCombatRoom.Instance?.GetGlobalRect() ?? viewport;

        Rect2 intersection = room.Intersection(viewport);
        Rect2 screen = intersection.Size.X > 100f && intersection.Size.Y > 100f ? intersection : room;

        return screen.Grow(-ScreenMargin);
    }

    // 是否压住本体（两轴都挤在一起才算）
    private static bool Covers(Vector2 candidate, NCreature copyNode, Vector2 refPos, float needX, float needY)
        => Mathf.Abs(candidate.X - refPos.X) < needX && Mathf.Abs(candidate.Y - refPos.Y) < needY;

    private static bool IsClear(NCreature copyNode, Vector2 candidate, IEnumerable<Creature> others)
    {
        float copyHalfW = copyNode.Visuals.Bounds.Size.X * 0.5f;
        float copyHalfH = copyNode.Visuals.Bounds.Size.Y * 0.5f;

        foreach (var other in others)
        {
            if (other.GetCreatureNode() is not { } otherNode) continue;

            float needX = copyHalfW + otherNode.Visuals.Bounds.Size.X * 0.5f + MinGap;
            float needY = copyHalfH + otherNode.Visuals.Bounds.Size.Y * 0.5f + MinGap;

            bool separatedX = Mathf.Abs(candidate.X - otherNode.GlobalPosition.X) >= needX;
            bool separatedY = Mathf.Abs(candidate.Y - otherNode.GlobalPosition.Y) >= needY;
            if (!separatedX && !separatedY) return false;
        }
        return true;
    }

    private static float HalfWidth(NCreature node) => node.Visuals.Bounds.Size.X * 0.5f;
}
