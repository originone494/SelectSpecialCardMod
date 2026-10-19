using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SelectSpecialCardMod.Scripts.Tool;

// 仅调整强夺召唤物的显示位置与朝向，不改变目标随机数或原版宠物的行为。
public partial class PetAllyPresentation : Node
{
    private NCreature? _node;
    private Creature? _facingTarget;
    private ulong _nextCheck;
    private bool _facesRight;
    private readonly Dictionary<Node2D, float> _bodyScale = [];

    public static void EnsureAttached(NCreature node)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsNodeReady() || node.HasNode("PetAllyPresentation")) return;
        node.AddChild(new PetAllyPresentation { Name = "PetAllyPresentation" });
    }

    public override void _Ready()
    {
        _node = GetParent() as NCreature;
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() < _nextCheck) return;
        _nextCheck = Time.GetTicksMsec() + 200;
        Refresh();
    }

    private void Refresh()
    {
        if (_node?.Entity is not { IsAlive: true } pet || !PetAlly.IsRegistered(pet)) return;
        if (_facingTarget?.IsAlive != true)
            _facingTarget = pet.CombatState?.HittableEnemies
                .Where(c => c.GetCreatureNode() != null)
                .OrderBy(c => c.GetCreatureNode()!.GlobalPosition.DistanceSquaredTo(_node.GlobalPosition)).FirstOrDefault();
        if (_facingTarget != null) FaceTarget(_facingTarget);
        Arrange();
    }

    public void FaceTarget(Creature target)
    {
        if (_node == null || target.GetCreatureNode() is not { } targetNode) return;
        _facingTarget = target;
        bool right = targetNode.GlobalPosition.X >= _node.GlobalPosition.X;
        bool changed = _facesRight != right;
        _facesRight = right;
        // 只翻转身体，保留血条、命中框、文字和头顶卡牌的方向与正尺寸。
        FaceBody(_node.Visuals.Body, right);
        FaceBody(_node.Visuals.GetCurrentBody(), right);
        if (changed) Arrange();
    }

    private void FaceBody(Node2D body, bool right)
    {
        if (!_bodyScale.TryGetValue(body, out float originalX)) _bodyScale[body] = originalX = body.Scale.X;
        body.Scale = new Vector2(originalX * (right ? -1f : 1f), body.Scale.Y);
    }

    private void Arrange()
    {
        if (_node?.Entity.PetOwner?.Creature.GetCreatureNode() is not { } ownerNode
            || _node.Entity.CombatState is not { } combat) return;
        var viewport = _node.GetViewport().GetVisibleRect();
        // 避开顶部信息栏和下方手牌区。
        var screen = new Rect2(viewport.Position + new Vector2(20f, Math.Max(110f, viewport.Size.Y * 0.11f)),
            new Vector2(viewport.Size.X - 40f, viewport.Size.Y * 0.75f - Math.Max(110f, viewport.Size.Y * 0.11f)));
        var players = combat.Players.Select(p => p.Creature.GetCreatureNode()).OfType<NCreature>()
            .Select(n => BodyRect(n).Grow(24f)).ToArray();
        var others = combat.Creatures.Where(c => c != _node.Entity && !c.IsPlayer && c.IsAlive)
            .Select(c => c.GetCreatureNode()).OfType<NCreature>().Select(n => BodyRect(n).Grow(16f)).ToArray();
        // 极大体型导致无合法位置时逐步缩小外观；不改变生命、伤害或召唤规则。
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var body = BodyRect(_node);
            if (PetAllyPlacement.TryFind(body, BodyRect(ownerNode), screen, players, others, out var shift))
            {
                _node.GlobalPosition += shift;
                return;
            }
            if (_node.Visuals.Scale.X <= 0.2f) return;
            _node.SetScaleAndHue(_node.Visuals.Scale.X * 0.8f, 0f);
        }
    }

    internal static Rect2 BodyRect(NCreature node)
    {
        var rect = node.Visuals.Bounds.GetGlobalRect();
        // 身体翻转后，偏离原点的非对称体型也要按翻转后的真实范围避让。
        if (node.GetNodeOrNull<PetAllyPresentation>("PetAllyPresentation")?._facesRight == true)
            rect.Position = new Vector2(2f * node.Visuals.GetCurrentBody().GlobalPosition.X - rect.End.X, rect.Position.Y);
        return rect;
    }
}

internal static class PetAllyPlacement
{
    internal static bool TryFind(Rect2 body, Rect2 owner, Rect2 screen, IReadOnlyList<Rect2> players,
        IReadOnlyList<Rect2> others, out Vector2 shift)
    {
        shift = Vector2.Zero;
        if (body.Size.X <= 0f || body.Size.Y <= 0f || body.Size.X > screen.Size.X || body.Size.Y > screen.Size.Y) return false;
        if (screen.Encloses(body) && players.All(p => !p.Intersects(body)) && others.All(p => !p.Intersects(body))) return true;

        Vector2 preferred = new(owner.End.X + 32f, owner.End.Y - body.Size.Y);
        Vector2 best = default;
        float bestScore = float.PositiveInfinity;
        Consider(preferred);
        Consider(new Vector2(owner.Position.X - body.Size.X - 32f, preferred.Y));
        Consider(new Vector2(owner.GetCenter().X - body.Size.X * 0.5f, owner.Position.Y - body.Size.Y - 32f));
        Consider(new Vector2(owner.GetCenter().X - body.Size.X * 0.5f, owner.End.Y + 32f));
        float stepX = Math.Max(24f, Math.Min(80f, body.Size.X + 32f));
        float stepY = Math.Max(24f, Math.Min(80f, body.Size.Y + 32f));
        for (float y = screen.Position.Y; y <= screen.End.Y - body.Size.Y + stepY; y += stepY)
            for (float x = screen.Position.X; x <= screen.End.X - body.Size.X + stepX; x += stepX)
                Consider(new Vector2(x, y));
        if (float.IsPositiveInfinity(bestScore)) return false;
        shift = best - body.Position;
        return true;

        void Consider(Vector2 position)
        {
            position.X = Mathf.Clamp(position.X, screen.Position.X, screen.End.X - body.Size.X);
            position.Y = Mathf.Clamp(position.Y, screen.Position.Y, screen.End.Y - body.Size.Y);
            var candidate = new Rect2(position, body.Size);
            if (players.Any(p => p.Intersects(candidate))) return;
            float score = others.Count(p => p.Intersects(candidate)) * 100_000_000f + position.DistanceSquaredTo(preferred);
            if (score >= bestScore) return;
            bestScore = score;
            best = position;
        }
    }
}
