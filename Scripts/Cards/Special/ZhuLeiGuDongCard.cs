using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Hook;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 珠泪·鼓动
//   打出：清除**目标敌人**的格挡、力量、人工制品，丢弃1张卡
//   送墓：从弃牌堆将1张「珠泪陷阱」卡加入手牌
public class ZhuLeiGuDongCard : SelectSpecialBaseCard, ISongMuCard
{
    // 目标改成"指定敌人"：效果只作用于一个敌人（描述里没有"所有敌人"）
    public ZhuLeiGuDongCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.ZhuLei,
        SelectTags.SongMu,
        SelectTags.ZhuLeiMoFa,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.SongMu),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLei),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiMoFa),
        HoverTipFactory.FromKeyword(SelectKeywords.ZhuLeiXianJing),
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // ① 清除目标敌人的格挡、力量、人工制品
        ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
        Creature enemy = cardPlay.Target;

        // 格挡：走官方的 LoseBlock（会播破防音效 + 触发 AfterBlockBroken）
        await CreatureCmd.LoseBlock(choiceContext, enemy, enemy.Block, Owner.Creature);
        // 力量 / 人工制品：直接移除整个能力（不在场时 Remove 内部会自己跳过）
        await PowerCmd.Remove<StrengthPower>(enemy);
        await PowerCmd.Remove<ArtifactPower>(enemy);

        // ② 丢弃1张卡（玩家选择，手牌为空则跳过）
        if (PileType.Hand.GetPile(Owner).Cards.Count == 0) return;

        var selected = (await CardSelectCmd.FromHand(
            prefs: CardPrefs(this, DiscardNamedPrompt, 1, 1),
            context: choiceContext,
            player: Owner,
            filter: null,
            source: this)).ToList();

        if (selected.Count == 0) return;

        await CardCmd.Discard(choiceContext, selected[0]);
    }

    // 送墓：从弃牌堆将1张「珠泪陷阱」卡加入手牌
    public async Task SongMuSpecial(PlayerChoiceContext ctx, Player player)
    {
        var discardPile = PileType.Discard.GetPile(player);
        if (!discardPile.Cards.Any(ZhuLeiFilter.IsTrap)) return;

        var picked = (await CardSelectCmd.FromCombatPile(
            ctx,
            discardPile,
            player,
            CardPrefs(this, SelectionScreenPrompt, 1, 1),
            filter: ZhuLeiFilter.IsTrap)).FirstOrDefault();

        if (picked != null)
        {
            await CardPileCmd.Add(picked, PileType.Hand);
        }
    }
}