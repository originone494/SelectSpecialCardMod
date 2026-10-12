using MegaCrit.Sts2.Core.Combat;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「一场战斗一次」的通用记录：键 = scope + 卡名（注册 Id），值 = 最近一次发动的战斗。
// 换战斗自动失效，不需要任何重置钩子。
public static class PerCombatOnce
{
    private static readonly Dictionary<string, CombatId?> Used = new();

    public static bool IsUsed(string key)
        => Used.TryGetValue(key, out var combat) && combat == CombatManager.Instance.CurrentCombatId;

    public static void Mark(string key)
        => Used[key] = CombatManager.Instance.CurrentCombatId;
}
