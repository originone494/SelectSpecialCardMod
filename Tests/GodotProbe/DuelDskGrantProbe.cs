using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using SelectSpecialCardMod.Scripts.Relics;
using SelectSpecialCardMod.Scripts.Tool;

public static class DuelDskGrantProbe
{
    private static int _obtained;

    public static async Task Check()
    {
        var h = new Harmony("scm.duel-disk-grant.godot-framework");
        try
        {
            // 保留生产迁移入口、原版 Replace 和真实遗物集合；只跳过获得/移除的存档及场景副作用。
            h.Patch(AccessTools.Method(typeof(RelicCmd), "Obtain", [typeof(RelicModel), typeof(Player), typeof(int)]),
                prefix: new HarmonyMethod(typeof(DuelDskGrantProbe), nameof(Obtain)));
            h.Patch(AccessTools.Method(typeof(RelicCmd), "Remove", [typeof(RelicModel)]),
                prefix: new HarmonyMethod(typeof(DuelDskGrantProbe), nameof(Remove)));
            var fresh = NewPlayer();
            _obtained = 0;
            await DuelDskGrant.Grant(fresh);
            await DuelDskGrant.Grant(fresh);
            AssertSingleDisk(fresh);
            if (_obtained != 1 || fresh.GetRelic<DuelDskRelic>()!.Rarity != RelicRarity.Starter)
                throw new Exception("New runs did not receive exactly one starter Duel Disk.");

            var loaded = NewPlayer();
            loaded.AddRelicInternal(ModelDb.Relic<BurningBlood>().ToMutable());
            var legacy = ModelDb.Relic<DeadEndRedRelic>().ToMutable();
            legacy.FloorAddedToDeck = 7;
            loaded.AddRelicInternal(legacy);
            _obtained = 0;
            await DuelDskGrant.Grant(loaded);
            await DuelDskGrant.Grant(loaded);
            AssertSingleDisk(loaded);
            var converted = loaded.GetRelic<DuelDskRelic>()!;
            if (_obtained != 1 || loaded.Relics.Count != 2 || !ReferenceEquals(loaded.Relics[1], converted)
                || converted.FloorAddedToDeck != 7)
                throw new Exception("Legacy replacement changed relic order/floor or duplicated the grant.");

            var mixed = NewPlayer();
            mixed.AddRelicInternal(ModelDb.Relic<DeadEndRedRelic>().ToMutable());
            var existingDisk = ModelDb.Relic<DuelDskRelic>().ToMutable();
            mixed.AddRelicInternal(existingDisk);
            // 即使旧数据有多枚兼容遗物，也只保留已有的那枚决斗盘。
            mixed.AddRelicInternal(ModelDb.Relic<DeadEndRedRelic>().ToMutable());
            _obtained = 0;
            await DuelDskGrant.Grant(mixed);
            await DuelDskGrant.Grant(mixed);
            AssertSingleDisk(mixed);
            if (_obtained != 0 || mixed.Relics.Count != 1 || !ReferenceEquals(mixed.Relics[0], existingDisk))
                throw new Exception("Migration replaced an existing Duel Disk or retained duplicate legacy relics.");

            var savedId = new ModelId("RELIC", "SELECT_SPECIAL_CARD_MOD_RELIC_DEAD_END_RED_RELIC");
            if (ModelDb.GetById<RelicModel>(savedId) is not DeadEndRedRelic
                || ModelDb.Relic<DeadEndRedRelic>().IsAllowed(fresh.RunState)
                || ModelDb.Relic<DeadEndRedRelic>().IsAllowedInShops)
                throw new Exception("Legacy relic cannot deserialize, or is still allowed as a new reward.");
        }
        finally { h.UnpatchAll(h.Id); _obtained = 0; }
        GD.Print("PASS: new runs obtain one starter Duel Disk; legacy IDs resolve and replace in place with the original floor; existing Duel Disks stay intact while all legacy duplicates are removed; repeated preparation is idempotent.");
    }

    private static Player NewPlayer()
    {
        var player = ArrivalEquipmentProbe.NewPlayer();
        AccessTools.Field(typeof(Player), "_relics").SetValue(player, new List<RelicModel>());
        return player;
    }

    private static void AssertSingleDisk(Player player)
    {
        if (player.Relics.OfType<DuelDskRelic>().Count() != 1 || player.Relics.OfType<DeadEndRedRelic>().Any())
            throw new Exception("Expected one Duel Disk and no Death Message relics.");
    }

    public static bool Obtain(RelicModel relic, Player player, int index, ref Task<RelicModel> __result)
    {
        _obtained++;
        player.AddRelicInternal(relic, index);
        relic.FloorAddedToDeck = 99; // 模拟原版 Obtain 覆盖获得楼层，迁移后应还原旧值。
        __result = Task.FromResult(relic);
        return false;
    }

    public static bool Remove(RelicModel relic, ref Task __result)
    {
        relic.Owner.RemoveRelicInternal(relic);
        __result = Task.CompletedTask;
        return false;
    }
}
