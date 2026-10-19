using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「额外卡组」大图选卡界面（局外）。
// 入口：主菜单 → 模组设置 → 额外卡组 → 「打开额外卡组选择」（ExtraDeckSettings）。
// 独立全屏界面：自适应大卡网格，完整卡面只渲染一次，滚动时使用缓存预览。
public partial class ExtraDeckPickerScreen : Control
{
    private const string LocTable = "card_selection";
    private const float TargetCardScale = 1f;
    private const float MaxCardScale = 1.15f;
    private const float PreviewScale = 1.25f;
    private const int CardSeparation = 24;
    private const ulong ScrollIdleMsec = 150;
    // 卡面视觉的外框（相对卡节点原点；从游戏 scenes/cards/card.tscn 量出来）：
    //   · 卡框本体 Frame 是以原点为中心的 300×422（-150..150 / -211..211）；
    //   · 费用徽章实际占 (-166..-102, -227..-163)（实测，从自检日志读出来的）；
    //   · 投影往右下伸到约 (162, 223)。
    // 所以外框 = x -166..162、y -227..223 → 位置 (-166,-227)、尺寸 328×450。
    //（星标在游戏场景里更靠左，但本 mod 的卡没有星费、已统一隐藏，所以不计入外框。）
    // 卡位不会自动把卡挪进自己的范围，必须按这个外框手动对位——否则卡的上半张会被画到瓦片上面去。
    private static readonly Rect2 CardVisualBox = new(-166f, -227f, 328f, 450f);

    private const string KeyTitle = "SELECT_SPECIAL_CARD_MOD_EXTRA_DECK_TITLE";
    private const string KeyHint = "SELECT_SPECIAL_CARD_MOD_EXTRA_DECK_HINT";
    private const string KeyCount = "SELECT_SPECIAL_CARD_MOD_EXTRA_DECK_COUNT";
    private const string KeyFull = "SELECT_SPECIAL_CARD_MOD_EXTRA_DECK_FULL";
    private const string KeyFullPerCard = "SELECT_SPECIAL_CARD_MOD_EXTRA_DECK_FULL_PER_CARD";
    private const string KeyDone = "SELECT_SPECIAL_CARD_MOD_EXTRA_DECK_DONE";
    private const string KeyClear = "SELECT_SPECIAL_CARD_MOD_EXTRA_DECK_CLEAR";

    private static CanvasLayer? _openLayer;

    private sealed class Entry
    {
        public Control Wrapper = null!;
        public Control? Tile;             // 静态卡面，滚动离屏后保留缓存
        public SubViewport? Preview;
        public Label Placeholder = null!;
        public CardModel Model = null!;
        public Label Count = null!;       // 右下角显示"×N"
    }

    private readonly List<Entry> _entries = [];
    private Label? _hintLabel;
    private Label? _countLabel;
    private ScrollContainer? _scroll;
    private GridContainer? _grid;
    private float _cardScale = MaxCardScale;
    private bool _layoutDirty = true;
    private bool _visibleDirty = true;
    private Entry? _renderingEntry;
    private Control? _renderingSource;
    private bool _previewReady;
    private bool _previewRequested;
    private ulong _previewStartFrame;
    private ulong _lastScrollMsec;

    public override void _Process(double delta)
    {
        if (_previewReady) FinishPreview();
        if (_renderingEntry != null && !_previewRequested
            && Engine.GetProcessFrames() >= _previewStartFrame + 2)
        {
            // 新视口和卡面的延迟排版需要先就绪，否则首帧可能缓存成空图。
            RenderingServer.FramePostDraw += OnPreviewDrawn;
            _renderingEntry.Preview!.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            _previewRequested = true;
        }
        if (_layoutDirty)
        {
            if (_scroll == null || _scroll.Size.X <= 1f) return;
            UpdateLayout();
            // 容器在本帧结束时重排，下一帧再判断哪些卡位可见。
            return;
        }
        if (_visibleDirty) RefreshVisibleCards();
        if (!_visibleDirty) SetProcess(false);
    }

    private void QueueLayout()
    {
        _layoutDirty = true;
        QueueVisibleRefresh();
    }

