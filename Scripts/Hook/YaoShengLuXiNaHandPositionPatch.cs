using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Cards.Special;

namespace SelectSpecialCardMod.Scripts.Hook;

[HarmonyPatch(typeof(CardModel), nameof(CardModel.OnPlayWrapper))]
public static class YaoShengLuXiNaHandPositionPatch
{
    public static void Prefix(CardModel __instance)
    {
        if (__instance is YaoShengHuaShiLuXiNaCard card) card.CaptureStartingPosition();
        else if (__instance is YaoShengBoShiDiNaCard tina) tina.CaptureStartingPosition();
        else if (__instance is YaoShengYueShiFuErTuNaCard fortuna) fortuna.CaptureStartingPosition();
        else if (__instance is YaoShengFengShiLeiQiNaCard regina) regina.CaptureStartingPosition();
    }
}
