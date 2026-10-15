using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 刻魔·咏圣（魔法）
//   打出：「检索」1张「光恶魔」，之后，丢弃1张卡
//   时机（从消耗堆发动）：除外消耗堆的这张卡可以发动 →
//         将手牌的怪兽作为融合素材消耗 → 从额外堆将1只「刻魔融合怪兽」加入手牌
public class KeMoYongShengCard : SelectSpecialBaseCard, IShiJiCard
{
    public KeMoYongShengCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
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
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
        HoverTipFactory.FromKeyword(SelectKeywords.ShiJi),
    ];

    private static bool IsGuangEMo(CardModel c) => c.Tags.Contains(SelectTags.GuangEMo);

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // ① 「检索」1张「光恶魔」
        await SelectToolCmd.RetrieverCard(
            choiceContext,
            Owner,
            IsGuangEMo,
            p => p is SelectSpecialCardPool,
            1, source: this);

        // ② 之后，丢弃1张卡（手牌里必须选1张；手牌空则跳过）
        var hand = PileType.Hand.GetPile(Owner);
        if (hand == null || hand.Cards.Count == 0) return;

        var toDiscard = (await CardSelectCmd.FromHandForDiscard(
            prefs: CardPrefs(this, DiscardNamedPrompt, 1, 1),
            context: choiceContext,
            player: Owner,
            filter: _ => true,
            source: this)).FirstOrDefault();
        if (toDiscard != null) await CardCmd.Discard(choiceContext, toDiscard);
    }

    // 时机：从消耗堆发动
    private static LocString PickMonsterPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_FUSION_PICK_MONSTER");
    private static LocString PickMaterialPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_FUSION_PICK_MATERIAL");

    public IEnumerable<PileType> ShiJiPiles => [PileType.Exhaust];

    // 能发动：手牌里凑得齐某只「刻魔融合怪兽」的融合素材（素材从手牌选，所以用 PushHand 当素材池）
    public bool CanActivateShiJi(Player player)
        => KeMoFusion.HasCandidate(player, KeMoFusion.PilePool(PileType.Hand));

    public async Task ActivateShiJi(PlayerChoiceContext ctx, Player player)
    {
        // 手牌的怪兽当融合素材（消耗），从额外堆取1只刻魔融合怪兽加入手牌
        await KeMoFusion.TryFusion(ctx, player, this, PickMonsterPrompt, PickMaterialPrompt,
            KeMoFusion.PilePool(PileType.Hand), KeMoFusionDestination.Exhaust,
            payActivationCost: async () =>
            {
                if (Pile?.Type != PileType.Exhaust) return false;
                await CardPileCmd.Add(this, Entry.ChuWaiPile);
                return Pile?.Type == Entry.ChuWaiPile;
            });
    }
}
