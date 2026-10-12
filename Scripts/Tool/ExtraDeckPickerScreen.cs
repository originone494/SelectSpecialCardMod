using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「额外卡组」大图选卡界面（局外）。
// 入口：主菜单 → 模组设置 → 额外卡组 → 「打开额外卡组选择」（ExtraDeckSettings）。
// 独立全屏界面：每张「额外怪兽」显示成一张大卡，点一下选中/取消，最多 15 张；选择即时保存。
public partial class ExtraDeckPickerScreen : Control
{
    private const string LocTable = "card_selection";
    private const int Columns = 5;
    private const float MaxCardScale = 0.85f;
    private const float MinCardScale = 0.45f;
    private const int CardSeparation = 28;
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
        public Control Tile = null!;      // 变暗/变亮用（游戏卡场景或自绘卡面）
        public NCard? Card;               // 游戏卡（走了兜底自绘卡面时为 null）
        public CardModel Model = null!;
        public Label Count = null!;       // 右下角显示"×N"
    }

    private readonly List<Entry> _entries = [];
    private Label? _hintLabel;
    private Label? _countLabel;
    private float _cardScale = MaxCardScale;
    private ulong _bornMsec;
    private int _dumpsDone;

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

    // ── 临时诊断：把界面/列表/瓦片/卡位/卡的实际几何写进 user://SelectSpecialCardMod/extra_deck_layout.txt ──
    // 定位完排版问题后删掉这段即可（每次打开界面最多写 3 次，间隔 0.5 秒）。
    public override void _Process(double delta)
    {
        if (_dumpsDone >= 3) return;
        ulong now = Time.GetTicksMsec();
        if (_bornMsec == 0) _bornMsec = now;
        if (now < _bornMsec + (ulong)(500 * (_dumpsDone + 1))) return;
        _dumpsDone++;
        DumpLayout(now);
    }

    private void DumpLayout(ulong now)
    {
        try
        {
            var lines = new List<string>
            {
                $"time={now} scale={_cardScale}",
                $"viewport={GetViewportRect()}",
                $"screen={GetGlobalRect()}",
            };
            if (_entries.Count > 0)
            {
                var entry = _entries[0];
                var holder = entry.Tile;
                lines.Add($"wrapper0={entry.Wrapper.GetGlobalRect()}");
                lines.Add($"holder0={holder.GetGlobalRect()} scale={holder.Scale} pos={holder.Position} size={holder.Size}");
                if (entry.Card != null)
                {
                    lines.Add($"card0={entry.Card.GetGlobalRect()} pos={entry.Card.Position} size={entry.Card.Size}");
                    if (entry.Card.GetNodeOrNull<Control>("CardContainer/Frame") is { } frame)
                        lines.Add($"frame0={frame.GetGlobalRect()} pos={frame.Position} size={frame.Size}");
                    if (entry.Card.GetNodeOrNull<Control>("CardContainer/EnergyIcon") is { } energy)
                        lines.Add($"energy0={energy.GetGlobalRect()} visible={energy.Visible}");
                    if (entry.Card.GetNodeOrNull<Control>("CardContainer/StarIcon") is { } star)
                        lines.Add($"star0={star.GetGlobalRect()} visible={star.Visible}");
                    // 标签实际文本 + 两个费用口径，方便核对"卡面写的"和"卡牌数据"是否一致
                    if (entry.Card.GetNodeOrNull<Label>("CardContainer/TitleLabel") is { } titleLabel)
                        lines.Add($"titleText=\"{titleLabel.Text}\"");
                    if (entry.Card.GetNodeOrNull<Label>("CardContainer/EnergyIcon/EnergyLabel") is { } costLabel)
                        lines.Add($"costText=\"{costLabel.Text}\"");
                    lines.Add($"model.Title=\"{entry.Model.Title}\" EnergyCost.Canonical={entry.Model.EnergyCost.Canonical}"
                              + $" EnergyCost.GetWithModifiers={entry.Model.EnergyCost.GetWithModifiers(CostModifiers.None)}"
                              + $" CostsX={entry.Model.EnergyCost.CostsX}"
                              + $" HasLocalModifiers={entry.Model.EnergyCost.HasLocalModifiers}");
                    lines.Add($"pool={entry.Model.Pool?.GetType().Name} EnergyColorName={entry.Model.Pool?.EnergyColorName}"
                              + $" DeckEntryCardColor={entry.Model.Pool?.DeckEntryCardColor}"
                              + $" EnergyOutlineColor={entry.Model.Pool?.EnergyOutlineColor}");
                    if (entry.Card.GetNodeOrNull<TextureRect>("CardContainer/EnergyIcon") is { } orb)
                        lines.Add($"orbTexture={orb.Texture?.ResourcePath ?? "(null)"}");
                    if (entry.Card.GetNodeOrNull<Label>("CardContainer/EnergyIcon/EnergyLabel") is { } costColorLabel)
                        lines.Add($"costFontColor={costColorLabel.GetThemeColor("font_color")}"
                                  + $" outline={costColorLabel.GetThemeColor("font_outline_color")}");
                }
            }
            DirAccess.MakeDirRecursiveAbsolute("user://SelectSpecialCardMod");
            using var file = Godot.FileAccess.Open("user://SelectSpecialCardMod/extra_deck_layout.txt",
                Godot.FileAccess.ModeFlags.Write);
            file?.StoreString(string.Join("\n", lines) + "\n");
        }
        catch (Exception e)
        {
            GD.PushWarning($"[SelectSpecialCardMod] 额外卡组：排版自检写入失败：{e.Message}");
        }
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

        _hintLabel = new Label { Text = Loc(KeyHint) };
        column.AddChild(_hintLabel);

        _countLabel = new Label();
        _countLabel.AddThemeFontSizeOverride("font_size", 22);
        column.AddChild(_countLabel);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        column.AddChild(scroll);

        // 按窗口宽度算缩放：保证 5 列加间隙一定放得下（不同分辨率都不会卡框出界）
        float viewportWidth = GetViewportRect().Size.X;
        float available = Mathf.Max(600f, viewportWidth - 96f /*左右边距*/ - 24f /*滚动条*/);
        float tileWidth = (available - CardSeparation * (Columns - 1)) / Columns;
        _cardScale = Mathf.Clamp(tileWidth / CardVisualBox.Size.X, MinCardScale, MaxCardScale);

        var grid = new GridContainer { Columns = Columns };
        grid.AddThemeConstantOverride("h_separation", CardSeparation);
        grid.AddThemeConstantOverride("v_separation", CardSeparation);
        scroll.AddChild(grid);

        foreach (var model in ExtraDeckStore.AvailableExtraMonsters())
        {
            grid.AddChild(BuildCardEntry(model));
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
    }

    private Control BuildCardEntry(CardModel model)
    {
        var wrapper = new Control { MouseFilter = MouseFilterEnum.Stop };
        Control visual;
        try
        {
            visual = BuildGameCardVisual(model, wrapper);
        }
        catch (Exception e)
        {
            // 兜底：局外渲染游戏卡场景失败时，自己拼一张（卡图 + 卡名 + 描述），保证界面能用
            GD.PushWarning($"[SelectSpecialCardMod] 额外卡组：游戏卡场景渲染失败，改用简易卡面（{model.Title}）：{e.Message}");
            foreach (Node child in wrapper.GetChildren()) child.QueueFree();
            visual = BuildSimpleCardVisual(model, wrapper);
        }

        // 已选份数：×N 显示在瓦片右下角偏上（左上角是费用徽章），绿色、字号加大
        var count = new Label
        {
            Position = new Vector2(wrapper.CustomMinimumSize.X - 60f, wrapper.CustomMinimumSize.Y - 54f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        count.AddThemeFontSizeOverride("font_size", 34);
        count.AddThemeColorOverride("font_color", new Color(0f, 1f, 0f));
        count.AddThemeColorOverride("font_outline_color", Colors.Black);
        count.AddThemeConstantOverride("outline_size", 8);
        wrapper.AddChild(count);

        var entry = new Entry { Wrapper = wrapper, Tile = visual, Model = model, Card = visual as NCard, Count = count };
        _entries.Add(entry);
        wrapper.GuiInput += ev => OnCardGuiInput(ev, entry);
        return wrapper;
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
        // 卡面视觉以节点原点为中心（卡框 300×422，费用徽章再往左上凸一点），
        // 缩放基准设成节点原点，再把"外框左上角"对到瓦片左上角。
        card.Scale = new Vector2(_cardScale, _cardScale);
        card.PivotOffset = Vector2.Zero;
        card.Position = -CardVisualBox.Position * _cardScale;
        // 卡面数据必须显式刷新一次：卡场景自带占位文本（标题停在 "Broken Card"、费用停在 0、
        // 星标常驻），要调 UpdateVisuals 才会用卡牌数据覆盖（意图卡那边就是这么做才显示正常的）。
        card.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
        // 费用 / 星标：局外没有战斗上下文，卡场景不会自己填，按卡牌数据直接写（费用=卡的真实费用；
        // 星标只在卡真的带星费时才显示）。
        ApplyCostAndStar(card, model);
        // 描述兜底：图鉴里能看到描述，但如果这条路径也没填上（占位文本），就用我们自己算的文本
        ApplyDescriptionText(card, model);
        // 点击交给外层 wrapper；卡面自己不吃鼠标（否则鼠标移上去会触发它自带的悬停放大）
        card.MouseFilter = MouseFilterEnum.Ignore;

        wrapper.CustomMinimumSize = CardVisualBox.Size * _cardScale;
        return card;
    }

    // 兜底：不依赖游戏卡场景，自己拼一张卡（卡图 + 卡名 + 描述）
    private static Control BuildSimpleCardVisual(CardModel model, Control wrapper)
    {
        const float width = 240f;
        wrapper.CustomMinimumSize = new Vector2(width, 340f);

        var panel = new PanelContainer();
        wrapper.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var column = new VBoxContainer();
        panel.AddChild(column);

        var picture = new TextureRect
        {
            Texture = model.Portrait,
            CustomMinimumSize = new Vector2(width, 160f),
            ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
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

    // 把卡面描述标签的文本替换成"算好的描述"
    private static void ApplyDescriptionText(NCard ncard, CardModel model)
    {
        try
        {
            string text = ResolveDescription(model);
            if (ncard.GetNodeOrNull<Control>("%DescriptionLabel") is RichTextLabel label) label.Text = text;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[SelectSpecialCardMod] 额外卡组：卡面描述填充失败（{model.Title}）：{e.Message}");
        }
    }

    // 费用与星标：按卡牌数据直接写卡面对应节点
    private static void ApplyCostAndStar(NCard ncard, CardModel model)
    {
        try
        {
            // 卡名：卡场景自带占位文本 "Broken Card"，直接按卡牌数据写（UpdateVisuals 在局外不填它）
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
        {
            bool selected = ExtraDeckStore.IsSelected(entry.Model);
            entry.Tile.Modulate = selected ? Colors.White : new Color(0.62f, 0.62f, 0.62f);
            int count = ExtraDeckStore.CountOf(entry.Model);
            entry.Count.Text = count > 0 ? $"×{count}" : "";
        }
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
