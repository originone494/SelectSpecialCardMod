using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace SelectSpecialCardMod.Scripts.Tool;

// 原版 AutoPlay 会把 Unplayable 卡移走；锁定卡应直接留在手牌。
[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.AutoPlay))]
public static class YiXiangMingAutoPlayLockPatch
{
    static bool Prefix(CardModel card, ref Task __result)
    {
        if (!YiXiangMingHandPlacement.IsPlayLocked(card)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

// 一些自动打出效果先移到 Play 再调用 AutoPlay；在移牌前拦住，以免离手时解锁。
[HarmonyPatch]
public static class YiXiangMingPlayPileLockPatch
{
    static MethodBase TargetMethod() => AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Add),
        [typeof(IEnumerable<CardModel>), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool), typeof(bool)]);

    static bool Prefix(IEnumerable<CardModel> cards, CardPile newPile, CardPilePosition position,
        AbstractModel? clonedBy, bool skipVisuals, bool isChangingOwners,
        ref Task<IReadOnlyList<CardPileAddResult>> __result)
    {
        if (newPile.Type != PileType.Play) return true;
        var requested = cards.ToList();
        if (!requested.Any(YiXiangMingHandPlacement.IsPlayLocked)) return true;
        __result = AddAllowed(requested, newPile, position, clonedBy, skipVisuals, isChangingOwners);
        return false;
    }

    private static async Task<IReadOnlyList<CardPileAddResult>> AddAllowed(List<CardModel> requested, CardPile pile,
        CardPilePosition position, AbstractModel? clonedBy, bool skipVisuals, bool isChangingOwners)
    {
        var blocked = requested.Where(YiXiangMingHandPlacement.IsPlayLocked).ToHashSet();
        var allowed = requested.Where(c => !blocked.Contains(c)).ToList();
        var results = allowed.Count > 0
            ? await CardPileCmd.Add(allowed, pile, position, clonedBy, skipVisuals, isChangingOwners)
            : Array.Empty<CardPileAddResult>();
        int index = 0;
        return requested.Select(card => blocked.Contains(card)
            ? new CardPileAddResult { cardAdded = card, success = false, oldPile = card.Pile, targetPile = pile.Type }
            : results[index++]).ToList();
    }
}