    private void QueueVisibleRefresh()
    {
        _visibleDirty = true;
        SetProcess(true);
    }

    private void OnScrollChanged(double value)
    {
        _lastScrollMsec = Time.GetTicksMsec();
        QueueVisibleRefresh();
    }

    private void UpdateLayout()
    {
        _layoutDirty = false;
        // 始终预留滚动条宽度，避免滚动条出现后反复改变列数。
        float available = Mathf.Max(1f, _scroll!.Size.X
            - _scroll.GetVScrollBar().GetCombinedMinimumSize().X - 8f);
        // 按接近原尺寸的大卡决定列数，避免为了多塞一列而把所有卡缩小。
        int columns = Math.Clamp((int)Mathf.Floor((available + CardSeparation)
            / (CardVisualBox.Size.X * TargetCardScale + CardSeparation)), 1, Math.Max(1, _entries.Count));
        float cellWidth = Mathf.Max(1f, (available - CardSeparation * (columns - 1)) / columns);
        _cardScale = Mathf.Min(MaxCardScale, cellWidth / CardVisualBox.Size.X);
        _grid!.Columns = columns;
        foreach (var entry in _entries)
        {
            entry.Wrapper.CustomMinimumSize = new Vector2(cellWidth, CardVisualBox.Size.Y * _cardScale);
        }
        _visibleDirty = true;
    }

    private void RefreshVisibleCards()
    {
        var visible = _scroll!.GetGlobalRect();
        Entry? nextVisible = null;
        Entry? nextHidden = null;
        foreach (var entry in _entries)
        {
            var rect = entry.Wrapper.GetGlobalRect();
            bool onScreen = rect.Intersects(visible);
            if (entry.Tile != null)
            {
                entry.Tile.Visible = onScreen && entry != _renderingEntry;
                continue;
            }
            if (onScreen) nextVisible ??= entry;
            else nextHidden ??= entry;
        }
        var next = nextVisible ?? nextHidden;
        _visibleDirty = next != null || _renderingEntry != null;
        if (next == null || _renderingEntry != null) return;
        // 滚动中只移动缓存图；停止后优先补齐可见卡，再逐帧预热其余卡。
        if (_lastScrollMsec != 0 && Time.GetTicksMsec() - _lastScrollMsec < ScrollIdleMsec) return;
        BuildVisual(next);
    }

    /// <summary>打开选卡界面（已经开着就什么都不做）。</summary>
    public static void Open()
    {
        if (_openLayer != null && GodotObject.IsInstanceValid(_openLayer)) return;
        if (Engine.GetMainLoop() is not SceneTree tree) return;

        var layer = new CanvasLayer { Name = "ExtraDeckPickerLayer", Layer = 100 };
        var screen = new ExtraDeckPickerScreen { Name = "ExtraDeckPicker" };
        layer.AddChild(screen);
        tree.Root.AddChild(layer);
        screen.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        screen.Build();
        _openLayer = layer;
    }

    private void Close()
    {
        if (_openLayer != null && GodotObject.IsInstanceValid(_openLayer)) _openLayer.QueueFree();
        _openLayer = null;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel")) return;
        Close();
        GetViewport().SetInputAsHandled();
    }

    private void Build()
    {
        // 遮罩：挡住背后的界面，顺便让鼠标不会点到下面
        // 不透明遮罩：半透明时背后的设置页文字会透出来
        var backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 1f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        AddChild(backdrop);
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var margin = new MarginContainer();
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 48);
        margin.AddThemeConstantOverride("margin_right", 48);
        margin.AddThemeConstantOverride("margin_top", 32);
        margin.AddThemeConstantOverride("margin_bottom", 32);

        var column = new VBoxContainer();
        margin.AddChild(column);

        var title = new Label { Text = Loc(KeyTitle) };
        title.AddThemeFontSizeOverride("font_size", 34);
        column.AddChild(title);

