using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Myth;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

public class SuiYuanShenHuaHeTiCard : SelectSpecialBaseCard
{
    public SuiYuanShenHuaHeTiCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self) { }
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override Task OnPlay(PlayerChoiceContext context, CardPlay cardPlay)
        => MythCardFactory.CreateAndGrant(Owner);
}
