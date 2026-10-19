using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace SelectSpecialCardMod.Scripts.Intent;

// 仅负责后续行动的只读卡面；不与本回合卡面、强夺入口或飞卡动画共用节点。
public partial class IntentFutureStackNode : Control
{
    private const int CollapsedVisibleCards = 3;
    private const float CollapsedStep = 12f;
    private const float ExpandedGap = 12f;
    private const float ScrollbarSpace = 20f;
    internal static readonly Color FutureTint = new(0.48f, 0.48f, 0.48f, 0.92f);
    private readonly List<CardModel> _models = [];
    private readonly List<(CardModel Model, Control Wrapper, NCard Card)> _views = [];
    private ScrollContainer? _scroll;
    private Control? _content;
    private Label? _count;
    private bool _expanded;
    private bool _interactive;
    private bool _coveredByCurrentIntent;
    private ulong _collapseAfter;
    private ulong _nextVisualRefresh;
    private int _visibleFirst;
    private int _visibleEnd;
    private bool _layoutDirty = true;
    private bool _laidOutExpanded;
    private float _laidOutWidth = -1f;
    private Vector2 _cardSize = new(135f, 190f);

    public float CardScale { get; set; } = 0.45f;
    public Action<NCard>? ConfigureCard { get; set; }
    public Func<Vector2, bool>? IsCoveredByCurrentIntent { get; set; }
    public Vector2 Footprint => _models.Count == 0 ? Vector2.Zero
        : new Vector2(_cardSize.X + CollapsedStep * (Math.Min(_models.Count, CollapsedVisibleCards) - 1), _cardSize.Y + ScrollbarSpace);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = false;
        ZAsRelative = false;
        ZIndex = 20;
        _scroll = new ScrollContainer
        {
            Name = "UpcomingActionsViewport", MouseFilter = MouseFilterEnum.Stop,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _content = new Control { Name = "UpcomingActions", MouseFilter = MouseFilterEnum.Ignore };
        _scroll.AddChild(_content);
        AddChild(_scroll);
        _scroll.GuiInput += OnScrollInput;
        _count = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore, ZIndex = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _count.AddThemeFontSizeOverride("font_size", 19);
        _count.AddThemeColorOverride("font_color", Colors.LightGray);
        _count.AddThemeColorOverride("font_outline_color", Colors.Black);
        _count.AddThemeConstantOverride("outline_size", 6);
        AddChild(_count);
    }

    public void SetModels(IReadOnlyList<CardModel> models, bool interactive)
    {
        SetInteractive(interactive);
        if (_models.Count == models.Count && _models.Zip(models).All(pair => ReferenceEquals(pair.First, pair.Second))) return;
        _models.Clear();
        _models.AddRange(models);
        _layoutDirty = true;
        for (int i = _views.Count - 1; i >= 0; i--)
        {
            if (_models.Any(m => ReferenceEquals(m, _views[i].Model))) continue;
            _views[i].Wrapper.GetParent()?.RemoveChild(_views[i].Wrapper);
            _views[i].Wrapper.QueueFree();
            _views.RemoveAt(i);
        }
        if (_models.Count == 0) _expanded = false;
        RefreshVisibleCards();
    }

    public void SetInteractive(bool interactive)
    {
        if (_interactive == interactive && (interactive || !_expanded)) return;
        _interactive = interactive;
        if (!interactive)
        {
            _expanded = false;
            if (_scroll != null) _scroll.ScrollHorizontal = 0;
        }
        RefreshVisibleCards();
    }

    // 将叠卡的第一张交给当前意图行；复用实际节点，避免原地消失后另造一张卡。
    internal (Control Wrapper, NCard Card)? TakeFront(CardModel model)
    {
        SetInteractive(false);
        if (_models.Count == 0 || !ReferenceEquals(_models[0], model)) return null;
        RefreshVisibleCards();
        int index = _views.FindIndex(v => ReferenceEquals(v.Model, model));
        if (index < 0) return null;
        var view = _views[index];
        _views.RemoveAt(index);
        _models.RemoveAt(0);
        RefreshVisibleCards();
        return (view.Wrapper, view.Card);
    }

    public void RefreshVisuals()
    {
        foreach (var (model, wrapper, card) in _views)
        {
            if (!wrapper.Visible || !GodotObject.IsInstanceValid(card)) continue;
            if (!_expanded && !ReferenceEquals(model, _models.FirstOrDefault())) continue;
            if (_expanded && _scroll != null && !_scroll.GetGlobalRect().Intersects(wrapper.GetGlobalRect())) continue;
            card.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
        }
    }

    public override void _Process(double delta)
    {
        if (_scroll == null || _models.Count == 0 || !IsVisibleInTree()) return;
        // Godot的鼠标命中顺序不跟随ZIndex；折叠卡在亮色卡后面时同时让出鼠标。
        bool covered = !_expanded && IsCoveredByCurrentIntent?.Invoke(GetGlobalMousePosition()) == true;
        if (_coveredByCurrentIntent != covered) { _coveredByCurrentIntent = covered; _layoutDirty = true; }
        var hovered = GetViewport().GuiGetHoveredControl();
        bool within = _interactive && !covered && hovered != null && (hovered == _scroll || IsAncestorOf(hovered));
        if (within)
        {
            _collapseAfter = Time.GetTicksMsec() + 160;
            if (!_expanded) { _expanded = true; RefreshVisibleCards(); }
        }
        else if (_expanded && (!_interactive || Time.GetTicksMsec() >= _collapseAfter))
        {
            _expanded = false;
            _scroll.ScrollHorizontal = 0;
            RefreshVisibleCards();
        }
        var range = VisibleRange();
        if (range.First != _visibleFirst || range.End != _visibleEnd) RefreshVisibleCards();
        LayoutCards();
        ulong now = Time.GetTicksMsec();
        if (now >= _nextVisualRefresh)
        {
            _nextVisualRefresh = now + 500;
            RefreshVisuals();
        }
    }

