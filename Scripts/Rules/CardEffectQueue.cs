using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace SelectSpecialCardMod.Scripts.Rules;

// 一段卡牌效果内只登记衍生触发；整段完成后按发生顺序逐个结算。
// 使用异步上下文隔离窗口，不以本机玩家身份决定结算顺序。
public static class CardEffectQueue
{
    internal sealed record Pending(Func<Task> Body, int Depth, Action? Canceled, Frame Origin);
    internal sealed class Window
    {
        internal readonly Queue<Pending> Pending = new();
        internal bool Active = true;
        internal int Depth;
    }
    public sealed class Frame
    {
        internal Window Window { get; }
        internal Frame? Parent { get; }
        internal bool OwnsWindow { get; }
        internal bool Failed { get; set; }
        internal Frame(Window window, Frame? parent, bool owns) { Window = window; Parent = parent; OwnsWindow = owns; }
    }
    private static readonly AsyncLocal<Frame?> Current = new();
    public static bool IsResolving => Current.Value?.Window.Active == true;

    public static Frame Begin()
    {
        var parent = IsResolving ? Current.Value : null;
        var frame = new Frame(parent?.Window ?? new Window(), parent, parent == null);
        Current.Value = frame;
        return frame;
    }
    public static void Restore(Frame frame)
    {
        if (ReferenceEquals(Current.Value, frame)) Current.Value = frame.Parent;
    }
    public static void Abort(Frame frame, bool failed = false)
    {
        if (failed) frame.Failed = true;
        if (frame.OwnsWindow) { frame.Window.Active = false; CancelPending(frame.Window); }
        Restore(frame);
    }

    public static async Task Complete(Task body, Frame frame)
    {
        var previous = Current.Value;
        Current.Value = frame;
        try
        {
            await body;
            if (!frame.OwnsWindow) return;
            int count = 0;
            while (frame.Window.Pending.TryDequeue(out var pending))
            {
                if (FailedOrigin(pending.Origin)) { pending.Canceled?.Invoke(); continue; }
                if (++count > 512)
                {
                    Log.Warn("[SelectSpecialCardMod] 卡牌衍生效果超过512次，停止本次连锁。");
                    pending.Canceled?.Invoke();
                    break;
                }
                frame.Window.Depth = pending.Depth;
                await pending.Body();
            }
        }
        catch { frame.Failed = true; throw; }
        finally
        {
            if (frame.OwnsWindow) { frame.Window.Active = false; CancelPending(frame.Window); }
            Current.Value = previous;
        }
    }

    public static async Task Run(Func<Task> body)
    {
        var frame = Begin();
        try { await Complete(body(), frame); }
        catch { frame.Failed = true; throw; }
        finally { Abort(frame); }
    }
    public static async Task<T> Run<T>(Func<Task<T>> body)
    {
        T result = default!;
        await Run(async () => { result = await body(); });
        return result;
    }

    private static void CancelPending(Window window)
    {
        while (window.Pending.TryDequeue(out var pending)) pending.Canceled?.Invoke();
    }
    private static bool FailedOrigin(Frame? frame)
    {
        for (; frame != null; frame = frame.Parent) if (frame.Failed) return true;
        return false;
    }

    public static Task Schedule(Func<Task> body, Action? onCanceled = null)
    {
        if (!IsResolving) return Run(body);
        var window = Current.Value!.Window;
        if (window.Depth >= 32)
        {
            Log.Warn("[SelectSpecialCardMod] 卡牌衍生效果超过32层，跳过本次触发。");
            onCanceled?.Invoke();
            return Task.CompletedTask;
        }
        window.Pending.Enqueue(new Pending(body, window.Depth + 1, onCanceled, Current.Value!));
        return Task.CompletedTask;
    }

    // 排队期间战斗、拥有者或卡牌状态改变时，不执行过期触发。
    public static Task Schedule(CardModel source, Func<Task> body, Func<bool>? eligible = null)
    {
        var owner = source.Owner;
        var combat = source.CombatState;
        var id = CombatManager.Instance.CurrentCombatId;
        return Schedule(async () =>
        {
            if (owner == null || combat == null || source.Owner != owner || source.CombatState != combat
                || owner.Creature.CombatState != combat || !owner.Creature.IsAlive || source.HasBeenRemovedFromState
                || CombatManager.Instance.CurrentCombatId != id || CombatManager.Instance.IsOverOrEnding
                || eligible?.Invoke() == false) return;
            await body();
        });
    }
}
