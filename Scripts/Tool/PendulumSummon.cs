using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Cards.Special;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Rules;
using STS2RitsuLib.Networking.ManagedActions;
using GameHook = MegaCrit.Sts2.Core.Hooks.Hook;

namespace SelectSpecialCardMod.Scripts.Tool;

public static class PendulumSummon
{
    private const string EffectKey = "PendulumSummon:OncePerCombat";
    private static bool _registered;
    private static bool _pendingRequest;
    public sealed record SummonRequest(int Version = 1);
    private static readonly RitsuLibManagedNetActionDescriptor<SummonRequest> SummonAction = new(
        ModuleId: Entry.ModId, ActionKey: "pendulum_summon_v1",
        Serialize: m => JsonSerializer.SerializeToUtf8Bytes(m),
        Deserialize: b => JsonSerializer.Deserialize<SummonRequest>(b)
            ?? throw new JsonException("Missing pendulum summon request."),
        Execute: async ctx =>
        {
            try
            {
                if (ctx.Message.Version == 1) await Execute(ctx.PlayerChoiceContext, ctx.Player);
            }
            finally { if (LocalContext.IsMe(ctx.Player)) _pendingRequest = false; }
        },
        ActionType: GameActionType.Combat);

    public static void Register()
    {
        if (_registered) return;
        RitsuLibManagedNetActions.Register(SummonAction);
        CombatManager.Instance.CombatEnded += _ => _pendingRequest = false;
        _registered = true;
    }

    public static bool IsUsed(Player player)
        => PlayerEffectState.For(player).IsCommitted(EffectKey, PlayerEffectState.Combat());

    public static bool IsBusy(Player player)
        => (LocalContext.IsMe(player) && _pendingRequest)
           || PlayerEffectState.For(player).IsUnavailable(EffectKey, PlayerEffectState.Combat());

    public static bool TryGetPair(Player player, out YiXiangMingVoiceCard? left,
        out YiXiangMingVoiceCard? right)
    {
        left = right = null;
        if (player.Creature.CombatState == null || CombatManager.Instance.IsOverOrEnding) return false;
        var hand = PileType.Hand.GetPile(player).Cards;
        if (hand.Count < 2) return false;
        left = hand[0] as YiXiangMingVoiceCard;
        right = hand[^1] as YiXiangMingVoiceCard;
        return left != null && right != null && left != right
            && left.Owner == player && right.Owner == player
            && left.LockedPendulumSide == PendulumEdgeSide.Left
            && right.LockedPendulumSide == PendulumEdgeSide.Right
            && left.CanApplyPendulumEffect && right.CanApplyPendulumEffect
            && left.PendulumScale >= 0 && right.PendulumScale >= 0;
    }

    public static bool CanOperate(Player player)
        => CombatManager.Instance.IsInProgress && !CombatManager.Instance.IsOverOrEnding
           && !CombatManager.Instance.PlayerActionsDisabled && player.Creature.IsAlive
           && player.PlayerCombatState != null && player.Creature.CombatState?.CurrentSide == CombatSide.Player
           && !CombatManager.Instance.IsPlayerReadyToEndTurn(player);

    public static bool IsCandidate(CardModel card, Player player, int leftScale, int rightScale)
        => IsCandidateFrom(card, player, leftScale, rightScale, fromExtra: false);

    public static bool IsExtraCandidate(CardModel card, Player player, int leftScale, int rightScale)
        => IsCandidateFrom(card, player, leftScale, rightScale, fromExtra: true);

