using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 常世明子：将这个回合打出的卡加入手牌，耗能在0-3之间随机变化（仅本回合生效）。
public class ChangShiMingZiCard : SelectSpecialBaseCard
{
    public ChangShiMingZiCard() : base(1, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    // 本回合打出的卡（按实例去重；回合开始清空）
    private static readonly HashSet<CardModel> PlayedThisTurn = new();

    static ChangShiMingZiCard()
    {
        RitsuLibFramework.SubscribeLifecycle<PlayerTurnStartedEvent>(_ => PlayedThisTurn.Clear());
    }

    // 广播钩子：记录本回合打出的每一张卡
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;
        if (cardPlay.Card == null || cardPlay.Card == this) return;
        if (cardPlay.Card.Owner != Owner) return;
        PlayedThisTurn.Add(cardPlay.Card);
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;

        // 将这个回合打出的卡加入手牌，耗能随机变为 0-3（SetThisTurn：本回合内生效）
        foreach (var card in PlayedThisTurn.ToList())
        {
            await CardPileCmd.Add(card, PileType.Hand);
            card.EnergyCost.SetThisTurn(Owner.RunState.Rng.Niche.NextInt(0, 4));
        }
        PlayedThisTurn.Clear();
    }
}
