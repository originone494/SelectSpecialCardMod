using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;

namespace SelectSpecialCardMod.Scripts.Tool;

// 「同名卡一回合一次」的通用记录。
//
// 键 = scope + 卡名（注册 Id）：scope 用来区分"同一张卡上的不同效果"
//（例如同一张卡既有「珠泪融合」限制、又有「送墓」限制，两者必须各算各的）。
// 值 = 最近一次发动的 (战斗, 回合号)：换战斗、换回合自动失效，不需要任何重置钩子。
//
// 用"卡名"而不是"卡实例"当键，因为限制对象是「同名卡」——同一张卡的多份拷贝共享一次。
// 另：供"每张卡自己一回合一次"（按实例而不是按卡名）使用的 CurrentKey 也放这里，
// 把 CurrentKey(player) 存进卡自己的实例字段，下次比一比就知道这张卡本回合用过没有。
public static class PerTurnOnce
{
    private static readonly Dictionary<string, (CombatId? Combat, int Turn)> Used = new();

    public static string Key(string scope, string cardEntry) => scope + ":" + cardEntry;

    public static bool IsUsed(Player player, string key)
        => Used.TryGetValue(key, out var record) && record == CurrentKey(player);

    public static void Mark(Player player, string key) => Used[key] = CurrentKey(player);

    // 供"每张卡自己一回合一次"（按实例而不是按卡名）使用
    public static (CombatId? Combat, int Turn) CurrentKey(Player player)
        => (CombatManager.Instance.CurrentCombatId, player.PlayerCombatState?.TurnNumber ?? 0);
}