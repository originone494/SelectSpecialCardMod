using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;

namespace SelectSpecialCardMod.Scripts.Intent;

// 意图卡牌悬浮层：挂在 NCreature（怪物 UI 节点）下。
// 结构 = 固定容器（本节点）＋ 内部居中的卡行（_row）：
//   · 容器自身只跟「怪物头顶正上方的锚点」走，定位与卡牌数量无关——增删卡牌不会移动容器；
//   · 卡片统一放进 _row，_row 每帧水平居中于容器原点、底边贴在容器原点，
//     所以卡片永远显示在锚点正上方（卡数变化只影响卡行自身）。
// 生命周期：随 NCreature 一起销毁，无需手动清理。
public partial class IntentCardOverlayNode : Control
{
    // 原版卡面相对较大，整体缩放到合适怪物头顶展示
    private const float CardScale = 0.45f;

    private NCreature? _creature;
    private string _signature = "";
    private bool _flyOutStarted;
    private ulong _flyOutDeadline;
    private bool _positionInitialized;
    private ulong _nextSignatureCheckMsec;
    // 强夺飞卡期间暂缓重建（否则新队首会立刻顶掉正在飞向手牌区的那张卡）
    private ulong _rebuildSuppressUntil;
    // 卡面数值刷新节流（力量/虚弱变化时重刷描述）
    private ulong _nextCardRefreshMsec;

    // 缩放占位：Control.Scale 不影响容器布局占位，用包装节点同步占位尺寸
    private readonly List<(Control Wrapper, NCard Card)> _cards = [];

    // 固定容器内部的卡行：所有意图卡都挂在这里。
    // 刻意**不用 HBoxContainer**：容器最小尺寸/锚点/枢轴会互相影响，手动排版才能保证严格居中。
    private Control? _row;

    // 卡与卡之间的间距
    private const float CardSeparation = 16f;

    // 「意图卡池」查看面板：右键敌人弹出，鼠标离开敌人与面板后关闭
    private GridContainer? _poolPopup;

    // 能量标签：怪物头顶（手牌上方）显示剩余费用，玩家据此预判它还能出几张牌。
    // 挂在血条节点下当普通子节点：血条自己会随怪物移动/死亡消失，标签跟着走即可
    //（不用 TopLevel —— 那种节点不继承父级可见性，开地图/卡组/牌堆时还会浮在最上面）。
    private Label? _energyLabel;
    private NHealthBar? _healthBar;
    private string _lastEnergyText = "";

