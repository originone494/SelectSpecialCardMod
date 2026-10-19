using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace SelectSpecialCardMod.Scripts.Tool;

// 复制敌人的外形与模型，不执行原怪物的初始能力；复制效果的关联能力由调用方随后施加。
internal static class EnemyCopySpawn
{
    private static readonly HashSet<Creature> Initializing = [];
    internal static bool IsInitializing(Creature creature) => Initializing.Contains(creature);

    internal static async Task<Creature> Add(Creature original, ICombatState combat, string? slot)
    {
        var copy = combat.CreateCreature(original.Monster!.CanonicalInstance.ToMutable(), CombatSide.Enemy, slot);
        Initializing.Add(copy);
        try
        {
            await CreatureCmd.Add(copy);
            // 其他入场钩子也可能施加能力，入场完成后清理；之后正常获得的增益／减益不受影响。
            foreach (var power in copy.Powers.ToArray()) await PowerCmd.Remove(power);
            return copy;
        }
        finally { Initializing.Remove(copy); }
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.AfterAddedToRoom))]
public static class EnemyCopyInitialAbilitiesPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    static bool Before(Creature __instance, ref Task __result)
    {
        if (!EnemyCopySpawn.IsInitializing(__instance)) return true;
        // 从源头跳过原怪物入场能力，巨斧机器人的备用机等能力不会先触发再被移除。
        __result = Task.CompletedTask;
        return false;
    }
}
