using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Saves.Runs;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Tool;

// 执行实际链接能力的死亡钩子；替换原生死亡动画／移除命令，并转发嵌套死亡事件。
public static class LinkedEnemyCopyProbe
{
    private static readonly List<LinkedEnemyCopyPower> Links = [];
    private static readonly List<Creature> Killed = [];
    private static readonly PlayerChoiceContext Context = new BlockingPlayerChoiceContext();
    private static int _axebotInitialAbilities;

    public static async Task Check()
    {
        var harmony = new Harmony("scm.linked-enemy-copy.death-probe");
        try
        {
            harmony.CreateClassProcessor(typeof(EnemyCopyInitialAbilitiesPatch)).Patch();
            harmony.Patch(AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Add), [typeof(Creature)]),
                prefix: new HarmonyMethod(typeof(LinkedEnemyCopyProbe), nameof(AddCopy)));
            harmony.Patch(AccessTools.Method(typeof(Axebot), nameof(Axebot.AfterAddedToRoom)),
                prefix: new HarmonyMethod(typeof(LinkedEnemyCopyProbe), nameof(RecordAxebotInitialAbilities)));
            await CheckInitialAbilities();
            harmony.Patch(AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Kill), [typeof(Creature), typeof(bool)]),
                prefix: new HarmonyMethod(typeof(LinkedEnemyCopyProbe), nameof(Kill)));
            foreach (bool daiZui in new[] { true, false })
            {
                Reset();
                var combat = ArrivalEquipmentProbe.NewPlayer().Creature.CombatState!;
                var original = Enemy(combat, 1);
                var unrelated = Enemy(combat, 2);
                var copy = Enemy(combat, 3);
                var sibling = Enemy(combat, 4);
                var otherCopy = Enemy(combat, 5);
                Link(copy, original, daiZui);
                Link(sibling, original, daiZui);
                Link(otherCopy, unrelated, !daiZui);

                // 没有真正死亡（复活／阻止移除）时不连带死亡。
                await Notify(original, prevented: true);
                if (Killed.Count != 0) throw new Exception("Prevented original death killed its copies.");

                // 同侧其他敌人死亡只影响它自己的复制品，不能按敌人种类或阵营误杀。
                SetHp(unrelated, 0);
                await Notify(unrelated);
                if (!Killed.SequenceEqual(new[] { otherCopy }) || !copy.IsAlive || !sibling.IsAlive || !original.IsAlive)
                    throw new Exception("A different original's death killed unrelated copies.");

                // 一份复制品先死亡不反杀本体，也不杀同本体的兄弟复制品。
                SetHp(copy, 0);
                await Notify(copy);
                if (Killed.Count != 1 || !original.IsAlive || !sibling.IsAlive)
                    throw new Exception("Copy death propagated backwards or sideways.");

                SetHp(original, 0);
                await Notify(original);
                if (!Killed.SequenceEqual(new[] { otherCopy, sibling }))
                    throw new Exception("Matching original death did not kill each surviving copy exactly once.");
                await Notify(original);
                if (Killed.Count != 2) throw new Exception("Repeated death notification killed a copy twice.");

                // 复制品再被复制：沿具体本体链级联死亡。
                Reset();
                original = Enemy(combat, 10);
                copy = Enemy(combat, 11);
                var child = Enemy(combat, 12);
                Link(copy, original, daiZui);
                Link(child, copy, !daiZui);
                SetHp(original, 0);
                await Notify(original);
                if (!Killed.SequenceEqual(new[] { copy, child }))
                    throw new Exception("Nested copies did not follow their direct-original chain.");

                // 原生SavedProperties恢复ID时必须清除旧缓存，死亡事件不依赖本体仍在列表中。
                Reset();
                original = Enemy(combat, 20);
                unrelated = Enemy(combat, 21);
                copy = Enemy(combat, 22);
                var restored = Link(copy, unrelated, daiZui);
                var property = typeof(LinkedEnemyCopyPower).GetProperty(nameof(LinkedEnemyCopyPower.OriginalCombatId))!;
                if (property.GetCustomAttribute<SavedPropertyAttribute>() == null)
                    throw new Exception("Copy original ID is not persisted by the native saved-property system.");
                new SavedProperties
                {
                    ints = [new SavedProperties.SavedProperty<int>(property.Name, 20)]
                }.Fill(restored);
                SetHp(unrelated, 0);
                await Notify(unrelated);
                if (Killed.Count != 0) throw new Exception("Restored copy still used its stale original reference.");
                SetHp(original, 0);
                await Notify(original);
                if (!Killed.SequenceEqual(new[] { copy })) throw new Exception("Restored original ID was not used for death linking.");
            }
            GD.Print("PASS: both copy powers follow only their original, ignore prevented deaths, isolate siblings/other originals, cascade through copied copies and restore saved original IDs without stale references.");
        }
        finally { harmony.UnpatchAll(harmony.Id); Reset(); }
    }

    private static async Task CheckInitialAbilities()
    {
        _axebotInitialAbilities = 0;
        var combat = ArrivalEquipmentProbe.NewPlayer().Creature.CombatState!;
        var original = new Creature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, "original-axebot")
        { CombatState = combat };
        await original.AfterAddedToRoom();
        if (_axebotInitialAbilities != 1 || original.GetPower<StockPower>()?.Amount != 2)
            throw new Exception("Normal Axebot entry abilities were suppressed.");
        var spawn = typeof(DaiZuiADaiPower).Assembly.GetType("SelectSpecialCardMod.Scripts.Tool.EnemyCopySpawn", true)!;
        var copy = await (Task<Creature>)AccessTools.Method(spawn, "Add").Invoke(null, [original, combat, "copy-axebot"])!;
        if (_axebotInitialAbilities != 1 || copy.Powers.Count != 0 || original.GetPower<StockPower>()?.Amount != 2)
            throw new Exception("Copy retained native or entry-hook abilities, or altered its original.");
        if ((bool)AccessTools.Method(spawn, "IsInitializing").Invoke(null, [copy])!)
            throw new Exception("Copy initialization scope leaked after spawning.");
        var link = Link(copy, original, daiZui: true);
        if (!copy.Powers.Contains(link)) throw new Exception("Copy cleanup removed its subsequently applied link.");
        Reset();
        GD.Print("PASS: normal Axebot retains Stock; copied Axebot skips native entry abilities, clears other entry powers, exits initialization scope and accepts its own copy link.");
    }

    public static bool AddCopy(Creature creature, ref Task __result)
    {
        __result = Add();
        return false;
        async Task Add()
        {
            await creature.AfterAddedToRoom();
            // 模拟独立入场钩子施加的能力，验证入场收尾仍会清除。
            ModelDb.Power<ArtifactPower>().ToMutable(1).ApplyInternal(creature, 1, true);
        }
    }

    public static bool RecordAxebotInitialAbilities(Axebot __instance, ref Task __result)
    {
        _axebotInitialAbilities++;
        ModelDb.Power<StockPower>().ToMutable(2).ApplyInternal(__instance.Creature, 2, true);
        __result = Task.CompletedTask;
        return false;
    }

    private static Creature Enemy(ICombatState combat, uint id)
    {
        var creature = new Creature(ModelDb.Monster<Vantom>().ToMutable(), CombatSide.Enemy, "copy-link-" + id)
        { CombatState = combat };
        AccessTools.Property(typeof(Creature), nameof(Creature.CombatId)).SetValue(creature, (uint?)id);
        SetHp(creature, 20);
        return creature;
    }

    private static LinkedEnemyCopyPower Link(Creature copy, Creature original, bool daiZui)
    {
        var power = daiZui
            ? (LinkedEnemyCopyPower)ModelDb.Power<DaiZuiADaiPower>().ToMutable()
            : (LinkedEnemyCopyPower)ModelDb.Power<ZengZhiPower>().ToMutable();
        power.Original = original;
        power.ApplyInternal(copy, 1, true);
        Links.Add(power);
        return power;
    }

    private static void Reset() { Links.Clear(); Killed.Clear(); }
    private static void SetHp(Creature creature, int hp)
        => AccessTools.Field(typeof(Creature), "_currentHp").SetValue(creature, hp);

    private static async Task Notify(Creature creature, bool prevented = false)
    {
        foreach (var link in Links.ToArray()) await link.AfterDeath(Context, creature, prevented, 0);
    }

    public static bool Kill(Creature creature, ref Task __result)
    {
        Killed.Add(creature);
        SetHp(creature, 0);
        __result = Notify(creature);
        return false;
    }
}
