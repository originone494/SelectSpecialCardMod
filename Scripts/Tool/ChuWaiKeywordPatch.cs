using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Custom;
using GameHook = MegaCrit.Sts2.Core.Hooks.Hook;

namespace SelectSpecialCardMod.Scripts.Tool;

// 像原版消耗一样，先设定打出后的落堆，再交给原版落堆修正和结算流程。
// 不在 AfterCardPlayed 中提前移动，保证自动打出、重复打出和装备清理读到同一个 ResultPile。
[HarmonyPatch(typeof(GameHook), nameof(GameHook.ModifyCardPlayResultLocation))]
public static class ChuWaiKeywordPatch
{
    private static void Prefix(CardModel card, ref CardLocation cardLocation)
    {
        if (card.Owner == null || !card.Keywords.Contains(SelectKeywords.ChuWai)) return;
        cardLocation = new CardLocation(cardLocation.player, Entry.ChuWaiPile, cardLocation.position);
    }
}
