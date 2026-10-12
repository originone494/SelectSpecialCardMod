using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 此时彼刻之人：
//   选择1张手牌。回合结束时，将手牌中的那张卡打出。
//   选择的卡离开手牌时，将其返回手牌，然后对所有敌人造成6点伤害。
//
// AfterCardChangedPiles 是广播钩子（任何卡的换堆都会触发，card 参数 = 换堆的那张），
// 所以这里不调用基类（基类有"只处理自己"的过滤），直接监听被选卡的去向。
public class CiShiBiKeZhiRenCard : SelectSpecialBaseCard
{
    private CardModel? _chosenCard;

    public CiShiBiKeZhiRenCard() : base(1, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        _chosenCard = null;
        var hand = PileType.Hand.GetPile(Owner).Cards.ToList();
        if (hand.Count == 0) return;

        _chosenCard = (await CardSelectCmd.FromHand(
            prefs: CardPrefs(this, SelectionScreenPrompt, 1, 1),
            context: choiceContext,
            player: Owner,
            filter: null,
            source: this)).FirstOrDefault();
    }

    // 回合结束时结算（用 BeforeSideTurnEnd：它在"清空手牌"之前）。
    // 若用 AfterSideTurnEnd，被选卡会先被清进弃牌堆，Contains 判定失败、效果静默不发生。
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player || _chosenCard == null || CombatState == null) return;

        var selected = _chosenCard;
        if (PileType.Hand.GetPile(Owner).Cards.Contains(selected))
        {
            // 暂存选择，自动打出导致离开手牌时由 AfterCardChangedPiles 处理回手与群体伤害。
            await CardCmd.AutoPlay(choiceContext, selected, null);
        }

        _chosenCard = null;
    }

    // 目标卡离开手牌时立刻返回手牌，并对所有敌人造成6点伤害。
    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (card != _chosenCard || oldPileType != PileType.Hand || CombatState == null) return;
        if (PileType.Hand.GetPile(Owner).Cards.Contains(card)) return;

        await CardPileCmd.Add(card, PileType.Hand);

        var enemies = CombatState.HittableEnemies.ToList();
        if (enemies.Count > 0)
        {
            await CreatureCmd.Damage(
                new ThrowingPlayerChoiceContext(),
                enemies,
                6,
                ValueProp.Move,
                Owner.Creature,
                this,
                null);
        }
    }
}