        _hintLabel = new Label
        {
            Text = Loc(KeyHint),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        column.AddChild(_hintLabel);

        _countLabel = new Label();
        _countLabel.AddThemeFontSizeOverride("font_size", 22);
        column.AddChild(_countLabel);

        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(_scroll);
        _scroll.Resized += QueueLayout;
        _scroll.GetVScrollBar().ValueChanged += OnScrollChanged;

        _grid = new GridContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", CardSeparation);
        _grid.AddThemeConstantOverride("v_separation", CardSeparation);
        _scroll.AddChild(_grid);
        _grid.Resized += QueueVisibleRefresh;

        foreach (var model in ExtraDeckStore.AvailableExtraMonsters())
        {
            _grid.AddChild(BuildCardEntry(model));
        }

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        column.AddChild(footer);

        var clear = new Button { Text = Loc(KeyClear) };
        clear.Pressed += () =>
        {
            ExtraDeckStore.Clear();
            RefreshSelection();
        };
        footer.AddChild(clear);

        var done = new Button { Text = Loc(KeyDone) };
        done.Pressed += Close;
        footer.AddChild(done);

        RefreshSelection();
        QueueLayout();
    }

    private Control BuildCardEntry(CardModel model)
    {
        var wrapper = new Control
        {
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = CardVisualBox.Size * _cardScale,
        };
        // 先创建轻量卡位，完整卡面按可见范围分帧补齐。
        var placeholder = new Label
        {
            Text = model.Title,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        wrapper.AddChild(placeholder);
        placeholder.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var count = new Label { MouseFilter = MouseFilterEnum.Ignore };
        count.AddThemeFontSizeOverride("font_size", 34);
        count.AddThemeColorOverride("font_color", new Color(0f, 1f, 0f));
        count.AddThemeColorOverride("font_outline_color", Colors.Black);
        count.AddThemeConstantOverride("outline_size", 8);
        wrapper.AddChild(count);
        count.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        count.OffsetLeft = -70f;
        count.OffsetTop = -54f;
        count.OffsetRight = -8f;
        count.OffsetBottom = -8f;
        count.ZIndex = 1;

        var entry = new Entry { Wrapper = wrapper, Model = model, Placeholder = placeholder, Count = count };
        _entries.Add(entry);
        wrapper.GuiInput += ev => OnCardGuiInput(ev, entry);
        return wrapper;
    }

    private void BuildVisual(Entry entry)
    {
        var model = entry.Model;
        // 每张缓存保留一个停止更新的渲染目标，无 GPU→CPU 读回和图片重传。
        // 卡面源节点在首次渲染后释放，滚动时不会再执行游戏卡的着色器和排版。
        var preview = new SubViewport
        {
            Size = new Vector2I((int)Mathf.Ceil(CardVisualBox.Size.X * PreviewScale),
                (int)Mathf.Ceil(CardVisualBox.Size.Y * PreviewScale)),
            TransparentBg = true,
            Disable3D = true,
            GuiDisableInput = true,
            World2D = new World2D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(preview);
        var holder = new Control { Size = CardVisualBox.Size };
        preview.AddChild(holder);
        Control source;
        try
        {
            source = BuildGameCardVisual(model, holder);
            source.Scale = Vector2.One * PreviewScale;
            source.PivotOffset = Vector2.Zero;
            source.Position = -CardVisualBox.Position * PreviewScale;
        }
        catch (Exception e)
        {
            // 兜底：局外渲染游戏卡场景失败时，自己拼一张（卡图 + 卡名 + 描述），保证界面能用
            GD.PushWarning($"[SelectSpecialCardMod] 额外卡组：游戏卡场景渲染失败，改用简易卡面（{model.Title}）：{e.Message}");
            foreach (Node child in holder.GetChildren())
            {
                if (child is CanvasItem canvas) canvas.Hide();
                child.QueueFree();
            }
            source = BuildSimpleCardVisual(model, holder);
            holder.Scale = Vector2.One * PreviewScale;
        }
        FreezePreview(source);
        var picture = new TextureRect
        {
            Texture = preview.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        entry.Wrapper.AddChild(picture);
        picture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        entry.Tile = picture;
        entry.Preview = preview;
        _renderingEntry = entry;
        _renderingSource = holder;
        _previewReady = false;
        _previewRequested = false;
        _previewStartFrame = Engine.GetProcessFrames();
        RefreshEntrySelection(entry);
    }

    private void OnPreviewDrawn() => _previewReady = true;

    private void FinishPreview()
    {
        RenderingServer.FramePostDraw -= OnPreviewDrawn;
        var entry = _renderingEntry!;
        entry.Preview!.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        _renderingSource!.QueueFree();
        entry.Placeholder.Hide();
        _renderingEntry = null;
        _renderingSource = null;
        _previewReady = false;
        _previewRequested = false;
        _visibleDirty = true;
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePostDraw -= OnPreviewDrawn;
    }

    private static void FreezePreview(Node node)
    {
        // 局外卡面是静态预览，不需要每张卡的观察器、动画和先古粒子持续运行。
        node.ProcessMode = ProcessModeEnum.Disabled;
        if (node is Control control) control.MouseFilter = MouseFilterEnum.Ignore;
        if (node is GpuParticles2D particles) { particles.Emitting = false; particles.Hide(); }
        if (node is CpuParticles2D cpuParticles) { cpuParticles.Emitting = false; cpuParticles.Hide(); }
        foreach (Node child in node.GetChildren()) FreezePreview(child);
    }

    // 首选：游戏自己的卡工厂 NCard.Create（图鉴、手牌同款），但**不套图鉴的 grid_card_holder**：
    // 那个卡位是"原点=卡的视觉中心 + 居中的 hitbox + 聚焦放大"，自带鼠标悬停放大预览，
    // 而且期望卡以它的原点为中心——放进规整的瓦片格里就会放大、错位。
    // 这里改成把卡直接放进瓦片，位置/缩放自己控制（和意图卡那边一样，且卡片不吃鼠标 → 不放大）。
    private Control BuildGameCardVisual(CardModel model, Control wrapper)
    {
        // 卡牌必须是「可变实例」，直接绑 ModelDb 的模板渲染不出来（ToMutable 是游戏自己的入口）
        NCard card = NCard.Create(model.ToMutable(), ModelVisibility.Visible)
                     ?? throw new InvalidOperationException("NCard.Create 返回了 null");
        wrapper.AddChild(card);
        // 卡面数据必须显式刷新一次：卡场景自带占位文本（标题停在 "Broken Card"、费用停在 0、
        // 星标常驻），要调 UpdateVisuals 才会用卡牌数据覆盖（意图卡那边就是这么做才显示正常的）。
        // 局外按卡组预览，不能用 Hand：否则会判定无法打出并在费用图标上画红线。
        card.SetForceUnpoweredPreview(true);
        card.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
        // 统一为局外的基础费用显示，并隐藏不适用于此处的战斗标记。
        ApplyCostAndStar(card, model);
        // 点击交给外层 wrapper；卡面自己不吃鼠标（否则鼠标移上去会触发它自带的悬停放大）
        card.MouseFilter = MouseFilterEnum.Ignore;

        return card;
    }

    // 兜底：不依赖游戏卡场景，自己拼一张卡（卡图 + 卡名 + 描述）
    private static Control BuildSimpleCardVisual(CardModel model, Control wrapper)
    {
        var panel = new PanelContainer();
        wrapper.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var column = new VBoxContainer();
        panel.AddChild(column);

        var picture = new TextureRect
        {
            Texture = model.Portrait,
            CustomMinimumSize = new Vector2(0f, 140f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        column.AddChild(picture);

        var title = new Label
        {
            Text = model.Title,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        title.AddThemeFontSizeOverride("font_size", 20);
        column.AddChild(title);

        var description = new Label
        {
            Text = DescriptionOf(model),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            ClipText = true,
        };
        description.AddThemeFontSizeOverride("font_size", 14);
        column.AddChild(description);
        return panel;
    }

    private static string DescriptionOf(CardModel model)
    {
        try
        {
            return ResolveDescription(model);
        }
        catch
        {
            return "";
        }
    }

    // 费用与星标：按卡牌数据直接写卡面对应节点
    private static void ApplyCostAndStar(NCard ncard, CardModel model)
    {
        try
        {
            // 卡名与费用始终显示规范卡的数据。
            if (ncard.GetNodeOrNull<Control>("CardContainer/TitleLabel") is Label titleLabel)
            {
                titleLabel.Text = model.Title;
            }
            if (ncard.GetNodeOrNull<Control>("CardContainer/EnergyIcon/EnergyLabel") is Label costLabel)
            {
                costLabel.Text = model.EnergyCost.CostsX
                    ? "X"
                    : model.EnergyCost.Canonical.ToString();
                // 费用数字颜色：卡场景写死的是纯绿（局外游戏不会覆盖它），按原版观感改成白色
                costLabel.AddThemeColorOverride("font_color", Colors.White);
            }
            // 能量球：卡场景默认用的是"铁甲战士"的球（energy_ironclad），而本 mod 卡池声明的是
            // Colorless（SelectSpecialCardPool.EnergyColorName）——局外游戏不会替换它，所以按卡牌
            // 自身的能量图标换过来。
            if (ncard.GetNodeOrNull<TextureRect>("CardContainer/EnergyIcon") is { } orb)
            {
                orb.Texture = model.EnergyIcon;
            }
            // 星标（原版某个角色的星费机制）：本 mod 的卡全部只有能量费用，
            // 代码里没有任何星费设置（已核对：只有 EnergyCost.CostsX 的用法），所以统一隐藏。
            if (ncard.GetNodeOrNull<Control>("CardContainer/StarIcon") is { } star) star.Visible = false;
            if (ncard.GetNodeOrNull<Control>("%UnplayableEnergyIcon") is { } energySlash) energySlash.Hide();
            if (ncard.GetNodeOrNull<Control>("%UnplayableStarIcon") is { } starSlash) starSlash.Hide();
        }
        catch (Exception e)
        {
            GD.PushWarning($"[SelectSpecialCardMod] 额外卡组：费用/星标写入失败（{model.Title}）：{e.Message}");
        }
    }

    // 克隆一份描述（不改共享实例）→ 把这张卡的动态变量整组加进去（{Damage:diff()} 这类占位就填上了）
    // → 取最终文本。用的是游戏自己的 DynamicVarSet.AddTo(LocString)。
    private static string ResolveDescription(CardModel model)
    {
        var source = model.Description;
        var loc = new LocString(source.LocTable, source.LocEntryKey);
        model.DynamicVars.AddTo(loc);
        return loc.GetFormattedText();
    }

    private void OnCardGuiInput(InputEvent @event, Entry entry)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mouse) return;

        // 右键 −1
        if (mouse.ButtonIndex == MouseButton.Right)
        {
            if (ExtraDeckStore.Remove(entry.Model)) RefreshSelection();
            return;
        }

        // 左键 +1
        if (mouse.ButtonIndex != MouseButton.Left) return;
        if (ExtraDeckStore.Add(entry.Model))
        {
            RefreshSelection();
            return;
        }

        // 加不进去：区分"同一张已满 3 份"和"总数已满 15 份"
        bool perCardFull = ExtraDeckStore.CountOf(entry.Model) >= ExtraDeckStore.MaxCopiesPerName;
        var loc = new LocString(LocTable, perCardFull ? KeyFullPerCard : KeyFull);
        loc.Add("Max", (perCardFull ? ExtraDeckStore.MaxCopiesPerName : ExtraDeckStore.MaxCards).ToString());
        _hintLabel!.Text = loc.GetFormattedText();
    }

    private void RefreshSelection()
    {
        _hintLabel!.Text = Loc(KeyHint);

        var total = new LocString(LocTable, KeyCount);
        total.Add("Count", ExtraDeckStore.Selected.Count.ToString());
        total.Add("Max", ExtraDeckStore.MaxCards.ToString());
        _countLabel!.Text = total.GetFormattedText();

        foreach (var entry in _entries)
            RefreshEntrySelection(entry);
    }

    private static void RefreshEntrySelection(Entry entry)
    {
        int count = ExtraDeckStore.CountOf(entry.Model);
        if (entry.Tile != null)
            entry.Tile.Modulate = count > 0 ? Colors.White : new Color(0.62f, 0.62f, 0.62f);
        entry.Count.Text = count > 0 ? $"×{count}" : "";
    }

    private static string Loc(string key)
    {
        try
        {
            var loc = new LocString(LocTable, key);
            string text = loc.GetFormattedText();
            return string.IsNullOrWhiteSpace(text) ? key : text;
        }
        catch
        {
            return key;
        }
    }
}
