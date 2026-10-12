using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 复苏的天空神：
//   打出：从抽牌堆·弃牌堆·消耗堆将1张「奥西里斯的天空龙」加入手牌，
//         卡组没有「奥西里斯的天空龙」的情况下，「检索」1张「奥西里斯的天空龙」，
//         之后，手牌抽至6张。
//   时机（从消耗堆发动）：将消耗堆的这张卡除外可以发动，
//         从抽牌堆·弃牌堆·消耗堆将1张「死者苏生」放置在抽牌堆顶，
//         卡组没有「死者苏生」的情况下，「检索」1张「死者苏生」，将其放置在抽牌堆顶，
//         「奥西里斯的天空龙」存在消耗堆的场合，抽1张牌。
public class FuSuDeTianKongShenCard : SelectSpecialBaseCard, IShiJiCard
{
    public FuSuDeTianKongShenCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    // 「时机」标签：右键牌堆按钮的判定（CanActivateAnyShiJi）按标签筛卡，缺了它时机永远不会被识别
    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ShiJi,
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust),
        HoverTipFactory.FromKeyword(SelectKeywords.ShiJi),
    ];

    // 时机只能从消耗堆发动（打出后这张卡经消耗进消耗堆）
    public IEnumerable<PileType> ShiJiPiles => [PileType.Exhaust];

    // 无次数限制：每次发动都会把这张卡除外，天然一次性
    public bool CanActivateShiJi(Player player) => true;

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;

        // 从抽牌堆·弃牌堆·消耗堆将1张「奥西里斯的天空龙」加入手牌
        var slifer = FindInPiles<AoXiLiSiDeTianKongLongCard>(Owner);
        if (slifer != null)
        {
            await CardPileCmd.Add(slifer, PileType.Hand);
        }
        // 卡组没有「奥西里斯的天空龙」的情况下，「检索」1张「奥西里斯的天空龙」
        else if (!Owner.Deck.Cards.OfType<AoXiLiSiDeTianKongLongCard>().Any())
        {
            await SelectToolCmd.RetrieverCard(choiceContext, Owner,
                c => c is AoXiLiSiDeTianKongLongCard,
                p => p is SelectSpecialCardPool,
                1, source: this);
        }

        // 之后，手牌抽至6张
        var hand = PileType.Hand.GetPile(Owner);
        if (hand.Cards.Count < 6)
        {
            await CardPileCmd.Draw(choiceContext, 6 - hand.Cards.Count, Owner);
        }
    }

    // 时机发动：将消耗堆的这张卡除外，从抽牌堆·弃牌堆·消耗堆
    // 将1张「死者苏生」放置在抽牌堆顶（卡组没有时改为「检索」）；
    // 「奥西里斯的天空龙」存在消耗堆的场合，抽1张牌。
    public async Task ActivateShiJi(PlayerChoiceContext ctx, Player player)
    {
        // 将消耗堆的这张卡除外
        await CardPileCmd.Add(this, Entry.ChuWaiPile);

        // 从抽牌堆·弃牌堆·消耗堆将1张「死者苏生」放置在抽牌堆顶
        var reborn = FindInPiles<SiZheSuShengCard>(player);
        if (reborn != null)
        {
            await SelectToolCmd.ReturnCardToPile(reborn, PileType.Draw, CardPilePosition.Top);
        }
        // 卡组没有「死者苏生」的情况下，「检索」1张「死者苏生」，将其放置在抽牌堆顶
        else if (!Owner.Deck.Cards.OfType<SiZheSuShengCard>().Any())
        {
            var retrieved = await SelectToolCmd.RetrieverCard(ctx, player,
                c => c is SiZheSuShengCard,
                p => p is SelectSpecialCardPool,
                1, source: this);
            foreach (var card in retrieved)
            {
                await SelectToolCmd.ReturnCardToPile(card, PileType.Draw, CardPilePosition.Top);
            }
        }

        // 「奥西里斯的天空龙」存在消耗堆的场合，抽1张牌
        var exhaustPile = PileType.Exhaust.GetPile(player);
        if (exhaustPile != null && exhaustPile.Cards.OfType<AoXiLiSiDeTianKongLongCard>().Any())
        {
            await CardPileCmd.Draw(ctx, 1, player);
        }
    }

    // 在抽牌堆·弃牌堆·消耗堆三个牌堆中查找指定类型的卡
    private static CardModel? FindInPiles<T>(Player player) where T : CardModel
    {
        foreach (var pileType in new[] { PileType.Draw, PileType.Discard, PileType.Exhaust })
        {
            var found = pileType.GetPile(player).Cards.OfType<T>().FirstOrDefault();
            if (found != null) return found;
        }
        return null;
    }
}
