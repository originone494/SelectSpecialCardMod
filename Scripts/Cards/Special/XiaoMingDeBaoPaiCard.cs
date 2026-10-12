using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using SelectSpecialCardMod.Scripts.Base;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 削命的宝牌：手牌抽至 3 张，回合结束时丢弃所有手牌。
// 本卡打出后即进入消耗堆，但引擎的钩子监听覆盖 PlayerCombatState.AllPiles 全部牌堆
//（含消耗堆），延迟效果由本卡的 BeforeSideTurnEnd 直接承载，无需临时能力；
// 用 BeforeSideTurnEnd 而不是 AfterSideTurnEnd：后者在"清空手牌"之后跑，
// 那时手牌已经被清掉，这条"丢弃所有手牌"就几乎什么都丢不到了。
// 一次性标记保证整场战斗只在打出的那个回合结束时结算一次。
public class XiaoMingDeBaoPaiCard : SelectSpecialBaseCard
{
    private const int _handTarget = 3;

    private bool _discardPending;

    public XiaoMingDeBaoPaiCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 手牌不足 3 张时补抽至 3 张
        int missing = _handTarget - PileType.Hand.GetPile(Owner).Cards.Count;
        if (missing > 0)
        {
            await CardPileCmd.Draw(choiceContext, missing, Owner);
        }

        _discardPending = true;
    }

    // 回合结束时丢弃所有手牌（只清自己的手牌，联机不影响队友）
    // 用 BeforeSideTurnEnd：此时手牌还没被清，才丢得到东西
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != Owner.Creature.Side || !_discardPending) return;
        _discardPending = false;

        var handCards = PileType.Hand.GetPile(Owner).Cards.ToList();
        if (handCards.Count == 0) return;

        await CardCmd.Discard(choiceContext, handCards);
    }

    // 战斗结束复位一次性标记
    public override Task AfterCombatEnd(CombatRoom room)
    {
        _discardPending = false;
        return Task.CompletedTask;
    }
}