using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SelectSpecialCardMod.Scripts.Cards.Special;

namespace SelectSpecialCardMod.Scripts.Tool;

internal static class YiXiangMingHandPlacement
{
    [ThreadStatic] private static bool _enforcing;
    [ThreadStatic] private static bool _syncingVisuals;

    internal static bool IsLocked(CardModel card, PendulumEdgeSide side)
        => card is YiXiangMingVoiceCard { PlacedAtEdgeByEffect: true } voice
           && voice.LockedPendulumSide == side;

    public static bool IsPlayLocked(CardModel card)
        => card is YiXiangMingVoiceCard { PlacedAtEdgeByEffect: true }
           && card.Pile?.Type == PileType.Hand;

    // 包括发动者自己：只要还有任意一张已放置卡留在手牌，两张声音卡都不能发动。
    public static bool CanPlacePair(Player player)
        => !PileType.Hand.GetPile(player).Cards.Any(IsPlayLocked);

    // 在原版入堆/排序操作完成前后维持锁定位置，其他卡的相对次序保持不变。
    public static void Enforce(CardPile pile)
    {
        if (_enforcing || pile.Type != PileType.Hand || pile.Cards.Count == 0) return;
        _enforcing = true;
        try
        {
            var left = pile.Cards.FirstOrDefault(c => IsLocked(c, PendulumEdgeSide.Left));
            var right = pile.Cards.FirstOrDefault(c => IsLocked(c, PendulumEdgeSide.Right));
            if (left != null && pile.Cards[0] != left) pile.MoveToTopInternal(left);
            if (right != null && pile.Cards[^1] != right) pile.MoveToBottomInternal(right);
        }
        finally { _enforcing = false; }
    }

    public static void SyncVisibleOrder(NPlayerHand hand)
    {
        if (_syncingVisuals || hand.CardHolderContainer == null) return;
        var holders = hand.CardHolderContainer.GetChildren().OfType<NHandCardHolder>().ToList();
        var model = holders.Select(h => h.CardNode?.Model).FirstOrDefault(c => c?.Owner != null);
        if (model?.Owner == null) return;
        var pile = PileType.Hand.GetPile(model.Owner);
        if (!pile.Cards.Any(c => IsLocked(c, PendulumEdgeSide.Left) || IsLocked(c, PendulumEdgeSide.Right))) return;
        _syncingVisuals = true;
        try
        {
            var container = hand.CardHolderContainer;
            int index = 0;
            foreach (var card in pile.Cards.ToList())
            {
                var holder = holders.FirstOrDefault(h => h.CardNode?.Model == card);
                if (holder?.GetParent() == container) container.MoveChild(holder, index++);
            }
        }
        finally { _syncingVisuals = false; }
    }

    // CardPile 的 MoveTo...Internal 不发 UI 事件；手牌节点的子节点次序也必须同步。
    public static async Task<bool> PutAtEdges(Player player, CardModel left, CardModel right)
    {
        var pile = PileType.Hand.GetPile(player);
        if (left == right || left.Owner != player || right.Owner != player
            || left.Pile != pile || right.Pile != pile || !CanPlacePair(player)) return false;

        pile.MoveToTopInternal(left);
        pile.MoveToBottomInternal(right);
        if (left is YiXiangMingVoiceCard leftVoice)
        {
            leftVoice.LockedPendulumSide = PendulumEdgeSide.Left;
            leftVoice.PlacedAtEdgeByEffect = true;
        }
        if (right is YiXiangMingVoiceCard rightVoice)
        {
            rightVoice.LockedPendulumSide = PendulumEdgeSide.Right;
            rightVoice.PlacedAtEdgeByEffect = true;
        }
        Enforce(pile);
        pile.InvokeContentsChanged();

        if (!LocalContext.IsMe(player)) return true;
        var hand = NCombatRoom.Instance?.Ui?.Hand;
        if (hand == null || !GodotObject.IsInstanceValid(hand) || !hand.IsInsideTree()) return true;

        // 生成卡的卡面节点有时在入堆后的下一帧才建立。
        for (int attempt = 0; attempt < 3 && hand.GetCardHolder(right) == null; attempt++)
            await hand.ToSignal(hand.GetTree(), SceneTree.SignalName.ProcessFrame);

        SyncVisibleOrder(hand);
        hand.ForceRefreshCardIndices();
        return true;
    }

    public static bool IsAtEdge(CardModel card)
    {
        if (card.Pile?.Type != PileType.Hand || card.Owner == null) return false;
        var cards = PileType.Hand.GetPile(card.Owner).Cards;
        return cards.Count > 0 && (ReferenceEquals(cards[0], card) || ReferenceEquals(cards[^1], card));
    }
}
