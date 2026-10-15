using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Rules;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 异响鸣·异神-光耀天使。PortraitPath 继承基类，按本类名寻找 PNG。
public class YiXiangMingYiShenGuangYaoTianShiCard : YiXiangMingLinkCard
{
    [SavedProperty] public int ReturnCharges { get; set; }
    [SavedProperty] public int ReturnTurnNumber { get; set; } = -1;
    private bool _returnPending;

    private bool CanReturn => ReturnCharges > 0 && Owner?.PlayerCombatState != null
        && ReturnTurnNumber == Owner.PlayerCombatState.TurnNumber
        && CombatState?.CurrentSide == CombatSide.Player && CanResolve(Owner);

    public override async Task DengChangSpecial(PlayerChoiceContext context, Player player)
    {
        var combat = CombatState;
        int repeats = Counters(player);
        for (int i = 0; i < repeats && CombatState == combat && CanResolve(player); i++)
        {
            var enemies = combat!.HittableEnemies.ToList();
            if (enemies.Count == 0) break;
            await CreatureCmd.Damage(context, enemies, 10m, ValueProp.Unpowered, player.Creature, this, null);
        }
    }

    public override bool CanActivateSpecialEffect(Player player)
        => CanUseSpecialEffect(player) && Counters(player) >= 3;

    public override async Task<bool> TryActivateSpecialEffect(PlayerChoiceContext context, Player player)
    {
        if (!CanActivateSpecialEffect(player)) return false;
        var combat = CombatState;
        var power = player.Creature.GetPower<YiXiangMingZhiShiWuPower>()!;
        int before = power.Amount;
        EffectActivation.PaymentStarted();
        int after = await PowerCmd.ModifyAmount(context, power, -3m, null, this);
        if (before - after < 3) return false;
        if (CombatState != combat || !CanUseSpecialEffect(player)) return true;
        if (!CanReturn) ReturnCharges = 0;
        ReturnCharges += 3;
        ReturnTurnNumber = player.PlayerCombatState!.TurnNumber;
        return true;
    }

    // 由原版在整次打出结束后移动；重复打出只产生一次实际回手。
    public override CardLocation ModifyCardPlayResultLocation(CardModel card, bool isAutoPlay,
        ResourceInfo resources, CardLocation cardLocation)
        => card == this && CanReturn
            ? new CardLocation(Owner, PileType.Hand, CardPilePosition.Bottom) : cardLocation;

    public override Task AfterModifyingCardPlayResultLocation(CardModel card, CardLocation cardLocation)
    {
        if (card == this) _returnPending = CanReturn && cardLocation.player == Owner && cardLocation.pileType == PileType.Hand;
        return base.AfterModifyingCardPlayResultLocation(card, cardLocation);
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (card == this && oldPileType == PileType.Play)
        {
            if (_returnPending && Pile?.Type == PileType.Hand && CanReturn) ReturnCharges--;
            _returnPending = false;
        }
        await base.AfterCardChangedPiles(card, oldPileType, clonedBy);
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext context, CombatSide side, IEnumerable<Creature> participants)
    {
        await base.AfterSideTurnEnd(context, side, participants);
        if (side == CombatSide.Player && participants.Contains(Owner.Creature)) ClearReturns();
    }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        ClearReturns();
    }

    public override async Task BeforeCombatStart()
    {
        await base.BeforeCombatStart();
        ClearReturns();
    }

    private void ClearReturns() { ReturnCharges = 0; ReturnTurnNumber = -1; _returnPending = false; }
}
