using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using SelectSpecialCardMod.Scripts.Rules;
namespace SelectSpecialCardMod.Scripts.Tool;
public static class PerCombatOnce
{
    public static string Key(string scope, string cardEntry) => scope + ":" + cardEntry;
    public static bool IsUsed(Player player, string key) => PlayerEffectState.For(player).IsUnavailable(key, PlayerEffectState.Combat());
    public static void Mark(Player player, string key) => PlayerEffectState.For(player).Mark(key, PlayerEffectState.Combat());
}
