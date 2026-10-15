using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using SelectSpecialCardMod.Scripts.Rules;
namespace SelectSpecialCardMod.Scripts.Tool;
public static class PerTurnOnce
{
    public static string Key(string scope, string cardEntry) => scope + ":" + cardEntry;
    public static bool IsUsed(Player player, string key) => PlayerEffectState.For(player).IsUnavailable(key, PlayerEffectState.Turn(player));
    public static void Mark(Player player, string key) => PlayerEffectState.For(player).Mark(key, PlayerEffectState.Turn(player));
    public static (CombatId? Combat, int Turn) CurrentKey(Player player)
    { var p = PlayerEffectState.Turn(player); return (p.Combat, p.Turn); }
}
