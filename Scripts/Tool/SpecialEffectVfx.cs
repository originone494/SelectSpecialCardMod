using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.TestSupport;

namespace SelectSpecialCardMod.Scripts.Tool;

// 只借用同一模型绘制演出卡面，不移动实际手牌，也不触发任何牌堆事件。
internal sealed class SpecialEffectVfx : IDisposable
{
    private readonly CardModel _card;
    private readonly NCombatRoom _room;
    private readonly NCard? _original;
    private readonly bool _originalVisible;
    private readonly bool _fromPlay;
    private readonly double _travelTime;
    private NCard? _visual;
    private Tween? _tween;

    private SpecialEffectVfx(CardModel card, NCombatRoom room, NCard? original, bool fromPlay)
    {
        _card = card;
        _room = room;
        _original = original;
        _originalVisible = original?.Visible == true;
        _fromPlay = fromPlay;
        _travelTime = SaveManager.Instance.PrefsSave.FastMode == FastModeType.Fast ? 0.18 : 0.35;
    }

    // 返回前已恢复原手牌并清理演出卡面；详细效果只能在此任务完成后开始。
    public static async Task<bool> Play(CardModel card)
    {
        using var presentation = TryCreate(card, false);
        if (presentation == null) return true;
        if (!await presentation.FlyToCenter()) return false;
        return await presentation.ReturnToHand();
    }

    // 打出中的驻场只使用视觉副本；真实卡继续留在 Play 并按原流程落堆。
    public static async Task<bool> PlayFromPlay(CardModel card)
    {
        using var presentation = TryCreate(card, true);
        if (presentation == null) return true;
        if (!await presentation.FlyToCenter()) return false;
        return await presentation.ReturnToHand();
    }

    // 入手通知先于手牌节点布局时，从本地手牌区域生成展示副本。
    public static async Task<bool> PlayFromHandAnchor(CardModel card)
    {
        if (card.Pile?.Type != PileType.Hand) return false;
        using var presentation = TryCreate(card, false, true);
        if (presentation == null) return true;
        if (!await presentation.FlyToCenter()) return false;
        return await presentation.ReturnToHand();
    }

    private static SpecialEffectVfx? TryCreate(CardModel card, bool fromPlay, bool allowHandAnchor = false)
    {
        if (TestMode.IsOn || NCombatRoom.Instance?.Ui == null) return null;
        SpecialEffectVfx? presentation = null;
        try
        {
            if (SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant) return null;
            var original = NCard.FindOnTable(card, fromPlay ? PileType.Play : PileType.Hand);
            if ((original == null || !original.IsVisibleInTree()) && !allowHandAnchor) return null;
            if (original != null && !original.IsVisibleInTree()) original = null;
            presentation = new(card, NCombatRoom.Instance, original, fromPlay);
            presentation._visual = NCard.Create(card);
            var visual = presentation._visual;
            if (visual == null) return null;
            presentation._room.Ui.AddChildSafely(visual);
            visual.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            visual.Visible = true;
            visual.MouseFilter = Control.MouseFilterEnum.Ignore;
            visual.GlobalPosition = original?.GlobalPosition ?? HandAnchor(presentation._room);
            var handTransform = original != null
                ? presentation._room.Ui.GetGlobalTransform().AffineInverse() * original.GetGlobalTransform()
                : Transform2D.Identity;
            visual.Scale = original != null ? handTransform.Scale : Vector2.One * 0.65f;
            visual.Rotation = original != null ? handTransform.Rotation : 0f;
            if (!fromPlay && original != null) original.Visible = false;
            return presentation;
        }
        catch (Exception ex)
        {
            presentation?.Dispose();
            Entry.Logger.Error($"[SpecialEffectVfx] 创建发动演出失败：{ex}");
            return null;
        }
    }

    private async Task<bool> FlyToCenter()
    {
        try
        {
            if (!IsActive()) return false;
            _tween = _visual!.CreateTween().SetParallel();
            _tween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
            _tween.TweenProperty(_visual, "global_position", _room.Ui.GetViewport().GetVisibleRect().GetCenter(), _travelTime);
            _tween.TweenProperty(_visual, "scale", Vector2.One * 1.15f, _travelTime);
            _tween.TweenProperty(_visual, "rotation", 0f, _travelTime);
            return await _tween.AwaitFinished(_visual);
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"[SpecialEffectVfx] 飞向中央失败：{ex}");
            // 显示异常不取消规则结算；退出战斗场景时才终止。
            return GodotObject.IsInstanceValid(_room) && _room.IsInsideTree();
        }
    }

    private async Task<bool> ReturnToHand()
    {
        try
        {
            if (!IsActive()) return false;
            if (!_fromPlay && _card.Pile?.Type != PileType.Hand) return false;
            if (!_fromPlay && _original != null && (!GodotObject.IsInstanceValid(_original) || !_original.IsInsideTree()
                || _original.Model != _card)) return false;
            _tween = _visual!.CreateTween();
            _tween.TweenInterval(_travelTime);
            var handTransform = !_fromPlay && _original != null
                ? _room.Ui.GetGlobalTransform().AffineInverse() * _original.GetGlobalTransform()
                : Transform2D.Identity;
            _tween.TweenProperty(_visual, "global_position", !_fromPlay && _original != null ? _original.GlobalPosition : HandAnchor(_room), _travelTime)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
            _tween.Parallel().TweenProperty(_visual, "scale", !_fromPlay && _original != null ? handTransform.Scale : Vector2.One * 0.65f, _travelTime);
            _tween.Parallel().TweenProperty(_visual, "rotation", !_fromPlay && _original != null ? handTransform.Rotation : 0f, _travelTime);
            return await _tween.AwaitFinished(_visual);
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"[SpecialEffectVfx] 返回手牌演出失败：{ex}");
            return GodotObject.IsInstanceValid(_room) && _room.IsInsideTree();
        }
    }

    public static void ShowStun(Creature enemy)
    {
        if (TestMode.IsOn || NCombatRoom.Instance == null) return;
        NStunnedVfx? visual = null;
        try
        {
            if (SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant) return;
            var container = enemy.GetVfxContainer();
            if (container == null) return;
            visual = NStunnedVfx.Create(enemy);
            if (visual != null) container.AddChildSafely(visual);
        }
        catch (Exception ex)
        {
            if (GodotObject.IsInstanceValid(visual)) visual!.QueueFreeSafely();
            Entry.Logger.Error($"[SpecialEffectVfx] 即时击晕特效失败：{ex}");
        }
    }

    private bool IsActive() => GodotObject.IsInstanceValid(_room) && _room.IsInsideTree()
        && GodotObject.IsInstanceValid(_visual) && _visual!.IsInsideTree();

    private static Vector2 HandAnchor(NCombatRoom room)
        => room.Ui.Hand.CardHolderContainer.GlobalPosition + room.Ui.Hand.CardHolderContainer.Size / 2;

    public void Dispose()
    {
        if (_tween?.IsValid() == true) _tween.Kill();
        if (!_fromPlay && GodotObject.IsInstanceValid(_original) && _original!.Model == _card) _original.Visible = _originalVisible;
        if (GodotObject.IsInstanceValid(_visual))
        {
            _visual!.Visible = false;
            _visual.QueueFreeSafely();
        }
        _visual = null;
    }
}
