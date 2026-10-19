using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SelectSpecialCardMod.Scripts.Base;

// 复制品与具体本体一对一关联；运行时保留引用，存档保存战斗内ID。
public abstract class LinkedEnemyCopyPower : SelectSpecialBasePower
{
    private Creature? _original;
    private int _originalCombatId = -1;
    private bool _resolvingOriginalDeath;

    [SavedProperty]
    public int OriginalCombatId
    {
        get => _originalCombatId;
        set { _originalCombatId = value; _original = null; }
    }

    public Creature? Original
    {
        get => _original ??= OriginalCombatId < 0 ? null
            : CombatState?.Creatures.FirstOrDefault(c => c.CombatId == (uint)OriginalCombatId);
        set
        {
            OriginalCombatId = value?.CombatId is { } id ? checked((int)id) : -1;
            _original = value;
        }
    }

    public override async Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature creature,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (wasRemovalPrevented || _resolvingOriginalDeath || creature == Owner || !Owner.IsAlive) return;
        // 本体可能已从Creatures列表移除，死亡事件仍用已保存的ID准确识别。
        if (!ReferenceEquals(_original, creature)
            && (OriginalCombatId < 0 || creature.CombatId != (uint)OriginalCombatId)) return;

        _resolvingOriginalDeath = true;
        try { await CreatureCmd.Kill(Owner); }
        finally { _resolvingOriginalDeath = false; }
    }
}
