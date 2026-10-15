using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Models.Capabilities;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Tool;

public sealed class XyzMaterialState { public int Group { get; set; } }

// 使用框架的 JSON 状态保存绑定关系；克隆素材卡不会复制素材归属。
[RegisterModelCapability]
public sealed class XyzMaterialCapability : StatefulModelCapability<CardModel, XyzMaterialState>
{
    public int Group { get => State.Group; set => SetState(new XyzMaterialState { Group = value }); }
    public override IModelCapability CloneFor(AbstractModel clonedOwner)
    {
        var clone = (XyzMaterialCapability)base.CloneFor(clonedOwner);
        clone.Group = 0;
        return clone;
    }
}

public static class XyzMaterials
{
    private static readonly HashSet<CardModel> Entering = [];
    private static readonly HashSet<XyzMonsterCard> Releasing = [];
    public static bool IsOverlaid(CardModel card) => card.Pile?.Type == Entry.XyzMaterialPile;
    internal static bool IsInactive(CardModel card) => IsOverlaid(card) && !Entering.Contains(card);
    private static XyzMaterialCapability? Binding(CardModel card)
        => card.Capabilities().All.OfType<XyzMaterialCapability>().FirstOrDefault(c => c.Group > 0);

    private static Player? MaterialOwner(XyzMonsterCard host)
        => host.Owner == null || host.MaterialOwnerKey.Length == 0 ? null
            : host.Owner.NetId.ToString() == host.MaterialOwnerKey ? host.Owner
            : host.Owner.RunState.Players.FirstOrDefault(p => p.NetId.ToString() == host.MaterialOwnerKey);

    public static IReadOnlyList<CardModel> Cards(XyzMonsterCard host)
    {
        var player = MaterialOwner(host);
        return player?.PlayerCombatState == null || host.MaterialGroupId <= 0 ? []
            : Entry.XyzMaterialPile.GetPile(player).Cards
                .Where(c => c.Owner == player && Binding(c)?.Group == host.MaterialGroupId).ToList();
    }

    private static int NextGroup(Player player)
    {
        // 由同步的牌堆状态分配序号，不使用本地随机数；包括空素材宿主以防重用其编号。
        var cards = player.Piles.SelectMany(p => p.Cards).Distinct().ToList();
        return checked(1 + cards.Select(c => Math.Max(c is XyzMonsterCard xyz ? xyz.MaterialGroupId : 0,
            Binding(c)?.Group ?? 0)).DefaultIfEmpty().Max());
    }

    public static async Task<bool> Stage(XyzMonsterCard host, IReadOnlyList<CardModel> selected)
    {
        if (host.MaterialGroupId != 0 || selected.Count != host.RequiredMaterialCount
            || selected.Distinct().Count() != selected.Count
            || selected.Any(c => !host.IsValidMaterial(c))) return false;
        host.MaterialGroupId = NextGroup(host.Owner);
        host.MaterialOwnerKey = host.Owner.NetId.ToString();
        host.MaterialsStaged = true;
        bool success = false;
        try
        {
            foreach (var card in selected)
            {
                if (CombatManager.Instance.IsOverOrEnding || !host.IsValidMaterial(card)) return false;
                foreach (var stale in card.Capabilities().All.OfType<XyzMaterialCapability>().ToList()) stale.RemoveFromOwner();
                card.AddCapability(new XyzMaterialCapability { Group = host.MaterialGroupId });
                Entering.Add(card);
                CardPileAddResult result;
                try { result = await CardPileCmd.Add(card, Entry.XyzMaterialPile, skipVisuals: true); }
                finally { Entering.Remove(card); }
                CompleteHiddenMove(result);
                if (!result.success || !IsOverlaid(card)) return false;
            }
            success = Cards(host).Count == host.RequiredMaterialCount;
            return success;
        }
        finally
        {
            if (!success)
            {
                // 失败不算支付；仅归还仍绑定在隐藏区的实际素材，不复制卡牌。
                int group = host.MaterialGroupId;
                await Cancel(host);
                foreach (var card in selected.Where(c => !IsOverlaid(c))) ClearBinding(card, group);
            }
        }
    }

    public static void ClearBinding(CardModel card, int? group = null)
    {
        foreach (var binding in card.Capabilities().All.OfType<XyzMaterialCapability>()
            .Where(c => group == null || c.Group == group).ToList()) binding.RemoveFromOwner();
    }

