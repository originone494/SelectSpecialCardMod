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
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Hook;

// 「时机」：实现了本接口的卡，在满足条件时可通过右键对应的牌堆按钮发动。
public interface IShiJiCard
{
    // 这张卡的时机效果可以从哪些牌堆发动
    // （如三极天龙/双叉天龙 = 弃牌堆；复苏的天空神 = 消耗堆；可声明多个）
    IEnumerable<PileType> ShiJiPiles { get; }

    // 发动条件（次数限制、攻击牌数等），由卡自己判断
    bool CanActivateShiJi(Player player);

    // 发动时机效果
    Task ActivateShiJi(PlayerChoiceContext ctx, Player player);
}

// 玩家右键牌堆按钮（抽牌堆/弃牌堆/消耗堆）→ 弹出选牌界面，列出该牌堆中
// 所有「带时机标签 + 能从该堆发动 + 满足条件」的卡，选 1 张发动。
//
// 重要：右键回调不在游戏的行动管线里，直接跑选牌/弃牌会导致动画与管线脱节
//（卡悬在屏幕中央、之后又弹回手牌）。所以这里照官方 DrawConsoleCmd 的做法，
// 用 HookPlayerChoiceContext + GenericHookGameAction 把整段流程包成 GameAction 入队执行。
public static class ShiJiHook
{
    private static Player? _turnPlayer;
    private static readonly HashSet<Control> ConnectedButtons = new();

    private static LocString ShiJiPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_SHI_JI");

    // 当前回合玩家（供牌堆高亮提示节点逐帧读取）
    public static Player? CurrentTurnPlayer => _turnPlayer;

    // 「时机」发动条件：玩家自己的回合 + 该牌堆中存在可发动的时机卡。
    // 各卡的具体条件（如"攻击牌打出3次以上"、"自己位于哪个牌堆"）由卡自己的
    // CanActivateShiJi 与 ShiJiPiles 判断，保证提示与实际能否发动一致。
    public static bool CanActivateAnyShiJi(Player player, PileType pile)
    {
        if (CombatManager.Instance == null || !CombatManager.Instance.IsInProgress) return false;
        if (player.Creature.CombatState?.CurrentSide != CombatSide.Player) return false;
        var cardPile = pile.GetPile(player);
        if (cardPile == null) return false;
        return cardPile.Cards.Any(c => c.Tags.Contains(SelectTags.ShiJi)
                                       && c.Owner == player
                                       && c is IShiJiCard s
                                       && s.ShiJiPiles.Contains(pile)
                                       && s.CanActivateShiJi(player));
    }

    // 每次玩家回合开始由 ShiJiTracker 调用：刷新当前玩家 + 确保三个牌堆按钮都已挂右键
    public static void EnsureConnected(Player player)
    {
        _turnPlayer = player;
        var ui = NCombatRoom.Instance?.Ui;
        if (ui == null) return;
        ConnectPileButton(ui.DrawPile, PileType.Draw);
        ConnectPileButton(ui.DiscardPile, PileType.Discard);
        ConnectPileButton(ui.ExhaustPile, PileType.Exhaust);
    }

    private static void ConnectPileButton(Control button, PileType pile)
    {
        if (button == null || !ConnectedButtons.Add(button)) return;
        button.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(ev => OnGuiInput(pile, ev)));
        // 「时机」可发动提示：在按钮下挂一个逐帧判定节点，条件满足时按钮呼吸泛光
        button.AddChild(new ShiJiHighlightNode(pile));
    }

    private static void OnGuiInput(PileType pile, InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
        if (_turnPlayer == null) return;

        // 同步预检（不满足就不入队，避免空跑一次行动）
        if (!CanActivateAnyShiJi(_turnPlayer, pile)) return;

        _ = EnqueueShiJiActionAsync(_turnPlayer, pile);
    }

    // 把时机发动包装成 GameAction 入队：只把 Task 交给 HookPlayerChoiceContext 即可。
    // 流程里第一次弹玩家选择时会调 SignalPlayerChoiceBegun，游戏会自己
    // GenerateHookAction → SetChoiceContext → RequestEnqueueHookAction。
    // （早先手动 Generate+SetChoiceContext+RequestEnqueue 是错的：那时 Task 还没赋值，
    //   action 立刻执行 → ExecuteAction 里 await ChoiceContext.Task 撞 null → NullReferenceException。）
    private static async Task EnqueueShiJiActionAsync(Player player, PileType pile)
    {
        if (LocalContext.NetId is not { } netId || netId != player.NetId) return;
        var ctx = new HookPlayerChoiceContext(player, netId, GameActionType.Combat);
        await ctx.AssignTaskAndWaitForPauseOrCompletion(ShiJiFlowAsync(ctx, player, pile));
    }

    // 入队后的实际流程：选牌界面（强制手动确认）→ 发动选中卡的时机效果
    private static async Task ShiJiFlowAsync(PlayerChoiceContext ctx, Player player, PileType pile)
    {
        var cardPile = pile.GetPile(player);
        if (cardPile == null) return;

        var eligible = cardPile.Cards
            .Where(c => c.Tags.Contains(SelectTags.ShiJi)
                        && c.Owner == player
                        && c is IShiJiCard s
                        && s.ShiJiPiles.Contains(pile)
                        && s.CanActivateShiJi(player))
            .ToList();
        if (eligible.Count == 0) return;

        // RequireManualConfirmation = true：必须由玩家手动确认；选择范围 (0,1)——什么都不选即放弃发动
        var picked = (await CardSelectCmd.FromCombatPile(
            ctx,
            cardPile,
            player,
            new CardSelectorPrefs(ShiJiPrompt, 0, 1) { RequireManualConfirmation = true },
            filter: eligible.Contains)).FirstOrDefault();

        if (picked is not IShiJiCard) return;
        await ActivateShiJiAsync(ctx, player, picked);
    }

    internal static async Task ActivateShiJiAsync(PlayerChoiceContext ctx, Player player,
        MegaCrit.Sts2.Core.Models.CardModel card)
    {
        if (card is not IShiJiCard shiJi) return;
        try
        {
            await shiJi.ActivateShiJi(ctx, player);
        }
        finally
        {
            // 外层 HookPlayerChoiceContext 只绑定玩家，不会通知被选中的时机卡。
            // 正常返回、取消和异常都结束该卡的 UI 执行生命周期。
            card.InvokeExecutionFinished();
        }
    }
}