    private static bool IsCandidateFrom(CardModel card, Player player, int leftScale, int rightScale, bool fromExtra)
    {
        if (card.Owner != player || card.Pile?.Type != (fromExtra ? Entry.ExtraPile : PileType.Hand)
            || card.Type != CardType.Attack || card.HasBeenRemovedFromState
            || YiXiangMingHandPlacement.IsPlayLocked(card))
            return false;
        if (fromExtra && !card.Tags.Contains(SelectTags.LingBaiGuaiShou)) return false;
        // 原版攻击牌无须补星级或计算费用；模组怪兽使用真实星级，手牌阶段排除额外怪兽。
        bool vanilla = card.GetType().Assembly == typeof(CardModel).Assembly;
        if (!vanilla && (card is not SelectSpecialBaseCard { StarLevel: > 0 } monster
            || (!fromExtra && card.Tags.Contains(SelectTags.ExtraMonster))
            || monster.StarLevel <= System.Math.Min(leftScale, rightScale)
            || monster.StarLevel >= System.Math.Max(leftScale, rightScale))) return false;

        return CanPlayFree(card, player);
    }

    private static bool CanPlayFree(CardModel card, Player player)
    {
        if (player.PlayerCombatState == null || player.Creature.CombatState == null) return false;
        card.CanPlay(out var reason, out _);
        // 免费打出只绕过资源费用；大终结等卡自身的条件、不可打出与目标要求仍成立。
        const UnplayableReason forbidden = UnplayableReason.HasUnplayableKeyword
            | UnplayableReason.BlockedByCardLogic | UnplayableReason.NoLivingAllies;
        if ((reason & forbidden) != 0) return false;
        if (card.TargetType == TargetType.AnyEnemy && !player.Creature.CombatState.HittableEnemies.Any())
            return false;
        return GameHook.ShouldPlay(player.Creature.CombatState, card, out _, AutoPlayType.Default);
    }

    public static List<CardModel> Candidates(Player player)
        => TryGetPair(player, out var left, out var right)
            ? PileType.Hand.GetPile(player).Cards
                .Where(c => IsCandidate(c, player, left!.PendulumScale, right!.PendulumScale)).ToList()
            : [];

    public static List<CardModel> ExtraCandidates(Player player)
        => TryGetPair(player, out var left, out var right)
            ? ExtraDeckSeries.Cards(player)
                .Where(c => IsExtraCandidate(c, player, left!.PendulumScale, right!.PendulumScale)).ToList()
            : [];

    public static bool HasCandidates(Player player)
        => Candidates(player).Count > 0 || ExtraCandidates(player).Count > 0;

    public static bool Request(Player player)
    {
        if (!_registered || !LocalContext.IsMe(player) || !CanOperate(player)
            || IsBusy(player) || !HasCandidates(player)) return false;
        _pendingRequest = true;
        try
        {
            if (RitsuLibManagedNetActions.Request(RunManager.Instance, SummonAction, new SummonRequest(), player.NetId))
                return true;
            _pendingRequest = false;
            return false;
        }
        catch { _pendingRequest = false; throw; }
    }

