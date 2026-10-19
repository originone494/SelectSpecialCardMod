using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 刻魔·赞圣（魔法）
//   打出条件：手牌没有攻击卡，或手牌的攻击卡全部是「光恶魔」
//   打出：将1张「刻魔衍生物」加入手牌（打出后消耗）
//   时机（自动触发）：手牌的「刻魔怪兽」被消耗时，将消耗堆的这张卡加入手牌
public class KeMoZanShengCard : SelectSpecialBaseCard
{
    public KeMoZanShengCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.KeMo,
        SelectTags.KeMoMoFa,
        SelectTags.ShiJi,
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMo),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMoMoFa),
        HoverTipFactory.FromKeyword(SelectKeywords.ShiJi),
    ];

    // 打出条件：手牌没有攻击卡，或手牌的攻击卡全部是「光恶魔」
    private static bool MeetsPlayCondition(Player player)
    {
        var hand = PileType.Hand.GetPile(player);
        if (hand == null) return true;

        var attacks = hand.Cards.Where(c => c.Type == CardType.Attack).ToList();
        if (attacks.Count == 0) return true;
        return attacks.All(c => c.Tags.Contains(SelectTags.GuangEMo));
    }

    protected override bool IsPlayable => Owner == null || MeetsPlayCondition(Owner);

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 将1张「刻魔衍生物」加入手牌（战斗副本，不进卡组/额外堆）
        if (CombatState == null) return;
        var template = ModelDb.AllCards.FirstOrDefault(c => c is KeMoYanShengWuCard);
        if (template == null) return;

        var copy = CombatState.CreateCard(template, Owner);
        await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Hand, Owner);
    }

    // 时机：手牌的「刻魔怪兽」被消耗时 → 将消耗堆的这张卡加入手牌
    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        await base.AfterCardChangedPiles(card, oldPileType, clonedBy);
        if (card == this || card.Owner != Owner) return;
        if (oldPileType != PileType.Hand) return;                 // 必须是"从手牌"被消耗
        if (card.Pile?.Type != PileType.Exhaust) return;          // 且确实进了消耗堆
        if (!card.Tags.Contains(SelectTags.KeMoGuaiShou)) return; // 「刻魔怪兽」
        if (Pile?.Type != PileType.Exhaust) return;               // 本卡此刻得在消耗堆里
        if (Owner == null) return;
        if (CombatManager.Instance.IsOverOrEnding) return;

        await SelectSpecialCardMod.Scripts.Rules.CardEffectQueue.Schedule(this,
            async () => { await CardPileCmd.Add(this, PileType.Hand); },
            () => Pile?.Type == PileType.Exhaust);
    }
}
