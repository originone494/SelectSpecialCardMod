using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// No.41-泥睡魔兽。准确类名用于继承的 PortraitPath；超量阶级独立于星级。
public class No41NiShuiMoShouCard : XyzMonsterCard
{
    public No41NiShuiMoShouCard() : base(1) { }
    public override int XyzRank => 4;
    public override int RequiredMaterialCount => 2;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(21m, ValueProp.Move), new BlockVar(20m, ValueProp.Move)];
    protected override HashSet<CardTag> CanonicalTags => [..base.CanonicalTags, SelectTags.ZhuChang];
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain];
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        ..base.AdditionalHoverTips,
        HoverTipFactory.FromKeyword(SelectKeywords.Xing4),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuChang),
    ];
    [SavedProperty] public int UpkeepTurn { get; set; } = -1;
    [SavedProperty] public int BlockTurn { get; set; } = -1;

    protected override bool MatchesMaterial(CardModel card)
        => card.Owner == Owner && card.Pile?.Type == PileType.Hand && !card.HasBeenRemovedFromState
           && (card is SelectSpecialBaseCard { StarLevel: 4 } || card.Tags.Contains(SelectTags.Xing4)
               || card.Keywords.Contains(SelectKeywords.Xing4));

    private bool Resident => Pile?.Type == PileType.Hand && CombatState != null
        && Owner?.Creature.IsAlive == true && !CombatManager.Instance.IsOverOrEnding;

    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay cardPlay)
    {
        if (cardPlay.Target == null) return;
        await DamageCmd.Attack(DynamicVars.Damage.IntValue)
            .FromCard(this, cardPlay).Targeting(cardPlay.Target).Execute(context);
    }

    // 同时约束原版手动与自动打出；只影响本卡的拥有者。
    public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
        => !Resident || card.Owner != Owner || card == this || card.Type != CardType.Attack;

    private static LocString DetachPrompt => new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_XYZ_DETACH");
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext context, Player player)
    {
        await base.AfterPlayerTurnStart(context, player);
        if (!Resident || player != Owner || UpkeepTurn == player.PlayerCombatState!.TurnNumber) return;
        UpkeepTurn = player.PlayerCombatState.TurnNumber;
        var materials = XyzMaterialCards;
        var picked = materials.Count == 0 ? null : (await CardSelectCmd.FromSimpleGrid(context, materials, player,
            CardPrefs(this, DetachPrompt, 1, 1))).FirstOrDefault();
        if (!Resident) return;
        if (picked != null && await XyzMaterials.Detach(context, this, picked)) return;
        if (Resident) await CardCmd.Exhaust(context, this);
    }

    public override async Task BeforeSideTurnEnd(PlayerChoiceContext context, CombatSide side, IEnumerable<Creature> participants)
    {
        await base.BeforeSideTurnEnd(context, side, participants);
        if (!Resident || side != CombatSide.Player || !participants.Contains(Owner.Creature)
            || BlockTurn == Owner.PlayerCombatState!.TurnNumber) return;
        BlockTurn = Owner.PlayerCombatState.TurnNumber;
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, null);
    }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        UpkeepTurn = BlockTurn = -1;
    }
}
