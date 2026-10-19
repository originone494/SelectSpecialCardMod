using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Scaffolding.Content;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Myth;

public class MythStunPower : SelectSpecialBasePower
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerAssetProfile AssetProfile => new(IconPath: MythArtManifestPower.Icon, BigIconPath: MythArtManifestPower.Icon);
    public static async Task Apply(FusionActionContext ctx, Creature target, int turns)
    {
        var power = await PowerCmd.Apply<MythStunPower>(ctx.ChoiceContext, target, turns, ctx.Source.Owner.Creature, ctx.Source);
        if (power != null && target.IsAlive) await CreatureCmd.Stun(target);
    }
    public override Task BeforeSideTurnStart(PlayerChoiceContext context, CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
        => side == Owner.Side && Owner.IsAlive ? CreatureCmd.Stun(Owner) : Task.CompletedTask;
    public override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
        => side != Owner.Side && Owner.IsAlive ? CreatureCmd.Stun(Owner) : Task.CompletedTask;
    public override Task AfterSideTurnEnd(PlayerChoiceContext context, CombatSide side, IEnumerable<Creature> participants)
        => side == Owner.Side ? PowerCmd.ModifyAmount(context, this, -1, null, null) : Task.CompletedTask;
}

public class MythClonePower : SelectSpecialBasePower
{
    [SavedProperty] public int OriginalId { get; set; } = -1;
    [SavedProperty] public bool MirrorDamage { get; set; }
    public override PowerType Type => PowerType.None;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    protected override bool IsVisibleInternal => false;
    public override PowerAssetProfile AssetProfile => new(IconPath: MythArtManifestPower.Icon, BigIconPath: MythArtManifestPower.Icon);
    public static async Task Spawn(FusionActionContext ctx, Creature? original, bool mirror)
    {
        if (!ctx.CanContinue || original?.Monster == null || !original.IsAlive) return;
        var copy = await EnemyCopySpawn.Add(original, ctx.Combat!, CopySpawnSlot.PickSlot(ctx.Combat!));
        CopySpawnSlot.EnsureClearOf(copy, original);
        await CreatureCmd.SetMaxHp(copy, original.MaxHp); await CreatureCmd.SetCurrentHp(copy, original.CurrentHp);
        var power = (MythClonePower)ModelDb.Power<MythClonePower>().ToMutable();
        power.OriginalId = (int)original.CombatId!.Value; power.MirrorDamage = mirror;
        await PowerCmd.Apply(ctx.ChoiceContext, power, copy, 1, ctx.Source.Owner.Creature, ctx.Source);
        await CreatureCmd.Stun(copy);
    }
    public override Task BeforeSideTurnStart(PlayerChoiceContext context, CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
        => side == Owner.Side && Owner.IsAlive ? CreatureCmd.Stun(Owner) : Task.CompletedTask;
    public override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
        => side != Owner.Side && Owner.IsAlive ? CreatureCmd.Stun(Owner) : Task.CompletedTask;
    public override async Task AfterDamageReceived(PlayerChoiceContext context, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? source)
    {
        var original = CombatState?.Creatures.FirstOrDefault(c => c.CombatId == (uint)OriginalId);
        if (MirrorDamage && target == Owner && result.TotalDamage > 0 && original?.IsAlive == true)
            await CreatureCmd.Damage(context, original, result.TotalDamage, ValueProp.Unpowered, null, null, null);
    }
    public override async Task AfterDeath(PlayerChoiceContext context, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
    {
        if (!wasRemovalPrevented && Owner.IsAlive && creature.CombatId == (uint)OriginalId) await CreatureCmd.Kill(Owner);
    }
}