    public static async Task Execute(PlayerChoiceContext context, Player player)
    {
        if (!CanOperate(player) || !TryGetPair(player, out var left, out var right)) return;
        var combat = player.Creature.CombatState;
        int leftScale = left!.PendulumScale, rightScale = right!.PendulumScale;
        var eligible = Candidates(player);
        if (eligible.Count == 0 && ExtraCandidates(player).Count == 0) return;
        using var reservation = PlayerEffectState.For(player).Reserve(EffectKey, PlayerEffectState.Combat());
        if (reservation == null) return;

        HashSet<CardModel> selected = [];
        if (eligible.Count > 0)
        {
            // 第一阶段仍使用生存者的原版手牌视图，空选跳过本阶段。
            selected = (await CardSelectCmd.FromHand(context, player, Prefs("SUMMON", eligible.Count),
                filter: c => eligible.Contains(c) && IsCandidate(c, player, leftScale, rightScale),
                source: null!)).ToHashSet();
        }
        // 使用原手牌顺序结算，避免鼠标选择次序或跨端排序改变连锁结果。
        var ordered = eligible.Where(selected.Contains).ToList();
        if (!CanOperate(player) || player.Creature.CombatState != combat
            || !SamePair()) return;

        var history = CombatManager.Instance.History;
        CardModel? playing = null;
        HashSet<CardModel> started = [];
        void OnHistoryChanged()
        {
            // 只在选中的卡真正开始打出时提交；取消、失效或自动打出被阻止不扣次数。
            if (history.Entries.LastOrDefault() is CardPlayStartedEntry entry
                && ReferenceEquals(entry.CardPlay.Card, playing))
            {
                started.Add(entry.CardPlay.Card);
                reservation.Commit();
            }
        }
        history.Changed += OnHistoryChanged;
        try
        {
            foreach (var card in ordered)
            {
                if (!CanOperate(player) || player.Creature.CombatState != combat || !SamePair()) break;
                if (!IsCandidate(card, player, leftScale, rightScale)) continue;
                playing = card;
                await CardCmd.AutoPlay(context, card, null);
                playing = null;
            }

            if (!CanOperate(player) || player.Creature.CombatState != combat || !SamePair()) return;
            // 手牌效果全部结算后再计算额外堆候选，允许本次手牌效果新加入的灵摆怪兽被选到。
            var extraEligible = ExtraCandidates(player);
            if (extraEligible.Count == 0) return;
            var extraSelected = (await CardSelectCmd.FromSimpleGrid(context, extraEligible, player,
                Prefs("EXTRA", extraEligible.Count))).ToHashSet();
            foreach (var card in extraEligible.Where(extraSelected.Contains))
            {
                if (!CanOperate(player) || player.Creature.CombatState != combat || !SamePair()) break;
                if (!IsExtraCandidate(card, player, leftScale, rightScale)) continue;
                await PlayExtra(card);
            }
        }
        finally { history.Changed -= OnHistoryChanged; }

        CardSelectorPrefs Prefs(string stage, int count)
        {
            var prompt = new LocString("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_PENDULUM_" + stage);
            prompt.Add("Lower", System.Math.Min(leftScale, rightScale));
            prompt.Add("Upper", System.Math.Max(leftScale, rightScale));
            return new CardSelectorPrefs(prompt, 0, count)
            { RequireManualConfirmation = true, Cancelable = true, PretendCardsCanBePlayed = true };
        }

        async Task PlayExtra(CardModel original)
        {
            CardModel? copy = null;
            try
            {
                await ExtraDeckSeries.Consume(player, original);
                if (!CanOperate(player) || player.Creature.CombatState != combat || !SamePair()) return;
                copy = ExtraDeckSeries.CreateSummonedCopy(player, original);
                if (!CanPlayFree(copy, player)) return;
                var result = await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Play, player);
                if (!result.success || copy.Pile?.Type != PileType.Play || copy.HasBeenRemovedFromState
                    || !CanOperate(player) || player.Creature.CombatState != combat || !SamePair()
                    || !CanPlayFree(copy, player)) return;
                playing = copy;
                await CardCmd.AutoPlay(context, copy, null);
            }
            finally
            {
                playing = null;
                if (copy != null && started.Contains(copy))
                    PendulumExtraDeck.RemoveTemporary(original);
                else
                {
                    // 没有真正开始打出时归还额外堆实体，清理未成功召唤的战斗副本。
                    if (original.Owner == player && original.Pile?.Type == Entry.ExtraUsedPile
                        && player.RunState.ContainsCard(original))
                        ExtraDeckSeries.RestorePersistentExtraCard(player, original, Entry.ExtraPile.GetPile(player));
                    if (copy is { HasBeenRemovedFromState: false })
                    {
                        if (copy.Pile is { IsCombatPile: true })
                            await CardPileCmd.RemoveFromCombat(copy, skipVisuals: true);
                        else if (copy.Pile == null) copy.RemoveFromState();
                    }
                }
            }
        }

        bool SamePair()
            => TryGetPair(player, out var currentLeft, out var currentRight)
               && ReferenceEquals(left, currentLeft) && ReferenceEquals(right, currentRight)
               && currentLeft!.PendulumScale == leftScale && currentRight!.PendulumScale == rightScale;
    }
}