    public static async Task Cancel(XyzMonsterCard host)
    {
        int group = host.MaterialGroupId;
        foreach (var card in Cards(host).ToList())
        {
            if (CombatManager.Instance.IsOverOrEnding) { ClearBinding(card, group); continue; }
            var result = await CardPileCmd.Add(card, PileType.Hand);
            if (!result.success) throw new InvalidOperationException("Could not return canceled Xyz material.");
            ClearBinding(card, group);
        }
        host.MaterialGroupId = 0;
        host.MaterialOwnerKey = "";
        host.MaterialsStaged = false;
    }

    public static async Task<bool> Detach(PlayerChoiceContext context, XyzMonsterCard host, CardModel material)
    {
        if (!Cards(host).Contains(material) || CombatManager.Instance.IsOverOrEnding) return false;
        int group = host.MaterialGroupId;
        var result = await CardCmd.Exhaust(context, material);
        if (result?.success != true) return false;
        ClearBinding(material, group);
        return true;
    }

    public static async Task ExhaustAll(PlayerChoiceContext context, XyzMonsterCard host)
    {
        if (!Releasing.Add(host)) return;
        try
        {
            foreach (var material in Cards(host).ToList())
            {
                if (CombatManager.Instance.IsOverOrEnding) break;
                if (Cards(host).Contains(material) && !await Detach(context, host, material))
                    throw new InvalidOperationException("Could not exhaust released Xyz material.");
            }
            if (Cards(host).Count == 0) { host.MaterialGroupId = 0; host.MaterialOwnerKey = ""; }
        }
        finally { Releasing.Remove(host); }
    }

    // Headless 入堆跳过飞行动画，但仍完整结束来源手牌节点和移除通知。
    private static void CompleteHiddenMove(CardPileAddResult result)
    {
        if (!result.success || result.oldPile == null || ReferenceEquals(result.oldPile, result.cardAdded.Pile)) return;
        result.oldPile.InvokeCardRemoved(result.cardAdded);
        result.oldPile.InvokeCardRemoveFinished();
        result.oldPile.InvokeContentsChanged();
        if (result.oldPile.Type != PileType.Hand || !LocalContext.IsMe(result.cardAdded.Owner)) return;
        var hand = NPlayerHand.Instance;
        var holder = hand?.GetCardHolder(result.cardAdded);
        if (holder == null) return;
        var node = holder.CardNode;
        hand!.Remove(result.cardAdded);
        if (node != null && GodotObject.IsInstanceValid(node)) node.QueueFreeSafely();
    }

    internal static bool IsInactiveModel(AbstractModel model)
        => model switch
        {
            CardModel card => IsInactive(card),
            EnchantmentModel enchantment when enchantment.HasCard => IsInactive(enchantment.Card),
            AfflictionModel affliction when affliction.HasCard => IsInactive(affliction.Card),
            ModelCapability capability when capability.Owner is CardModel card => IsInactive(card),
            _ => false,
        };
}

[HarmonyPatch(typeof(CombatState), nameof(CombatState.IterateHookListeners))]
public static class XyzMaterialHooksPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref IEnumerable<AbstractModel> __result)
        => __result = __result.Where(model => !XyzMaterials.IsInactiveModel(model));
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.AfterCardChangedPiles))]
public static class XyzMaterialBindingCleanupPatch
{
    private static void Postfix(CardModel card, PileType oldPile, ref Task __result)
        => __result = Complete(__result, card, oldPile);
    private static async Task Complete(Task task, CardModel card, PileType oldPile)
    {
        try { await task; }
        finally
        {
            if (oldPile == Entry.XyzMaterialPile && !XyzMaterials.IsOverlaid(card)) XyzMaterials.ClearBinding(card);
            // 原版移出战斗先移除牌堆、再广播；此时宿主不再是监听器，需要兜住清理。
            if (card is XyzMonsterCard host && !host.MaterialsStaged && host.MaterialGroupId > 0
                && !CombatManager.Instance.IsOverOrEnding
                && (host.HasBeenRemovedFromState || host.Pile?.Type is not (PileType.Hand or PileType.Play)
                    || host.Owner?.NetId.ToString() != host.MaterialOwnerKey))
                await XyzMaterials.ExhaustAll(new BlockingPlayerChoiceContext(), host);
        }
    }
}
