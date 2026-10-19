using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Myth;
using SelectSpecialCardMod.Scripts.Tool;

// 需在Godot中运行；不把本探针的静态检查等同于执行通过。
public static class MythFullLibraryProbe
{
    private static CardModel? _interrupt;
    private static int _paid;
    public static async Task Check()
    {
        var conditions = MythConditionRegistry.All; var effects = MythEffectRegistry.All;
        if (conditions.Count != 45 || effects.Count != 121 || conditions.Count(c => c.Kind == "payment") != 24
            || conditions.Count(c => c.Kind == "restriction") != 20) throw new Exception("Full replacement library is incomplete.");
        var owner = Player();
        foreach (var e in effects)
        foreach (bool maximum in new[] { false, true })
        {
            var snapshot = Snapshot(e, conditions.Single(c => c.Kind == "none"), maximum);
            var card = Card(owner, snapshot);
            var clone = (MythMutationCard)owner.Creature.CombatState!.CloneCard(card);
            var loaded = (MythMutationCard)CardModel.FromSerializable(card.ToSerializable());
            if (clone.SnapshotJson != card.SnapshotJson || loaded.SnapshotJson != card.SnapshotJson
                || MythCardText.Description(card).Contains('{') || card.Tags.ToHashSet().SetEquals(MythSeriesTags.Resolve(snapshot.SeriesTags)) == false)
                throw new Exception("Full-library snapshot/text/tag round trip failed: " + e.Id);
            if (card.TargetType != (e.Target switch { "enemy" => TargetType.AnyEnemy, "all_enemies" => TargetType.AllEnemies,
                "any_creature" => STS2RitsuLib.Combat.CardTargeting.CustomTargetType.Anyone, _ => TargetType.Self }))
                throw new Exception("Full-library target mismatch: " + e.Id);
            card.UpgradeInternal(); card.FinalizeUpgradeInternal();
            // 新输入没有升级增量，不能替作者杜撰；旧快照的增量另有回归覆盖。
            if (!card.CaptureSnapshot().EffectValues.OrderBy(p => p.Key).SequenceEqual(snapshot.EffectValues.OrderBy(p => p.Key)))
                throw new Exception("Replacement library invented unspecified upgrade increments.");
            foreach (var item in new[] { card, clone }) { item.RemoveFromCurrentPile(silent: true); if (!item.HasBeenRemovedFromState) item.RemoveFromState(); }
        }
        foreach (var c in conditions)
        foreach (var e in effects) MythCardFactory.ValidateSnapshot(Snapshot(e, c, false));
        var aoLi = Card(owner, Snapshot(effects.Single(e => e.Id == "fx.ao_li_ha_gang_de_jie_jie"),
            conditions.Single(c => c.Id == "cond.ao_li_ha_gang_purge_buffs"), false));
        if (aoLi.Title != "奥利哈刚的奥利哈刚") throw new Exception("Same-source condition/effect combination was blocked.");
        CheckRestrictions();
        await CheckPayments();
        await CheckOngoing();
        GD.Print("PASS: full 45/121 data, 5445 combinations, min/max values, targets, snapshots, same-source naming, replacement upgrades and payment interruption fixtures. Native effects and UI still require integration testing.");
    }
    private static MythCardSnapshot Snapshot(MythEffectTemplate effect, MythConditionTemplate condition, bool max)
    {
        var values = effect.Parameters.ToDictionary(p => p.Key, p => max ? p.Value.Max : p.Value.Min);
        return new() { Condition = condition, Effect = effect, ConditionValues = condition.Parameters.ToDictionary(p => p.Key, p => max ? p.Value.Max : p.Value.Min),
            EffectValues = values, Actions = MythCardFactory.Resolve(effect, values), CardType = "Skill", EnergyCost = 2,
            Art = MythArtManifest.All[0], SeriesTags = MythSeriesTags.FromSource(effect.SourceCard) };
    }
    private static Player Player()
    {
        var player = ArrivalEquipmentProbe.NewPlayer();
        AccessTools.Field(typeof(Player), "<Creature>k__BackingField").SetValue(player,
            new Creature(player, 50, 50) { CombatState = player.Creature.CombatState });
        AccessTools.Field(typeof(RunState), "_allCards").SetValue(player.RunState, new List<CardModel>());
        return player;
    }
    private static MythMutationCard Card(Player owner, MythCardSnapshot snapshot)
    {
        var card = owner.Creature.CombatState!.CreateCard<MythMutationCard>(owner); card.Bind(snapshot);
        PileType.Hand.GetPile(owner).AddInternal(card, silent: true); return card;
    }
    private static MythMutationCard Condition(Player owner, string id)
        => Card(owner, Snapshot(MythEffectRegistry.All.Single(e => e.Id == "fx.tian_shi_de_shi_she"), MythConditionRegistry.All.Single(c => c.Id == id), false));
    private static void CheckRestrictions()
    {
        var player = Player(); var card = Condition(player, "cond.no_other_attack_in_hand");
        if (!MythLibraryConditions.CanPlay(card)) throw new Exception("No-other-attack restriction rejected an empty hand.");
        ArrivalEquipmentProbe.AddCard(typeof(StrikeIronclad), player, PileType.Hand);
        if (MythLibraryConditions.CanPlay(card)) throw new Exception("No-other-attack restriction ignored an attack.");
        player = Player(); card = Condition(player, "cond.exhaust_attacks");
        ArrivalEquipmentProbe.AddCard(typeof(DefendIronclad), player, PileType.Hand);
        if (MythLibraryConditions.CanPlay(card)) throw new Exception("Attack payment counted a skill.");
        int required = card.CaptureSnapshot().ConditionValues["count"];
        for (int n = 0; n < required; n++) ArrivalEquipmentProbe.AddCard(typeof(StrikeIronclad), player, PileType.Hand);
        if (!MythLibraryConditions.CanPlay(card)) throw new Exception("Attack payment rejected sufficient attacks.");
        player = Player(); card = Condition(player, "cond.lose_health");
        int hp = card.CaptureSnapshot().ConditionValues["hp"];
        AccessTools.Field(typeof(Creature), "_currentHp").SetValue(player.Creature, hp);
        if (MythLibraryConditions.CanPlay(card)) throw new Exception("Library allowed lethal HP payment.");
    }
    private static async Task CheckPayments()
    {
        var harmony = new Harmony("scm.full-myth-library-payments");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(CardCmd), "Exhaust"), prefix: new HarmonyMethod(typeof(MythFullLibraryProbe), nameof(Exhaust)));
            foreach (bool cancel in new[] { true, false })
            {
                _paid = 0; _interrupt = null;
                using var selection = CardSelectCmd.PushSelector(new ManualChoiceProbe { ReturnEmpty = cancel });
                var player = Player(); var card = Condition(player, "cond.exhaust_other_xiao_shi_jie");
                ArrivalEquipmentProbe.AddCard(typeof(StrikeIronclad), player, PileType.Hand);
                var play = new CardPlay { Card = card, Player = player, Target = null, IsAutoPlay = false, PlayIndex = 0, Resources = default, ResultPile = PileType.Discard, PlayCount = 1 };
                bool paid = await MythLibraryConditions.Pay(card, new FusionActionContext(card, new BlockingPlayerChoiceContext(), play));
                if (paid == cancel || _paid != (cancel ? 0 : 1)) throw new Exception("Cancelled payment exhausted cards or skipped actual payment.");
            }
            _paid = 0;
            var owner = Player(); var source = Condition(owner, "cond.exhaust_all_other");
            ArrivalEquipmentProbe.AddCard(typeof(StrikeIronclad), owner, PileType.Hand);
            _interrupt = ArrivalEquipmentProbe.AddCard(typeof(StrikeIronclad), owner, PileType.Hand);
            var sourcePlay = new CardPlay { Card = source, Player = owner, Target = null, IsAutoPlay = false, PlayIndex = 0, Resources = default, ResultPile = PileType.Discard, PlayCount = 1 };
            if (await MythLibraryConditions.Pay(source, new FusionActionContext(source, new BlockingPlayerChoiceContext(), sourcePlay)) || _paid != 1)
                throw new Exception("Interrupted multi-card payment incorrectly succeeded.");
        }
        finally { harmony.UnpatchAll(harmony.Id); _interrupt = null; }
    }
    public static bool Exhaust(CardModel card, ref Task<CardPileAddResult?> __result)
    {
        _paid++; var old = card.Pile!;
        card.RemoveFromCurrentPile(silent: true); PileType.Exhaust.GetPile(card.Owner).AddInternal(card, silent: true);
        if (_interrupt != null && _paid == 1)
        { _interrupt.RemoveFromCurrentPile(silent: true); PileType.Discard.GetPile(_interrupt.Owner).AddInternal(_interrupt, silent: true); }
        __result = Task.FromResult<CardPileAddResult?>(new() { success = true, cardAdded = card, oldPile = old, targetPile = PileType.Exhaust }); return false;
    }
    private static MythOngoingPower Ongoing(Player owner, string op)
    {
        var power = (MythOngoingPower)ModelDb.Power<MythOngoingPower>().ToMutable();
        AccessTools.Property(typeof(PowerModel), "Owner").SetValue(power, owner.Creature);
        power.Operation = op; return power;
    }
    private static int _removed;
    private static async Task CheckOngoing()
    {
        var owner = Player(); var other = Player();
        var ownAttack = ArrivalEquipmentProbe.AddCard(typeof(StrikeIronclad), owner, PileType.Hand);
        var ownSkill = ArrivalEquipmentProbe.AddCard(typeof(DefendIronclad), owner, PileType.Hand);
        var otherAttack = ArrivalEquipmentProbe.AddCard(typeof(StrikeIronclad), other, PileType.Hand);
        var power = Ongoing(owner, "custom_gain_double_damage_half_taken_this_turn");
        foreach (var props in new[] { ValueProp.Move, ValueProp.Unpowered })
        {
            if (power.ModifyDamageMultiplicative(null, 10, props, owner.Creature, ownAttack, null) != 2
                || power.ModifyDamageMultiplicative(owner.Creature, 10, props, other.Creature, otherAttack, null) != .5m
                || power.ModifyDamageMultiplicative(other.Creature, 10, props, other.Creature, otherAttack, null) != 1)
                throw new Exception("Mutation double/half damage affected another player or missed previews.");
        }
        power = Ongoing(owner, "custom_power_unlimited_hand");
        if (power.ModifyMaxHandSizeLate(owner, 10) != int.MaxValue || power.ModifyMaxHandSizeLate(other, 10) != 10)
            throw new Exception("Mutation hand-size modifier leaked to another player.");
        power = Ongoing(owner, "custom_power_free_until_attack");
        if (!power.TryModifyEnergyCostInCombat(ownSkill, 2, out decimal free) || free != 0
            || power.TryModifyEnergyCostInCombat(otherAttack, 2, out _) || power.TryModifyEnergyCostInCombat(ownAttack, 2, out _))
            throw new Exception("Mutation free-nonattack cost filter is wrong.");
        CardPlay Play(CardModel c) => new() { Card = c, Player = c.Owner, Target = null, IsAutoPlay = false, PlayIndex = 0,
            Resources = default, ResultPile = PileType.Discard, PlayCount = 1 };
        await power.BeforeCardPlayed(Play(otherAttack));
        if (!power.TryModifyEnergyCostInCombat(ownSkill, 2, out _)) throw new Exception("Teammate attack consumed mutation free casts.");
        await power.BeforeCardPlayed(Play(ownAttack));
        if (power.TryModifyEnergyCostInCombat(ownSkill, 2, out _)) throw new Exception("Free-nonattack effect survived an owner attack.");
        var h = new Harmony("scm.full-myth-power-expiry");
        try
        {
            h.Patch(AccessTools.Method(typeof(PowerCmd), "Remove", [typeof(PowerModel)]), prefix: new HarmonyMethod(typeof(MythFullLibraryProbe), nameof(Remove)));
            _removed = 0; power = Ongoing(owner, "custom_gain_half_damage_taken_this_turn");
            await power.AfterSideTurnEnd(new BlockingPlayerChoiceContext(), CombatSide.Player, [owner.Creature]);
            if (_removed != 0) throw new Exception("Half damage expired before enemy attacks.");
            await power.AfterSideTurnEnd(new BlockingPlayerChoiceContext(), CombatSide.Enemy, []);
            if (_removed != 1) throw new Exception("Half damage did not expire at enemy turn end.");
            power = Ongoing(owner, "custom_next_attack_free");
            await power.BeforeCardPlayed(Play(otherAttack));
            if (_removed != 1) throw new Exception("Teammate consumed next-free-attack.");
            await power.BeforeCardPlayed(Play(ownAttack));
            if (_removed != 2) throw new Exception("Next-free-attack was not consumed by the owner.");
        }
        finally { h.UnpatchAll(h.Id); }
    }
    public static bool Remove(ref Task __result) { _removed++; __result = Task.CompletedTask; return false; }
}
