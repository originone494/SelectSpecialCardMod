using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using SelectSpecialCardMod.Scripts.Relics;
using SelectSpecialCardMod.Scripts.Tool;
using STS2RitsuLib.RunData;

public static class DuelDskGrantProbe
{
    private static int _obtained;

    public static async Task Check()
    {
        CardAvailability.Register(); DuelDskGrant.Register(); DuelDskGrant.Register();
        if (!new CardContentPreferences().GrantDuelDiskAtStart
            || !System.Text.Json.JsonSerializer.Deserialize<CardContentRunPolicy>("{\"Version\":1,\"OnlyYuGiOh\":true}")!.GrantDuelDiskAtStart)
            throw new Exception("The Duel Disk option changed defaults or old run-policy compatibility.");
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
            fresh.RemoveRelicInternal(fresh.GetRelic<DuelDskRelic>()!);
            await DuelDskGrant.Grant(fresh);
            if (_obtained != 1 || fresh.GetRelic<DuelDskRelic>() != null)
                throw new Exception("A lost Duel Disk was automatically reissued during later preparation.");

            var disabled = NewPlayer();
            SetAutoGrant(disabled, false);
            _obtained = 0;
            await DuelDskGrant.Grant(disabled);
            await DuelDskGrant.Grant(disabled);
            if (_obtained != 0 || disabled.GetRelic<DuelDskRelic>() != null)
                throw new Exception("Disabled runs still received a Duel Disk.");
            var manuallyOwned = ModelDb.Relic<DuelDskRelic>().ToMutable();
            disabled.AddRelicInternal(manuallyOwned);
            await DuelDskGrant.Grant(disabled);
            if (!ReferenceEquals(disabled.GetRelic<DuelDskRelic>(), manuallyOwned) || _obtained != 0)
                throw new Exception("Disabled configuration removed or replaced an existing Duel Disk.");

            // 同一局中两名玩家采用相反的本人设置，各端保存同样的逐玩家快照。
            var personalOn = NewPlayer();
            var personalOff = NewPlayer();
            AccessTools.Field(typeof(Player), "<NetId>k__BackingField").SetValue(personalOn, 101UL);
            AccessTools.Field(typeof(Player), "<NetId>k__BackingField").SetValue(personalOff, 202UL);
            AccessTools.Field(typeof(Player), "_runState").SetValue(personalOff, personalOn.RunState);
            var policy = (RunSavedData<CardContentRunPolicy>)AccessTools.Field(typeof(CardAvailability), "_runPolicy").GetValue(null)!;
            policy.Set((RunState)personalOn.RunState, new() { Version = 3, GrantDuelDiskAtStart = false });
            var grants = (PlayerRunSavedData<DuelDskGrantState>)AccessTools.Field(typeof(DuelDskGrant), "_saved").GetValue(null)!;
            grants.Set((RunState)personalOn.RunState, personalOn.NetId, new() { PreferenceResolved = true, GrantAtStart = true });
            grants.Set((RunState)personalOff.RunState, personalOff.NetId, new() { PreferenceResolved = true, GrantAtStart = false });
            _obtained = 0;
            await DuelDskGrant.Grant(personalOn);
            await DuelDskGrant.Grant(personalOff);
            await DuelDskGrant.Grant(personalOn);
            await DuelDskGrant.Grant(personalOff);
            if (_obtained != 1 || personalOn.GetRelic<DuelDskRelic>() == null || personalOff.GetRelic<DuelDskRelic>() != null
                || !grants.Get(personalOn).InitialGrantHandled || !grants.Get(personalOff).InitialGrantHandled)
                throw new Exception("A player's personal Duel Disk preference affected their teammate or used the old host setting.");

            var loaded = NewPlayer();
            SetAutoGrant(loaded, false); // 关闭新发放仍必须迁移已拥有的兼容遗物。
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
        GD.Print("PASS: personal per-player Duel Disk settings are isolated within one run; default/legacy grants, disabled grants, no reissue after loss, legacy migration and idempotent preparation are preserved.");
    }

    private static void SetAutoGrant(Player player, bool enabled)
    {
        var policy = (RunSavedData<CardContentRunPolicy>)AccessTools.Field(typeof(CardAvailability), "_runPolicy").GetValue(null)!;
        policy.Set((RunState)player.RunState, new() { Version = 2, GrantDuelDiskAtStart = enabled });
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