    private void OnScrollInput(InputEvent input)
    {
        if (!_expanded || _scroll == null || input is not InputEventMouseButton { Pressed: true } mouse) return;
        int direction = mouse.ButtonIndex switch
        {
            MouseButton.WheelUp or MouseButton.WheelLeft => -1,
            MouseButton.WheelDown or MouseButton.WheelRight => 1,
            _ => 0,
        };
        if (direction == 0) return;
        _scroll.ScrollHorizontal += direction * (int)(_cardSize.X + ExpandedGap);
        _scroll.AcceptEvent();
    }

    private void RefreshVisibleCards()
    {
        if (_content == null) return;
        (_visibleFirst, _visibleEnd) = VisibleRange();
        _layoutDirty = true;
        // 展开后的长队列也只创建可见窗口的卡；滚动到后面时再按需创建并缓存。
        for (int i = _visibleFirst; i < _visibleEnd; i++)
        {
            var model = _models[i];
            if (_views.Any(v => ReferenceEquals(v.Model, model))) continue;
            var card = PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
            var wrapper = new Control { MouseFilter = MouseFilterEnum.Pass, Modulate = FutureTint, TooltipText = model.Title };
            wrapper.AddChild(card);
            _content.AddChild(wrapper);
            card.Model = model;
            card.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            card.Scale = Vector2.One * CardScale;
            card.PivotOffset = Vector2.Zero;
            var frame = card.GetNodeOrNull<Control>("CardContainer/Frame");
            _cardSize = (frame is { Size.X: > 0, Size.Y: > 0 } ? frame.Size : new Vector2(300f, 422f)) * CardScale;
            card.Position = _cardSize * 0.5f;
            ConfigureCard?.Invoke(card);
            IgnoreCardInput(card);
            _views.Add((model, wrapper, card));
        }
        LayoutCards();
    }

    private void LayoutCards()
    {
        if (_scroll == null || _content == null || _count == null) return;
        var footprint = Footprint;
        int shown = _expanded ? _models.Count : Math.Min(_models.Count, CollapsedVisibleCards);
        float step = _expanded ? _cardSize.X + ExpandedGap : CollapsedStep;
        float width = shown == 0 ? 0 : _cardSize.X + step * (shown - 1);
        float panelWidth = _expanded ? Math.Min(width, AvailableWidth()) : footprint.X;
        if (!_layoutDirty && _laidOutExpanded == _expanded && Mathf.IsEqualApprox(_laidOutWidth, panelWidth)) return;
        _layoutDirty = false;
        _laidOutExpanded = _expanded;
        _laidOutWidth = panelWidth;
        Size = footprint;
        CustomMinimumSize = footprint;
        ZIndex = _expanded ? 80 : 20;
        bool acceptsInput = _interactive && !_coveredByCurrentIntent;
        _scroll.MouseFilter = acceptsInput ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        // 展开不移动当前卡或折叠锚点；屏幕右侧空间不足时在该区域滚动。
        _scroll.Size = new Vector2(panelWidth, footprint.Y);
        _scroll.HorizontalScrollMode = _expanded ? ScrollContainer.ScrollMode.Auto : ScrollContainer.ScrollMode.Disabled;
        _content.CustomMinimumSize = new Vector2(width, _cardSize.Y);
        _content.Size = new Vector2(width, _cardSize.Y);
        foreach (var (model, wrapper, _) in _views)
        {
            int index = _models.FindIndex(m => ReferenceEquals(m, model));
            wrapper.Visible = index >= _visibleFirst && index < _visibleEnd;
            wrapper.MouseFilter = acceptsInput ? MouseFilterEnum.Pass : MouseFilterEnum.Ignore;
            if (!wrapper.Visible) continue;
            wrapper.Size = _cardSize;
            wrapper.Position = new Vector2(index * step, 0);
            wrapper.ZIndex = _expanded ? 0 : CollapsedVisibleCards - index;
        }
        _count.Text = _models.Count.ToString();
        _count.Visible = !_expanded && _models.Count > 1;
        _count.Size = new Vector2(footprint.X, 26f);
        _count.Position = new Vector2(0, _cardSize.Y - 25f);
    }

    private float AvailableWidth()
        => Math.Max(Footprint.X, GetViewport().GetVisibleRect().End.X - GlobalPosition.X - 8f);

    private (int First, int End) VisibleRange()
    {
        if (!_expanded || _scroll == null) return (0, Math.Min(_models.Count, CollapsedVisibleCards));
        if (_models.Count == 0) return (0, 0);
        float step = _cardSize.X + ExpandedGap;
        int first = Math.Clamp((int)(_scroll.ScrollHorizontal / step), 0, _models.Count - 1);
        int count = (int)Math.Ceiling(AvailableWidth() / step) + 1;
        return (first, Math.Min(_models.Count, first + count));
    }

    private static void IgnoreCardInput(Node node)
    {
        if (node is Control control) control.MouseFilter = MouseFilterEnum.Ignore;
        foreach (Node child in node.GetChildren()) IgnoreCardInput(child);
    }
}
