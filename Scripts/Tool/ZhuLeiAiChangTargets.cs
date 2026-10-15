using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace SelectSpecialCardMod.Scripts.Tool;

// 战斗实体 CombatId 由引擎分配，联机/重放两端一致；账本按战斗对象隔离。
internal static class ZhuLeiAiChangTargets
{
    internal sealed class Ledger
    {
        public HashSet<uint> Stunned = [];
        public HashSet<uint> Reserved = [];
    }
    private static readonly ConditionalWeakTable<object, Ledger> Ledgers = new();
    private static Ledger For(ICombatState combat) => Ledgers.GetOrCreateValue(combat);
    internal static readonly HashSet<IntentType> Allowed = [
        IntentType.Buff, IntentType.Debuff, IntentType.DebuffStrong,
        IntentType.Defend, IntentType.Heal, IntentType.Escape,
        IntentType.Summon, IntentType.StatusCard, IntentType.CardDebuff,
    ];

    public static List<Creature> Get(ICombatState combat)
        => combat.HittableEnemies.Where(e => IsEligible(combat, e)).ToList();

    public static bool IsEligible(ICombatState combat, Creature enemy, bool includeReserved = false)
    {
        if (enemy.CombatState != combat || enemy.CombatId is not uint id
            || !enemy.IsAlive || !combat.HittableEnemies.Contains(enemy)
            || enemy.Monster?.NextMove?.Intents is not { Count: > 0 } intents
            || !intents.Any(i => Allowed.Contains(i.IntentType))) return false;
        var ledger = For(combat);
        return !ledger.Stunned.Contains(id) && (includeReserved || !ledger.Reserved.Contains(id));
    }

    public static Reservation? Reserve(ICombatState combat, Creature enemy)
    {
        if (!IsEligible(combat, enemy) || enemy.CombatId is not uint id) return null;
        var ledger = For(combat);
        if (!ledger.Reserved.Add(id)) return null;
        return new Reservation(ledger, id);
    }

    internal sealed class Reservation : IDisposable
    {
        private readonly Ledger _ledger;
        private readonly uint _id;
        private bool _disposed;
        internal Reservation(Ledger ledger, uint id) { _ledger = ledger; _id = id; }
        public void Commit()
        {
            if (_disposed) return;
            _ledger.Stunned.Add(_id);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ledger.Reserved.Remove(_id);
        }
    }
}
