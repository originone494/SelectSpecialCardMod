using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;

namespace SelectSpecialCardMod.Scripts.Rules;

public static class PlayerEffectState
{
    public readonly record struct Period(CombatId? Combat, int Turn);
    private static readonly ConditionalWeakTable<Player, EffectLedger<Period>> Ledgers = new();
    public static EffectLedger<Period> For(Player player) => Ledgers.GetOrCreateValue(player);
    public static Period Turn(Player p) => new(CombatManager.Instance.CurrentCombatId, p.PlayerCombatState?.TurnNumber ?? 0);
    public static Period Combat() => new(CombatManager.Instance.CurrentCombatId, -1);
}
