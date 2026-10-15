using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Power;

public static class YiCunJiaProbe
{
    private static int _removals;

    public static async Task Check()
    {
        var h = new Harmony("scm.yicunjia.godot-framework");
        try
        {
            h.Patch(AccessTools.PropertyGetter(typeof(CombatManager), "IsInProgress"), prefix: new HarmonyMethod(typeof(ArrivalEquipmentProbe), "InProgress"));
            h.Patch(AccessTools.Method(typeof(RunState), "IterateHookListeners", [typeof(ICombatState)]), prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"));
            h.Patch(AccessTools.Method(typeof(CombatState), "IterateHookListeners", Type.EmptyTypes), prefix: new HarmonyMethod(typeof(EquipmentHookProbe), "Prefix"));
            h.Patch(AccessTools.Method(typeof(PowerCmd), "Remove", [typeof(PowerModel)]), prefix: new HarmonyMethod(typeof(YiCunJiaProbe), nameof(Remove)));
            var player = ArrivalEquipmentProbe.NewPlayer();
            AccessTools.Field(typeof(Creature), "<Player>k__BackingField").SetValue(player.Creature, player);
            AccessTools.Field(typeof(Creature), "<Side>k__BackingField").SetValue(player.Creature, CombatSide.Player);
            player.MaxEnergy = 3;
            var power = MakePower(player);
            EquipmentHookProbe.Models = [power];
            var attack = ArrivalEquipmentProbe.AddCard(typeof(MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad), player, PileType.Hand);
            attack.DynamicVars.ClearPreview();
            attack.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, attack.DynamicVars);
            if (attack.DynamicVars.Damage.PreviewValue != attack.DynamicVars.Damage.BaseValue * 2)
                throw new Exception("Yi Cun Jia does not double the actual untargeted card preview.");

            var other = ArrivalEquipmentProbe.NewPlayer().Creature; // 玩家目标也适用，不依赖怪物身份。
            foreach (var props in new[] { ValueProp.Move, ValueProp.Unpowered })
            {
                if (power.ModifyDamageMultiplicative(other, 10, props, player.Creature, null, null) != 2
                    || power.ModifyDamageMultiplicative(null, 10, props, player.Creature, attack, null) != 2
                    || power.ModifyDamageMultiplicative(player.Creature, 10, props, other, null, null) != 0.5m
                    || power.ModifyDamageMultiplicative(player.Creature, 10, props, null, null, null) != 0.5m
                    || power.ModifyDamageMultiplicative(other, 10, props, other, null, null) != 1)
                    throw new Exception("Yi Cun Jia damage depends on monster/attack-card restrictions.");
            }
            if (power.ModifyDamageMultiplicative(player.Creature, 10, ValueProp.Move, player.Creature, null, null) != 1)
                throw new Exception("Outgoing and incoming modifiers did not compose for self-damage.");

            var ctx = new BlockingPlayerChoiceContext();
            var state = player.PlayerCombatState!;
            state.Energy = 1;
            _removals = 0;
            await power.AfterSideTurnEndLate(ctx, CombatSide.Enemy, [other]);
            await power.AfterSideTurnEndLate(ctx, CombatSide.Player, [other]);
            if (state.MaxEnergy != 3 || _removals != 0) throw new Exception("Another side/player ended Yi Cun Jia.");
            await power.AfterSideTurnEndLate(ctx, CombatSide.Player, [player.Creature]);
            if (state.MaxEnergy != 2 || _removals != 0) throw new Exception("First end did not lower maximum energy by one.");
            await power.AfterSideTurnEndLate(ctx, CombatSide.Player, [player.Creature]);
            if (state.MaxEnergy != 1 || _removals != 0) throw new Exception("Second end did not preserve the effect at positive energy.");
            await power.AfterSideTurnEndLate(ctx, CombatSide.Player, [player.Creature]);
            if (_removals != 1 || state.MaxEnergy != 3)
                throw new Exception("Maximum energy reached zero but Yi Cun Jia waited for another turn or failed to restore it.");

            power = MakePower(player);
            EquipmentHookProbe.Models = [power];
            state.Energy = 1;
            // 其他普通回合结束效果先将剩余能量变为0；在本次末尾立即退出。
            await power.AfterSideTurnEnd(ctx, CombatSide.Player, [player.Creature]);
            state.Energy = 0;
            await power.AfterSideTurnEndLate(ctx, CombatSide.Player, [player.Creature]);
            if (_removals != 2 || state.MaxEnergy != 3)
                throw new Exception("Zero remaining energy at end of turn did not remove and restore Yi Cun Jia immediately.");
            attack.DynamicVars.ClearPreview();
            attack.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, attack.DynamicVars);
            if (attack.DynamicVars.Damage.PreviewValue != attack.DynamicVars.Damage.BaseValue)
                throw new Exception("Card preview stayed doubled after Yi Cun Jia ended.");
        }
        finally { h.UnpatchAll(h.Id); EquipmentHookProbe.Models = []; _removals = 0; }
        GD.Print("PASS: native card preview doubles without a target and resets on removal; damage source/recipient rules include non-monsters and non-attack damage; only the owner's turn reduces max energy; max/remaining energy reaching zero removes the effect in the same end and restores maximum energy.");
    }

    private static YiCunJiaPower MakePower(Player player)
    {
        var power = new YiCunJiaPower();
        AccessTools.Property(typeof(AbstractModel), "IsMutable").SetValue(power, true);
        AccessTools.Property(typeof(PowerModel), "Owner").SetValue(power, player.Creature);
        return power;
    }

    public static bool Remove(PowerModel power, ref Task __result)
    {
        _removals++;
        EquipmentHookProbe.Models = EquipmentHookProbe.Models.Where(model => !ReferenceEquals(model, power)).ToArray();
        __result = Task.CompletedTask;
        return false;
    }
}
