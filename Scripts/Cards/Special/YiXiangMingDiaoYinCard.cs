using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 异响鸣·调音：技能，先古，0费。卡图按准确类名继承基类 PortraitPath。
public class YiXiangMingDiaoYinCard : YiXiangMingChoiceCard
{
    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        ..base.AdditionalHoverTips, HoverTipFactory.FromKeyword(SelectKeywords.Xing4),
    ];

    internal static bool IsFourStar(CardModel card)
        => card is SelectSpecialBaseCard { StarLevel: 4 }
            || card.Tags.Contains(SelectTags.Xing4) || card.Keywords.Contains(SelectKeywords.Xing4);

    protected override async Task ResolveChoice(PlayerChoiceContext context, bool first)
    {
        var combat = CombatState;
        var pile = PileType.Exhaust.GetPile(Owner);
        var candidates = pile.Cards.Where(c => c.Owner == Owner && !c.HasBeenRemovedFromState
            && (first ? c.Type == CardType.Attack : IsFourStar(c))).ToList();
        if (candidates.Count == 0) return;
        CardModel? picked;
        if (first)
            picked = Owner.RunState.Rng.Niche.NextItem(candidates);
        else
            picked = (await CardSelectCmd.FromCombatPile(context, pile, Owner,
                CardPrefs(this, SelectionScreenPrompt, 1, 1), filter: IsFourStar)).FirstOrDefault();
        if (CombatState != combat || CombatManager.Instance.IsOverOrEnding || !Owner.Creature.IsAlive
            || picked?.Owner != Owner || picked.Pile?.Type != PileType.Exhaust || picked.HasBeenRemovedFromState) return;
        await CardPileCmd.Add(picked, PileType.Hand);
        if (first && picked.Pile?.Type == PileType.Hand)
        {
            picked.EnergyCost.SetThisTurn(0);
            picked.SetStarCostThisTurn(0);
        }
    }
}
