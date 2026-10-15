using System.Reflection;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Players;
using SelectSpecialCardMod.Scripts.Rules;
namespace SelectSpecialCardMod.Scripts.Tool;
public static class UiConditions
{
    private static long _revision;
    private static readonly TimedQueryCache<CardModel, bool> Special = new(100);
    private static readonly TimedQueryCache<Player, bool> Summon = new(100);
    public static void Invalidate() => _revision++;
    public static bool SpecialEffect(CardModel card) => Special.Get(card, _revision, Time.GetTicksMsec(), () => Hook.SpecialEffectHook.CanActivate(card));
    public static bool ExtraSummon(Player p) => Summon.Get(p, _revision, Time.GetTicksMsec(), () => ExtraDeckSummonHook.CanSummonAny(p));
}
[HarmonyPatch]
public static class UiRuleInvalidationPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var names = new HashSet<string> { "AfterCardChangedPiles", "AfterCardPlayed", "AfterCardDiscarded", "AfterCardExhausted",
            "AfterEnergySpent", "AfterEnergyReset", "AfterPowerAmountChanged", "AfterPowerApplied", "AfterPowerRemoved", "AfterPlayerTurnStart" };
        return typeof(MegaCrit.Sts2.Core.Hooks.Hook).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(Task) && names.Contains(m.Name));
    }
    static void Postfix(ref Task __result) { UiConditions.Invalidate(); __result = Complete(__result); }
    static async Task Complete(Task task) { try { await task; } finally { UiConditions.Invalidate(); } }
}
