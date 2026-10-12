using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
using SelectSpecialCardMod.Scripts.Power;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 刻魔·怜歌（陷阱）
//   打出：这个回合受到的伤害减半
//   时机（从消耗堆发动）：除外消耗堆的这张卡 → 把手牌的攻击卡作为融合素材消耗 →
//         从额外堆将1张「刻魔融合怪兽」加入手牌；发动时「作为装备的刻魔怪兽」也能当素材
public class KeMoLianGeCard : SelectSpecialBaseCard, IShiJiCard
{
    public KeMoLianGeCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.KeMo,
        SelectTags.KeMoXianJing,
        SelectTags.ShiJi,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.KeMo),
        HoverTipFactory.FromKeyword(SelectKeywords.KeMoXianJing),
        HoverTipFactory.FromKeyword(SelectKeywords.ShiJi),
        HoverTipFactory.FromPower<KeMoLianGePower>(),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<KeMoLianGePower>(choiceContext, Owner.Creature, 1, Owner.Creature, this);
    }

    // 时机：从消耗堆发动
    private static LocString PickMonsterPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_FUSION_PICK_MONSTER");
    private static LocString PickMaterialPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KE_MO_FUSION_PICK_MATERIAL");

    public IEnumerable<PileType> ShiJiPiles => [PileType.Exhaust];

    public bool CanActivateShiJi(Player player)
        => KeMoFusion.HasCandidate(player, FusionPool);

    public async Task ActivateShiJi(PlayerChoiceContext ctx, Player player)
    {
        // 除外消耗堆的这张卡
        await CardPileCmd.Add(this, Entry.ChuWaiPile);

        // 手牌的攻击卡当融合素材（消耗）；「作为装备的刻魔怪兽」也能当素材
        await KeMoFusion.TryFusion(ctx, player, this, PickMonsterPrompt, PickMaterialPrompt,
            FusionPool, KeMoFusionDestination.Exhaust);
    }

    // 素材池 = 手牌的攻击卡 + 正在当装备的「刻魔怪兽」
    private static List<CardModel> FusionPool(Player player)
    {
        var pool = PileType.Hand.GetPile(player).Cards
            .Where(c => c.Type == CardType.Attack)
            .ToList();
        pool.AddRange(KeMoEquip.EquipmentCards(player)
            .Where(c => c.Tags.Contains(SelectTags.KeMoGuaiShou)));
        return pool;
    }
}
