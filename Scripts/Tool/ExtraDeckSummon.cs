using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using STS2RitsuLib.CardPiles;
using STS2RitsuLib.CardPiles.Nodes;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「额外堆手动召唤」：额外怪兽实现本接口，自己声明召唤条件与支付方式。
// 召回流只依赖这个接口 —— 以后新增额外怪兽只要实现它，流程代码一行都不用改（复用 + 扩展）。
public interface IExtraDeckSummonCard
{
    /// <summary>召唤条件是否满足（决定额外堆是否高亮、能不能出现在右键列表里）。</summary>
    bool CanSummonFromExtraDeck(Player player);

    /// <summary>支付召唤条件（由卡自己选择/消耗）。返回是否支付成功；失败则整次召唤不结算。</summary>
    Task<bool> PaySummonCost(PlayerChoiceContext ctx, Player player);
}

// 右键额外堆 → 列出额外堆里"当前满足召唤条件"的怪兽 → 选1张 → 支付召唤条件 → 加入手牌。
// 结构与 ShiJiHook 一致：右键回调不在行动管线里，必须包成 GameAction 入队执行；
// 高亮沿用「时机」那套按钮呼吸泛光（ShiJiHighlightNode 同款做法）。
public static class ExtraDeckSummonHook
{
    private static Player? _turnPlayer;
    private static readonly HashSet<Control> ConnectedButtons = [];

    private static LocString SummonPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_EXTRA_DECK_SUMMON");

    public static Player? CurrentTurnPlayer => _turnPlayer;

    public static void Register()
    {
        RitsuLibFramework.SubscribeLifecycle<PlayerTurnStartedEvent>(evt =>
        {
            _turnPlayer = evt.Player;
            EnsureConnected();
        });
    }

    /// <summary>额外堆里当前可以召唤的怪兽（主人是自己 + 满足召唤条件）。</summary>
    public static List<CardModel> SummonableIn(Player player)
    {
        var pile = Entry.ExtraPile.GetPile(player);
        if (pile == null) return [];
        return pile.Cards
            .Where(c => c.Owner == player
                        && c is IExtraDeckSummonCard s
                        && s.CanSummonFromExtraDeck(player))
            .ToList();
    }

    /// <summary>现在能不能召唤（额外堆高亮的判据）。</summary>
    public static bool CanSummonAny(Player player)
    {
        if (CombatManager.Instance == null || !CombatManager.Instance.IsInProgress) return false;
        if (player.Creature.CombatState?.CurrentSide != CombatSide.Player) return false;
        return SummonableIn(player).Count > 0;
    }

    // 每次玩家回合开始：找到额外堆的按钮并挂上右键 + 高亮节点（按钮晚于事件入树时会漏，下一回合会补）
    private static void EnsureConnected()
    {
        var ui = NCombatRoom.Instance?.Ui;
        if (ui == null) return;
        ConnectRecursive(ui);
    }

