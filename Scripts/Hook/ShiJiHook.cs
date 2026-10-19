using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
using STS2RitsuLib.Networking.ManagedActions;
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
// 右键只发送托管行动，由所有客户端在原版行动队列中执行同一流程。
// GenericHookGameAction 只适用于各端已经进入同一个钩子的情况，不能从本机 UI 单独启动。
public static class ShiJiHook
{
    private static Player? _turnPlayer;
    private static (CombatId? Combat, int Turn)? _pendingRequest;
    private static bool _registered;
    private static readonly HashSet<Control> ConnectedButtons = new();

    public sealed record ShiJiRequest(PileType Pile, int Turn, int Version = 1);
    private static readonly RitsuLibManagedNetActionDescriptor<ShiJiRequest> ShiJiAction = new(
        ModuleId: Entry.ModId, ActionKey: "shi_ji_v1",
        Serialize: message => JsonSerializer.SerializeToUtf8Bytes(message),
        Deserialize: bytes => JsonSerializer.Deserialize<ShiJiRequest>(bytes)
            ?? throw new JsonException("Missing timing request."),
        Execute: async context =>
        {
            var pending = PerTurnOnce.CurrentKey(context.Player);
            try { await ExecuteRequest(context.PlayerChoiceContext, context.Player, context.Message); }
            finally
            {
                if (LocalContext.IsMe(context.Player) && _pendingRequest == pending
                    && pending.Turn == context.Message.Turn) _pendingRequest = null;
            }
        },
        ActionType: GameActionType.CombatPlayPhaseOnly);

    private static LocString ShiJiPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_SHI_JI");

    // 只保存本机玩家，供本机牌堆按钮和高亮使用；规则结算使用同步行动的拥有者。
    public static Player? CurrentTurnPlayer => _turnPlayer;

    public static void Register()
    {
        if (_registered) return;
        RitsuLibManagedNetActions.Register(ShiJiAction);
        CombatManager.Instance.CombatEnded += _ => { _turnPlayer = null; _pendingRequest = null; };
        _registered = true;
    }

    private static bool IsSupportedPile(PileType pile)
        => pile == PileType.Draw || pile == PileType.Discard || pile == PileType.Exhaust;

    // 「时机」发动条件：玩家自己的回合 + 该牌堆中存在可发动的时机卡。
    // 各卡的具体条件（如"攻击牌打出3次以上"、"自己位于哪个牌堆"）由卡自己的
    // CanActivateShiJi 与 ShiJiPiles 判断，保证提示与实际能否发动一致。
    public static bool CanActivateAnyShiJi(Player player, PileType pile)
    {
        if (!IsSupportedPile(pile) || CombatManager.Instance == null || !CombatManager.Instance.IsInProgress
            || CombatManager.Instance.IsOverOrEnding) return false;
        if (CombatManager.Instance.PlayerActionsDisabled || CombatManager.Instance.IsPlayerReadyToEndTurn(player)
            || !player.Creature.IsAlive) return false;
        if (player.Creature.CombatState?.CurrentSide != CombatSide.Player
            || !CombatManager.Instance.IsPartOfPlayerTurn(player)) return false;
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
        if (!LocalContext.IsMe(player)) return;
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
        button.TreeExiting += () => ConnectedButtons.Remove(button);
        button.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(ev => OnGuiInput(pile, ev)));
        // 「时机」可发动提示：在按钮下挂一个逐帧判定节点，条件满足时按钮呼吸泛光
        button.AddChild(new ShiJiHighlightNode(pile));
    }

    private static void OnGuiInput(PileType pile, InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
        if (_turnPlayer is { } player) Request(player, pile);
    }

    internal static bool Request(Player player, PileType pile)
    {
        var pending = PerTurnOnce.CurrentKey(player);
        if (!_registered || !LocalContext.IsMe(player) || _pendingRequest == pending
            || !CanActivateAnyShiJi(player, pile)) return false;
        _pendingRequest = pending;
        try
        {
            if (RitsuLibManagedNetActions.Request(RunManager.Instance, ShiJiAction,
                new ShiJiRequest(pile, pending.Turn), player.NetId)) return true;
            _pendingRequest = null;
            return false;
        }
        catch { _pendingRequest = null; throw; }
    }

    internal static async Task ExecuteRequest(PlayerChoiceContext ctx, Player player, ShiJiRequest request)
    {
        // UI 的本机玩家和候选不能作为远端结算依据；旧回合请求及未知协议直接拒绝。
        if (request.Version != 1 || request.Turn != player.PlayerCombatState?.TurnNumber
            || !CanActivateAnyShiJi(player, request.Pile)) return;
        await ResolveShiJiAsync(ctx, player, request.Pile);
    }

    private static async Task ResolveShiJiAsync(PlayerChoiceContext ctx, Player player, PileType pile)
    {
        var combat = player.Creature.CombatState;
        if (combat == null || CombatManager.Instance.IsOverOrEnding || !player.Creature.IsAlive) return;
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

        if (picked is not IShiJiCard shiJi || !eligible.Contains(picked)
            || picked.Owner != player || picked.Pile != cardPile || picked.HasBeenRemovedFromState
            || player.Creature.CombatState != combat || CombatManager.Instance.IsOverOrEnding
            || combat.CurrentSide != CombatSide.Player || !player.Creature.IsAlive
            || CombatManager.Instance.IsPlayerReadyToEndTurn(player)
            || !CombatManager.Instance.IsPartOfPlayerTurn(player)
            || !shiJi.ShiJiPiles.Contains(pile) || !shiJi.CanActivateShiJi(player)) return;
        await ActivateShiJiAsync(ctx, player, picked);
    }

    internal static async Task ActivateShiJiAsync(PlayerChoiceContext ctx, Player player,
        MegaCrit.Sts2.Core.Models.CardModel card)
    {
        if (card is not IShiJiCard shiJi) return;
        try
        {
            await SelectSpecialCardMod.Scripts.Rules.CardEffectQueue.Run(() => shiJi.ActivateShiJi(ctx, player));
        }
        finally
        {
            // 托管行动不以被选中的时机卡为来源，不会替它通知执行结束。
            // 正常返回、取消和异常都结束该卡的 UI 执行生命周期。
            card.InvokeExecutionFinished();
        }
    }
}
