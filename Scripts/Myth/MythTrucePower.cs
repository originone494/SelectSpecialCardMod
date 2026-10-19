using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace SelectSpecialCardMod.Scripts.Power;

[RegisterPower]
public class MythTrucePower : SelectSpecialBasePower
{
    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://SelectSpecialCardMod/images/powers/YiShiXiuZhanPower.png",
        BigIconPath: "res://SelectSpecialCardMod/images/powers/YiShiXiuZhanPower.png");
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => dealer?.CombatState != null && dealer.CombatState == Owner.CombatState ? 0m : 1m;
    public override async Task AfterSideTurnEnd(PlayerChoiceContext context, CombatSide side, IEnumerable<Creature> participants)
    { if (side == CombatSide.Enemy) await PowerCmd.Remove(this); }
}
