using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using SelectSpecialCardMod.Scripts.Cards.Special;

namespace SelectSpecialCardMod.Scripts.Tool;

[HarmonyPatch(typeof(CardPile), nameof(CardPile.AddInternal))]
public static class YiXiangMingHandInsertPatch
{
    static void Prefix(CardPile __instance, CardModel card, ref int index)
    {
        if (__instance.Type != PileType.Hand) return;
        if (YiXiangMingHandPlacement.IsLocked(card, PendulumEdgeSide.Left)) { index = 0; return; }
        if (YiXiangMingHandPlacement.IsLocked(card, PendulumEdgeSide.Right)) { index = -1; return; }
        int minimum = __instance.Cards.Any(c => YiXiangMingHandPlacement.IsLocked(c, PendulumEdgeSide.Left)) ? 1 : 0;
        int maximum = __instance.Cards.Count - (__instance.Cards.Any(c => YiXiangMingHandPlacement.IsLocked(c, PendulumEdgeSide.Right)) ? 1 : 0);
        if (minimum == 0 && maximum == __instance.Cards.Count) return;
        index = Math.Clamp(index < 0 ? __instance.Cards.Count : index, minimum, maximum);
    }

    static void Postfix(CardPile __instance) => YiXiangMingHandPlacement.Enforce(__instance);
}

[HarmonyPatch(typeof(CardPile), nameof(CardPile.RemoveInternal))]
public static class YiXiangMingHandRemovePatch
{
    static void Postfix(CardPile __instance, CardModel card)
    {
        if (__instance.Type == PileType.Hand && card is YiXiangMingVoiceCard voice)
            voice.PlacedAtEdgeByEffect = false;
    }
}

[HarmonyPatch]
public static class YiXiangMingHandOrderPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(CardPile), nameof(CardPile.MoveToTopInternal));
        yield return AccessTools.Method(typeof(CardPile), nameof(CardPile.MoveToBottomInternal));
        yield return AccessTools.Method(typeof(CardPile), nameof(CardPile.RandomizeOrderInternal));
    }

    static void Postfix(CardPile __instance) => YiXiangMingHandPlacement.Enforce(__instance);
}

[HarmonyPatch(typeof(CardPile), nameof(CardPile.InvokeContentsChanged))]
public static class YiXiangMingHandContentsPatch
{
    static void Prefix(CardPile __instance) => YiXiangMingHandPlacement.Enforce(__instance);
}

[HarmonyPatch(typeof(NPlayerHand), "RefreshLayout")]
public static class YiXiangMingHandLayoutPatch
{
    static void Prefix(NPlayerHand __instance) => YiXiangMingHandPlacement.SyncVisibleOrder(__instance);
}