    public override void _Ready()
    {
        _creature = GetParent() as NCreature;
        // 容器本身不参与布局，只当固定锚点；卡片统一放进内部卡行（由 LayoutRow 手动居中排列）
        _row = new Control { Name = "IntentCardRow" };
        AddChild(_row);
        IntentCardFlySync.CardHit += OnFlySyncCardHit;
        // 右键敌人 → 查看意图卡池（Hitbox 是怪物身上覆盖的可交互区域）
        _creature?.Hitbox?.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(OnCreatureGuiInput));
    }

    public override void _ExitTree()
    {
        IntentCardFlySync.CardHit -= OnFlySyncCardHit;
        ClosePool();
    }

    private void OnFlySyncCardHit(Creature creature)
    {
        if (creature != _creature?.Entity) return;
        // 路线 A：飞卡由 MonsterDeck.PlayTurn → OnCardPlayed 逐张驱动，
        // 忽略拆分/效果命令发出的飞卡信号，避免同一张卡飞两次。
        if (MonsterDeck.IsTracked(creature)) return;
        FlyNext();
    }

    public override void _Process(double delta)
    {
        if (_creature == null || !GodotObject.IsInstanceValid(_creature)) return;

        // 卡池面板每帧跟随怪物并检测鼠标离开（放在早退分支之前，保证各状态下都能关闭）
        TickPoolPopup();

        // 路线 A：原版意图图标隐藏（见下方 intentContainer.Visible），头顶手牌是唯一意图展示；
        // 意图容器不再显示，但仍作为手牌的定位锚点（其 Position/Size 在隐藏后依然有效）。
        var intentContainer = _creature.IntentContainer;
        var enemy = _creature.Entity;
        bool inCombat = CombatManager.Instance?.CurrentCombatId != null;
        // 路线 A：敌方怪物一律用头顶手牌作为唯一意图展示——原版意图图标隐藏（避免两套预告不一致）
        bool routeA = inCombat && enemy != null && !enemy.IsPlayer && enemy.Side == CombatSide.Enemy;
        if (routeA && intentContainer != null) intentContainer.Visible = false;

        // 怪物死亡：立即清卡，不等意图清空（游戏在死亡动画播完后才清 NextMove，
        // 若等意图分支处理，卡牌会滞留到动画结束）
        if (inCombat && enemy != null && enemy.IsDead)
        {
            if (_cards.Count > 0) ClearCards();
            IntentCardTheft.ClearAll(enemy);
            MonsterDeck.Clear(enemy);
            _signature = "";
            return;
        }

        if (routeA && enemy != null)
        {
            // 玩家回合：头顶展示怪物这一手。抽牌后重建；出牌期间由
            // OnCardPlayed 逐张飞出，不在这里重建（避免清掉正在飞的卡）。
            if (enemy.CombatState?.CurrentSide == CombatSide.Player && _cards.Count == 0
                && Time.GetTicksMsec() >= _rebuildSuppressUntil)
            {
                // 手牌为空但牌堆还有牌 → 先补抽（自愈；正常由 PrepareTurn 抽好）
                if (MonsterDeck.HandOf(enemy).Count == 0) MonsterDeck.EnsureHand(enemy);
                if (MonsterDeck.HandOf(enemy).Count > 0) RebuildFromHand(enemy);
            }
            // 失联兜底：连飞已开始但迟迟没飞完，超时把剩余卡全部飞出
            if (_flyOutStarted && _cards.Count > 0 && Time.GetTicksMsec() > _flyOutDeadline)
            {
                FlushRemaining();
            }

            // 数值会随力量/虚弱等实时变化（如怪物被虚弱后伤害降低）：节流重刷卡面，
            // 让描述里的数字跟上（卡面文本走 LiveText，每次渲染都重算）。
            if (_cards.Count > 0)
            {
                ulong now = Time.GetTicksMsec();
                if (now >= _nextCardRefreshMsec)
                {
                    _nextCardRefreshMsec = now + 200;
                    foreach (var (_, card) in _cards)
                        if (GodotObject.IsInstanceValid(card))
                            card.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
                }
            }
        }
        else
        {
            var intents = enemy?.Monster?.NextMove?.Intents;
            if (!inCombat || intents == null || intents.Count == 0)
            {
                if (_cards.Count > 0)
                {
                    // 怪物死亡：直接清理，不播飞出动画（否则死亡后卡牌还会在原地滞留一秒多）；
                    // 战斗中意图消失（活怪）：把剩余未飞的卡全部快速飞出（兜底，正常应由攻击结算逐张驱动）；
                    // 战斗外（战斗结束/怪物离场）：直接清理
                    if (inCombat && enemy != null && !enemy.IsDead)
                    {
                        FlushRemaining();
                    }
                    else
                    {
                        ClearCards();
                    }
                    _signature = "";
                }
                return;
            }

            // 内容签名防抖：意图没变就不重建。
            // 签名计算含 GetSingleDamage（内部跑 ModifyDamage hook 链），节流 200ms——
            // 意图变化最多延迟 200ms 反映到卡面，无感；避免 60fps × 怪数的 hook 链开销。
            // 连飞开始后冻结重建：攻击落地会触发实时数值变化（受击触发效果/复合招式自强化改变
            // GetSingleDamage）→ 签名变 → 若此时重建会清掉未飞的卡并重置计数，导致飞卡次数与
            // 实际攻击次数错位。展示卡对应的是行动开始时的意图快照，等飞完（或超时兜底）再按
            // 新签名重建。_signature 同步不更新，保证飞完后能立即触发一次重建。
            ulong now = Time.GetTicksMsec();
            if (now >= _nextSignatureCheckMsec)
            {
                _nextSignatureCheckMsec = now + 200;
                string signature = IntentCardMapper.BuildIntentSignature(intents, IntentCardHook.LocalPlayer, enemy);
                if (signature != _signature && !_flyOutStarted)
                {
                    _signature = signature;
                    RebuildCards(enemy);
                }
            }

            // 失联兜底：连飞已开始但攻击信号迟迟没来（如伤害被其他 mod 拦截），超时把剩余卡全部飞出
            if (_flyOutStarted && _cards.Count > 0 && Time.GetTicksMsec() > _flyOutDeadline)
            {
                FlushRemaining();
            }
        }

        // 固定容器定位：锚点 = **怪物本体（Hitbox）的头顶中点**（全局坐标算），
        // 也就是"怪物头顶正上方的正中央"。只用怪物自己的矩形，不掺原版意图容器
        //（那个容器的宽度会随意图数量变、位置未必居中在怪物身上）。
        // 容器自身永不因卡数变化而移动；弹性追踪让怪物移动/体型变化时带一点惯性。
        var agentRect = _creature.Hitbox != null && _creature.Hitbox.Size.X > 0f
            ? _creature.Hitbox.GetGlobalRect()
            : (intentContainer != null ? intentContainer.GetGlobalRect() : new Rect2());
        if (agentRect.Size.X > 0f)
        {
            var anchor = new Vector2(agentRect.Position.X + agentRect.Size.X * 0.5f, agentRect.Position.Y);
            if (_positionInitialized)
            {
                GlobalPosition = GlobalPosition.Lerp(anchor, 1f - Mathf.Exp(-10f * (float)delta));
            }
            else
            {
                GlobalPosition = anchor;
                _positionInitialized = true;
            }
        }
        SyncWrapperSizes();
        LayoutRow();
        DumpLayout();
        ClampToViewport();
        SyncEnergyDisplay(routeA, enemy);

        // 定期补挂（节流 2s）：覆盖战斗中途召唤的怪（节点晚于事件入树，事件挂载会漏）
        IntentCardHook.TryAttachThrottled();
    }

    // 位置钳制：卡行超出可视区域就整体拉回来（保证大体型怪物的意图卡不飞出屏幕，
    // 必要时允许压在怪物身上）。通过移动容器实现，卡行相对容器保持"居中 + 底边贴锚点"。
    private void ClampToViewport()
    {
        if (_row == null || _cards.Count == 0) return;
        var size = _row.Size;
        if (size.X <= 0f || size.Y <= 0f) size = _row.GetCombinedMinimumSize();
        if (size.X <= 0f || size.Y <= 0f) return;
        var vp = GetViewport();
        if (vp == null) return;
        var rect = vp.GetVisibleRect();
        var rowGlobal = _row.GlobalPosition;
        float minX = rect.Position.X + 4f;
        float minY = rect.Position.Y + 4f;
        float maxX = rect.End.X - size.X - 4f;
        float maxY = rect.End.Y - size.Y - 4f;
        float x = Mathf.Clamp(rowGlobal.X, minX, Mathf.Max(minX, maxX));
        float y = Mathf.Clamp(rowGlobal.Y, minY, Mathf.Max(minY, maxY));
        if (!Mathf.IsEqualApprox(x, rowGlobal.X) || !Mathf.IsEqualApprox(y, rowGlobal.Y))
            GlobalPosition += new Vector2(x, y) - rowGlobal;
    }

    // 每帧同步能量标签：文本变化才重排；死亡/战斗结束（deck 清空）自动隐藏
    private void SyncEnergyDisplay(bool routeA, Creature? enemy)
    {
        // 血条一清零就隐藏：不能只看 IsDead——那是死亡动画播完才翻的标记，
        // 否则会变成"敌人已经倒了、费用文字还挂在血条上"。
        bool show = routeA && enemy != null && MonsterDeck.IsBuilt(enemy)
                    && !enemy.IsDead && enemy.IsAlive && enemy.CurrentHp > 0;
        if (!show)
        {
            if (_energyLabel != null) _energyLabel.Visible = false;
            return;
        }
        // 防御：标签若已被外部释放（历史遗留场景），丢弃引用走下方重建
        if (_energyLabel != null && !GodotObject.IsInstanceValid(_energyLabel)) _energyLabel = null;
        // 血条引用同样做失效检查（怪物换场景/重进战斗会重建 UI 子树）
        if (_healthBar != null && !GodotObject.IsInstanceValid(_healthBar)) _healthBar = null;
        if (_healthBar == null && _creature != null) _healthBar = FindHealthBar(_creature);

        if (_energyLabel == null)
        {
            // 注意：**不要**设 TopLevel。TopLevel 的节点不继承父级的可见性，
            // 打开地图/卡组/抽牌堆等全屏界面时战斗层被隐藏，这张标签却还会画在最上面
            //（表现为"地图上浮着一个费用文字"）。
            _energyLabel = new Label();
            _energyLabel.AddThemeColorOverride("font_color", new Color(1f, 0.84f, 0.25f));
            _energyLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
            _energyLabel.AddThemeConstantOverride("outline_size", 8);
            _energyLabel.AddThemeFontSizeOverride("font_size", 20);
        }
        // 父节点优先用血条：挂在血条上就不用每帧写全局坐标，也不会被怪物动画甩出抖动
        var parent = (Node?)_healthBar ?? this;
        if (_energyLabel.GetParent() != parent)
        {
            _energyLabel.GetParent()?.RemoveChild(_energyLabel);
            parent.AddChild(_energyLabel);
        }
        _energyLabel.Visible = true;
        string text = MonsterText.Of(MonsterText.EnergyLabel,
            ("Current", MonsterDeck.EnergyOf(enemy).ToString()),
            ("Max", MonsterDeck.MaxEnergy.ToString()));
        if (text != _lastEnergyText)
        {
            _lastEnergyText = text;
            _energyLabel.Text = text;
        }
        var minSize = _energyLabel.GetMinimumSize();
        _energyLabel.Size = minSize;
        // 位置：血条正上方（血条局部坐标：底边贴血条顶、水平居中于血条）；
        // 找不到血条时退回本容器上方居中（同样是父节点局部坐标）
        if (_healthBar != null)
        {
            _energyLabel.Position = new Vector2(
                (_healthBar.Size.X - minSize.X) * 0.5f,
                -minSize.Y);
        }
        else
        {
            float rowWidth = _row?.Size.X ?? 0f;
            _energyLabel.Position = new Vector2((rowWidth - minSize.X) * 0.5f, -minSize.Y - 6f);
        }
    }

    // 按类型递归找第一个 NHealthBar（血条位于 HealthBar 子场景内部，不依赖节点名与 owner）
    private static NHealthBar? FindHealthBar(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is NHealthBar bar) return bar;
            var found = FindHealthBar(child);
            if (found != null) return found;
        }
        return null;
    }

    private void RebuildCards(Creature? enemy)
    {
        ClearCards();
        // 重建 = 怪物换了新的一手：强夺/占位计数作废（废牌池另存，不受影响）
        IntentCardTheft.Invalidate(enemy);
        var player = IntentCardHook.LocalPlayer;
        if (player == null || enemy == null) return;

        // 本手原版卡（打 23 = 3 张攻击卡；强化 = 1 张…）
        var originals = IntentCardMapper.BuildIntentCards(enemy, player);

        // 严格队列抽牌（不洗牌）：队列 = 废牌池（按塞入顺序，队首）→ 本手原版卡（按意图顺序）。
        // 抽「本手卡数」N 张 = 队列最前面的 N 张；废牌在队首 → 塞进去的废牌必定最先被抽到。
        // 抽中几张废牌就顶掉本回合几个动作（每张废牌挤掉等量原版卡）。
        // 与「右键查看意图卡池」面板的前段顺序完全一致：面板前 N 张就是这一手。
        var junkPool = IntentCardTheft.SnapshotJunk(enemy);
        int junkDrawn = Mathf.Min(junkPool.Count, originals.Count);

        for (int i = 0; i < junkDrawn; i++)
            IntentCardTheft.ConsumeJunk(enemy, junkPool[i]); // 抽中的废牌移出池子（没抽中的留到下回合）
        IntentCardTheft.AddBlocked(enemy, junkDrawn);        // 每张 = 本回合少一个动作（复用既有拦截链）

        // 显示顺序 = 抽牌顺序 = 飞卡顺序（废牌在前，拦截点先飞出它们）
        for (int i = 0; i < junkDrawn; i++)
            BuildRealCard(MonsterDisplayCard.CreateJunkCard(player, junkPool[i]));
        for (int i = 0; i < originals.Count - junkDrawn; i++)
            BuildRealCard(originals[i]);

        SettleLayoutNow();
    }

    // ── 路线 A：头顶展示怪物这一手（手牌）──

    // 手牌来源 = MonsterDeck 抽牌堆/弃牌堆抽出的牌。出牌期间不重建：
    // 每张牌由 OnCardPlayed 逐张飞出，避免清掉正在飞的卡。
    private void RebuildFromHand(Creature? enemy)
    {
        ClearCards();
        // 新一手的开始：重置连飞状态。否则上一回合留下的 _flyOutStarted=true +
        // 已过期的超时，会让 _Process 里的兜底逻辑把刚抽上来的手牌当作"未飞完"全部飞掉，
        // 表现为「第一回合能看到卡、第二回合卡消失」。
        _flyOutStarted = false;
        _flyOutDeadline = 0;
        if (enemy == null) return;
        GD.Print($"[SCM-DECK] RebuildFromHand {enemy.Name} n={MonsterDeck.HandOf(enemy).Count}");
        foreach (var model in MonsterDeck.HandOf(enemy))
            BuildRealCard(model);

        SettleLayoutNow();
    }

    // 建完这一手立刻排版 + 视口钳制一次。
    // 不这么做的话，新一手的头一帧还停在"上一帧的行宽 / 容器原点"上（LayoutRow 每帧才跑），
    // 下一帧才被摆成"水平居中、底边贴锚点"，看起来就是卡牌一出现就突兀地跳一下
    //（横向最多半个卡行宽、纵向最多一个卡行高，再叠加 ClampToViewport 的整体位移）。
    private void SettleLayoutNow()
    {
        SyncWrapperSizes();
        LayoutRow();
        ClampToViewport();
    }

    // 抽牌后刷新头顶展示（MonsterDeck.PrepareTurn 调用）
    public void RefreshHand() => RebuildFromHand(_creature?.Entity);

    // 怪物打出一张手牌：头顶对应卡飞出（MonsterDeck.PlayTurn 逐张驱动）
    public void OnCardPlayed(CardModel model)
    {
        var idx = _cards.FindIndex(c => c.Card.Model == model);
        if (idx < 0) return;
        var (wrapper, card) = _cards[idx];
        _cards.RemoveAt(idx);
        FlyOutSingle(wrapper, card, 0f);
    }

    // 回合结束弃掉剩余手牌：头顶剩余卡一并飞出
    public void OnHandDiscarded()
    {
        if (_cards.Count > 0) FlushRemaining();
    }

    // 废牌入池后立刻请求重抽：清空签名，下一次签名比对必然不等 → 重建，
    // 废牌（若被抽中）马上显示在怪物头顶。
    public void RequestRebuild() => _signature = "";

    // ── 意图卡池查看面板 ──

    private void OnCreatureGuiInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
        var enemy = _creature?.Entity;
        if (enemy == null || enemy.IsDead || enemy.IsPlayer) return;
        // 只在玩家回合查看：怪物行动期间牌面正在逐张飞出，查看没有意义
        if (enemy.CombatState?.CurrentSide != CombatSide.Player) return;
        // 路线 A：牌堆在手就有内容可看；未建牌堆的怪退回原版意图要求
        if (!MonsterDeck.IsTracked(enemy) && enemy.Monster?.NextMove == null) return;
        ShowPool(enemy);
    }

    // 展开意图卡池（本手原版卡 + 废牌池，与抽牌同源），面板贴在怪物脚下
    private void ShowPool(Creature enemy)
    {
        var player = IntentCardHook.LocalPlayer;
        if (player == null || _creature == null) return;

        if (_poolPopup == null || !GodotObject.IsInstanceValid(_poolPopup))
        {
            // 网格布局（6 列）：招式多的怪能有十几张卡，横排会冲出屏幕
            _poolPopup = new GridContainer { Name = "IntentCardPoolPopup", Columns = 6 };
            _poolPopup.AddThemeConstantOverride("h_separation", 16);
            _poolPopup.AddThemeConstantOverride("v_separation", 16);
            // 拦截鼠标：防止点击穿透到怪物 Hitbox（否则会反复开关）
            _poolPopup.MouseFilter = Control.MouseFilterEnum.Stop;
            // 抬高绘制层级：即使怪物能力图标未能隐藏，面板也画在它之上，不会相互遮挡
            _poolPopup.ZIndex = 100;
            _creature.AddChild(_poolPopup);
        }
        foreach (Node child in _poolPopup.GetChildren())
        {
            _poolPopup.RemoveChild(child);
            child.QueueFree();
        }

        // 路线 A：展示该怪抽牌堆 → 弃牌堆（不含当前手牌）；
        // 未建牌堆的退回原版（本手 + 其余招式）
        var models = MonsterDeck.IsTracked(enemy)
            ? IntentCardMapper.BuildDeckPreview(enemy)
            : IntentCardMapper.BuildPool(enemy, player);
        foreach (var model in models)
        {
            var ncard = PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
            var wrapper = new Control();
            _poolPopup.AddChild(wrapper);
            wrapper.AddChild(ncard);
            ncard.Model = model;
            ncard.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            ncard.Scale = new Vector2(CardScale, CardScale);
            ncard.PivotOffset = Vector2.Zero;   // 同头顶卡：缩放基准改左上角，布局框与视觉一致
            ncard.Position = ResolveCardSize(ncard) * CardScale * 0.5f;   // 同头顶卡：视觉以原点为中心，挪半个卡身
            ApplyDescriptionSize(ncard);
            var size = ResolveCardSize(ncard);
            if (size.X > 0f && size.Y > 0f) wrapper.CustomMinimumSize = size * CardScale;
        }

        // 顶层 Control 不会自动按内容定尺寸，必须显式赋 Size，否则定位/命中判定都为 0
        var min = _poolPopup.GetCombinedMinimumSize();
        _poolPopup.Size = new Vector2(Mathf.Max(min.X, 1f), Mathf.Max(min.Y, 1f));
        PositionPoolPopup();

        // 面板展开时隐藏怪物能力图标，避免与卡池面板互相遮挡
        SetPowersVisible(false);
    }

    // 怪物能力图标容器（NPowerContainer）：右键面板展开时隐藏，关闭时恢复。
    // 用「按类型递归扫描」而不是按名字查找——PowerContainer 位于 HealthBar 子场景内部，
    // 唯一名(%)与 FindChild(owned:true) 都可能因 owner 不在本场景而漏掉；按 C# 类型找最稳。
    private void SetPowersVisible(bool visible)
    {
        if (_creature == null || !GodotObject.IsInstanceValid(_creature)) return;
        var powers = FindPowers(_creature);
        if (powers == null)
        {
            // 找不到：只在展开时打印一次整棵子树用于定位（恢复时静默）
            if (!visible && !_powerDiagLogged)
            {
                _powerDiagLogged = true;
                GD.PushWarning("[SelectSpecialCardMod] 未找到 NPowerContainer，转储 NCreature 子树：");
                DumpTree(_creature, "");
            }
            return;
        }
        powers.Visible = visible;
    }

    private bool _powerDiagLogged;

    // 按类型递归找第一个 NPowerContainer（不依赖节点名与 owner）
    private static NPowerContainer? FindPowers(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is NPowerContainer pc) return pc;
            var found = FindPowers(child);
            if (found != null) return found;
        }
        return null;
    }

    private static void DumpTree(Node node, string indent)
    {
        string vis = node is CanvasItem ci ? ci.Visible.ToString() : "-";
        GD.Print($"[SCM] {indent}{node.Name} ({node.GetType().Name}) visible={vis}");
        foreach (Node child in node.GetChildren()) DumpTree(child, indent + "  ");
    }

    private void TickPoolPopup()
    {
        if (_poolPopup == null || !GodotObject.IsInstanceValid(_poolPopup)) { _poolPopup = null; return; }
        var enemy = _creature?.Entity;
        if (enemy == null || enemy.IsDead || CombatManager.Instance?.CurrentCombatId == null)
        {
            ClosePool();
            return;
        }
        PositionPoolPopup();

        // 鼠标既不在怪物身上、也不在面板上 → 关闭
        var mouse = _poolPopup.GetGlobalMousePosition();
        bool onEnemy = _creature!.Hitbox != null && _creature.Hitbox.GetGlobalRect().HasPoint(mouse);
        if (!onEnemy && !_poolPopup.GetGlobalRect().HasPoint(mouse)) ClosePool();
    }

    private void PositionPoolPopup()
    {
        if (_poolPopup == null || _creature?.Hitbox == null) return;
        var hitbox = _creature.Hitbox;
        _poolPopup.GlobalPosition = hitbox.GlobalPosition
            + new Vector2((hitbox.Size.X - _poolPopup.Size.X) * 0.5f, hitbox.Size.Y + 8f);
    }

    private void ClosePool()
    {
        if (_poolPopup != null && GodotObject.IsInstanceValid(_poolPopup)) _poolPopup.QueueFree();
        _poolPopup = null;
        // 面板关闭：恢复怪物能力图标显示
        SetPowersVisible(true);
    }

    // 用游戏原版卡场景实例化 NCard 并绑定展示卡模型。
    // 两个关键点：
    // 1. 必须先入树再设 Model —— NCard.Reload 开头是 `if (!IsNodeReady() || Model == null) return`，
    //    实例化后节点还没进树，立即设 Model 会让 Reload 静默跳过；
    // 2. Reload 只刷新卡图/边框等贴图，卡名和描述由 UpdateVisuals 渲染（游戏里由牌堆管理器调用，
    //    我们的悬浮卡没有牌堆驱动，必须手动调一次，否则 TitleLabel 停留在场景占位文本 "Broken card"）。
    private void BuildRealCard(CardModel model)
    {
        if (_row == null) return;
        var ncard = PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>();
        var wrapper = new Control();
        wrapper.AddChild(ncard);
        _row.AddChild(wrapper);
        ncard.Model = model;
        ncard.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
        ncard.Scale = new Vector2(CardScale, CardScale);
        ncard.PivotOffset = Vector2.Zero;
        // 卡面视觉是以"节点原点为中心"画的（实测：视觉宽 108 = 240×0.45，右边缘落在节点原点右侧半张）。
        // 把节点往右下挪半个卡身，视觉的左上角才正好落在布局框的 (0,0)。
        ncard.Position = ResolveCardSize(ncard) * CardScale * 0.5f;
        ApplyDescriptionSize(ncard);
        _cards.Add((wrapper, ncard));
    }

    private void SyncWrapperSizes()
    {
        foreach (var (wrapper, card) in _cards)
        {
            if (!GodotObject.IsInstanceValid(card)) continue;
            var size = ResolveCardSize(card);
            if (size.X > 0f && size.Y > 0f)
            {
                wrapper.CustomMinimumSize = size * CardScale;
            }
        }
    }

    // 手动排版卡行：整行水平居中于容器原点、底边贴在容器原点（也就是锚点正上方）。
    // 每张卡的位置与行宽全由这里算，卡数变化只影响这一行，容器永不移动。
    private void LayoutRow()
    {
        if (_row == null) return;

        var items = new List<(Control Wrapper, Vector2 Size)>();
        float totalWidth = 0f;
        float maxHeight = 0f;
        foreach (Node child in _row.GetChildren())
        {
            if (child is not Control wrapper) continue;
            var size = wrapper.CustomMinimumSize;
            if (size.X <= 0f || size.Y <= 0f) size = ResolveCardSize(wrapper) * CardScale;
            if (size.X <= 0f || size.Y <= 0f) continue;
            items.Add((wrapper, size));
            totalWidth += size.X;
            maxHeight = Mathf.Max(maxHeight, size.Y);
        }
        if (items.Count == 0)
        {
            _row.Size = Vector2.Zero;
            _row.Position = Vector2.Zero;
            return;
        }
        if (items.Count > 1) totalWidth += CardSeparation * (items.Count - 1);

        // 行矩形：水平居中（左边缘在 -行宽/2）、底边贴在原点
        _row.Size = new Vector2(totalWidth, maxHeight);
        _row.Position = new Vector2(-totalWidth * 0.5f, -maxHeight);

        // 行内每张卡依次从左往右排
        float x = 0f;
        foreach (var (wrapper, size) in items)
        {
            wrapper.Size = size;
            wrapper.Position = new Vector2(x, 0f);
            x += size.X + CardSeparation;
        }
    }

    // ── 临时诊断：把头顶卡的实际几何写进 user://SelectSpecialCardMod/layout_debug.txt ──
    // 排查完把 LayoutDebug 改成 false 即可（每秒最多写一次，不影响游戏）。
    private const bool LayoutDebug = true;
    private ulong _nextDebugMsec;

    private void DumpLayout()
    {
        if (!LayoutDebug || _row == null || _cards.Count == 0) return;
        ulong now = Time.GetTicksMsec();
        if (now < _nextDebugMsec) return;
        _nextDebugMsec = now + 1000;
        try
        {
            var (wrapper, card) = _cards[0];
            var visual = FirstSizedChild(card);
            string text = string.Join("\n",
                $"time={now} creature={_creature?.Name}",
                $"viewport={GetViewport()?.GetVisibleRect()}",
                $"hitbox={_creature?.Hitbox?.GetGlobalRect().ToString() ?? "null"}",
                $"intent={_creature?.IntentContainer?.GetGlobalRect().ToString() ?? "null"}",
                $"containerGlobal={GlobalPosition} rowPos={_row.Position} rowSize={_row.Size}",
                $"wrapperMin={wrapper.CustomMinimumSize} cardSize={card.Size} cardCombined={card.GetCombinedMinimumSize()} cardScale={card.Scale} cardLocalPos={card.Position}",
                $"cardGlobal={card.GlobalPosition} cardRect={card.GetGlobalRect()}",
                $"visualPos={visual?.Position.ToString() ?? "null"} visualRect={visual?.GetGlobalRect().ToString() ?? "null"}",
                DescribeForLog(card),
                "");
            DirAccess.MakeDirRecursiveAbsolute("user://SelectSpecialCardMod");
            using var file = Godot.FileAccess.Open("user://SelectSpecialCardMod/layout_debug.txt",
                Godot.FileAccess.ModeFlags.Write);
            file?.StoreString(text);
            // 同时打印到游戏日志（一行），方便直接排查
            GD.Print("[SCM-LAYOUT] " + text.Replace("\n", " | "));
        }
        catch
        {
            // 诊断失败无所谓
        }
    }

    // 卡面里第一个"有实际尺寸"的子节点（就是看得见的卡面内容）
    private static Control? FirstSizedChild(NCard card)
    {
        foreach (Node child in card.GetChildren())
        {
            if (child is not Control control) continue;
            var size = control.Size.X > 1f ? control.Size : control.GetCombinedMinimumSize();
            if (size.X > 1f && size.Y > 1f) return control;
        }
        return null;
    }

    // 诊断用：把这张卡的"招式键 / 算出的效果 / 卡面文字"都打出来，方便比对"显示的"与"实际打出的"
    private static string DescribeForLog(NCard card)
    {
        try
        {
            if (card.Model is not MonsterMoveCard move) return $"cardModel={card.Model?.GetType().Name}";
            string moveId = move.SourceMove?.Id ?? "";
            var observed = MonsterEffectRecord.Get(move.MonsterKind, moveId);
            var fromTable = MonsterEffectTable.Lookup(move.MonsterKind, moveId, IntentCardMapper.FindSource(move));
            var fx = MonsterMoveEffects.Merge(observed, fromTable);
            string powers = string.Join("+", fx.PlayerPowers.ConvertAll(p => $"{p.Power}x{p.Amount}"));
            string self = string.Join("+", fx.SelfPowers.ConvertAll(p => $"{p.Power}x{p.Amount}"));
            return $"move={move.MonsterKind}|{moveId} observed={(observed == null ? "-" : "yes")}"
                   + $" fx[block={fx.Block} heal={fx.Heal} self={self} player={powers} seal={fx.SealJunk}]"
                   + $" text=\"{move.LiveText?.Invoke()}\"";
        }
        catch (System.Exception e)
        {
            return "describe-error=" + e.Message;
        }
    }

    // 卡面真实视觉尺寸（游戏场景 scenes/cards/card.tscn 里的 Frame）：300×422，视觉以节点原点为中心。
    // 之前这里用的是 240×336 的估算值，比真实卡面小一圈 → 卡行按小尺寸排版，
    // 卡与卡之间实际会互相压住、整行宽度也偏窄（现在改成读真实尺寸）。
    private static readonly Vector2 FallbackCardVisualSize = new(300f, 422f);

    // 卡面尺寸：优先读卡场景里 Frame 的实际尺寸；读不到再退回实际 Size / 布局最小尺寸 / 常量。
    // 传进来的可能是 NCard，也可能是包着它的 wrapper，所以两种都试。
    private static Vector2 ResolveCardSize(Control card)
    {
        if (FrameSizeOf(card) is { } frameSize) return frameSize;

        var size = card.Size;
        if (size.X < 1f || size.Y < 1f) size = card.GetCombinedMinimumSize();
        if (size.X < 1f || size.Y < 1f) size = FallbackCardVisualSize;
        return size;
    }

    // 从 NCard（或包着 NCard 的 wrapper）里读出卡面 Frame 的尺寸
    private static Vector2? FrameSizeOf(Node node)
    {
        if (TryFrame(node) is { } direct) return direct;
        foreach (Node child in node.GetChildren())
        {
            if (TryFrame(child) is { } fromChild) return fromChild;
        }
        return null;

        static Vector2? TryFrame(Node node)
            => node is NCard card
               && card.GetNodeOrNull<Control>("CardContainer/Frame") is { } frame
               && frame.Size.X > 1f && frame.Size.Y > 1f
                ? frame.Size
                : null;
    }

    // 怪物意图卡的描述字号。卡面描述是「自动适配填满描述框」的（MegaRichTextLabel.AdjustFontSize），
    // 所以扩大/缩小描述框就等于放大/缩小字号，卡的整体尺寸与占位不变。
    // 想调字号只改这两个常量：
    //   DescriptionExpandY —— 描述框上下各扩多少像素：正数=更大，0=卡面原始大小，负数=比原始更小
    //   DescriptionMaxFontSize —— 字号上限：0 = 不改游戏默认值
    private const float DescriptionExpandY = 20f;
    private const int DescriptionMaxFontSize = 36;

    private static void ApplyDescriptionSize(NCard ncard)
    {
        var desc = ncard.GetNodeOrNull<Control>("%DescriptionLabel");
        if (desc == null) return;
        if (DescriptionExpandY != 0f)
        {
            desc.OffsetTop -= DescriptionExpandY;
            desc.OffsetBottom += DescriptionExpandY;
        }
        if (desc is RichTextLabel rtl) rtl.ClipContents = false;
        if (DescriptionMaxFontSize > 0 && desc is MegaRichTextLabel mega)
            mega.MaxFontSize = DescriptionMaxFontSize;   // 直接给定上限（绝对值），效果可预期
    }

    // 怪物开始行动（NCreature.PerformIntent）：标记连飞开始（启动失联超时兜底）。
    // 卡牌本体不在这里飞——每张卡由对应效果的执行触发：攻击结算前（MonsterAttackSplitPatch）
    // 和强化/格挡/治疗效果入口（IntentEffectFlyPatch）都会调 IntentCardFlySync.NotifyHit，
    // 实现"怪物每做一个动作，飞一张对应卡"。
    public void BeginFlyOut()
    {
        if (_flyOutStarted) return;
        _flyOutStarted = true;
        _flyOutDeadline = Time.GetTicksMsec() + 6000; // 效果信号失联超时
    }

    // 飞出下一张未飞出的卡（对应怪物的一次攻击结算）。
    // 防御：飞卡信号本身就是"行动已开始"的铁证——即使行动不经过 PerformIntent
    // （BeginFlyOut 未被调用），也在这里启动连飞标记，否则连飞冻结重建会失效，
    // 攻击落地数值变化 → 中途重建 → 飞卡次数与攻击次数错位。
    private void FlyNext()
    {
        BeginFlyOut();
        if (_cards.Count == 0) return;
        var (wrapper, card) = _cards[0];
        _cards.RemoveAt(0);
        FlyOutSingle(wrapper, card, 0f);
    }

    // 「强夺」：怪物头顶当前可被抢走的意图卡（供强夺卡弹选牌界面）。
    // 塞状态/诅咒卡插入的占位废牌不可被抢（它是废牌，抢走没有意义还会打乱计数）
    public List<CardModel> StealableModels
        => _cards.Select(c => c.Card.Model)
            .Where(m => m is not MonsterDisplayCard { IsPlaceholder: true })
            .Where(m => m != null).Cast<CardModel>().ToList();

    // 抢走指定意图卡：从展示队列移除并飞向手牌区（卡牌易主的演出）。
    // 计数在 IntentCardTheft 里按类型记录，怪物行动时对应动作不发生。
    public bool StealCard(CardModel model)
    {
        var idx = _cards.FindIndex(c => c.Card.Model == model);
        if (idx < 0) return false;
        var (wrapper, card) = _cards[idx];
        _cards.RemoveAt(idx);
        // 路线 A：同步从怪物手牌移除——这一手不会打出它（真抢）
        var enemy = _creature?.Entity;
        if (enemy != null)
        {
            MonsterDeck.RemoveFromHand(enemy, model);
            // 让怪物这一手空过（被抢走的招式本阶段不再出现，换阶段重建时恢复）
            MonsterDeck.RegisterSteal(enemy);
        }
        FlyToHand(card, wrapper);
        return true;
    }

    // 「消耗」指定意图卡：从展示队列移除，并播**原版「消耗」特效**（卡面被侵蚀消散 + 原版音效）。
    // 与 StealCard 的唯一区别是演出——不强夺、不飞向手牌区。
    public bool ConsumeCard(CardModel model)
    {
        var idx = _cards.FindIndex(c => c.Card.Model == model);
        if (idx < 0) return false;
        var (wrapper, card) = _cards[idx];
        _cards.RemoveAt(idx);

        // 路线 A：同步从怪物手牌移除——这一手不会打出它（与强夺同一套）
        var enemy = _creature?.Entity;
        if (enemy != null)
        {
            MonsterDeck.RemoveFromHand(enemy, model);
            MonsterDeck.RegisterSteal(enemy);
        }

        PlayExhaustVfx(card, wrapper);
        return true;
    }

    // 原版「消耗」特效：把卡面交给 NCardExhaustVfx 播侵蚀消散（与原版 CardPileCmd 进消耗堆时同一套）。
    // NCardExhaustVfx.Create 会把卡面从原父节点摘走，所以外壳可以立刻释放。
    private void PlayExhaustVfx(NCard card, Control wrapper)
    {
        if (!GodotObject.IsInstanceValid(card))
        {
            wrapper.QueueFree();
            return;
        }

        var vfx = NCardExhaustVfx.Create(card);
        if (vfx == null)
        {
            wrapper.QueueFree();
            return;
        }

        _rebuildSuppressUntil = Time.GetTicksMsec() + 600;
        NCombatRoom.Instance?.Ui.AddChildSafely(vfx);
        NDebugAudioManager.Instance?.Play("card_exhaust.mp3");
        TaskHelper.RunSafely(vfx.PlayAnimation());
        wrapper.QueueFree();
    }

    // 飞向玩家手牌区（屏幕底部中央）：抢卡演出的终点
    private void FlyToHand(NCard card, Control wrapper)
    {
        if (!GodotObject.IsInstanceValid(card))
        {
            wrapper.QueueFree();
            return;
        }
        wrapper.CustomMinimumSize = Vector2.Zero;
        DetachForFlight(card);
        _rebuildSuppressUntil = Time.GetTicksMsec() + 600;
        var vpSize = GetViewport().GetVisibleRect().Size;
        // 卡面视觉以节点原点为中心 → 原点摆到目标点，视觉就正好居中在那里
        var target = new Vector2(vpSize.X * 0.5f, vpSize.Y * 0.88f);
        var tween = card.CreateTween();
        tween.TweenProperty(card, "global_position", target, 0.4f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        tween.Chain().TweenCallback(Callable.From(() => wrapper.QueueFree()));
    }

    // 兜底：把剩余未飞的卡全部快速飞出（意图清空 / 攻击信号失联超时）
    public void FlushRemaining()
    {
        float delay = 0f;
        foreach (var (wrapper, card) in _cards)
        {
            FlyOutSingle(wrapper, card, delay);
            delay += 0.2f;
        }
        _cards.Clear();
        _flyOutStarted = false;
    }

    private void FlyOutSingle(Control wrapper, NCard card, float delay)
    {
        if (!GodotObject.IsInstanceValid(card))
        {
            wrapper.QueueFree();
            return;
        }
        wrapper.CustomMinimumSize = Vector2.Zero;
        DetachForFlight(card);
        var vpSize = GetViewport().GetVisibleRect().Size;
        // NCard 场景根 Size 恒为 0（同 SyncWrapperSizes 的坑），必须用兜底链尺寸
        // 放大同样绕原点（视觉中心），所以原点摆到目标点即可
        var target = new Vector2(vpSize.X * 0.5f, vpSize.Y * 0.52f);
        var tween = card.CreateTween();
        tween.TweenInterval(delay);
        tween.TweenProperty(card, "global_position", target, 0.45f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(card, "scale", new Vector2(CardScale * 1.35f, CardScale * 1.35f), 0.45f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.Chain().TweenProperty(card, "modulate:a", 0.0f, 0.2f).SetDelay(0.3f);
        tween.Chain().TweenCallback(Callable.From(() => wrapper.QueueFree()));
    }

    // 起飞前把卡从"跟着怪物脑袋动的容器"里摘出来。
    // 卡本体设成 TopLevel 后不再吃父级变换：容器追踪怪物、卡行重排、视口钳制都不会再拽它。
    // 不这么做的话，飞行动画一结束（tween 停了）卡就重新变成容器的子节点，
    // 怪物的出手动作（往斜上方扑）会把停在屏幕中央的卡一起带跑，看起来就是"抽搐一下往斜上跳"。
    // 注意顺序：先记下当前全局坐标，再开 TopLevel，然后写回去——保证从原位置起飞、不闪一下。
    private static void DetachForFlight(NCard card)
    {
        if (!GodotObject.IsInstanceValid(card)) return;
        var takeoff = card.GlobalPosition;
        card.TopLevel = true;
        card.GlobalPosition = takeoff;
    }

    private void ClearCards()
    {
        _cards.Clear();
        _flyOutStarted = false;
        // 只清空卡行里的卡；容器位置不重置——卡数变化不应让容器移动（能量标签挂在容器上，不受影响）
        if (_row == null) return;
        foreach (Node child in _row.GetChildren())
        {
            _row.RemoveChild(child);
            child.QueueFree();
        }
    }
}