    private static void ConnectRecursive(Node node)
    {
        if (node is NModCardPileButton button && button.Definition?.PileType == Entry.ExtraPile)
        {
            if (ConnectedButtons.Add(button))
            {
                // 用一层"最上层 + 透传鼠标(Pass)"的输入层来接右键：
                // 牌堆按钮内部还有子节点，右键事件常常停在子节点上，直接连按钮的 gui_input 收不到；
                // Pass 会把事件继续交给父节点，所以按钮自己的左键（打开牌堆）不受影响。
                var catcher = new Control
                {
                    Name = "ExtraDeckSummonInput",
                    MouseFilter = Control.MouseFilterEnum.Pass,
                };
                button.AddChild(catcher);
                catcher.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                catcher.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(OnGuiInput));
                button.AddChild(new ExtraDeckHighlightNode());
            }
            return;
        }
        foreach (Node child in node.GetChildren()) ConnectRecursive(child);
    }

    private static void OnGuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
        Entry.Logger.Info($"[ExtraDeckSummon] 收到额外堆右键：turnPlayer={_turnPlayer?.NetId.ToString() ?? "null"}"
                          + $" 可召唤={(_turnPlayer is { } p ? SummonableIn(p).Count : -1)}");
        if (_turnPlayer is not { } player) return;
        if (SummonableIn(player).Count == 0) return;
        _ = EnqueueSummonAsync(player);
    }

    // 把整段流程包成 GameAction 入队：只把 Task 交给 HookPlayerChoiceContext 即可。
    // 流程里第一次弹玩家选择时会调 SignalPlayerChoiceBegun，游戏会自己
    // GenerateHookAction → SetChoiceContext → RequestEnqueueHookAction。
    // （早先手动 Generate+SetChoiceContext+RequestEnqueue 是错的：那时 Task 还没赋值，
    //   action 立刻执行 → ExecuteAction 里 await ChoiceContext.Task 撞 null → NullReferenceException。）
    private static async Task EnqueueSummonAsync(Player player)
    {
        var ctx = new HookPlayerChoiceContext(player, LocalContext.NetId ?? 0, GameActionType.Combat);
        await ctx.AssignTaskAndWaitForPauseOrCompletion(SummonFlowAsync(ctx, player));
    }

    private static async Task SummonFlowAsync(PlayerChoiceContext ctx, Player player)
    {
        try
        {
            var eligible = SummonableIn(player);
            if (eligible.Count == 0) return;

            // 用"卡牌列表"选卡：额外堆是跨战斗的 mod 牌堆，原版 FromCombatPile 只服务于战斗牌堆。
            // 选择范围 (0,1)：什么都不选即放弃。
            var picked = (await CardSelectCmd.FromSimpleGrid(
                ctx,
                eligible,
                player,
                new CardSelectorPrefs(SummonPrompt, 0, 1) { RequireManualConfirmation = true })).FirstOrDefault();
            if (picked is not IExtraDeckSummonCard summon) return;
            // 选完再判一次：期间手牌可能已经变了
            if (!summon.CanSummonFromExtraDeck(player)) return;

            // 条件没支付成功（没选够/取消）→ 什么都不发生：不消耗额外堆、不加手牌
            if (!await summon.PaySummonCost(ctx, player)) return;

            // 额外堆那张移出（战斗中消耗，战斗结束由 ExtraDeckSeries 补齐）
            await ExtraDeckSeries.Consume(player, picked);
            var template = ModelDb.AllCards.FirstOrDefault(c => c.GetType() == picked.GetType());
            if (template == null) return;
            var copy = player.Creature.CombatState!.CreateCard(template, player);
            await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Hand, player);
            Entry.Logger.Info($"[ExtraDeckSummon] 召唤完成：{picked.Title}");
        }
        catch (System.Exception e)
        {
            Entry.Logger.Error($"[ExtraDeckSummon] 召唤流程异常：{e}");
        }
    }
}

// 额外堆按钮的高亮：满足召唤条件时按钮金色呼吸泛光（与「时机」的 ShiJiHighlightNode 同款）
public partial class ExtraDeckHighlightNode : Node
{
    private static readonly Color GlowColor = new(1f, 0.85f, 0.35f);

    private Control? _button;
    private Tween? _tween;
    private bool _highlighting;

    public override void _Ready()
    {
        _button = GetParent() as Control;
    }

    public override void _Process(double delta)
    {
        if (_button == null || !GodotObject.IsInstanceValid(_button)) return;

        bool shouldGlow = ExtraDeckSummonHook.CurrentTurnPlayer is { } player
                          && ExtraDeckSummonHook.CanSummonAny(player);
        if (shouldGlow == _highlighting) return;

        _highlighting = shouldGlow;
        if (shouldGlow) StartGlow();
        else StopGlow();
    }

    private void StartGlow()
    {
        _tween?.Kill();
        _tween = _button!.CreateTween().SetLoops();
        _tween.TweenProperty(_button, "modulate", GlowColor, 0.7)
              .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        _tween.TweenProperty(_button, "modulate", Colors.White, 0.7)
              .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
    }

    private void StopGlow()
    {
        _tween?.Kill();
        _tween = null;
        if (_button != null && GodotObject.IsInstanceValid(_button)) _button.Modulate = Colors.White;
    }

    public override void _ExitTree()
    {
        _tween?.Kill();
        _tween = null;
    }
}
