using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using SelectSpecialCardMod.Scripts.Base;
using SelectSpecialCardMod.Scripts.Custom;
using SelectSpecialCardMod.Scripts.Pool;
using SelectSpecialCardMod.Scripts.Tool;

namespace SelectSpecialCardMod.Scripts.Cards.Special;

// 游戏王「燦幻開門」（Sangen Kaimen，站内 sc_name「灿幻开门」）：
//   打出：这个回合没打出过攻击卡的情况下，是 →「检索」1张「天杯龙」；否 → 使手牌的1张「天杯龙」卡的费用在这个回合为0
//   （参考珠泪·水仙的是/否实现；X 费卡无法设为 0 费，排除）
public class CanHuanKaiMenCard : SelectSpecialBaseCard
{
    public CanHuanKaiMenCard() : base(0, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    // 是/否：「检索」还是 0 费
    private static LocString KaiMenChoicePrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KAI_MEN_CHOICE");

    // 0 费选卡提示
    private static LocString ZeroCostPrompt { get; } = new("card_selection", "SELECT_SPECIAL_CARD_MOD_PROMPT_KAI_MEN_ZERO_COST");

    private static bool IsTianBeiLong(CardModel c) => c.Tags.Contains(SelectTags.TianBeiLong);

    // X 费卡无法设为 0 费，排除（搜查日记同款处理）
    private static bool CanBeFree(CardModel c) => IsTianBeiLong(c) && !c.EnergyCost.CostsX;

    protected override HashSet<CardTag> CanonicalTags => [
        SelectTags.CanHuanMoFa,
    ];

    public override IEnumerable<CardKeyword> CanonicalKeywords => [
    ];

    protected override IEnumerable<IHoverTip> AdditionalHoverTips => [
        HoverTipFactory.FromKeyword(SelectKeywords.CanHuanMoFa),
        HoverTipFactory.FromKeyword(SelectKeywords.Retriever),
    ];

    // 广播钩子：上报攻击卡打出（供"这个回合没打出过攻击卡"判定；去重保证不重复计数）
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;
        ShiJiTracker.ReportAttackPlayed(Owner, cardPlay);
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner == null) return;

        // 这个回合没打出过攻击卡的情况下，适用其中一个效果
        if (ShiJiTracker.AttacksPlayedThisTurn(Owner) > 0)
        {
            await RetrieveTianBeiLong(choiceContext, Owner);
            await MakeTianBeiLongFree(choiceContext, Owner);


        }
        else
        {
            // 是 →「检索」1张「天杯龙」；否 → 使手牌的1张「天杯龙」卡的费用在这个回合为0
            if (await SelectToolCmd.AskYesNo(choiceContext, Owner, KaiMenChoicePrompt, source: this))
            {
                await RetrieveTianBeiLong(choiceContext, Owner);
            }
            else
            {
                await MakeTianBeiLongFree(choiceContext, Owner);
            }
        }


    }

    private async Task RetrieveTianBeiLong(PlayerChoiceContext choiceContext, Player player)
    {
        await SelectToolCmd.RetrieverCard(
            choiceContext,
            player,
            IsTianBeiLong,
            p => p is SelectSpecialCardPool,
            1, source: this);
    }

    private async Task MakeTianBeiLongFree(PlayerChoiceContext choiceContext, Player player)
    {
        var hand = PileType.Hand.GetPile(player).Cards.Where(CanBeFree).ToList();
        if (hand.Count == 0) return;

        var selected = await CardSelectCmd.FromHand(
            prefs: CardPrefs(this, ZeroCostPrompt, 1, 1),
            context: choiceContext,
            player: player,
            filter: CanBeFree,
            source: this);

        var card = selected.FirstOrDefault();
        if (card != null)
        {
            // 费用在这个回合为 0（原版是 SetThisCombat 整场战斗；这里按新描述只到回合结束）
            card.EnergyCost.SetThisTurn(0);
        }
    }
}
